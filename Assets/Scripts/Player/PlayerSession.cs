using Mirror;
using System;
using VrBattlegrounds.Managers;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.PhysicalSpaceUtils;

using VrBattlegrounds.Player.Avatars;
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
        public event Action<PlayerSession> OnSessionReady;

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

        /// <summary>
        /// Пропорции игрока, снятые калибровкой роста: отношение его роста к базовому
        /// росту глаз аватара. Единица — «не калибровался».
        ///
        /// <para>
        /// Живёт в сессии, а не на аватаре, потому что это постоянная характеристика
        /// игрока: она переживает смену скина, команды и карты, а аватар при каждом
        /// из этих событий пересоздаётся.
        /// </para>
        ///
        /// <para>
        /// До T-14 масштаб применялся только локально (<c>PhysicalSpaceSyncManager.ApplyScale</c>),
        /// и на чужих экранах игрок оставался в исходных пропорциях: у <c>Dummy Forward</c>
        /// своего <c>NetworkTransform</c> нет и быть не может — объект создаёт SDK в рантайме, —
        /// а на корневых <c>NetworkTransform</c> аватаров <c>syncScale</c> выключен.
        /// Коллайдеры при этом ехали за костями, то есть расходились прицел и попадание (VR-01).
        /// </para>
        /// </summary>
        [SyncVar(hook = nameof(OnCalibrationScaleChanged))]
        public float CalibrationScale = 1f;

        /// <summary>Нижняя граница пропорций: примерно рост ребёнка при базовых 1.75 м.</summary>
        public const float MinCalibrationScale = 0.5f;

        /// <summary>Верхняя граница пропорций: выше начинается уже не игрок, а способ занять пол-арены.</summary>
        public const float MaxCalibrationScale = 1.5f;

        /// <summary>
        /// Смещение пола, снятое первым шагом калибровки высоты: на столько поднят
        /// пивот камеры относительно префаба. Ноль — «пол не калибровался».
        ///
        /// <para>
        /// Живёт здесь по той же причине, что и <see cref="CalibrationScale" />: это
        /// характеристика физического пространства игрока, она переживает смену скина,
        /// команды и карты, а аватар при каждом из этих событий пересоздаётся.
        /// </para>
        ///
        /// <para>
        /// До этой правки смещение не уезжало никуда (<b>VR-08</b>).
        /// <c>ApplyHeightDelta</c> двигает <c>UxrAvatar.CameraController</c> — родителя
        /// камеры, — а <c>NetworkTransform</c> во всех шести аватарных префабах стоит
        /// на самой <c>Camera</c> и синхронизирует <c>localPosition</c>
        /// (<c>coordinateSpace: Local</c>). Локальная позиция камеры относительно
        /// пивота при калибровке пола не меняется — меняется позиция пивота, а на нём
        /// <c>NetworkTransform</c> нет ни у одного префаба. Итог: на чужих экранах
        /// игрок стоял на исходной высоте.
        /// </para>
        /// </summary>
        [SyncVar(hook = nameof(OnCalibrationHeightOffsetChanged))]
        public float CalibrationHeightOffset;

        /// <summary>
        /// Предел смещения пола по модулю, метры. Значение приходит от клиента и двигает
        /// голову аватара, то есть точку попадания в неё, — поэтому граница жёсткая
        /// и проверяется на сервере. Полтора метра с запасом перекрывают любую разницу
        /// между виртуальным полом и физическим.
        /// </summary>
        public const float MaxCalibrationHeightOffset = 1.5f;

        /// <summary>
        /// Сколько смещения уже наложено на <b>текущий</b> аватар. Нужен, потому что
        /// <see cref="PhysicalSpaceSyncManager.ShiftAvatarCameraPivot" /> сдвигает, а не
        /// ставит: базовая высота пивота у каждого префаба своя. Обнуляется при смене
        /// аватара — новый приходит из префаба, то есть со сдвигом ноль.
        /// </summary>
        private float _appliedHeightOffset;

        // ── Статус готовности (Round State) ───────────────────────────────────
        
        [SyncVar] public bool IsInSpawnZone = false;
        [SyncVar] public bool HasGrabbedDogTag = false;

        public bool IsReadyForRound => IsInSpawnZone && HasGrabbedDogTag;

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
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameLog.Player.Info($"[PlayerSession] {netId} started on server for {PlayerName}.");
            OnSessionReady?.Invoke(this);
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

        // ── Публикация локальной калибровки ───────────────────────────────────

        /// <summary>Подписан ли этот экземпляр на калибровку. Защита от двойной подписки и от отписки чужой сессии.</summary>
        private bool _subscribedToCalibration;

        /// <summary>
        /// Локальный игрок начинает публиковать результат своей калибровки роста.
        ///
        /// Направление зависимости выбрано «сеть → калибровка», а не наоборот:
        /// <see cref="PhysicalSpaceSyncManager" /> по своему контракту ничего не знает
        /// о Mirror (калибровка — процедура физического пространства одной машины),
        /// поэтому подписывается сессия, а не менеджер зовёт команду.
        ///
        /// Текущее значение отправляется сразу: игрок обычно калибруется в лобби,
        /// то есть до того, как сервер создаст ему сессию, и одного события
        /// <c>OnHeightCalibrationCompleted</c> не хватило бы — оно уже прошло.
        /// </summary>
        private void SubscribeToLocalCalibration()
        {
            PhysicalSpaceSyncManager sync = PhysicalSpaceSyncManager.Instance;

            if (sync == null)
            {
                GameLog.Player.Warning(
                    $"[PlayerSession] {PlayerName}: PhysicalSpaceSyncManager.Instance пуст — " +
                    "пропорции игрока не поедут на другие машины.");
                return;
            }

            if (!_subscribedToCalibration)
            {
                sync.OnHeightCalibrationCompleted += PublishLocalCalibration;
                sync.OnFloorHeightCalibrated += PublishLocalCalibration;
                _subscribedToCalibration = true;
            }

            PublishLocalCalibration();
        }

        private void UnsubscribeFromLocalCalibration()
        {
            if (!_subscribedToCalibration) return;

            PhysicalSpaceSyncManager sync = PhysicalSpaceSyncManager.Instance;
            if (sync != null)
            {
                sync.OnHeightCalibrationCompleted -= PublishLocalCalibration;
                sync.OnFloorHeightCalibrated -= PublishLocalCalibration;
            }

            _subscribedToCalibration = false;
        }

        /// <summary>
        /// Отправляет серверу текущий результат калибровки физического пространства:
        /// пропорции игрока и смещение пола. Оба значения снимаются одной процедурой
        /// в два шага, поэтому и уезжают вместе.
        /// </summary>
        private void PublishLocalCalibration()
        {
            PhysicalSpaceSyncManager sync = PhysicalSpaceSyncManager.Instance;
            if (sync == null) return;

            CmdSetCalibrationScale(sync.AccumulatedScaleMultiplier);
            CmdSetCalibrationHeightOffset(sync.AccumulatedHeightOffset);
        }

        // ── SyncVar Hooks ─────────────────────────────────────────────────────

        private void OnPlayerNameChanged(string oldName, string newName)
        {
            gameObject.name = $"PlayerSession_{newName}";
            GameLog.Debug.Info($"[PlayerSession] {netId} name changed → {newName}");
        }

        private void OnTeamIndexChanged(int oldIndex, int newIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(newIndex);
            GameLog.Debug.Info(
                $"[PlayerSession] {PlayerName} команда изменена → {(team != null ? team.displayName : "нет")}");
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
        /// Приехали новые пропорции игрока. Применяем их к аватару на этой машине.
        /// Если аватара ещё нет — молчим: масштаб доложит <see cref="ApplyCalibrationScale" />
        /// из <see cref="LinkAvatar" />, когда аватар заспавнится.
        /// </summary>
        private void OnCalibrationScaleChanged(float oldScale, float newScale)
        {
            ApplyCalibrationScale();
        }

        /// <summary>
        /// Приехало новое смещение пола. Ставим его аватару на этой машине.
        /// Если аватара ещё нет — молчим: смещение доложит <see cref="ApplyCalibrationHeightOffset" />
        /// из <see cref="LinkAvatar" />, когда аватар заспавнится.
        /// </summary>
        private void OnCalibrationHeightOffsetChanged(float oldOffset, float newOffset)
        {
            ApplyCalibrationHeightOffset();
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

            // Новый аватар пришёл из префаба, то есть пивот камеры у него не сдвинут.
            // Счётчик наложенного обязан обнулиться раньше, чем ApplyCalibrationHeightOffset
            // посчитает, сколько досылать.
            _appliedHeightOffset = 0f;

            if (avatar != null) avatar.LinkSession(this);

            // Аватар пересоздаётся при смене скина, команды и карты, а пропорции игрока
            // живут в сессии и переживают это. Досылаем их каждому новому аватару —
            // иначе после первой же смены скина игрок снова стал бы стандартного роста.
            ApplyCalibrationScale();
            ApplyCalibrationHeightOffset();

            if (LocalSession == this) LocalAvatarChanged?.Invoke(avatar);
        }

        /// <summary>
        /// Ставит <see cref="CalibrationScale" /> текущему аватару. Зовётся с двух сторон —
        /// из хука SyncVar (значение приехало) и из <see cref="LinkAvatar" /> (появился аватар), —
        /// потому что порядок этих двух событий не определён: SyncVar может доехать до спавна
        /// аватара и наоборот. Применение идемпотентно, поэтому двойной вызов безопасен.
        /// </summary>
        private void ApplyCalibrationScale()
        {
            if (_activeAvatar == null) return;

            var uxrAvatar = _activeAvatar.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (uxrAvatar == null) return;

            PhysicalSpaceSyncManager.ApplyScaleToAvatar(uxrAvatar, CalibrationScale);
        }

        /// <summary>
        /// Ставит <see cref="CalibrationHeightOffset" /> пивоту камеры текущего аватара.
        /// Зовётся с тех же двух сторон, что и <see cref="ApplyCalibrationScale" />, и
        /// по той же причине: порядок «приехал SyncVar» и «появился аватар» не определён.
        ///
        /// <para>
        /// <b>Свой аватар пропускается.</b> Ему смещение уже наложил
        /// <c>PhysicalSpaceSyncManager.ApplyAvatarHeight</c> при спавне — там же ставится
        /// <c>UxrControllerTracking.GlobalHeightOffset</c>, отвечающий за трекинг
        /// собственных рук. Наложить второй раз значило бы поднять себя вдвое, а
        /// разбирать здесь, «сколько уже сделал менеджер», — завести второго владельца
        /// у одного значения. Владелец локального смещения — менеджер, владелец
        /// удалённого — сессия.
        /// </para>
        /// </summary>
        private void ApplyCalibrationHeightOffset()
        {
            if (_activeAvatar == null) return;

            var uxrAvatar = _activeAvatar.GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (uxrAvatar == null) return;

            if (ReferenceEquals(uxrAvatar, UltimateXR.Avatar.UxrAvatar.LocalAvatar)) return;

            float delta = CalibrationHeightOffset - _appliedHeightOffset;
            if (Mathf.Approximately(delta, 0f)) return;

            PhysicalSpaceSyncManager.ShiftAvatarCameraPivot(uxrAvatar, delta);
            _appliedHeightOffset = CalibrationHeightOffset;
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

        // ── Клиентские команды ────────────────────────────────────────────────

        [Command]
        public void CmdRequestAvatarChange(int newAvatarId)
        {
            if (AvatarManager.Instance != null)
            {
                GameLog.Player.Info($"[PlayerSession] {PlayerName}: Клиент запросил смену скина на ID {newAvatarId}");
                AvatarManager.Instance.ChangeAvatar(connectionToClient, this, this.TeamIndex, newAvatarId);
            }
        }

        [Command]
        public void CmdRequestTeamChange(int newTeamId, int newAvatarId)
        {
            if (GameplayManager.Instance != null)
            {
                GameLog.Player.Info($"[PlayerSession] {PlayerName}: Клиент запросил смену команды на {newTeamId} и скина на {newAvatarId}");
                GameplayManager.Instance.ProcessTeamChangeRequest(this, newTeamId, newAvatarId);
            }
            else if (AvatarManager.Instance != null)
            {
                // Если матч не идет, меняем напрямую
                AvatarManager.Instance.ChangeAvatar(connectionToClient, this, newTeamId, newAvatarId);
            }
        }

        [Command]
        public void CmdSetDogTagGrabbed(bool state)
        {
            HasGrabbedDogTag = state;
            GameLog.Player.Verbose($"[PlayerSession] {PlayerName} dog tag grabbed set to {state}");
        }

        /// <summary>
        /// Клиент сообщает результат своей калибровки роста. Дальше значение расходится
        /// SyncVar-ом, и каждая машина ставит его своему экземпляру аватара.
        ///
        /// Границы жёсткие и проверяются на сервере: значение приходит от клиента,
        /// а масштаб аватара — это ещё и размер коллайдеров, то есть площадь попадания.
        /// Ноль, отрицательное или NaN дополнительно уронили бы матрицы трансформа.
        /// </summary>
        [Command]
        public void CmdSetCalibrationScale(float scale)
        {
            if (!TryNormalizeCalibrationScale(scale, out float normalized))
            {
                GameLog.Player.Warning(
                    $"[PlayerSession] {PlayerName}: пришёл нечисловой масштаб калибровки — запрос отброшен.");
                return;
            }

            if (!Mathf.Approximately(normalized, scale))
            {
                GameLog.Player.Warning(
                    $"[PlayerSession] {PlayerName}: масштаб калибровки {scale:F2} вне границ " +
                    $"[{MinCalibrationScale:F2}; {MaxCalibrationScale:F2}] — обрезан до {normalized:F2}.");
            }

            CalibrationScale = normalized;

            // На выделенном сервере хук SyncVar не вызывается — Mirror зовёт его только
            // в host-режиме, — поэтому применяем здесь же. Для сервера это не косметика:
            // масштаб двигает коллайдеры, а попадания считает именно он.
            ApplyCalibrationScale();

            GameLog.Player.Info(
                $"[PlayerSession] {PlayerName}: пропорции игрока приняты сервером — {normalized:F2}");
        }

        /// <summary>
        /// Приводит присланный клиентом масштаб к допустимому. Вынесен из
        /// <see cref="CmdSetCalibrationScale" /> отдельным чистым методом, потому что тело
        /// <c>[Command]</c> weaver переписывает и напрямую из теста его не вызвать.
        /// </summary>
        /// <returns><c>false</c>, если значение нечисловое и принимать его нельзя вовсе.</returns>
        public static bool TryNormalizeCalibrationScale(float scale, out float normalized)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale))
            {
                normalized = 1f;
                return false;
            }

            normalized = Mathf.Clamp(scale, MinCalibrationScale, MaxCalibrationScale);
            return true;
        }

        /// <summary>
        /// Клиент сообщает результат калибровки пола. Дальше значение расходится
        /// SyncVar-ом, и каждая машина сдвигает пивот камеры своего экземпляра
        /// этого аватара.
        ///
        /// Границы проверяются на сервере по той же причине, что и у масштаба:
        /// смещение двигает голову аватара, то есть точку попадания в неё.
        /// </summary>
        [Command]
        public void CmdSetCalibrationHeightOffset(float offset)
        {
            if (!TryNormalizeCalibrationHeightOffset(offset, out float normalized))
            {
                GameLog.Player.Warning(
                    $"[PlayerSession] {PlayerName}: пришло нечисловое смещение пола — запрос отброшен.");
                return;
            }

            if (!Mathf.Approximately(normalized, offset))
            {
                GameLog.Player.Warning(
                    $"[PlayerSession] {PlayerName}: смещение пола {offset:F2} м вне границ " +
                    $"±{MaxCalibrationHeightOffset:F2} м — обрезано до {normalized:F2} м.");
            }

            CalibrationHeightOffset = normalized;

            // На выделенном сервере хук SyncVar не вызывается — Mirror зовёт его только
            // в host-режиме, — поэтому применяем здесь же: попадания считает сервер,
            // а смещение двигает голову.
            ApplyCalibrationHeightOffset();

            GameLog.Player.Info(
                $"[PlayerSession] {PlayerName}: смещение пола принято сервером — {normalized:F2} м");
        }

        /// <summary>
        /// Приводит присланное клиентом смещение пола к допустимому. Вынесен отдельным
        /// чистым методом по той же причине, что и <see cref="TryNormalizeCalibrationScale" />:
        /// тело <c>[Command]</c> weaver переписывает и напрямую из теста его не вызвать.
        /// </summary>
        /// <returns><c>false</c>, если значение нечисловое и принимать его нельзя вовсе.</returns>
        public static bool TryNormalizeCalibrationHeightOffset(float offset, out float normalized)
        {
            if (float.IsNaN(offset) || float.IsInfinity(offset))
            {
                normalized = 0f;
                return false;
            }

            normalized = Mathf.Clamp(offset, -MaxCalibrationHeightOffset, MaxCalibrationHeightOffset);
            return true;
        }

        [Server]
        public void ServerSetInSpawnZone(bool state)
        {
            IsInSpawnZone = state;
        }

        [Server]
        public void ServerResetRoundReadiness()
        {
            HasGrabbedDogTag = false;
            IsInSpawnZone = false;
        }
    }
}
