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
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.HUD;

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

        // T-50: результаты калибровки пола и роста здесь больше не хранятся и к аватарам отсюда
        // не применяются. Менеджер — процедура (фазы, замеры, якоря, подсказки); замер уходит
        // в LocalPlayerCalibration.Submit, значение игрока принадлежит PlayerSession, а к аватару
        // его ставит AvatarCalibrationApplier.

        /// <summary>
        /// Калибровка своего игрока, от которой считается новый замер: то, что сейчас стоит на своём
        /// аватаре (с неотвеченным предсказанием), а без сессии — память машины.
        /// </summary>
        private static PlayerCalibration CurrentCalibration =>
            PlayerSession.LocalSession != null ? PlayerSession.LocalSession.EffectiveCalibration : LocalPlayerCalibration.Current;

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

        // Events
        public event Action CalibrationStarted;
        public event Action CalibrationCancelled;
        public event Action FirstAnchorRegistered;
        public event Action SecondAnchorRegistered;
        public event Action CalibrationCompleted;

        public event Action HeightCalibrationStarted;
        public event Action HeightCalibrationCompleted;
        public event Action HeightCalibrationCancelled;

        /// <summary>
        /// Отработал первый шаг калибровки высоты — синхронизация пола; замер пола отправлен.
        ///
        /// Отдельное событие, а не <see cref="HeightCalibrationCompleted" />, потому
        /// что «завершено» поднимается только после второго шага (рост). Нужно подсказкам HUD.
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
            // Калибровку пола и роста новому аватару ставит его применитель по данным сессии (T-50),
            // а не этот обработчик: при смене аватара UxrAvatar.LocalAvatar ещё указывает на старый.

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
            if (!CanChangeCalibrationNow()) return;
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
            if (!CanChangeCalibrationNow())
            {
                CancelCalibration();
                return;
            }

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
                // обе точки зарегистрированы и преобразование посчитано. Решает сервер (T-50).
                LocalPlayerCalibration.Submit(CurrentCalibration.WithCalibrated(true));

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
            if (!CanChangeCalibrationNow()) return;

            CurrentHeightCalibrationPhase = HeightCalibrationPhase.Floor;
            HeightCalibrationStarted?.Invoke();
            GameLog.PhysicalSpace.Info("[PhysicalSpaceSyncManager] Phase 1: Height Calibration started. Please touch the physical floor with a controller and press Button 1.");
        }

        private void ProcessHeightCalibrationStep(UxrHandSide hand)
        {
            if (!CanChangeCalibrationNow())
            {
                CurrentHeightCalibrationPhase = HeightCalibrationPhase.None;
                HeightCalibrationCancelled?.Invoke();
                return;
            }
            if (UxrAvatar.LocalAvatar == null) return;

            if (CurrentHeightCalibrationPhase == HeightCalibrationPhase.Floor)
            {
                // PHASE 1: СИНХРОНИЗАЦИЯ ПОЛА
                Vector3 controllerPos = UxrAvatar.LocalAvatarInput.GetController3DModel(hand).transform.position;
                
                float avatarFloorY = UxrAvatar.LocalAvatar.transform.position.y;
                float deltaY = avatarFloorY - controllerPos.y;

                // Контроллер уже сдвинут текущим полом (смещение рук = пол), поэтому новый пол —
                // текущий плюс невязка. Своя сессия предсказывает его сразу: вторая фаза меряет
                // рост уже от сдвинутого пивота камеры.
                PlayerCalibration current = CurrentCalibration;
                LocalPlayerCalibration.Submit(current.WithFloor(current.FloorOffset + deltaY));
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

                // Рост — в метрах (T-50): масштаб каждая машина считает сама для каждой модели
                // по её EyesBaseHeight, поэтому калибровка на одном аватаре верна и на другом.
                LocalPlayerCalibration.Submit(CurrentCalibration.WithEyeHeight(playerRealHeight));

                CurrentHeightCalibrationPhase = HeightCalibrationPhase.None;
                HeightCalibrationCompleted?.Invoke();

                GameLog.PhysicalSpace.Info($"[PhysicalSpaceSyncManager] Phase 2: рост глаз {playerRealHeight:F3} м.");
            }
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
        /// Разрешает менять физическую калибровку вне боя или для выбывшего игрока.
        /// </summary>
        private static bool CanChangeCalibrationNow()
        {
            var mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            PlayerSession session = PlayerSession.LocalSession;

            // То же правило, что решает сервер (PlayerCalibrationRules): здесь — только чтобы
            // не начинать заведомо отклонённую процедуру.
            if (session == null || !PlayerCalibrationRules.IsLockedByCombat(mode, session.IsEliminated, session.Role)) return true;

            // Изменение координат посреди боя невозможно отличить от прохода через стену.
            GameLog.PhysicalSpace.Warning("[PhysicalSpaceSyncManager] Калибровка отклонена: идёт бой.");
            WatchNotifications.Post("Калибровка доступна вне боя", key: "calibration-combat");
            return false;
        }

        /// <summary>Применяет рассчитанное смещение и поворот к локальному аватару.</summary>
        public void ApplyAvatarTransform()
        {
            if (!CanChangeCalibrationNow()) return;
            if (UxrAvatar.LocalAvatar == null) return;

            Vector3 newPosition = TransformRealToVirtual(UxrAvatar.LocalAvatar.transform.position);

            // Высоту оставляем как есть. Калибровка по якорям выравнивает только плоскость пола:
            // в CalculateTransform оба направления сплющены через Scale(1, 0, 1), поэтому поворот
            // чисто вокруг Y, а Y-компонента _realToVirtualOffset определяется случайной высотой,
            // на которой игрок держал контроллер при регистрации точки. Рост калибруется отдельно
            // (BeginHeightCalibration).
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
