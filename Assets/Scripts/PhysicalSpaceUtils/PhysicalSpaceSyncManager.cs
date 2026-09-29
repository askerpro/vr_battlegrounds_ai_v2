using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Locomotion;
using UltimateXR.Extensions.Unity;
using UltimateXR.Extensions.Unity.Math;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Global manager for aligning the visual position of the UxrAvatar 
    /// with the physical tracked space of the player.
    /// 
    /// Inherits from MonoBehaviour as calibration logic is entirely local to the client's physical space.
    /// Virtual position changes are naturally synced by the avatar's NetworkTransform.
    /// </summary>
    [DefaultExecutionOrder(VrBattlegrounds.Managers.ManagerOrder.PhysicalSpaceSyncManager)]
    public class PhysicalSpaceSyncManager : MonoBehaviour
    {
        public static PhysicalSpaceSyncManager Instance { get; private set; }

        [Header("Calibration Data (Persistent)")]
        [SerializeField] private Vector3 _realToVirtualOffset;
        [SerializeField] private Quaternion _realToVirtualRotation = Quaternion.identity;
        [SerializeField] private float _realToVirtualScale = 1f;
        [SerializeField] private float _heightOffset = 0f; // Stores the physical to virtual floor difference
        [SerializeField] private float _accumulatedHeightOffset = 0f; // Tracks the total vertical shift applied
        [SerializeField] private float _accumulatedScaleMultiplier = 1f; // Target scale for player proportions

        // ── Рефлексия во внутренности UltimateXR ─────────────────────────────
        //
        // Калибровка роста читает и правит приватные поля SDK: публичного доступа к ним
        // нет. Зависимость жёсткая и молчаливая — переименованное при обновлении поле
        // не даёт ошибки компиляции, рефлексия просто вернёт null, и рост перестанет
        // калиброваться без единого сообщения. Поэтому каждое обращение идёт через
        // ResolveSdkField, который на ненайденное поле пишет Error.
        //
        // Полный список точек, что сломается и есть ли публичная альтернатива —
        // Docs/UltimateXR/sdk-patches.md, раздел «Зависимости от приватных членов SDK».

        private const System.Reflection.BindingFlags SdkPrivateField =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        /// <summary>
        /// Приватное поле SDK по имени. Отсутствие поля — это сломавшееся обновление
        /// UltimateXR, а не штатная ситуация, поэтому здесь <c>Error</c> с именем поля
        /// и типа: иначе поломка видна только по жалобам на рост, спустя неделю.
        /// </summary>
        private static System.Reflection.FieldInfo ResolveSdkField(Type sdkType, string fieldName)
        {
            System.Reflection.FieldInfo field = sdkType.GetField(fieldName, SdkPrivateField);

            if (field == null)
            {
                GameLog.Error(
                    $"[PhysicalSpaceSyncManager] В типе {sdkType.FullName} больше нет приватного поля " +
                    $"\"{fieldName}\". UltimateXR обновился и переименовал его — калибровка роста " +
                    "работать не будет. См. Docs/UltimateXR/sdk-patches.md, " +
                    "раздел «Зависимости от приватных членов SDK».");
            }

            return field;
        }

        private float ExpectedEyeHeight
        {
            get
            {
                if (UxrAvatar.LocalAvatar != null)
                {
                    var controller = UxrAvatar.LocalAvatar.GetComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>();
                    if (controller != null)
                    {
                        var field = ResolveSdkField(
                            typeof(UltimateXR.Avatar.Controllers.UxrStandardAvatarController), "_bodyIKSettings");

                        if (field != null)
                        {
                            var settings = (UltimateXR.Animation.IK.UxrBodyIKSettings)field.GetValue(controller);
                            if (settings != null) return settings.EyesBaseHeight;
                        }
                    }
                }

                // Запасной рост глаз: аватара ещё нет либо настройки IK не достались.
                return 1.75f;
            }
        }

        /// <summary>
        /// Результат калибровки роста: отношение роста игрока к базовому росту глаз аватара.
        /// Единица означает «не калибровался».
        ///
        /// Значение локальное по происхождению, но не по применению: чужие машины должны
        /// видеть игрока в его пропорциях, иначе коллайдеры разъезжаются с картинкой (VR-01).
        /// Наружу его отдаёт <c>PlayerSession.CalibrationScale</c> — сюда сеть не заходит.
        /// </summary>
        public float AccumulatedScaleMultiplier => _accumulatedScaleMultiplier;

        /// <summary>
        /// Результат калибровки пола: суммарный вертикальный сдвиг пивота камеры, метры.
        /// Ноль означает «пол не калибровался».
        ///
        /// Как и масштаб, значение локальное по происхождению и общее по применению:
        /// без него чужие машины показывают игрока на исходной высоте (VR-08).
        /// Наружу его отдаёт <c>PlayerSession.CalibrationHeightOffset</c>.
        /// </summary>
        public float AccumulatedHeightOffset => _accumulatedHeightOffset;

        public enum HeightCalibrationPhase { None, Floor, PlayerScale }
        public HeightCalibrationPhase CurrentHeightCalibrationPhase { get; private set; } = HeightCalibrationPhase.None;

        [Header("Scene Transition Sync")]
        [Tooltip("If true, the avatar's last known global position and rotation will be reapplied when it respawns in a new scene.")]
        [SerializeField] private bool _preserveAvatarPositionAcrossScenes = true;

        // ── Место игрока, которое переживает смену карты ──────────────────────
        //
        // Здесь лежала МИРОВАЯ поза аватара, и это была находка CAL-02: клиент клал её
        // в GamePlayerConnectMessage, а сервер применял дословно. Тогда арена в TestMap1
        // была повёрнута на 90° относительно TestMap2 и Lobby — одна и та же мировая точка
        // означала на соседней карте другое место арены. Сейчас арены выровнены
        // (MapAlignmentTests), но хранить надо всё равно позицию ОТНОСИТЕЛЬНО
        // ЯКОРЕЙ: они отмечают одни и те же физические метки в комнате, и поза
        // относительно них у карт общая (см. PhysicalSpaceAnchorFrame).
        //
        // Пересчёт делается здесь, а не при отправке сообщения, и это существенно:
        // к моменту отправки клиент уже переехал на карту сервера, старой сцены нет,
        // и переводить мировую позицию было бы не по чему.

        private Vector3? _savedPlacePosition;
        private Quaternion? _savedPlaceRotation;
        private string _savedPlaceMap = string.Empty;

        /// <summary>Система координат якорей той сцены, что сейчас активна. Кэш.</summary>
        private PhysicalSpaceAnchorFrame _sceneFrame;

        /// <summary>Дескриптор сцены, для которой построен <see cref="_sceneFrame" />. Ноль — ни для какой.</summary>
        private int _sceneFrameHandle;

        /// <summary>Когда снова пробовать построить систему координат, если прошлый раз не вышло.</summary>
        private float _nextFrameAttempt;

        /// <summary>
        /// Как часто повторять неудачную попытку построить систему координат якорей,
        /// секунды. Поиск якорей идёт через <c>FindObjectsByType</c>, а спрашивают его
        /// на каждое движение аватара: на сцене без якорей (Offline, меню) без паузы
        /// это был бы полный обход сцены каждый кадр.
        /// </summary>
        private const float FrameRetryPeriod = 1f;

        // Calibration State
        private List<PhysicalSpaceAnchor> _virtualAnchors = new List<PhysicalSpaceAnchor>();
        private Vector3[] _realAnchorPositions = new Vector3[2];
        private int _currentAnchorIndex = 0;
        public bool IsCalibrating { get; private set; } = false;
        public bool IsCalibratingHeight => CurrentHeightCalibrationPhase != HeightCalibrationPhase.None;

        /// <summary>
        /// Калибровка по якорям <b>состоялась</b>: обе точки зарегистрированы и
        /// <see cref="CalculateTransform" /> отработал. В отличие от
        /// <see cref="IsCalibrating" /> (идёт ли процесс прямо сейчас) это факт о прошлом,
        /// и живёт он ровно столько же, сколько сам менеджер, — то есть всю сессию.
        ///
        /// <para>
        /// Отдельный признак нужен потому, что по самим результатам калибровки её факт
        /// не восстанавливается: <see cref="_realToVirtualRotation" /> по умолчанию
        /// <c>identity</c>, <see cref="_realToVirtualScale" /> равен единице, и
        /// «не калибровался» неотличимо от «калибровался и вышло единично».
        /// </para>
        ///
        /// <para>
        /// Кому нужно. Серверу — чтобы выбрать точку спавна: до калибровки игрока можно
        /// ставить куда угодно (зона своей команды), после — его место задано физически,
        /// и двигать его нельзя. Наружу признак отдаёт <c>PlayerSession.IsCalibrated</c>,
        /// сюда сеть не заходит (T-30).
        /// </para>
        /// </summary>
        public bool IsCalibrated { get; private set; }

        // Events
        public event Action CalibrationStarted;
        public event Action CalibrationCancelled;
        public event Action FirstAnchorRegistered;
        public event Action SecondAnchorRegistered;
        public event Action CalibrationCompleted;

        public event Action HeightCalibrationStarted;
        public event Action HeightCalibrationCompleted;

        /// <summary>
        /// Отработал первый шаг калибровки высоты — синхронизация пола, — и
        /// <see cref="AccumulatedHeightOffset" /> изменился.
        ///
        /// Отдельное событие, а не <see cref="HeightCalibrationCompleted" />, потому
        /// что «завершено» поднимается только после второго шага (масштаб). Игрок,
        /// который откалибровал пол и до масштаба не дошёл, иначе не разослал бы
        /// свою высоту вообще.
        /// </summary>
        public event Action FloorHeightCalibrated;

        /// <summary>
        /// Где стоял аватар этой машины в последний раз — <b>в системе координат
        /// якорей</b> той карты, где он стоял.
        ///
        /// <para>
        /// Единственный потребитель — <c>GamePlayerConnectMessage</c>: игрок приносит
        /// своё место с собой, когда подключается к серверу, стоящему уже на другой
        /// карте. Применять его или нет, решает сервер по признаку калибровки
        /// (<c>SpawnPlaceRegistry</c>): до калибровки игра не знает, где игрок
        /// внутри арены, и вправе поставить его в зону команды.
        /// </para>
        /// </summary>
        /// <param name="localPosition">Позиция относительно якорей.</param>
        /// <param name="localRotation">Поворот относительно якорей.</param>
        /// <param name="capturedOnMap">Имя карты, на которой снят замер. Только для лога.</param>
        public bool TryGetSavedAvatarPlace(out Vector3 localPosition, out Quaternion localRotation,
                                           out string capturedOnMap)
        {
            if (_preserveAvatarPositionAcrossScenes && _savedPlacePosition.HasValue && _savedPlaceRotation.HasValue)
            {
                localPosition = _savedPlacePosition.Value;
                localRotation = _savedPlaceRotation.Value;
                capturedOnMap = _savedPlaceMap;
                return true;
            }

            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            capturedOnMap = string.Empty;
            return false;
        }

        /// <summary>
        /// Запоминает мировую позу аватара этой машины в координатах якорей активной сцены.
        ///
        /// <para>
        /// Публичный, потому что это единственный вход в память о месте игрока и его
        /// проверяет EditMode-тест: поднимать ради этого настоящий <c>UxrAvatar</c>
        /// и гонять событие SDK дороже, чем польза. Игровой код зовёт метод из
        /// <c>UxrAvatar.GlobalAvatarMoved</c> и <c>UxrAvatar.LocalAvatarStarted</c>.
        /// </para>
        ///
        /// <para>
        /// Без якорей на сцене замер не делается вовсе — и это правильнее, чем запомнить
        /// мировую позицию «на всякий случай»: непереводимая поза хуже её отсутствия,
        /// потому что молча означает не то место.
        /// </para>
        /// </summary>
        public void RecordLocalAvatarPlace(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!_preserveAvatarPositionAcrossScenes) return;

            PhysicalSpaceAnchorFrame frame;
            if (!TryGetSceneFrame(out frame)) return;

            _savedPlacePosition = frame.ToLocal(worldPosition);
            _savedPlaceRotation = frame.ToLocal(worldRotation);
            _savedPlaceMap = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        /// <summary>
        /// Система координат якорей активной сцены, с кэшем на саму сцену.
        ///
        /// <para>
        /// Кэш нужен из-за частоты вызова: <c>UxrAvatar.GlobalAvatarMoved</c> приходит
        /// на каждое перемещение аватара, а построение системы координат — это
        /// <c>FindObjectsByType</c> по всей сцене. Ключ кэша — дескриптор сцены,
        /// то есть при смене карты кадр строится заново сам.
        /// </para>
        /// </summary>
        private bool TryGetSceneFrame(out PhysicalSpaceAnchorFrame frame)
        {
            int activeHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;

            if (_sceneFrameHandle == activeHandle && _sceneFrame.IsValid)
            {
                frame = _sceneFrame;
                return true;
            }

            // Сцена сменилась — прошлая попытка ничего не говорит о новой.
            if (_sceneFrameHandle != activeHandle)
            {
                _sceneFrameHandle = activeHandle;
                _sceneFrame = default(PhysicalSpaceAnchorFrame);
                _nextFrameAttempt = 0f;
            }

            if (Time.realtimeSinceStartup < _nextFrameAttempt)
            {
                frame = default(PhysicalSpaceAnchorFrame);
                return false;
            }

            string diagnosis;
            if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out _sceneFrame, out diagnosis))
            {
                _nextFrameAttempt = Time.realtimeSinceStartup + FrameRetryPeriod;
                frame = default(PhysicalSpaceAnchorFrame);
                return false;
            }

            frame = _sceneFrame;
            return true;
        }

        /// <summary>
        /// Отсев дубликата — только собственного компонента.
        ///
        /// <para>
        /// Здесь стоял <c>Destroy(gameObject)</c>, и это была находка <b>NET-20</b>:
        /// компонент висит на <b>корне</b> общей ветки менеджеров, поэтому уничтожал
        /// не себя, а всех соседей разом. Отсев дубликата всей ветки — работа
        /// <see cref="Managers.PersistentRoot" />, который делает это в <c>Start</c>,
        /// когда все <c>Awake</c> отработали. Ранний <c>Destroy</c> корня оставлял
        /// выключенным <c>NetworkManager</c>, успевший уйти из ветки своим ходом.
        /// </para>
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // DontDestroyOnLoad обеспечивается родительским PersistentRoot.
            // Вызов DontDestroyOnLoad напрямую вызывает ошибку, если объект не корневой.
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            UxrAvatar.GlobalAvatarMoved += UxrAvatar_GlobalAvatarMoved;
            UxrAvatar.LocalAvatarStarted += UxrAvatar_LocalAvatarStarted;
        }

        private void OnDisable()
        {
            UxrAvatar.GlobalAvatarMoved -= UxrAvatar_GlobalAvatarMoved;
            UxrAvatar.LocalAvatarStarted -= UxrAvatar_LocalAvatarStarted;
        }

        private void Start()
        {
            // Apply if avatar is already present on startup
            if (UxrAvatar.LocalAvatar != null)
            {
                ApplySyncToAvatar();
            }
        }

        private void UxrAvatar_GlobalAvatarMoved(object sender, UxrAvatarMoveEventArgs e)
        {
            UxrAvatar avatar = sender as UxrAvatar;
            // Запоминаем корневые координаты локального аватара — в координатах якорей.
            if (UxrAvatar.LocalAvatar != null && avatar == UxrAvatar.LocalAvatar)
            {
                RecordLocalAvatarPlace(avatar.transform.position, avatar.transform.rotation);
            }
        }

        private void UxrAvatar_LocalAvatarStarted(object sender, UxrAvatarStartedEventArgs e)
        {
            ApplySyncToAvatar();

            // Первый замер — сразу, не дожидаясь перемещения. Иначе игрок, который
            // с момента спавна никуда не телепортировался, не имел бы своего места
            // вовсе: UxrAvatar.GlobalAvatarMoved поднимает UxrManager, то есть
            // телепорт и локомоция, а не шаги по комнате.
            if (UxrAvatar.LocalAvatar != null)
            {
                RecordLocalAvatarPlace(UxrAvatar.LocalAvatar.transform.position,
                                       UxrAvatar.LocalAvatar.transform.rotation);
            }
        }

        /// <summary>
        /// Новый аватар локального игрока появился — вернуть ему то, что задано
        /// калибровкой этой машины.
        ///
        /// <para>
        /// <b>Здесь больше не вызывается <see cref="ApplyAvatarTransform" /></b>, и это
        /// намеренно (T-30). Стояло условие <c>_realToVirtualScale &gt; 0</c>, то есть
        /// «всегда»: масштаб по умолчанию равен единице. Пока игрок не калибровался,
        /// преобразование единично и вызов ничего не делал — но у откалиброванного
        /// это <b>сдвиг</b>, посчитанный один раз в мировых координатах той карты, где
        /// калибровались. Наложить его повторно на каждый новый аватар значит увезти
        /// игрока ещё раз на ту же дельту — при смене скина, при смене карты, при каждом
        /// респавне.
        /// </para>
        ///
        /// <para>
        /// Место откалиброванного игрока после смены карты восстанавливает сервер
        /// (<c>SpawnPlaceRegistry</c>), причём в системе координат якорей новой
        /// карты, — см. <see cref="PhysicalSpaceAnchorFrame" />. Единственный законный
        /// вызов <see cref="ApplyAvatarTransform" /> остался там, где он и должен быть:
        /// в момент самой калибровки (<see cref="RegisterCalibrationPoint" />).
        /// </para>
        /// </summary>
        private void ApplySyncToAvatar()
        {
            if (UxrAvatar.LocalAvatar == null) return;

            // Локальное смещение высоты камеры (калибровка пола) живёт в префабе аватара,
            // а не в мире, поэтому новому экземпляру его нужно наложить заново.
            ApplyAvatarHeight();
        }

        private void Update()
        {
            if (IsCalibratingHeight)
            {
                // Both hands can be used to touch the floor or press button
                if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Left, UxrInputButtons.Button1))
                {
                    ProcessHeightCalibrationStep(UxrHandSide.Left);
                }
                else if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Right, UxrInputButtons.Button1))
                {
                    ProcessHeightCalibrationStep(UxrHandSide.Right);
                }
                return;
            }

            if (!IsCalibrating) return;

            // Wait for input to register anchor points
            if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Left, UxrInputButtons.Button1))
            {
                RegisterCalibrationPoint(UxrAvatar.LocalAvatarInput.GetController3DModel(UxrHandSide.Left).transform.position);
            }
            if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Right, UxrInputButtons.Button1))
            {
                RegisterCalibrationPoint(UxrAvatar.LocalAvatarInput.GetController3DModel(UxrHandSide.Right).transform.position);
            }
        }

        /// <summary>
        /// Begins the visual to physical space synchronization process.
        /// </summary>
        public void BeginCalibration()
        {
            CollectAnchors();

            if (_virtualAnchors.Count < 2)
            {
                GameLog.Error("[PhysicalSpaceSyncManager] Two PhysicalSpaceAnchors are required for calibration. Found: " + _virtualAnchors.Count);
                return;
            }

            _currentAnchorIndex = 0;
            IsCalibrating = true;

            ToggleRealVirtualSpaceRendering();
            UpdateAnchorHighlights();

            CalibrationStarted?.Invoke();
            GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Calibration started. Please proceed to Point 1.");
        }

        /// <summary>
        /// Cancels ongoing calibration process.
        /// </summary>
        public void CancelCalibration()
        {
            IsCalibrating = false;
            _currentAnchorIndex = 0;

            ToggleRealVirtualSpaceRendering();
            ResetAnchorHighlights();

            CalibrationCancelled?.Invoke();
            GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Calibration cancelled.");
        }

        private void CollectAnchors()
        {
            _virtualAnchors = FindObjectsByType<PhysicalSpaceAnchor>(FindObjectsSortMode.None).ToList();
            _virtualAnchors.Sort((a, b) => a.id.CompareTo(b.id));
        }

        private void RegisterCalibrationPoint(Vector3 realControllerPosition)
        {
            if (!IsCalibrating || _currentAnchorIndex > 1) return;

            _realAnchorPositions[_currentAnchorIndex] = realControllerPosition;
            GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Point {_currentAnchorIndex} registered at real space pos: {realControllerPosition}");

            if (_currentAnchorIndex == 0)
            {
                FirstAnchorRegistered?.Invoke();

                _currentAnchorIndex++;
                UpdateAnchorHighlights();

                GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Move to Point 2.");
            }
            else if (_currentAnchorIndex == 1)
            {
                SecondAnchorRegistered?.Invoke();

                CalculateTransform();
                ApplyAvatarTransform();

                // Признак ставится ровно здесь: калибровка состоялась тогда, когда
                // обе точки зарегистрированы и преобразование посчитано.
                IsCalibrated = true;

                IsCalibrating = false;
                ToggleRealVirtualSpaceRendering();
                ResetAnchorHighlights();

                CalibrationCompleted?.Invoke();
                GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Calibration completed. Virtual offset applied.");
            }
        }

        /// <summary>
        /// Begins the height calibration process (Phase 1: Floor).
        /// </summary>
        public void BeginHeightCalibration()
        {
            if (IsCalibrating || IsCalibratingHeight) return; // Don't mix calibrations

            CurrentHeightCalibrationPhase = HeightCalibrationPhase.Floor;
            HeightCalibrationStarted?.Invoke();
            GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Phase 1: Height Calibration started. Please touch the physical floor with a controller and press Button 1.");
        }

        private void ProcessHeightCalibrationStep(UxrHandSide hand)
        {
            if (UxrAvatar.LocalAvatar == null) return;

            if (CurrentHeightCalibrationPhase == HeightCalibrationPhase.Floor)
            {
                // PHASE 1: СИНХРОНИЗАЦИЯ ПОЛА
                Vector3 controllerPos = UxrAvatar.LocalAvatarInput.GetController3DModel(hand).transform.position;
                
                float avatarFloorY = UxrAvatar.LocalAvatar.transform.position.y;
                float deltaY = avatarFloorY - controllerPos.y;

                _heightOffset = deltaY;
                _accumulatedHeightOffset += deltaY;

                ApplyHeightDelta(deltaY);
                FloorHeightCalibrated?.Invoke();

                CurrentHeightCalibrationPhase = HeightCalibrationPhase.PlayerScale;
                GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Phase 1 Floor Registered. Delta: {deltaY}. Phase 2: Stand upright and press Button 1 to calibrate scale.");
            }
            else if (CurrentHeightCalibrationPhase == HeightCalibrationPhase.PlayerScale)
            {
                // PHASE 2: МАСШТАБ ТЕЛА СИНХРОНИЗАЦИЯ
                if (UxrAvatar.LocalAvatar.CameraComponent == null) return;

                // We calculate global height of the headset relative to the avatar's ground level.
                // Using LocalPosition ignores the Phase 1 floor offset (deltaY applied to CameraController).
                float playerRealHeight = UxrAvatar.LocalAvatar.CameraComponent.transform.position.y - UxrAvatar.LocalAvatar.transform.position.y;

                if (playerRealHeight < 0.6f)
                {
                    GameLog.PhysicalSpace.Warning("[PhysicalSpaceSyncManager] HMD is too low. Please stand up in your full height and press Button 1 again.");
                    return;
                }

                _accumulatedScaleMultiplier = playerRealHeight / ExpectedEyeHeight;
                ApplyScale();

                CurrentHeightCalibrationPhase = HeightCalibrationPhase.None;
                HeightCalibrationCompleted?.Invoke();
                
                GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Phase 2 Scale Registered. HMD Height: {playerRealHeight}m. Extents Scale: {_accumulatedScaleMultiplier:F2}");
            }
        }

        private void ApplyHeightDelta(float deltaY)
        {
            if (deltaY == 0f) return;

            // 1. Смещаем системную камеру локально
            ShiftAvatarCameraPivot(UxrAvatar.LocalAvatar, deltaY);

            // 2. Смещаем трекинг рук глобально, изменяя константу в исходниках UXR.
            //    Это про руки своего игрока, поэтому в ShiftAvatarCameraPivot не уехало:
            //    чужому аватару глобальный офсет трекинга не нужен и вреден.
            UltimateXR.Devices.UxrControllerTracking.GlobalHeightOffset = _accumulatedHeightOffset;
        }

        /// <summary>
        /// Сдвигает пивот камеры <b>любого</b> аватара по вертикали — своего или чужого.
        ///
        /// <para>
        /// Вынесено из <see cref="ApplyHeightDelta" /> по той же причине, по которой
        /// в T-14 вынесли <see cref="ApplyScaleToAvatar" />: результат калибровки пола
        /// нужен не только той машине, где калибровались. Тело удалённого аватара
        /// собирает <c>UxrBodyIK</c>, и шею он ставит от мировой позиции камеры
        /// (<c>UxrBodyIK.cs:227</c>), а <c>UxrManager</c> решает IK у <b>всех</b>
        /// аватаров, не только у локального (<c>UxrManager.cs:1901-1908</c>). Значит
        /// не сдвинутый пивот на чужой машине — это не «невидимая камера не там»,
        /// а игрок, стоящий не на своей высоте (находка VR-08).
        /// </para>
        ///
        /// <para>
        /// Сдвиг, а не установка: базовая высота пивота у каждого префаба своя, и
        /// абсолютное значение потребовало бы её знать. Учёт того, сколько уже
        /// наложено, ведёт вызывающая сторона — <c>PlayerSession</c>, у которой
        /// на каждый аватар ровно одна связь и известен момент её появления.
        /// </para>
        /// </summary>
        /// <param name="avatar">Аватар-получатель. <c>null</c> игнорируется молча.</param>
        /// <param name="deltaY">Насколько сдвинуть пивот вверх, метры.</param>
        public static void ShiftAvatarCameraPivot(UxrAvatar avatar, float deltaY)
        {
            if (avatar == null || deltaY == 0f) return;

            Transform cameraController = avatar.CameraController;

            if (cameraController == null)
            {
                GameLog.PhysicalSpace.Warning(
                    $"[PhysicalSpaceSyncManager] У аватара '{avatar.name}' нет пивота камеры — " +
                    "смещение высоты применить некуда.");
                return;
            }

            Vector3 localPos = cameraController.localPosition;
            cameraController.localPosition = new Vector3(localPos.x, localPos.y + deltaY, localPos.z);
        }

        public void ApplyAvatarHeight()
        {
            // При спавне или старте сцены мы должны применить всё накопленное смещение разом,
            // так как префаб аватара создается с нулевыми локальными оффсетами.
            ApplyHeightDelta(_accumulatedHeightOffset);
            ApplyScale();
        }

        private void ApplyScale()
        {
            ApplyScaleToAvatar(UxrAvatar.LocalAvatar, _accumulatedScaleMultiplier);
        }

        /// <summary>
        /// Применяет пропорции игрока к <b>любому</b> аватару — своему или чужому.
        ///
        /// <para>
        /// Метод статический и принимает аватар параметром именно потому, что зовут его
        /// с двух сторон: локально после калибровки (<see cref="ApplyScale" />) и на каждой
        /// машине из хука <c>PlayerSession.CalibrationScale</c>, когда значение приехало
        /// по сети. Раньше масштаб применялся только к <c>UxrAvatar.LocalAvatar</c>, из-за
        /// чего чужие аватары оставались в исходных пропорциях, а коллайдеры расходились
        /// с картинкой — находка VR-01.
        /// </para>
        ///
        /// <para>
        /// Идемпотентен: <c>localScale</c> ставится абсолютным значением, а правка IK идёт
        /// от отношения нового масштаба к старому и на повторном вызове с тем же значением
        /// вырождается в единицу. Поэтому двойное применение (локальный аватар получает
        /// масштаб и из <see cref="ApplyAvatarHeight" />, и из сетевого хука) безопасно.
        /// </para>
        /// </summary>
        /// <param name="avatar">Аватар-получатель. <c>null</c> игнорируется молча: аватар мог ещё не заспавниться.</param>
        /// <param name="scaleMultiplier">Отношение роста игрока к базовому росту глаз аватара.</param>
        public static void ApplyScaleToAvatar(UxrAvatar avatar, float scaleMultiplier)
        {
            if (avatar == null) return;

            var controller = avatar.GetComponent<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>();
            if (controller == null)
            {
                GameLog.PhysicalSpace.Warning("UxrStandardAvatarController not found. Cannot apply scale.");
                return;
            }

            // Масштабируем внутренний скелет (Dummy Forward), а не всё трекинг-пространство UxrAvatar,
            // чтобы у игрока не сломался двуручный хват оружия (рассинхрон расстояний в реале и виаре).
            //
            // "Dummy Forward" создаёт сам SDK в UxrStandardAvatarController.Awake (UxrBodyIK.Initialize),
            // причём независимо от UxrAvatarMode. Поэтому объект есть и на удалённых аватарах,
            // и своего NetworkTransform у него быть не может — он не часть префаба.
            Transform dummyForward = avatar.transform.Find("Dummy Forward");
            float oldScale = 1f;

            if (dummyForward != null)
            {
                oldScale = dummyForward.localScale.x;
                dummyForward.localScale = new Vector3(scaleMultiplier, scaleMultiplier, scaleMultiplier);
            }
            else
            {
                GameLog.PhysicalSpace.Warning("Dummy Forward not found on Avatar. Scale wasn't applied correctly.");
                return;
            }

            // Пересчитываем мировые векторы смещения внутри приватных переменных UxrBodyIK с помощью рефлексии.
            //
            // Правка нужна и удалённому аватару, а не только своему. Здесь раньше стояло
            // обратное утверждение — «UxrManager решает body IK только у аватара с
            // AvatarMode.Local» — и оно неверно: на стадии Animation действительно
            // обновляется только локальный, а вот PostProcess, где и вызывается
            // SolveBodyIK, UxrManager прогоняет по EnabledAvatarControllers, то есть
            // по всем (UxrManager.cs:1901-1908). Отдельно от Animation крутится только
            // UpdateHandPoseTransforms.
            //
            // Поэтому же VR-08 вообще заметна глазом: шею удалённого аватара ставит
            // тот же IK от мировой позиции камеры (UxrBodyIK.cs:227).
            float relativeScale = scaleMultiplier / oldScale;
            if (Mathf.Approximately(relativeScale, 1f)) return;

            var bodyIKField = ResolveSdkField(
                typeof(UltimateXR.Avatar.Controllers.UxrStandardAvatarController), "_bodyIK");
            if (bodyIKField == null) return;

            var bodyIK = bodyIKField.GetValue(controller);
            if (bodyIK == null)
            {
                GameLog.Error("[PhysicalSpaceSyncManager] UxrStandardAvatarController._bodyIK пуст — " +
                              "пересчитать смещения IK не от чего, аватар останется в старых пропорциях.");
                return;
            }

            var type = bodyIK.GetType();

            var forwardPosField = ResolveSdkField(type, "_avatarForwardPosRelativeToNeck");
            if (forwardPosField != null)
            {
                Vector3 val = (Vector3)forwardPosField.GetValue(bodyIK);
                forwardPosField.SetValue(bodyIK, val * relativeScale);
            }

            var neckPosField = ResolveSdkField(type, "_neckPosRelativeToEyes");
            if (neckPosField != null)
            {
                Vector3 val = (Vector3)neckPosField.GetValue(bodyIK);
                neckPosField.SetValue(bodyIK, val * relativeScale);
            }
            
            GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Dynamic IK Scale applied. Relative Scale Delta: {relativeScale}");
        }
        private void CalculateTransform()
        {
            Vector3 virtualA = _virtualAnchors[0].transform.position;
            Vector3 virtualB = _virtualAnchors[1].transform.position;

            Vector3 realA = _realAnchorPositions[0];
            Vector3 realB = _realAnchorPositions[1];

            // Assuming scaling is 1:1, but previously realToVirtualScale was hardcoded to 1
            // We can calculate scale if desired, but 1 is usually best for VR consistency
            _realToVirtualScale = 1f;

            Vector3 dirVirtual = Vector3.Scale(virtualB - virtualA, new Vector3(1, 0, 1)).normalized;
            Vector3 dirReal = Vector3.Scale(realB - realA, new Vector3(1, 0, 1)).normalized;
            _realToVirtualRotation = Quaternion.FromToRotation(dirReal, dirVirtual);

            Vector3 realBInVirtual = _realToVirtualRotation * (realB * _realToVirtualScale);
            _realToVirtualOffset = Vector3.Scale(virtualB - realBInVirtual, new Vector3(1, 1, 1));

            GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Calculated Offset: {_realToVirtualOffset}, Rotation: {_realToVirtualRotation.eulerAngles}");
        }

        /// <summary>
        /// Applies the currently calculated offset and rotation to the local UXR Avatar.
        /// </summary>
        public void ApplyAvatarTransform()
        {
            if (UxrAvatar.LocalAvatar == null) return;

            Vector3 newPosition = TransformRealToVirtual(UxrAvatar.LocalAvatar.transform.position);

            // Высоту оставляем как есть. Калибровка по якорям выравнивает только плоскость пола:
            // в CalculateTransform оба направления сплющены через Scale(1, 0, 1), поэтому поворот
            // чисто вокруг Y, а Y-компонента _realToVirtualOffset определяется случайной высотой,
            // на которой игрок держал контроллер при регистрации точки. Рост калибруется отдельно
            // (BeginHeightCalibration → ApplyHeightDelta).
            // Здесь было Vector3.Scale(newPosition, new Vector3(1, position.y, 1)) — умножение Y
            // на саму себя: при y = 0 высота обнулялась, при y = 2 давала 4.
            newPosition.y = UxrAvatar.LocalAvatar.transform.position.y;

            // Rotate the local avatar's current rotation by the calculated yaw difference
            Quaternion newRotation = UxrAvatar.LocalAvatar.transform.rotation * Quaternion.Euler(0, _realToVirtualRotation.eulerAngles.y, 0);

            UxrManager.Instance.TeleportLocalAvatar(
                newPosition,
                newRotation,
                UxrTranslationType.Immediate);
        }

        private Vector3 TransformRealToVirtual(Vector3 realPosition)
        {
            return _realToVirtualRotation * (realPosition * _realToVirtualScale) + _realToVirtualOffset;
        }

        private void UpdateAnchorHighlights()
        {
            if (_virtualAnchors == null) return;

            for (int i = 0; i < _virtualAnchors.Count; i++)
            {
                if (_virtualAnchors[i] != null)
                {
                    bool shouldHighlight = IsCalibrating && i == _currentAnchorIndex;
                    _virtualAnchors[i].SetHighlighted(shouldHighlight);
                }
            }
        }

        private void ResetAnchorHighlights()
        {
            if (_virtualAnchors == null) return;
            foreach (var anchor in _virtualAnchors)
            {
                if (anchor != null) anchor.SetHighlighted(false);
            }
        }

        private void ToggleRealVirtualSpaceRendering()
        {
            if (UxrAvatar.LocalAvatar?.CameraComponent == null) return;

            // Ensure these layers exist in Project Settings -> Tags and Layers
            int virtualSpaceLayer = LayerMask.NameToLayer("VirtualSpace");
            int realSpaceLayer = LayerMask.NameToLayer("RealSpace");

            if (virtualSpaceLayer == -1 || realSpaceLayer == -1)
            {
                GameLog.PhysicalSpace.Warning("[PhysicalSpaceSyncManager] 'VirtualSpace' or 'RealSpace' layers not found in project settings. Camera culling toggle ignored.");
                return;
            }

            if (IsCalibrating)
            {
                // Show RealSpace, Hide VirtualSpace
                UxrAvatar.LocalAvatar.CameraComponent.cullingMask &= ~(1 << virtualSpaceLayer);
                UxrAvatar.LocalAvatar.CameraComponent.cullingMask |= (1 << realSpaceLayer);
            }
            else
            {
                // Show VirtualSpace, Hide RealSpace
                UxrAvatar.LocalAvatar.CameraComponent.cullingMask &= ~(1 << realSpaceLayer);
                UxrAvatar.LocalAvatar.CameraComponent.cullingMask |= (1 << virtualSpaceLayer);
            }
        }
    }
}
