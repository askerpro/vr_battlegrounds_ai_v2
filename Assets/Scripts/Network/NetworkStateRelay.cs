using Mirror;
using UltimateXR.Core;
using UltimateXR.Core.Settings;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UnityEngine;
using VrBattlegrounds.Core;

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
        ///     Получен ли начальный снимок состояния сцены. До него применять
        ///     инкрементальные события нельзя: они опишут изменения относительно
        ///     состояния, которого у клиента ещё нет.
        ///
        ///     Поле экземплярное. Статическим оно было в SDK, и именно поэтому
        ///     второй подключившийся клиент начинал применять события до снимка (NET-01).
        /// </summary>
        private bool _initialStateLoaded;

        /// <summary>Подписан ли релей на <see cref="UxrManager.ComponentStateChanged" />.</summary>
        private bool _subscribed;

        private static LogLevel Log => GameSettings.Instance.LogLevelNetwork;

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

            // Сервер — источник правды: снимок ему просить не у кого.
            _initialStateLoaded = true;
            Subscribe();

            GameLog.Info(Log, "[NetworkStateRelay] Канал состояния поднят на сервере.");
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

            RequestInitialState();
        }

        public override void OnStopClient()
        {
            if (!isServer)
            {
                GameNetworkManager.ClientSceneChanged -= HandleClientSceneChanged;
                Unsubscribe();
            }

            base.OnStopClient();
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
        }

        /// <summary>
        ///     Локальное изменение состояния любого компонента UltimateXR.
        ///     Сервер рассылает его всем клиентам, клиент отправляет серверу.
        ///
        ///     Фильтр по владению здесь не нужен: релей один на процесс, и он
        ///     ретранслирует то, что породила именно эта машина. События, применённые
        ///     из сети, сюда не приходят — <c>UxrManager</c> не поднимает
        ///     <c>ComponentStateChanged</c> внутри <c>ExecuteStateSyncEvent</c>.
        /// </summary>
        private void HandleComponentStateChanged(IUxrStateSync component, UxrSyncEventArgs eventArgs)
        {
            if (!eventArgs.Options.HasFlag(UxrStateSyncOptions.Network))
                return;

            byte[] serializedEvent = eventArgs.SerializeEventBinary(component);
            if (serializedEvent == null)
                return;

            GameLog.Verbose(Log,
                $"[NetworkStateRelay] Отправка состояния: {component.Component.name} " +
                $"({eventArgs.GetType().Name}), {serializedEvent.Length} Б, isServer={isServer}");

            if (isServer)
            {
                RpcComponentStateChanged(serializedEvent, 0u);
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
                GameLog.Verbose(Log,
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

            if (!_initialStateLoaded)
            {
                GameLog.Verbose(Log,
                    "[NetworkStateRelay] Событие состояния отброшено: начальный снимок ещё не получен.");
                return;
            }

            UxrManager.Instance.ExecuteStateSyncEvent(serializedEvent);
        }

        // ── Начальный снимок состояния сцены ──────────────────────────────

        /// <summary>
        ///     Клиент догрузил новую сцену: всё, что было в предыдущей, уничтожено,
        ///     а объекты новой ещё не описаны — снимок надо взять заново.
        /// </summary>
        private void HandleClientSceneChanged()
        {
            RequestInitialState();
        }

        private void RequestInitialState()
        {
            _initialStateLoaded = false;

            if (!NetworkClient.active)
                return;

            GameLog.Info(Log, "[NetworkStateRelay] Запрашиваю у сервера начальный снимок состояния сцены.");
            CmdRequestInitialState();
        }

        /// <summary>
        ///     Клиент просит текущее состояние сцены. Ответ уходит только ему.
        /// </summary>
        [Command(requiresAuthority = false)]
        private void CmdRequestInitialState(NetworkConnectionToClient sender = null)
        {
            if (sender == null)
                return;

            byte[] state = UxrManager.Instance.SaveStateChanges(
                null,
                null,
                UxrStateSaveLevel.ChangesSinceBeginning,
                UxrGlobalSettings.Instance.NetFormatInitialState);

            GameLog.Info(Log,
                $"[NetworkStateRelay] Отдаю начальный снимок клиенту {sender.connectionId}: " +
                $"{(state != null ? state.Length : 0)} Б.");

            TargetLoadInitialState(sender, state ?? new byte[0]);
        }

        /// <summary>
        ///     Начальный снимок пришёл. До этого момента инкрементальные события
        ///     отбрасывались — теперь их можно применять.
        /// </summary>
        [TargetRpc]
        private void TargetLoadInitialState(NetworkConnectionToClient target, byte[] serializedState)
        {
            if (serializedState != null && serializedState.Length > 0)
            {
                UxrManager.Instance.LoadStateChanges(serializedState);
            }

            _initialStateLoaded = true;

            GameLog.Info(Log,
                $"[NetworkStateRelay] Начальный снимок применён ({(serializedState != null ? serializedState.Length : 0)} Б). " +
                "Канал состояния открыт.");
        }
    }
}
