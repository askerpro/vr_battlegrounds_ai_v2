using Mirror;
using UltimateXR.Core;
using UltimateXR.Core.Settings;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Network
{
    /// <summary>
    ///     Транспорт канала состояния UltimateXR — уровень сессии, а не игрока.
    ///
    ///     <para>
    ///     В проекте два независимых канала репликации. Первый — штатный Mirror
    ///     (<c>SyncVar</c>, <c>ClientRpc</c>): команда, счёт, выбор карты. Второй —
    ///     канал состояния UltimateXR: сериализованные <c>byte[]</c>-блобы, которыми
    ///     едут захваты предметов, состояние оружия и <b>здоровье со смертью</b>
    ///     (<c>UxrActor.Life</c> — синхронизируемое свойство).
    ///     </para>
    ///
    ///     <para>
    ///     Раньше второй канал физически проходил через <c>UxrMirrorAvatar</c> —
    ///     компонент на аватаре, то есть на самом недолговечном объекте проекта:
    ///     аватар пересоздаётся при смене скина, смене команды и каждой смене карты.
    ///     Держался канал на двух статических полях, и <c>AvatarManager.ChangeAvatar</c>
    ///     (сначала спавн нового, потом уничтожение старого) их гарантированно ломал.
    ///     Разбор — <c>Docs/audit/architecture-review-2026-08.md</c>, «Корень 1»,
    ///     находки NET-01, NET-02, NET-03.
    ///     </para>
    ///
    ///     <para>
    ///     Теперь канал живёт здесь: объект спавнится один раз в
    ///     <see cref="GameNetworkManager.OnStartServer" /> вместе с
    ///     <see cref="Managers.SessionManager" /> и живёт до остановки сервера.
    ///     Спавн, деспавн и горячая замена аватара повлиять на него не могут —
    ///     связи между ними больше нет.
    ///     </para>
    ///
    ///     <para>
    ///     Экземпляр ровно один на процесс, поэтому все состояния канала —
    ///     обычные поля экземпляра, а не <c>static</c>. Это и есть суть правки:
    ///     статика делала канал общим на процесс и переживала объект, которому
    ///     принадлежала.
    ///     </para>
    /// </summary>
    [DefaultExecutionOrder(VrBattlegrounds.Managers.ManagerOrder.NetworkStateRelay)]
    public class NetworkStateRelay : NetworkBehaviour
    {
        /// <summary>Релей текущего процесса. Null, пока сервер не поднят.</summary>
        public static NetworkStateRelay Instance { get; private set; }

        /// <summary>
        ///     Клиентский протокол начального снимка: когда просить, какой ответ применить,
        ///     открыт ли канал инкрементов. До открытия инкременты отбрасываются: они описали бы
        ///     изменения относительно состояния, которого у клиента ещё нет.
        ///
        ///     Состояние экземплярное. Статическим оно было в SDK, и именно поэтому
        ///     второй подключившийся клиент начинал применять события до снимка (NET-01).
        /// </summary>
        private readonly InitialStateBarrier _barrier = new InitialStateBarrier();

        /// <summary>Клиент: подписан на смену сцены и готовность запуска карты.</summary>
        private bool _clientSubscribed;

        /// <summary>
        ///     Открыт ли канал состояния этой машины для запуска карты <paramref name="key"/>: сервер — всегда
        ///     (он источник), клиент — после применения свежего снимка этого запуска. Из этого выводится
        ///     <c>MapRunAdmission.IsLocalPlayable</c>.
        /// </summary>
        public bool HasInitialState(MapRunKey key) => isServer || _barrier.IsOpenFor(key);

        /// <summary>Подписан ли релей на <see cref="UxrManager.ComponentStateChanged" />.</summary>
        private bool _subscribed;

        /// <summary>
        ///     События сетевых аватаров, порождённые до выравнивания их <c>UniqueId</c>.
        ///     См. <see cref="AvatarStateEventGate" />.
        /// </summary>
        private AvatarStateEventGate _gate;


        // ── Жизненный цикл ────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (Instance == this)
                Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Сервер — источник правды: снимок ему просить не у кого, канал открыт сразу (HasInitialState).
            Subscribe();

            GameLog.Network.Info("[NetworkStateRelay] Канал состояния поднят на сервере.");
        }

        public override void OnStopServer()
        {
            Unsubscribe();
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // На хосте OnStartServer уже подписал нас. Повторная подписка тут —
            // это NET-03: каждое изменение состояния уходило клиентам дважды.
            if (isServer)
                return;

            Subscribe();
            GameNetworkManager.ClientSceneChanged += HandleClientSceneChanged;
            MapBootstrap.LocalReadinessChanged += EvaluateInitialState;
            _clientSubscribed = true;

            EvaluateInitialState();
        }

        public override void OnStopClient()
        {
            if (_clientSubscribed)
            {
                GameNetworkManager.ClientSceneChanged -= HandleClientSceneChanged;
                MapBootstrap.LocalReadinessChanged -= EvaluateInitialState;
                _clientSubscribed = false;
                CloseInitialState();
                Unsubscribe();
            }

            base.OnStopClient();
        }

        /// <summary>
        ///     Страховка событий готовности: пока канал не открыт и запроса нет, клиент каждый кадр
        ///     сверяет локальную готовность запуска. Открытый канал и ожидаемый ответ ничего не стоят.
        /// </summary>
        private void Update()
        {
            if (_clientSubscribed && !_barrier.IsOpen && _barrier.PendingRequest == 0)
                EvaluateInitialState();
        }

        // ── Отправка ──────────────────────────────────────────────────────

        private void Subscribe()
        {
            if (_subscribed) return;

            UxrManager.ComponentStateChanged += HandleComponentStateChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            UxrManager.ComponentStateChanged -= HandleComponentStateChanged;
            _subscribed = false;
            _gate?.Clear();
        }

        /// <summary>
        ///     Локальное изменение состояния любого компонента UltimateXR.
        ///     Сервер рассылает его всем клиентам, клиент отправляет серверу.
        ///
        ///     События, применённые из сети, сюда не приходят — <c>UxrManager</c> не
        ///     поднимает <c>ComponentStateChanged</c> внутри <c>ExecuteStateSyncEvent</c>.
        ///     Но «родилось на этой машине» ещё не значит «эта машина автор»: UltimateXR
        ///     пересчитывает действия чужого игрока на каждой машине, и пересчёт порождает
        ///     те же события. Раньше здесь стояло «фильтр по владению не нужен» — из-за этого
        ///     выстрел клиента в сетевой игре давал вторую пулю. Кто автор —
        ///     <see cref="StateEventAuthority"/>.
        /// </summary>
        private void HandleComponentStateChanged(IUxrStateSync component, UxrSyncEventArgs eventArgs)
        {
            if (!eventArgs.Options.HasFlag(UxrStateSyncOptions.Network))
                return;

            // Объект уже снят сетью и доживает до конца кадра — его OnDisable (телепорт)
            // не событие для другой стороны: там объекта уже нет.
            if (DespawnedObjectEventFilter.ShouldDrop(component))
                return;

            // Событие пересчитано на копии действия чужого игрока (выстрел, затвор его оружия):
            // автор разошлёт его сам. Иначе оно размножалось — см. StateEventAuthority.
            if (!StateEventAuthority.ShouldSend(component, eventArgs))
                return;

            // Событие невыровненного сетевого аватара уйдёт само по AvatarSpawned (NET-26).
            _gate ??= new AvatarStateEventGate(Send);
            if (_gate.TryDefer(component, eventArgs, Time.realtimeSinceStartup))
                return;

            Send(component, eventArgs);
        }

        /// <summary>
        ///     Сериализует событие и отправляет его по сети. Сериализация здесь, а не
        ///     в момент события: у придержанного события ссылки на компоненты должны
        ///     записаться уже выровненными id.
        /// </summary>
        private void Send(IUxrStateSync component, UxrSyncEventArgs eventArgs)
        {
            byte[] serializedEvent = eventArgs.SerializeEventBinary(component);
            if (serializedEvent == null)
                return;

            // Событие на каждый хват/отпускание/выстрел: строку не собираем, если Verbose
            // выключен — иначе интерполяция и component.name аллоцируют на каждом событии.
            if (GameLog.Network.IsEnabled(LogLevel.Verbose))
            {
                GameLog.Network.Verbose(
                    $"[NetworkStateRelay] Отправка состояния: {component.Component.name} " +
                    $"({eventArgs.GetType().Name}), {serializedEvent.Length} Б, isServer={isServer}");
            }

            if (isServer)
            {
                RpcComponentStateChanged(serializedEvent, 0u);
                StateEventAuthority.MarkAmmoAdmissionPublished(component, eventArgs);
            }
            else if (NetworkClient.active && NetworkClient.ready)
            {
                CmdComponentStateChanged(serializedEvent);
            }
            else
            {
                // Клиент между сценами: до NetworkClient.Ready() Mirror команду не примет
                // и ругнётся в лог. Отправлять всё равно нечего — сервер пересоберёт
                // состояние сцены и отдаст его начальным снимком.
                GameLog.Network.Verbose(
                    "[NetworkStateRelay] Изменение состояния не отправлено: клиент ещё не готов (смена сцены).");
            }
        }

        /// <summary>
        ///     Клиент прислал изменение состояния. Сервер применяет его у себя
        ///     и рассылает остальным.
        ///
        ///     <c>requiresAuthority = false</c>: у объекта уровня сессии нет владельца
        ///     среди клиентов — авторитет здесь и не нужен, потому что применяет
        ///     событие всё равно сервер.
        /// </summary>
        [Command(requiresAuthority = false)]
        private void CmdComponentStateChanged(byte[] serializedEvent, NetworkConnectionToClient sender = null)
        {
            // Новый admission разрешён только server queue; client-only payload не исполняется и не отражается observers.
            // Нераспознанное событие идёт прежним путём (выполнить и разослать), фильтр — только для ammo-payload.
            if (UxrSyncEventArgs.DeserializeEventBinary(serializedEvent, out var target, out var args, out _) &&
                IsClientAmmoAdmissionPayload(target, args)) return;
            UxrManager.Instance.ExecuteStateSyncEvent(serializedEvent);

            // netId объекта игрока отправителя — метка «кто это породил».
            // Автору событие обратно не применяем: у него оно уже произошло,
            // а повторное исполнение метода (захват, выстрел) сработало бы дважды.
            uint originNetId = sender != null && sender.identity != null ? sender.identity.netId : 0u;

            RpcComponentStateChanged(serializedEvent, originNetId);
        }

        /// <summary>
        ///     Сервер рассылает изменение состояния клиентам.
        /// </summary>
        /// <param name="serializedEvent">Сериализованное событие</param>
        /// <param name="originNetId">
        ///     netId объекта игрока, породившего событие, или 0, если событие
        ///     породил сам сервер. Клиент с этим netId событие пропускает.
        /// </param>
        [ClientRpc]
        private void RpcComponentStateChanged(byte[] serializedEvent, uint originNetId)
        {
            // Хост: сервер уже применил событие у себя — либо породил сам,
            // либо применил в Cmd.
            if (isServer)
                return;

            if (originNetId != 0 && NetworkClient.localPlayer != null &&
                NetworkClient.localPlayer.netId == originNetId)
                return;

            if (!_barrier.AcceptsIncrements)
            {
                GameLog.Network.Verbose(
                    "[NetworkStateRelay] Событие состояния отброшено: начальный снимок ещё не получен.");
                return;
            }

            UxrManager.Instance.ExecuteStateSyncEvent(serializedEvent);
        }

        // ── Начальный снимок состояния сцены ──────────────────────────────

        /// <summary>
        ///     Клиент догрузил новую сцену: всё, что было в предыдущей, уничтожено,
        ///     а объекты новой ещё не описаны — снимок надо взять заново, когда запуск
        ///     новой сцены станет локально готов.
        /// </summary>
        private void HandleClientSceneChanged()
        {
            CloseInitialState();
            EvaluateInitialState();
        }

        /// <summary>
        ///     Запуск карты активной сцены и его локальная готовность. Сцена без MapBootstrap (стенд)
        ///     готова, как только готов клиент, и просит снимок с пустым ключом.
        /// </summary>
        private static bool LocalRun(out MapRunKey key)
        {
            key = default;
            if (!NetworkClient.active || !NetworkClient.ready || NetworkClient.isLoadingScene) return false;

            MapBootstrap bootstrap = MapBootstrap.ForScene(SceneManager.GetActiveScene());
            if (bootstrap == null) return true;

            key = bootstrap.LocalRunKey;
            return key.IsValid && bootstrap.IsLocallyReady(key);
        }

        /// <summary>
        ///     Готовность запуска изменилась: запросить свежий снимок, если он нужен. Сам запрос и есть
        ///     сообщение серверу о готовности клиента; отдельного подтверждения нет.
        /// </summary>
        private void EvaluateInitialState()
        {
            if (!_clientSubscribed) return;

            bool ready = LocalRun(out MapRunKey key);
            uint request = _barrier.Evaluate(key, ready);
            if (request != 0) SendInitialStateRequest(key, request);
        }

        private void SendInitialStateRequest(MapRunKey key, uint request)
        {
            GameLog.Network.Info($"[NetworkStateRelay] Запрашиваю у сервера начальный снимок состояния (запуск {key}, запрос {request}).");
            CmdRequestInitialState(key.SessionEpoch, key.LoadSequence, request);
        }

        private void CloseInitialState() => _barrier.Reset();

        public void RequestAmmoAdmissionResynchronization()
        {
            if (isServer || !NetworkClient.active || !NetworkClient.ready) return;

            MapRunKey key = _barrier.OpenKey;
            uint request = _barrier.RequestResynchronization();
            if (request == 0) return;

            SendInitialStateRequest(key, request);
        }

        [Server]
        public bool ServerResynchronizeAmmoAdmissionPeers()
        {
            if (!UxrManager.HasInstance) return false;
            byte[] snapshot = UxrManager.Instance.SaveStateChanges(null, null,
                UxrStateSaveLevel.ChangesSinceBeginning, UxrGlobalSettings.Instance.NetFormatInitialState);
            if (snapshot == null || snapshot.Length == 0) return false;
            MapRunKey key = ServerCurrentRun();
            foreach (var connection in NetworkServer.connections.Values)
                // Хост — сам источник снимка; загрузка поверх авторитетного состояния сбила бы его захваты.
                // Номер запроса 0: клиент применит снимок только поверх уже открытого канала того же запуска.
                if (connection != null && connection.isReady && !(connection is LocalConnectionToClient))
                    TargetLoadInitialState(connection, key.SessionEpoch, key.LoadSequence, 0u, snapshot);
            return true;
        }

        private static bool IsClientAmmoAdmissionPayload(IUxrStateSync target, UxrSyncEventArgs args)
        {
            if (target?.Component is UxrFirearmAmmoUnit ||
                target?.Component is UxrFirearmMag store && store.IsFixedAmmoStore) return true;
            if (!(args is UxrMethodInvokedSyncEventArgs method)) return false;
            if (target?.Component is UxrFirearmWeapon &&
                (method.MethodName == "CommitAmmoAdmission" || method.MethodName == "TryAcceptAmmoUnit" ||
                 method.MethodName == "TryBeginAmmoAdmissionBarrier" || method.MethodName == "TryEndAmmoAdmissionBarrier" ||
                 method.MethodName == "TryAcknowledgeFixedAmmoResynchronization" || method.MethodName == "WriteReadinessCommit")) return true;
            foreach (object parameter in method.Parameters)
                if (parameter is UxrAmmoAdmissionCommit || parameter is UxrFixedAmmoSnapshot ||
                    parameter is UxrFirearmReadinessCommit commit && commit.Operation == UxrFirearmReadinessOperation.AmmoAdmission) return true;
            return false;
        }

        /// <summary>
        ///     Запуск карты активной сцены сервера — ключ descriptor; пустой ключ — сцена без MapBootstrap.
        /// </summary>
        private static MapRunKey ServerCurrentRun()
        {
            if (MapBootstrap.ForScene(SceneManager.GetActiveScene()) == null) return default;
            MapRunAuthority authority = MapRunAuthority.Instance;
            return authority != null ? authority.Current.Key : default;
        }

        /// <summary>
        ///     Принимает ли сервер запрос снимка для запуска <paramref name="key"/>: это текущий запуск активной
        ///     сцены и он собран (CompositionReady/Ready). Запрос старого запуска (клиент ещё не знает о
        ///     перезагрузке, отмене или смене карты) не обслуживается: клиент попросит снова, когда его новая
        ///     сцена станет готова. Сцена без MapBootstrap принимает только пустой ключ.
        /// </summary>
        public static bool ServerAcceptsInitialRequest(MapRunKey key, out string reason)
        {
            if (MapBootstrap.ForScene(SceneManager.GetActiveScene()) == null)
            {
                reason = key.IsValid ? "на сервере сцена без запуска карты" : null;
                return reason == null;
            }

            MapRunAuthority authority = MapRunAuthority.Instance;
            MapRunSnapshot current = authority != null ? authority.Current : default;
            reason = !key.IsValid ? "клиент не назвал запуск"
                : current.Key != key ? $"текущий запуск сервера {current.Key}"
                : current.Status != MapBootstrapStatus.CompositionReady && current.Status != MapBootstrapStatus.Ready
                    ? $"запуск в статусе {current.Status}"
                    : null;
            return reason == null;
        }

        /// <summary>
        ///     Клиент готов к запуску <c>(epoch, sequence)</c> и просит свежее состояние сцены. Ответ уходит только
        ///     ему и несёт тот же ключ и номер запроса. Снимок снимается сейчас: всё, что сервер разошлёт позже,
        ///     уйдёт этому соединению после ответа тем же надёжным упорядоченным каналом.
        /// </summary>
        [Command(requiresAuthority = false)]
        private void CmdRequestInitialState(System.Guid epoch, ulong sequence, uint request, NetworkConnectionToClient sender = null)
        {
            if (sender == null || request == 0)
                return;

            var key = new MapRunKey(epoch, sequence);
            if (!ServerAcceptsInitialRequest(key, out string reason))
            {
                GameLog.Network.Info($"[NetworkStateRelay] Запрос снимка {request} клиента {sender.connectionId} " +
                                     $"для запуска {key} отклонён: {reason}.");
                return;
            }

            byte[] state = UxrManager.Instance.SaveStateChanges(
                null,
                null,
                UxrStateSaveLevel.ChangesSinceBeginning,
                UxrGlobalSettings.Instance.NetFormatInitialState);

            GameLog.Network.Info(
                $"[NetworkStateRelay] Отдаю начальный снимок клиенту {sender.connectionId} (запуск {key}, запрос {request}): " +
                $"{(state != null ? state.Length : 0)} Б.");

            TargetLoadInitialState(sender, epoch, sequence, request, state ?? new byte[0]);
        }

        /// <summary>
        ///     Снимок пришёл. Применяется, только если это ответ на текущий запрос текущего запуска либо
        ///     разосланная сервером пересинхронизация поверх уже открытого канала того же запуска. Ответ старого
        ///     запуска отбрасывается и канал не открывает.
        /// </summary>
        [TargetRpc]
        private void TargetLoadInitialState(NetworkConnectionToClient target, System.Guid epoch, ulong sequence, uint request,
                                            byte[] serializedState)
        {
            var key = new MapRunKey(epoch, sequence);
            bool accepted = request != 0 ? _barrier.TryAcceptResponse(key, request) : _barrier.AcceptsUnsolicited(key);
            if (!accepted)
            {
                GameLog.Network.Info($"[NetworkStateRelay] Снимок запуска {key} (запрос {request}) отброшен: ждём запрос " +
                                     $"{_barrier.PendingRequest} запуска {_barrier.PendingKey}, канал " +
                                     (_barrier.IsOpen ? $"открыт для {_barrier.OpenKey}." : "закрыт."));
                return;
            }

            if (serializedState != null && serializedState.Length > 0)
            {
                // Барьер ждал только локальную готовность запуска: всё, что снимок упоминает, обязано уже быть.
                // Пропуск адресата — дефект порядка, а не повод молча потерять его состояние.
                int missing = InitialStateInventory.CountMissingTargets(serializedState, out int total);
                if (missing > 0)
                {
                    GameLog.Network.Error($"[NetworkStateRelay] В снимке запуска {key} {missing} из {total} адресатов " +
                                          "не зарегистрированы на клиенте: их состояние не применится.");
                }

                UxrManager.Instance.LoadStateChanges(serializedState);
            }

            GameLog.Network.Info(
                $"[NetworkStateRelay] Начальный снимок запуска {key} применён ({(serializedState != null ? serializedState.Length : 0)} Б). " +
                "Канал состояния открыт.");

        }
    }
}
