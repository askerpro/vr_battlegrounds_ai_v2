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
    public class PhysicalSpaceSyncManager : MonoBehaviour
    {
        public static PhysicalSpaceSyncManager Instance { get; private set; }

        [Header("Calibration Data (Persistent)")]
        [SerializeField] private Vector3 _realToVirtualOffset;
        [SerializeField] private Quaternion _realToVirtualRotation = Quaternion.identity;
        [SerializeField] private float _realToVirtualScale = 1f;
        [SerializeField] private float _heightOffset = 0f; // Stores the physical to virtual floor difference
        [SerializeField] private float _accumulatedHeightOffset = 0f; // Tracks the total vertical shift applied

        // Calibration State
        private List<PhysicalSpaceAnchor> _virtualAnchors = new List<PhysicalSpaceAnchor>();
        private Vector3[] _realAnchorPositions = new Vector3[2];
        private int _currentAnchorIndex = 0;
        public bool IsCalibrating { get; private set; } = false;
        public bool IsCalibratingHeight { get; private set; } = false;

        // Events
        public event Action OnCalibrationStarted;
        public event Action OnCalibrationCancelled;
        public event Action OnFirstAnchorRegistered;
        public event Action OnSecondAnchorRegistered;
        public event Action OnCalibrationCompleted;

        public event Action OnHeightCalibrationStarted;
        public event Action OnHeightCalibrationCompleted;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private bool _initialOffsetApplied = false;

        private void Start()
        {
            // Initialization
        }

        private void OnDestroy()
        {
        }

        private void Update()
        {
            if (!_initialOffsetApplied && UxrAvatar.LocalAvatar != null && _realToVirtualScale > 0)
            {
                _initialOffsetApplied = true;
                ApplyAvatarTransform();
                ApplyAvatarHeight();
            }

            if (IsCalibratingHeight)
            {
                // Both hands can be used to touch the floor
                if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Left, UxrInputButtons.Button1))
                {
                    Vector3 controllerPos = UxrAvatar.LocalAvatarInput.GetController3DModel(UxrHandSide.Left).transform.position;
                    RegisterHeightCalibration(controllerPos);
                }
                else if (UxrAvatar.LocalAvatarInput.GetButtonsPressUp(UxrHandSide.Right, UxrInputButtons.Button1))
                {
                    Vector3 controllerPos = UxrAvatar.LocalAvatarInput.GetController3DModel(UxrHandSide.Right).transform.position;
                    RegisterHeightCalibration(controllerPos);
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
        /// Begins the height calibration process. User should place controller on the physical floor.
        /// </summary>
        public void BeginHeightCalibration()
        {
            if (IsCalibrating) return; // Don't mix calibrations

            IsCalibratingHeight = true;
            OnHeightCalibrationStarted?.Invoke();
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, "[PhysicalSpaceSyncManager] Height Calibration started. Please touch the physical floor with a controller and press Button 1.");
        }

        private void RegisterHeightCalibration(Vector3 controllerPos)
        {
            if (!IsCalibratingHeight || UxrAvatar.LocalAvatar == null) return;

            IsCalibratingHeight = false;

            // controllerPos = The world position of the true tracker when touching the physical floor
            // UxrAvatar root Y = The "0" floor of the virtual world.
            // When touching the physical floor, the tracker's World Y *should* be at root.Y.
            // If the physical floor is higher/lower than virtual Y, the tracker's Y will show the difference.
            // We want the virtual Camera Controller to shift by that difference.

            // Насколько контроллер сейчас выше/ниже нужного виртуального пола (который для нас всегда 0 относительно корня аватара)?
            // Так как мы применяем смещения ВНУТРИ аватара (локально), корень аватара всегда остается на Y=0 
            // (или на уровне земли по мнению локомоции). 
            // Значит, идеальный виртуальный пол всегда равен мировой Y-координате корня 아ватарa.
            float avatarFloorY = UxrAvatar.LocalAvatar.transform.position.y;
            
            // Если контроллер по мировой высоте не совпадает с корнем аватара, значит физический пол отличается от виртуального
            float deltaY = avatarFloorY - controllerPos.y;

            _heightOffset = deltaY;
            _accumulatedHeightOffset += deltaY;

            ApplyHeightDelta(deltaY);

            OnHeightCalibrationCompleted?.Invoke();
            GameLog.Info(GameSettings.Instance.LogLevelPhysicalSpace, $"[PhysicalSpaceSyncManager] Height Calibration completed. AvatarFloor: {avatarFloorY}, Delta: {deltaY}, Total accumulated: {_accumulatedHeightOffset}");
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
            newPosition = Vector3.Scale(newPosition, new Vector3(1, UxrAvatar.LocalAvatar.transform.position.y, 1));

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
