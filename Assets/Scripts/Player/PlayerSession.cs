using Mirror;
using System;
using VrBattlegrounds.Managers;
using VrBattlegrounds.GameModes;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.PhysicalSpaceUtils;

using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Player.WallPass;
namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Сохраняемая сессия игрока. В отличие от аватара (куклы), этот объект не уничтожается
    /// при смене скина (горячей замене префаба). Он хранит счет, ID команды, никнейм и токен устройства.
    /// Когда игрок переподключается, сервер может восстановить эту сессию по deviceToken.
    /// </summary>
    public class PlayerSession : NetworkBehaviour
    {
        public static PlayerSession LocalSession { get; private set; }

        // ── События сервера ───────────────────────────────────────────────────
        public event Action<PlayerSession> SessionReady;

        /// <summary>
        /// Аватар локального игрока появился, сменился (скин, карта) или исчез (null).
        /// Замена опроса в <c>Update</c>: клиентскому коду больше не нужно каждый кадр
        /// спрашивать «мой аватар уже заспавнился?».
        /// На выделенном сервере не срабатывает — там нет локальной сессии.
        /// </summary>
        public static event Action<PlayerController> LocalAvatarChanged;

        // ── Сетевые данные (Хранятся на сервере, синхронизируются всем) ───────

        [SyncVar] public string DeviceToken = string.Empty;

        [SyncVar] public ClientDeviceType DeviceType;
        [SyncVar] public bool IsAdmin;
        [SyncVar] public GameRole Role;

        [SyncVar(hook = nameof(OnPlayerNameChanged))]
        public string PlayerName = "Player";

        [SyncVar(hook = nameof(OnTeamIndexChanged))]
        public int TeamIndex = 0;

        [SyncVar] public int AvatarIndex = 0;

        [SyncVar] public int Kills = 0;
        [SyncVar] public int Deaths = 0;
        [SyncVar] public int Score = 0;

        // Состояние стены переживает замену скина: замена тела не отменяет нарушение.
        [SyncVar] private WallPassStatus _wallPassStatus;

        public WallPassStatus WallPassStatus => _wallPassStatus;

        /// <summary>Публикует единый серверный снимок T-40 для оружия и локальных эффектов.</summary>
        [Server]
        internal void ServerSetWallPassStatus(WallPassStatus status)
        {
            if (StateEventAuthority.IsWorldAuthority) _wallPassStatus = status;
        }

        // ── Калибровка игрока (T-50) ──────────────────────────────────────────
        //
        // Сессия — владелец калибровки игрока: абсолютные значения в метрах (PlayerCalibration),
        // единственный писатель — сервер (ServerAcceptCalibration). Значение переживает смену скина,
        // команды и карты, а аватар при каждом из этих событий пересоздаётся. К аватару его ставит
        // AvatarCalibrationApplier — на каждой машине, своему и чужому, одним правилом.
        //
        // Своя сессия предсказывает свой запрос: замер применяется к своему аватару сразу, а при
        // отказе сервера (бой) откатывается к значению сервера тем же применителем. Ответ сервера —
        // номер последнего обработанного запроса (_answeredCalibrationRequest), SyncVar рядом со
        // значением: опоздавший клиент получает то же состояние, отдельный RPC не нужен.

        /// <summary>Калибровка игрока, решённая сервером. Пишет только <see cref="ServerAcceptCalibration" />.</summary>
        [SyncVar(hook = nameof(OnCalibrationChanged))]
        private PlayerCalibration _calibration;

        /// <summary>Номер последнего запроса калибровки клиента, на который сервер ответил (принял или отклонил).</summary>
        [SyncVar(hook = nameof(OnCalibrationAnswered))]
        private int _answeredCalibrationRequest;

        /// <summary>Калибровка игрока, решённая сервером.</summary>
        public PlayerCalibration Calibration => _calibration;

        /// <summary>
        /// Игрок откалибровал своё физическое пространство по якорям карты. Признак выбора точки
        /// спавна (T-30): до калибровки игрока можно ставить в зону команды, после — его место задано
        /// физически. Только чтение: пишет сервер через <see cref="ServerAcceptCalibration" />.
        /// </summary>
        public bool IsCalibrated => _calibration.IsCalibrated;

        /// <summary>Номер последнего отправленного запроса. Только у своей сессии.</summary>
        private int _sentCalibrationRequest;

        /// <summary>Предсказанный ответ сервера на последний запрос. Только у своей сессии.</summary>
        private PlayerCalibration _predictedCalibration;
        // Только состояние процедуры запроса: SDK-перенос публикуется после ответа сервера.
        private bool _placementPredictionPending;

        /// <summary>
        /// Калибровка, которую видит аватар на этой машине: у своей сессии с неотвеченным запросом —
        /// предсказание, иначе — значение сервера.
        /// </summary>
        public PlayerCalibration EffectiveCalibration =>
            isLocalPlayer && _sentCalibrationRequest > _answeredCalibrationRequest ? _predictedCalibration : _calibration;

        /// <summary>Откуда пришло значение: подключение (сообщение клиента или снимок) или запрос игрока.</summary>
        public enum CalibrationOrigin
        {
            /// <summary>Первичное значение при создании сессии — без запрета в бою.</summary>
            Connect,

            /// <summary>Игрок прошёл процедуру калибровки.</summary>
            Player,

            /// <summary>Серверное наблюдение позы: меняет только placement, не параметры калибровки.</summary>
            ObservedPlacement
        }

        private const double InitialCalibrationWindow = 5.0;
        private double _initialCalibrationDeadline;
        private bool _initialCalibrationReceived;

        /// <summary>
        /// Первичная калибровка приходит до выбора опоры T-40 — в сообщении подключения. Бот не ждёт
        /// клиента; сессия без первичного значения ждёт ограниченное окно.
        /// </summary>
        internal bool InitialCalibrationReady => connectionToClient == null ||
            _initialCalibrationReceived || NetworkTime.time >= _initialCalibrationDeadline;

        // ── Готовность к раунду ───────────────────────────────────────────────
        //
        // Три разных вещи, которые раньше были слиты в одну (T-29):
        //   · ReadyState      — намерение игрока: «я закончил, начинайте».
        //   · IsInSpawnZone   — физическое условие: игрок стоит в своей зоне спавна.
        //   · HasGrabbedDogTag — жест, которым намерение обычно и объявляется.
        //
        // Готовность выводилась из двух последних (`IsInSpawnZone && HasGrabbedDogTag`),
        // и у неё не было отмены: передумал — уже никак. Теперь готовность — явное
        // состояние, а жест и зона остались тем, чем являются: способом её объявить
        // и условием её сохранять.

        /// <summary>
        /// Явная готовность игрока к раунду. Единственный источник правды.
        ///
        /// Поле приватное намеренно: писать его вправе только
        /// <see cref="ServerSetReady" />, и это единственная точка записи во всём
        /// проекте. Клиент своё намерение сообщает командой <see cref="CmdSetReady" />.
        /// </summary>
        [SyncVar(hook = nameof(OnReadyStateChanged))]
        private bool _readyState;

        /// <summary>
        /// Объявил ли игрок готовность к раунду. Читается и на сервере, и на клиенте:
        /// значение реплицируется, поэтому вновь подключившийся получает его начальным
        /// значением спавна, а не ждёт следующего изменения.
        /// </summary>
        public bool ReadyState => _readyState;

        /// <summary>Значение <see cref="SpawnZoneTeamIndex" />, означающее «игрок не в зоне спавна».</summary>
        public const int NoSpawnZone = -1;

        /// <summary>
        /// В зоне спавна <b>какой команды</b> сейчас стоит игрок. <see cref="NoSpawnZone" /> —
        /// ни в какой.
        ///
        /// <para>
        /// Здесь стоял один булев флаг «в зоне», и это была находка <b>RDY-04</b>:
        /// <c>TeamSpawnZone</c> писала его любому вошедшему, не спрашивая команду.
        /// Игрок, забредший в базу противника, считался стоящим «в своей зоне» и
        /// сохранял право на готовность оттуда. Обратная сторона того же флага: выход
        /// из <b>чужой</b> зоны снимал признак игроку, который в этот момент уже стоял
        /// в своей, — а с исправлением WPN-03 переход «база A → база B» стал штатным,
        /// потому что смена команды переносит игрока в новую базу.
        /// </para>
        ///
        /// <para>
        /// Индекс команды вместо флага «свой/чужой» выбран потому, что отвечает и на
        /// второй вопрос — «игрок в чужой базе» (<see cref="IsInEnemySpawnZone" />), —
        /// который правилам матча ещё понадобится, и потому что позволяет зоне убирать
        /// за собой только собственную запись: см. <see cref="ServerExitSpawnZone" />.
        /// </para>
        /// </summary>
        [SyncVar] public int SpawnZoneTeamIndex = NoSpawnZone;

        /// <summary>
        /// Игрок физически находится в зоне спавна <b>своей</b> команды. <b>Условие</b>
        /// готовности, а не сама готовность: выход из зоны её снимает
        /// (см. <see cref="ServerExitSpawnZone" />).
        ///
        /// Значение выводится, а не хранится: обе его половины —
        /// <see cref="SpawnZoneTeamIndex" /> и <see cref="TeamIndex" /> — реплицируются,
        /// поэтому клиент получает тот же ответ, что и сервер, и лишнему <c>SyncVar</c>
        /// не с чем разъезжаться.
        /// </summary>
        public bool IsInSpawnZone => SpawnZoneTeamIndex == TeamIndex;

        /// <summary>
        /// Игрок стоит в зоне спавна <b>чужой</b> команды. Правилам матча этот вопрос
        /// ещё пригодится, а держать на него отдельный учёт больше не нужно.
        /// </summary>
        public bool IsInEnemySpawnZone => SpawnZoneTeamIndex != NoSpawnZone && SpawnZoneTeamIndex != TeamIndex;

        /// <summary>
        /// Жетон в арсенале взят. Жест, которым игрок объявляет готовность, — но не она
        /// сама: жетон берут один раз за фазу, а готовность можно и отменить.
        ///
        /// Поле осталось публичным и свободно записываемым командой
        /// <see cref="CmdSetDogTagGrabbed" />, потому что сценарии яруса C используют
        /// его как единственный обратный канал «клиент → сервер» (см. `Docs/testing.md`).
        /// Фазу раунда оно больше не двигает.
        /// </summary>
        [SyncVar] public bool HasGrabbedDogTag = false;

        // ── Связь с аватаром ──────────────────────────────────────────────────

        /// <summary>
        /// netId текущего физического аватара (куклы) — единственный источник правды о связи.
        /// Реплицируется, поэтому «где мой аватар» может спросить и клиент, а не только сервер.
        /// Ноль означает «аватара нет»: ещё не заспавнен или уничтожен при смене карты.
        /// </summary>
        [SyncVar(hook = nameof(OnActiveAvatarNetIdChanged))]
        public uint ActiveAvatarNetId;

        /// <summary>Разрешённая ссылка на аватар. Кэш, источник правды — <see cref="ActiveAvatarNetId"/>.</summary>
        private PlayerController _activeAvatar;

        /// <summary>
        /// Текущий физический аватар (кукла). Доступен и на сервере, и на клиенте.
        ///
        /// Ссылка кэшируется. Если кэш пуст — аватар уничтожен либо netId приехал раньше,
        /// чем сам объект заспавнился, — выполняется отложенное разрешение по netId.
        /// Присваивать имеет смысл только на сервере: он владеет <see cref="ActiveAvatarNetId"/>.
        /// </summary>
        public PlayerController ActiveAvatar
        {
            get
            {
                // Сравнение с null по-Unity ловит и уничтоженный аватар (смена скина, смена карты).
                if (_activeAvatar == null) ResolveActiveAvatar();
                return _activeAvatar;
            }
            set
            {
                if (value != null && value.netId == 0)
                {
                    GameLog.Player.Warning(
                        $"[PlayerSession] {PlayerName}: аватар назначен до NetworkServer.Spawn — netId ещё 0, клиенты связь не получат.");
                }

                ActiveAvatarNetId = value != null ? value.netId : 0u;

                // На выделенном сервере хук SyncVar не вызывается (Mirror зовёт его только
                // в host-режиме), поэтому связь проставляем здесь же. Повторный вызов
                // из хука безвреден — LinkAvatar идемпотентен.
                LinkAvatar(value);
            }
        }

        public TeamData Team => TeamRegistry.Instance?.GetByIndex(TeamIndex);

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            // Компоненты сессии, а не аватара: все скины и боты получают одну механику.
            if (GetComponent<WallPassMonitor>() == null) gameObject.AddComponent<WallPassMonitor>();
            if (GetComponent<WallPassFeedback>() == null) gameObject.AddComponent<WallPassFeedback>();
            if (GetComponent<PlayerPlacementTracker>() == null) gameObject.AddComponent<PlayerPlacementTracker>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Признак первичной калибровки не сбрасывается: её может принести создатель сессии
            // (PlayersManager) раньше спавна, а экземпляр сессии всегда новый.
            _initialCalibrationDeadline = NetworkTime.time + InitialCalibrationWindow;
            GameLog.Player.Info($"[PlayerSession] {netId} started on server for {PlayerName}.");
            SessionReady?.Invoke(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (isLocalPlayer)
            {
                LocalSession = this;

                // Хук ActiveAvatarNetId мог отработать раньше: Mirror применяет SyncVar
                // до вызова OnStartClient, и тогда LocalSession ещё не был назначен,
                // а событие ушло «в никуда». Досылаем текущее состояние подписчикам.
                LocalAvatarChanged?.Invoke(ActiveAvatar);

                SubscribeToLocalCalibration();

                // Значение, решённое сервером при подключении, приехало в данных спавна сессии:
                // машина запоминает его (в том числе снимок после перезапуска) и ставит своему аватару.
                ApplyCalibrationToAvatar();
            }
            GameLog.Player.Info($"[PlayerSession] {netId} started on client for {PlayerName}.");
        }

        /// <summary>
        /// Клиент отключился или сессию деспавнили. Снимаем статическую ссылку, иначе она
        /// указывает на уничтоженный объект: <see cref="LocalSession"/> — единственный путь
        /// клиентского кода к своему аватару (T-11), и висящая ссылка выглядит как живая.
        ///
        /// Сравнение с <c>this</c> обязательно. При переподключении новая сессия успевает
        /// встать в <see cref="LocalSession"/> раньше, чем Mirror доберётся до деспавна
        /// старой, — безусловное обнуление стёрло бы ссылку на актуальную сессию.
        /// </summary>
        public override void OnStopClient()
        {
            base.OnStopClient();

            UnsubscribeFromLocalCalibration();

            if (LocalSession == this)
            {
                LocalSession = null;
            }
        }

        // ── Калибровка своего игрока: запрос и предсказание ──────────────────

        /// <summary>Подписан ли этот экземпляр на замеры. Защита от двойной подписки и от отписки чужой сессии.</summary>
        private bool _subscribedToCalibration;

        /// <summary>
        /// Своя сессия слушает замеры процедуры калибровки (<see cref="LocalPlayerCalibration.Submitted" />).
        ///
        /// <para>
        /// Первичного запроса здесь нет и быть не должно (T-50 этап 3): своё значение клиент принёс
        /// в сообщении подключения, а без значения (перезапуск приложения) он ничего не публикует —
        /// иначе пустые данные затёрли бы снимок сервера, и откалиброванный игрок после следующей
        /// смены карты оказался бы в зоне команды.
        /// </para>
        /// </summary>
        private void SubscribeToLocalCalibration()
        {
            if (_subscribedToCalibration) return;

            LocalPlayerCalibration.Submitted += RequestCalibration;
            _subscribedToCalibration = true;
        }

        private void UnsubscribeFromLocalCalibration()
        {
            if (!_subscribedToCalibration) return;

            LocalPlayerCalibration.Submitted -= RequestCalibration;
            _subscribedToCalibration = false;
        }

        /// <summary>
        /// Своя сессия просит сервер принять новую калибровку и сразу показывает её на своём аватаре.
        /// Предсказание нормализуется тем же правилом, что и на сервере, поэтому совпадает с ответом,
        /// кроме отказа (бой) — тогда ответ сервера откатывает аватар к его значению.
        /// </summary>
        public void RequestCalibration(PlayerCalibration requested)
        {
            if (!isLocalPlayer)
            {
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: запрос калибровки не от своей сессии — отброшен.");
                return;
            }

            if (!PlayerCalibrationRules.TryNormalize(requested, out PlayerCalibration predicted))
            {
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: замер калибровки нечисловой — не отправлен.");
                return;
            }

            bool placementChanged = predicted.Placement != EffectiveCalibration.Placement;
            _placementPredictionPending |= placementChanged;
            _predictedCalibration = predicted;
            _sentCalibrationRequest++;
            ApplyCalibrationToAvatar(applyPlacement: placementChanged);

            CmdRequestCalibration(requested, _sentCalibrationRequest);
        }

        [Command]
        private void CmdRequestCalibration(PlayerCalibration requested, int request)
        {
            ServerAcceptCalibration(requested, CalibrationOrigin.Player, request);
        }

        /// <summary>
        /// <b>Единственный писатель</b> калибровки игрока. Все правила — нормализация, отказ
        /// нечисловому, запрет в бою — здесь, через <see cref="PlayerCalibrationRules" />.
        ///
        /// <para>
        /// Без атрибута <c>[Server]</c> по той же причине, что <see cref="ServerSetReady" />: метод
        /// гоняется EditMode-тестами без поднятого сервера, заглушка Mirror съела бы вызов. Игровые
        /// вызывающие — серверные: <c>CmdRequestCalibration</c> и <c>PlayersManager</c> при подключении.
        /// </para>
        /// </summary>
        /// <param name="requested">Что прислал клиент (или что восстановлено из снимка).</param>
        /// <param name="origin">Подключение — без запрета в бою; запрос игрока — с ним.</param>
        /// <param name="request">Номер запроса клиента, на который это ответ; 0 — не ответ на запрос.</param>
        /// <returns><c>true</c> — значение принято (возможно, обрезанным).</returns>
        public bool ServerAcceptCalibration(PlayerCalibration requested, CalibrationOrigin origin, int request = 0)
        {
            PlayerPlacement previousPlacement = _calibration.Placement;
            // Наблюдение не может подменить пол/рост/признак и не отвечает на запрос клиента.
            if (origin == CalibrationOrigin.ObservedPlacement)
            {
                requested = _calibration.WithPlacement(requested.Placement);
                request = 0;
            }
            bool accepted = PlayerCalibrationRules.TryNormalize(requested, out PlayerCalibration normalized);

            if (!accepted)
            {
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: калибровка нечисловая — отклонена.");
            }
            else if (origin == CalibrationOrigin.Player && normalized != _calibration && IsCalibrationLockedByCombat())
            {
                accepted = false;
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: изменение калибровки отклонено — идёт бой.");
            }

            if (accepted)
            {
                if (normalized != requested)
                {
                    GameLog.PhysicalSpace.Warning(
                        $"[PlayerSession] {PlayerName}: калибровка вне границ (пол ±{PlayerCalibrationRules.MaxFloorOffset:F1} м, " +
                        $"рост {PlayerCalibrationRules.MinEyeHeight:F1}–{PlayerCalibrationRules.MaxEyeHeight:F1} м): {requested} → {normalized}.");
                }

                _calibration = normalized;
                GameLog.PhysicalSpace.Info($"[PlayerSession] {PlayerName}: калибровка принята ({origin}) — {normalized}.");
            }

            if (origin == CalibrationOrigin.Connect) _initialCalibrationReceived = true;

            // Ответ — и на принятый, и на отклонённый запрос: клиент снимает предсказание.
            if (request > _answeredCalibrationRequest) _answeredCalibrationRequest = request;

            // На выделенном сервере хук SyncVar не вызывается, а попадания считает он: применяем сами.
            ApplyCalibrationToAvatar(applyPlacement: accepted && origin == CalibrationOrigin.Player &&
                                                     _calibration.Placement != previousPlacement);
            return accepted;
        }

        /// <summary>Единый захват для смены тела/карты/отключения; принятая привязка защищена от задержки NT.</summary>
        public bool ServerCapturePlacement(Vector3 position, Quaternion rotation, string reason, bool preferAccepted = true)
        {
            if (preferAccepted && IsCalibrated && _calibration.Placement.IsAnchored) return true;
            string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!PlayerPlacement.TryCapture(position, rotation, IsCalibrated, map, out PlayerPlacement place, out string diagnosis))
            {
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: место не снято ({reason}): {diagnosis}.");
                return false;
            }
            if (!string.IsNullOrEmpty(diagnosis))
                GameLog.PhysicalSpace.Warning($"[PlayerSession] {PlayerName}: {diagnosis} ({reason}).");
            return ServerRememberPlacement(place);
        }

        /// <summary>Фасады передают value type; запись выполняет только ServerAcceptCalibration.</summary>
        public bool ServerRememberPlacement(PlayerPlacement placement) =>
            ServerAcceptCalibration(_calibration.WithPlacement(placement), CalibrationOrigin.ObservedPlacement);

        private bool IsCalibrationLockedByCombat()
        {
            var mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            return PlayerCalibrationRules.IsLockedByCombat(mode, IsEliminated, Role);
        }

        // ── SyncVar Hooks ─────────────────────────────────────────────────────

        private void OnPlayerNameChanged(string oldName, string newName)
        {
            gameObject.name = $"PlayerSession_{newName}";
            GameLog.Debug.Info($"[PlayerSession] {netId} name changed → {newName}");
        }

        /// <summary>
        /// Готовность приехала с сервера. Нужен только клиентской стороне: на выделенном
        /// сервере Mirror хук в сеттере не зовёт, и там сообщение пишет
        /// <see cref="ServerSetReady" />.
        /// </summary>
        private void OnReadyStateChanged(bool oldState, bool newState)
        {
            GameLog.Player.Verbose(
                $"[PlayerSession] {PlayerName}: готовность {(newState ? "объявлена" : "снята")} (реплицировано)");
        }

        private void OnTeamIndexChanged(int oldIndex, int newIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(newIndex);
            GameLog.Debug.Info(
                $"[PlayerSession] {PlayerName} команда изменена → {(team != null ? team.Name : "нет")}");
        }

        /// <summary>
        /// Пришёл новый netId аватара. Разрешаем его в ссылку на каждой машине.
        /// Если объекта ещё нет в spawned (порядок доставки спавнов не гарантирован),
        /// связь закроется с другой стороны — из <c>PlayerController.OnStartClient</c>.
        /// </summary>
        private void OnActiveAvatarNetIdChanged(uint oldNetId, uint newNetId)
        {
            ResolveActiveAvatar();
        }

        /// <summary>
        /// Сервер изменил калибровку. Ставим её аватару на этой машине. Если аватара ещё нет —
        /// значение применит <see cref="LinkAvatar" />, когда аватар появится: порядок «SyncVar
        /// приехал / аватар заспавнился» не определён, а применение абсолютное.
        /// </summary>
        private void OnCalibrationChanged(PlayerCalibration oldValue, PlayerCalibration newValue)
        {
            ApplyCalibrationToAvatar();
        }

        /// <summary>Сервер ответил на запрос своей сессии: предсказание снимается или откатывается.</summary>
        private void OnCalibrationAnswered(int oldRequest, int newRequest)
        {
            bool settled = newRequest >= _sentCalibrationRequest;
            bool apply = settled && _placementPredictionPending;
            bool acceptedPlacement = _predictedCalibration.Placement == _calibration.Placement;
            // Предсказание/откат не порождают сетевого SDK-события. Иначе observer серверного
            // replay успел бы принять предсказанную позу раньше проверки запрета калибровки в бою.
            ApplyCalibrationToAvatar(applyPlacement: apply, synchronizePlacement: apply && acceptedPlacement);
            if (settled) _placementPredictionPending = false;
        }

        /// <summary>
        /// Ставит <see cref="EffectiveCalibration" /> текущему аватару через его
        /// <see cref="AvatarCalibrationApplier" />. Зовётся отовсюду, где значение или аватар
        /// могли измениться (связь, хуки, запрос, решение сервера), — применение идемпотентно.
        ///
        /// <para>
        /// Свой аватар — аватар своей сессии (<c>isLocalPlayer</c>), а не <c>UxrAvatar.LocalAvatar</c>:
        /// при смене аватара тот ещё указывает на старый, и на этом держался B1.
        /// </para>
        /// </summary>
        private void ApplyCalibrationToAvatar(bool applyPlacement = false, bool synchronizePlacement = false)
        {
            // Своя сессия без неотвеченного запроса знает решение сервера — машина запоминает его
            // для следующего подключения (в том числе откат отклонённого замера).
            if (isLocalPlayer && _sentCalibrationRequest <= _answeredCalibrationRequest)
                LocalPlayerCalibration.Adopt(_calibration);

            if (_activeAvatar == null) return;

            var uxrAvatar = _activeAvatar.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (uxrAvatar == null) return;

            AvatarCalibrationApplier.For(uxrAvatar).Apply(EffectiveCalibration, ownAvatar: isLocalPlayer,
                applyPlacement: applyPlacement, synchronizePlacement: synchronizePlacement);
        }

        // ── Разрешение связи ──────────────────────────────────────────────────

        /// <summary>Ищет аватар по <see cref="ActiveAvatarNetId"/> и обновляет кэш.</summary>
        private void ResolveActiveAvatar()
        {
            if (ActiveAvatarNetId == 0)
            {
                LinkAvatar(null);
                return;
            }

            NetworkIdentity identity = Mirror.Utils.GetSpawnedInServerOrClient(ActiveAvatarNetId);
            LinkAvatar(identity != null ? identity.GetComponent<PlayerController>() : null);
        }

        /// <summary>
        /// Ставит ссылку с обеих сторон и оповещает подписчиков, если это локальный игрок.
        /// Идемпотентен: повторный вызов с той же ссылкой ничего не делает.
        /// </summary>
        private void LinkAvatar(PlayerController avatar)
        {
            // Именно ReferenceEquals, а не ==: уничтоженный аватар по-Unity равен null,
            // и переход «был аватар → его больше нет» иначе остался бы незамеченным.
            if (ReferenceEquals(_activeAvatar, avatar)) return;

            _activeAvatar = avatar;

            if (avatar != null) avatar.LinkSession(this);

            // Аватар пересоздаётся при смене скина, команды и карты, а калибровка игрока живёт
            // в сессии и переживает это. Новый аватар получает её от базы своего префаба.
            ApplyCalibrationToAvatar();

            if (LocalSession == this) LocalAvatarChanged?.Invoke(avatar);
        }

        /// <summary>
        /// Аватар сообщает, что он заспавнился. Закрывает гонку «netId приехал раньше объекта».
        /// Чужой аватар игнорируется: источник правды о связи — <see cref="ActiveAvatarNetId"/>,
        /// который ставит только сервер.
        /// </summary>
        internal void NotifyAvatarSpawned(PlayerController avatar)
        {
            if (avatar == null || avatar.netId != ActiveAvatarNetId) return;
            LinkAvatar(avatar);
        }

        // ── Жизнь игрока ──────────────────────────────────────────────────────

        /// <summary>
        /// Игрок выбыл (погиб или выбыл без смерти) и ждёт возрождения. Состояние игрока, а не тела:
        /// аватар сменный (смена скина, призрак на смерти — T-35), и новый аватар выбывшего не
        /// оживает. <c>SyncVar</c> — поздний клиент получает его в снимке спавна, а не событием
        /// (класс ошибки T-34). Здоровье живого — по-прежнему у тела (<c>UxrActor.Life</c>).
        /// Пишут только <see cref="PlayerController.Die"/>, <see cref="PlayerController.ServerEliminateSilently"/>,
        /// <see cref="PlayerController.Respawn"/> и новый аватар без прошлого (<c>AvatarManager</c>).
        /// </summary>
        [SyncVar] private bool _isEliminated;

        public bool IsEliminated => _isEliminated;

        /// <summary>
        /// Кто ранил игрока с последнего возрождения — для зачёта убийства и ассистов. Только
        /// сервер. Живёт на сессии, чтобы пережить смену аватара.
        /// </summary>
        internal DamageLedger DamageLedger { get; } = new DamageLedger();

        [Server]
        internal void ServerSetEliminated(bool eliminated)
        {
            _isEliminated = eliminated;
        }

        // ── Клиентские команды ────────────────────────────────────────────────

        [Command]
        public void CmdRequestTeamChange(int newTeamId, int newAvatarId)
        {
            if (MapReferee.Instance != null)
            {
                GameLog.Player.Info($"[PlayerSession] {PlayerName}: Клиент запросил смену команды на {newTeamId} и скина на {newAvatarId}");
                TeamChangeRequests.ServerPlayerRequest(MapReferee.Instance.ActiveGameMode, this, newTeamId, newAvatarId);
            }
            else if (AvatarManager.Instance != null)
            {
                // Если матч не идет, меняем напрямую
                AvatarManager.Instance.ChangeAvatar(connectionToClient, this, newTeamId, newAvatarId);
            }
        }

        /// <summary>
        /// Админ (эта сессия) выдаёт команду игроку <paramref name="targetSessionNetId"/>.
        /// Право проверяет сервер (<c>SessionPermissions.IsAdmin</c>) — экран админа на клиенте
        /// только прячет кнопки.
        /// </summary>
        [Command]
        public void CmdAdminAssignTeam(uint targetSessionNetId, int teamId)
        {
            if (MapReferee.Instance == null) return;

            PlayerSession target = NetworkServer.spawned.TryGetValue(targetSessionNetId, out NetworkIdentity identity)
                ? identity.GetComponent<PlayerSession>()
                : null;

            TeamChangeRequests.ServerAdminAssign(MapReferee.Instance.ActiveGameMode, this, target, teamId);
        }

        /// <summary>
        /// Админ (эта сессия) задаёт название команды на серию; пустое — вернуть имя по умолчанию.
        /// Право и чистку ввода — сервер (<c>AdminNaming</c>).
        /// </summary>
        [Command]
        public void CmdAdminRenameTeam(int teamIndex, string name)
        {
            AdminNaming.ServerRenameTeam(this, teamIndex, name);
        }

        /// <summary>Админ (эта сессия) даёт ник игроку <paramref name="targetSessionNetId"/>.</summary>
        [Command]
        public void CmdAdminRenamePlayer(uint targetSessionNetId, string name)
        {
            PlayerSession target = NetworkServer.spawned.TryGetValue(targetSessionNetId, out NetworkIdentity identity)
                ? identity.GetComponent<PlayerSession>()
                : null;

            AdminNaming.ServerRenamePlayer(this, target, name);
        }

        /// <summary>Админ (эта сессия) разово раскладывает игроков без команды автобалансом.</summary>
        [Command]
        public void CmdAdminAutoBalance()
        {
            if (MapReferee.Instance != null)
                TeamChangeRequests.ServerAdminAutoBalance(MapReferee.Instance.ActiveGameMode, this);
        }

        /// <summary>
        /// Админ (эта сессия) жмёт кнопку управления матчем: «Начать матч», «Пауза»,
        /// «Продолжить», «Стоп». Право и уместность проверяет сервер (<c>AdminMapCommands</c>).
        /// </summary>
        [Command]
        public void CmdAdminMapCommand(MapCommand command)
        {
            AdminMapCommands.ServerExecute(this, command);
        }

        /// <summary>
        /// Админ (эта сессия) запускает серию из очереди карт меню выбора сессии.
        /// Право проверяет сервер (<c>AdminMapCommands.ServerStartSeries</c>).
        /// </summary>
        [Command]
        public void CmdAdminStartSeries(string modeId, string[] maps)
        {
            AdminMapCommands.ServerStartSeries(this, modeId, maps);
        }

        [Command]
        public void CmdSetDogTagGrabbed(bool state)
        {
            HasGrabbedDogTag = state;
            GameLog.Player.Verbose($"[PlayerSession] {PlayerName} dog tag grabbed set to {state}");
        }

        /// <summary>
        /// Игрок целиком вошёл в зону спавна команды <paramref name="zoneTeamIndex" />.
        /// Зовёт <see cref="Maps.TeamSpawnZone" /> на сервере.
        ///
        /// <para>
        /// Зона сообщает <b>чья она</b>, а не «свой это игрок или чужой»: решение
        /// принимается здесь, потому что здесь же лежит команда игрока. Раньше зона
        /// решала сама и решала неверно — писала «в зоне» любому вошедшему (RDY-04).
        /// </para>
        ///
        /// <para>
        /// Готовность вход не трогает: её объявляет игрок, а не место, где он стоит.
        /// </para>
        ///
        /// <para>
        /// Без атрибута <c>[Server]</c> — по той же причине, что и
        /// <see cref="ServerSetReady" />: методы гоняются EditMode-тестами без поднятого
        /// сервера Mirror, и заглушка съела бы вызов. Право записи и так серверное:
        /// единственный игровой вызывающий — <see cref="Maps.TeamSpawnZone" /> под
        /// проверкой <c>NetworkServer.active</c>.
        /// </para>
        /// </summary>
        /// <param name="zoneTeamIndex">Индекс команды, которой принадлежит зона.</param>
        public void ServerEnterSpawnZone(int zoneTeamIndex)
        {
            if (zoneTeamIndex == NoSpawnZone) return;
            if (SpawnZoneTeamIndex == zoneTeamIndex) return;

            bool wasInOwn = IsInSpawnZone;
            SpawnZoneTeamIndex = zoneTeamIndex;

            // Переход «своя зона → чужая» без промежуточного выхода: возможен, когда
            // игрока переносит спавн (смена команды, респавн), а не собственные ноги.
            if (wasInOwn && !IsInSpawnZone && _readyState)
                ServerSetReady(false, "игрок оказался в зоне спавна чужой команды");
        }

        /// <summary>
        /// Игрок покинул зону спавна команды <paramref name="zoneTeamIndex" />.
        ///
        /// <para>
        /// Запись снимается <b>только своя</b>. Иначе выход из чужой зоны затирал бы
        /// признак игроку, который в этот момент уже стоит в своей: при переносе между
        /// базами «вошёл в новую» приходит раньше, чем «вышел из старой», и порядок
        /// событий Unity не гарантирует (RDY-04).
        /// </para>
        ///
        /// <para>
        /// Выход из <b>своей</b> зоны снимает готовность — в этом и состоит роль зоны как
        /// <b>условия</b>: объявить «я готов» можно жестом, но стоять при этом
        /// полагается у себя на спавне (T-29).
        /// </para>
        /// </summary>
        /// <param name="zoneTeamIndex">Индекс команды, которой принадлежит зона.</param>
        public void ServerExitSpawnZone(int zoneTeamIndex)
        {
            if (SpawnZoneTeamIndex != zoneTeamIndex) return;

            bool wasInOwn = IsInSpawnZone;
            SpawnZoneTeamIndex = NoSpawnZone;

            if (wasInOwn && _readyState)
                ServerSetReady(false, "игрок вышел из зоны спавна");
        }

        /// <summary>
        /// Единственная точка записи готовности. Всё, что меняет готовность, — жест
        /// с жетоном, отмена игроком, выход из зоны, предел ожидания, начало нового
        /// раунда — проходит здесь и оставляет в логе причину.
        ///
        /// Без атрибута <c>[Server]</c> намеренно: метод зовётся в том числе
        /// из <see cref="GameModes.RoundReadiness" />, а тот гоняется EditMode-тестами
        /// без поднятого сервера Mirror — заглушка Mirror съела бы вызов, и тест зеленел
        /// бы впустую. Право записи здесь и так серверное: клиенту принадлежит только
        /// <see cref="CmdSetReady" />.
        /// </summary>
        /// <param name="ready">Новое состояние готовности.</param>
        /// <param name="reason">Почему готовность изменилась — уходит в лог как есть.</param>
        public void ServerSetReady(bool ready, string reason = null)
        {
            if (_readyState == ready) return;

            _readyState = ready;

            GameLog.Player.Info(
                $"[PlayerSession] {PlayerName}: готовность {(ready ? "объявлена" : "снята")}" +
                (string.IsNullOrEmpty(reason) ? "." : $" — {reason}."));
        }

        /// <summary>
        /// Намерение игрока: «я закончил» либо «я передумал». Отмена возможна всё то
        /// время, пока раунд ждёт готовности; как только пошёл обратный отсчёт, менять
        /// уже нечего — фаза сменилась, и решение принято.
        ///
        /// Нахождения в зоне спавна команда не требует. Объявить готовность игрок может
        /// откуда угодно, но <see cref="ServerExitSpawnZone" /> тут же снимет её, если
        /// он не у себя на спавне, — так зона остаётся условием, не превращаясь
        /// в ловушку: жетон висит на стене арсенала, и отказ принять жест «потому что
        /// ты стоишь на шаг в стороне» оставил бы игрока без второй попытки —
        /// жетон берут один раз за фазу.
        /// </summary>
        [Command]
        public void CmdSetReady(bool ready)
        {
            ServerSetReady(ready, ready ? "игрок объявил готовность" : "игрок отменил готовность");
        }

        /// <summary>
        /// Сбрасывает готовность к началу нового раунда: каждый раунд её объявляют
        /// заново. Жест тоже сбрасывается — жетон в открывшемся арсенале снова на месте.
        ///
        /// Раньше метод существовал, но его никто не звал: готовность прошлого раунда
        /// доживала до следующего, и фаза <c>Equipment</c> второго раунда кончалась,
        /// не начавшись (T-29).
        /// </summary>
        public void ServerResetRoundReadiness()
        {
            HasGrabbedDogTag = false;
            ServerSetReady(false, "начался новый раунд");
        }
    }
}
