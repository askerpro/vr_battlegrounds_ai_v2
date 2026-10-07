using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
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

        /// <summary>Проекция памяти своего игрока; отдельного сохранённого места здесь нет.</summary>
        public bool TryGetSavedAvatarPlace(out Vector3 localPosition, out Quaternion localRotation,
                                           out string capturedOnMap)
        {
            PlayerPlacement placement = LocalPlayerCalibration.Current.Placement;
            localPosition = placement.IsAnchored ? placement.Position : Vector3.zero;
            localRotation = placement.IsAnchored ? placement.Rotation : Quaternion.identity;
            capturedOnMap = placement.IsAnchored ? placement.CapturedOnMap : string.Empty;
            return placement.IsAnchored;
        }

        /// <summary>Совместимый вход записи снимка в память машины.</summary>
        public void RecordLocalAvatarPlace(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out var frame, out _)) return;
            LocalPlayerCalibration.RecordPlacement(PlayerPlacement.Anchored(
                frame.ToLocal(worldPosition), frame.ToLocal(worldRotation),
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name), CurrentCalibration);
        }

        private static UxrAvatar CurrentAvatar => PlayerSession.LocalSession != null
            ? PlayerSession.LocalSession.ActiveAvatar?.GetComponent<UxrAvatar>()
            : UxrAvatar.LocalAvatar;

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

        private void Update()
        {
            if (CurrentAvatar == null || CurrentAvatar.ControllerInput == null) return;
            if (IsCalibratingHeight)
            {
                // Both hands can be used to touch the floor or press button
                if (CurrentAvatar.ControllerInput.GetButtonsPressUp(UxrHandSide.Left, UxrInputButtons.Button1))
                {
                    ProcessHeightCalibrationStep(UxrHandSide.Left);
                }
                else if (CurrentAvatar.ControllerInput.GetButtonsPressUp(UxrHandSide.Right, UxrInputButtons.Button1))
                {
                    ProcessHeightCalibrationStep(UxrHandSide.Right);
                }
                return;
            }

            if (!IsCalibrating) return;

            // Wait for input to register anchor points
            if (CurrentAvatar.ControllerInput.GetButtonsPressUp(UxrHandSide.Left, UxrInputButtons.Button1))
            {
                RegisterCalibrationPoint(CurrentAvatar.ControllerInput.GetController3DModel(UxrHandSide.Left).transform.position);
            }
            if (CurrentAvatar.ControllerInput.GetButtonsPressUp(UxrHandSide.Right, UxrInputButtons.Button1))
            {
                RegisterCalibrationPoint(CurrentAvatar.ControllerInput.GetController3DModel(UxrHandSide.Right).transform.position);
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
                if (!ApplyAvatarTransform())
                {
                    CancelCalibration();
                    return;
                }

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
            if (CurrentAvatar == null) return;

            if (CurrentHeightCalibrationPhase == HeightCalibrationPhase.Floor)
            {
                // PHASE 1: СИНХРОНИЗАЦИЯ ПОЛА
                Vector3 controllerPos = CurrentAvatar.ControllerInput.GetController3DModel(hand).transform.position;
                
                float avatarFloorY = CurrentAvatar.transform.position.y;
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
                if (CurrentAvatar.CameraComponent == null) return;

                // We calculate global height of the headset relative to the avatar's ground level.
                // Using LocalPosition ignores the Phase 1 floor offset (deltaY applied to CameraController).
                float playerRealHeight = CurrentAvatar.CameraComponent.transform.position.y - CurrentAvatar.transform.position.y;

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
            _realToVirtualRotation = Quaternion.AngleAxis(Vector3.SignedAngle(dirReal, dirVirtual, Vector3.up), Vector3.up);

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

        /// <summary>Передаёт единый замер места и признака калибровки владельцу игрока.</summary>
        public bool ApplyAvatarTransform()
        {
            UxrAvatar avatar = CurrentAvatar;
            if (!CanChangeCalibrationNow() || avatar == null) return false;
            if (!PhysicalSpaceAnchorFrame.TryBuildFromScene(out var frame, out _)) return false;
            Vector3 position = TransformRealToVirtual(avatar.transform.position);
            position.y = avatar.transform.position.y;
            Quaternion rotation = _realToVirtualRotation * avatar.transform.rotation;
            PlayerCalibration measured = CurrentCalibration.WithCalibrated(true).WithPlacement(
                PlayerPlacement.Anchored(frame.ToLocal(position), frame.ToLocal(rotation),
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name));
            if (!PlayerCalibrationRules.TryNormalize(measured, out measured)) return false;
            LocalPlayerCalibration.Submit(measured);
            return true;
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
            if (CurrentAvatar?.CameraComponent == null) return;

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
                CurrentAvatar.CameraComponent.cullingMask &= ~(1 << virtualSpaceLayer);
                CurrentAvatar.CameraComponent.cullingMask |= (1 << realSpaceLayer);
            }
            else
            {
                // Show VirtualSpace, Hide RealSpace
                CurrentAvatar.CameraComponent.cullingMask &= ~(1 << realSpaceLayer);
                CurrentAvatar.CameraComponent.cullingMask |= (1 << virtualSpaceLayer);
            }
        }
    }
}
