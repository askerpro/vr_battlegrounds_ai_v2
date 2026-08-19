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

        public enum HeightCalibrationPhase { None, Floor, PlayerScale }
        public HeightCalibrationPhase CurrentHeightCalibrationPhase { get; private set; } = HeightCalibrationPhase.None;

        [Header("Scene Transition Sync")]
        [Tooltip("If true, the avatar's last known global position and rotation will be reapplied when it respawns in a new scene.")]
        [SerializeField] private bool _preserveAvatarPositionAcrossScenes = true;
        private Vector3? _lastSavedAvatarPosition = null;
        private Quaternion? _lastSavedAvatarRotation = null;

        // Calibration State
        private List<PhysicalSpaceAnchor> _virtualAnchors = new List<PhysicalSpaceAnchor>();
        private Vector3[] _realAnchorPositions = new Vector3[2];
        private int _currentAnchorIndex = 0;
        public bool IsCalibrating { get; private set; } = false;
        public bool IsCalibratingHeight => CurrentHeightCalibrationPhase != HeightCalibrationPhase.None;

        // Events
        public event Action OnCalibrationStarted;
        public event Action OnCalibrationCancelled;
        public event Action OnFirstAnchorRegistered;
        public event Action OnSecondAnchorRegistered;
        public event Action OnCalibrationCompleted;

        public event Action OnHeightCalibrationStarted;
        public event Action OnHeightCalibrationCompleted;

        public bool TryGetSavedAvatarTransform(out Vector3 position, out Quaternion rotation)
        {
            if (_preserveAvatarPositionAcrossScenes && _lastSavedAvatarPosition.HasValue && _lastSavedAvatarRotation.HasValue)
            {
                position = _lastSavedAvatarPosition.Value;
                rotation = _lastSavedAvatarRotation.Value;
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // DontDestroyOnLoad обеспечивается родительским PersistentRoot.
            // Вызов DontDestroyOnLoad напрямую вызывает ошибку, если объект не корневой.
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
            // Сохраняем корневые координаты (position and rotation) локального аватара
            if (_preserveAvatarPositionAcrossScenes && UxrAvatar.LocalAvatar != null && avatar == UxrAvatar.LocalAvatar)
            {
                _lastSavedAvatarPosition = avatar.transform.position;
                _lastSavedAvatarRotation = avatar.transform.rotation;
            }
        }

        private void UxrAvatar_LocalAvatarStarted(object sender, UxrAvatarStartedEventArgs e)
        {
            ApplySyncToAvatar();
        }

        private void ApplySyncToAvatar()
        {
            if (UxrAvatar.LocalAvatar == null) return;

            // Если была проведена калибровка комнаты по физическим якорям - она в приоритете
            if (_realToVirtualScale > 0)
            {
                ApplyAvatarTransform();
            }

            // Мировая позиция между сценами теперь восстанавливается сетью (GameNetworkManager) при спавне.
            // Нам остается только восстановить локальное смещение высоты камеры (калибровку роста).
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

            OnCalibrationStarted?.Invoke();
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Calibration started. Please proceed to Point 1.");
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

            OnCalibrationCancelled?.Invoke();
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Calibration cancelled.");
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
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Point {_currentAnchorIndex} registered at real space pos: {realControllerPosition}");

            if (_currentAnchorIndex == 0)
            {
                OnFirstAnchorRegistered?.Invoke();

                _currentAnchorIndex++;
                UpdateAnchorHighlights();

                GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Move to Point 2.");
            }
            else if (_currentAnchorIndex == 1)
            {
                OnSecondAnchorRegistered?.Invoke();

                CalculateTransform();
                ApplyAvatarTransform();

                IsCalibrating = false;
                ToggleRealVirtualSpaceRendering();
                ResetAnchorHighlights();

                OnCalibrationCompleted?.Invoke();
                GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Calibration completed. Virtual offset applied.");
            }
        }

        /// <summary>
        /// Begins the height calibration process (Phase 1: Floor).
        /// </summary>
        public void BeginHeightCalibration()
        {
            if (IsCalibrating || IsCalibratingHeight) return; // Don't mix calibrations

            CurrentHeightCalibrationPhase = HeightCalibrationPhase.Floor;
            OnHeightCalibrationStarted?.Invoke();
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Phase 1: Height Calibration started. Please touch the physical floor with a controller and press Button 1.");
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

                CurrentHeightCalibrationPhase = HeightCalibrationPhase.PlayerScale;
                GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Phase 1 Floor Registered. Delta: {deltaY}. Phase 2: Stand upright and press Button 1 to calibrate scale.");
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
                    GameLog.Warning(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] HMD is too low. Please stand up in your full height and press Button 1 again.");
                    return;
                }

                _accumulatedScaleMultiplier = playerRealHeight / ExpectedEyeHeight;
                ApplyScale();

                CurrentHeightCalibrationPhase = HeightCalibrationPhase.None;
                OnHeightCalibrationCompleted?.Invoke();
                
                GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Phase 2 Scale Registered. HMD Height: {playerRealHeight}m. Extents Scale: {_accumulatedScaleMultiplier:F2}");
            }
        }

        private void ApplyHeightDelta(float deltaY)
        {
            if (deltaY == 0f) return;

            // 1. Смещаем системную камеру локально
            var cameraController = UxrAvatar.LocalAvatar.CameraController;
            if (cameraController != null)
            {
                var localPos = cameraController.localPosition;
                cameraController.localPosition = new Vector3(localPos.x, localPos.y + deltaY, localPos.z);
            }

            // 2. Смещаем трекинг рук глобально, изменяя константу в исходниках UXR
            UltimateXR.Devices.UxrControllerTracking.GlobalHeightOffset = _accumulatedHeightOffset;
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
                GameLog.Warning(GameSettings.Instance.LogLevelPhysicalSpace, "UxrStandardAvatarController not found. Cannot apply scale.");
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
                GameLog.Warning(GameSettings.Instance.LogLevelPhysicalSpace, "Dummy Forward not found on Avatar. Scale wasn't applied correctly.");
                return;
            }

            // Пересчитываем мировые векторы смещения внутри приватных переменных UxrBodyIK с помощью рефлексии.
            //
            // Для удалённого аватара (UxrAvatarMode.UpdateExternally) эта правка холостая:
            // UxrManager решает body IK только у аватара с AvatarMode.Local, остальным крутит
            // лишь позы кистей. Видимый размер там задаёт один localScale выше. Правку всё равно
            // делаем безусловно, а не по AvatarMode: режим аватару проставляет UxrMirrorAvatar,
            // и ветвление по нему привязало бы масштаб к порядку инициализации сети. Холостая
            // запись в два Vector3 дешевле такой зависимости.
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
            
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Dynamic IK Scale applied. Relative Scale Delta: {relativeScale}");
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

            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Calculated Offset: {_realToVirtualOffset}, Rotation: {_realToVirtualRotation.eulerAngles}");
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
                GameLog.Warning(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] 'VirtualSpace' or 'RealSpace' layers not found in project settings. Camera culling toggle ignored.");
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
