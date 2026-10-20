// Ярус C (несколько процессов) — пилот скриптового ввода (контракт tasks/bots-control-modes/contracts/xr-input.md).
// Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Пилот: намерения сценария («иди к точке», «смотри туда», «подведи кисть к точке хвата», «нажми grip») →
    /// кадр устройства, как его дал бы человек со шлемом. Источник ввода для <see cref="E2EScriptedInput"/>.
    ///
    /// <para>
    /// <b>Ходьба — только головой</b> в пространстве трекинга (физическая ходьба): корень аватара не двигается,
    /// скорость не выше <see cref="MaxWalkSpeed"/>, без скачков. Руки идут за головой в позе «опущены».
    /// </para>
    ///
    /// <para>
    /// <b>Кисть к цели.</b> Цель задаётся мировой позой грабера (например, <c>ComputeRequiredGrabberTransform</c> —
    /// только чтение SDK). Пилот переводит её в позу контроллера через смещения «грабер → запястье» (кости аватара) и
    /// «сенсор → запястье» (<see cref="ScriptedControllerTracking.TryGetWristOffset"/>). Хват делает штатный
    /// контроллер SDK по нажатию Grip — пилот ничего в SDK не вызывает.
    /// </para>
    ///
    /// <para>
    /// <b>Замер.</b> В конце кадра UltimateXR (<c>StageUpdated(PostProcess)</c>) сравниваются мировые позы камеры и
    /// запястий пустых рук с тем, что задал кадр: максимумы — в <see cref="Precision"/>.
    /// </para>
    /// </summary>
    public sealed class ScriptedPilot : IAvatarInputSource
    {
        public const float MaxWalkSpeed = 1.2f;
        private const float TurnSpeed = 180f;
        private const float HoldGain = 0.5f;

        /// <summary>Максимальные ошибки поз за прогон (метры и градусы).</summary>
        public struct PrecisionStats
        {
            public float HeadPosition;
            public float HeadAngle;
            public float WristPosition;
            public float WristAngle;
            public int Frames;
            public override string ToString() =>
                $"голова ≤{HeadPosition * 100f:0.###} см / {HeadAngle:0.###}°, запястья ≤{WristPosition * 1000f:0.###} мм / " +
                $"{WristAngle:0.###}°, кадров {Frames}";
        }

        private enum HandMode { Rest, Target, Hold }

        private struct HandState
        {
            public HandMode Mode;
            public Vector3 TargetPosition;   // Target: мировая поза грабера; Hold: точка грабера в системе курса головы
            public Quaternion TargetRotation; // Target: мировой поворот грабера; Hold: команда поворота грабера (курс головы)
            public Transform HeldFrame;      // Hold: ось предмета в руке (обратная связь)
            public Quaternion HeldRotation;  // Hold: желаемый поворот оси предмета в системе курса головы
            public UxrInputButtons Pressed;
            public bool Valid;               // кадр уже содержит позу этой руки
            public Vector3 SensorPosition;   // мировая поза сенсора из последнего кадра — для замера
            public Quaternion SensorRotation;
        }

        private Vector3 _headWorld;          // мировая точка головы над полом (y — пол корня + рост)
        private float _yaw;                  // курс головы, градусы в мире
        private bool _placed;
        private HandState _left;
        private HandState _right;
        private PrecisionStats _precision;
        private bool _measuring;
        private Vector3 _appliedHeadPosition;
        private Quaternion _appliedHeadRotation = Quaternion.identity;
        private bool _applied;
        private Vector3 _trackedHeadPosition;
        private Quaternion _trackedHeadRotation = Quaternion.identity;
        private bool _hasTrackedHead;

        /// <summary>Рост глаз над корнем, м: из калибровки аватара, иначе <see cref="E2EHeadPose.DefaultEyeHeight"/>.</summary>
        public float EyeHeight { get; private set; } = E2EHeadPose.DefaultEyeHeight;

        public PrecisionStats Precision => _precision;

        /// <summary>Мировая точка пола под головой.</summary>
        public Vector3 FloorPosition
        {
            get
            {
                UxrAvatar avatar = E2EScriptedInput.Avatar;
                return avatar != null ? _headWorld - avatar.transform.up * EyeHeight : _headWorld;
            }
        }

        public float Yaw => _yaw;

        public ScriptedPilot()
        {
            E2EScriptedInput.Rebound += OnRebound;
            UxrManager.StageUpdated += OnStageUpdated;
        }

        /// <summary>Отписка; пилот после этого не используется.</summary>
        public void Dispose()
        {
            E2EScriptedInput.Rebound -= OnRebound;
            UxrManager.StageUpdated -= OnStageUpdated;
        }

        public void ResetPrecision() => _precision = default;

        public void SetMeasuring(bool measuring) => _measuring = measuring;

        // ── Намерения ────────────────────────────────────────────────────────

        /// <summary>
        /// Шаг ходьбы к мировой точке пола за <paramref name="deltaTime"/>: курс поворачивается к цели, голова идёт
        /// не быстрее <paramref name="speed"/>. true — дошёл (ближе <paramref name="tolerance"/>).
        /// </summary>
        public bool StepTowards(Vector3 floorTarget, float speed, float deltaTime, float tolerance = 0.05f)
        {
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            if (avatar == null || !_placed) return false;

            Vector3 up = avatar.transform.up;
            Vector3 delta = Vector3.ProjectOnPlane(floorTarget - FloorPosition, up);
            float distance = delta.magnitude;
            if (distance <= tolerance) return true;

            float targetYaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, TurnSpeed * deltaTime);

            float step = Mathf.Min(distance, Mathf.Clamp(speed, 0.05f, MaxWalkSpeed) * deltaTime);
            _headWorld += delta / distance * step;
            return distance - step <= tolerance;
        }

        /// <summary>Поворачивает курс головы к мировому направлению (без тангажа). true — повернул.</summary>
        public bool TurnTowards(Vector3 worldDirection, float deltaTime, float toleranceDeg = 1f)
        {
            Vector3 flat = new Vector3(worldDirection.x, 0f, worldDirection.z);
            if (flat.sqrMagnitude < 1e-6f) return true;

            float targetYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, targetYaw, TurnSpeed * deltaTime);
            return Mathf.Abs(Mathf.DeltaAngle(_yaw, targetYaw)) <= toleranceDeg;
        }

        /// <summary>Кисть к мировой позе грабера (цель задаётся каждый кадр — предмет может двигаться).</summary>
        public void ReachGrabber(UxrHandSide side, Vector3 grabberPosition, Quaternion grabberRotation)
        {
            HandState hand = Hand(side);
            hand.Mode = HandMode.Target;
            hand.TargetPosition = grabberPosition;
            hand.TargetRotation = grabberRotation;
            SetHand(side, hand);
        }

        /// <summary>
        /// Рука несёт предмет, как человек: грабер в точке <paramref name="localPosition"/> относительно головы (глаза,
        /// курс без тангажа), ось предмета <paramref name="frame"/> держится в повороте <paramref name="frameRotation"/>
        /// в той же системе. Поворот грабера доводится по фактической оси предмета: хват SDK может подстраивать кисть
        /// под предмет, и расчёт «кисть = сенсор × смещение» тогда неточен. <paramref name="initialGrabberRotation"/> —
        /// первая команда (система курса головы).
        /// </summary>
        public void HoldObject(UxrHandSide side, Vector3 localPosition, Transform frame, Quaternion frameRotation,
                               Quaternion initialGrabberRotation)
        {
            HandState hand = Hand(side);
            hand.Mode = HandMode.Hold;
            hand.TargetPosition = localPosition;
            hand.TargetRotation = initialGrabberRotation;
            hand.HeldFrame = frame;
            hand.HeldRotation = frameRotation;
            SetHand(side, hand);
        }

        /// <summary>Рука в позу «опущена» и следует за головой.</summary>
        public void Rest(UxrHandSide side)
        {
            HandState hand = Hand(side);
            hand.Mode = HandMode.Rest;
            SetHand(side, hand);
        }

        /// <summary>Нажать или отпустить кнопку контроллера.</summary>
        public void Press(UxrHandSide side, UxrInputButtons button, bool pressed)
        {
            HandState hand = Hand(side);
            hand.Pressed = pressed ? hand.Pressed | button : hand.Pressed & ~button;
            SetHand(side, hand);
        }

        /// <summary>Мировая поза грабера сейчас достигла цели (ближе <paramref name="tolerance"/>).</summary>
        public bool GrabberAtTarget(UxrHandSide side, float tolerance = 0.01f)
        {
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            HandState hand = Hand(side);
            return grabber != null && hand.Mode == HandMode.Target &&
                   (grabber.transform.position - hand.TargetPosition).magnitude <= tolerance;
        }

        // ── Источник ввода ───────────────────────────────────────────────────

        public bool TryRead(ref DeviceFrame frame)
        {
            UxrAvatar avatar = E2EScriptedInput.Avatar;
            Transform space = E2EScriptedInput.TrackingSpace;
            if (avatar == null || space == null) return false;
            if (!_placed) Place(avatar);

            Quaternion headWorldRotation = Quaternion.AngleAxis(_yaw, avatar.transform.up);
            frame.HeadPosition = space.InverseTransformPoint(_headWorld);
            frame.HeadRotation = Quaternion.Inverse(space.rotation) * headWorldRotation;
            // Поза головы в пространстве трекинга — то, что устройство сохранит при смене аватара.
            _trackedHeadPosition = frame.HeadPosition;
            _trackedHeadRotation = frame.HeadRotation;
            _hasTrackedHead = true;
            // Замер в конце кадра сравнивает с применённым кадром: сценарий может сдвинуть цель позже в этом же кадре.
            _appliedHeadPosition = _headWorld;
            _appliedHeadRotation = headWorldRotation;
            _applied = true;

            frame.Left = BuildHand(UxrHandSide.Left, avatar, space, headWorldRotation, ref _left);
            frame.Right = BuildHand(UxrHandSide.Right, avatar, space, headWorldRotation, ref _right);
            return true;
        }

        private DeviceHand BuildHand(UxrHandSide side, UxrAvatar avatar, Transform space, Quaternion headRotation,
                                     ref HandState hand)
        {
            Vector3 sensorPosition;
            Quaternion sensorRotation;

            Vector3 grabberPosition = hand.TargetPosition;
            Quaternion grabberRotation = hand.TargetRotation;
            if (hand.Mode == HandMode.Hold)
            {
                if (hand.HeldFrame != null)
                {
                    // Обратная связь по оси предмета: отклонение фактической позы от желаемой копится в команде.
                    // Коэффициент < 1 — кадр применится в этом же кадре, а ось прочитана по прошлому.
                    Quaternion desired = headRotation * hand.HeldRotation;
                    Quaternion correction = desired * Quaternion.Inverse(hand.HeldFrame.rotation);
                    Quaternion command = headRotation * hand.TargetRotation;
                    command = Quaternion.Slerp(Quaternion.identity, correction, HoldGain) * command;
                    hand.TargetRotation = Quaternion.Inverse(headRotation) * command;
                }

                grabberPosition = _headWorld + headRotation * hand.TargetPosition;
                grabberRotation = headRotation * hand.TargetRotation;
            }

            if (hand.Mode != HandMode.Rest && TrySensorForGrabber(side, avatar, grabberPosition, grabberRotation,
                                                                  out sensorPosition, out sensorRotation))
            {
                // поза посчитана
            }
            else
            {
                DeviceFrame rest = DeviceFrame.Rest(EyeHeight);
                DeviceHand restHand = rest[side];
                Vector3 offset = restHand.Position - rest.HeadPosition;
                sensorPosition = _headWorld + headRotation * offset;
                sensorRotation = headRotation * restHand.Rotation;
            }

            hand.SensorPosition = sensorPosition;
            hand.SensorRotation = sensorRotation;
            hand.Valid = true;

            return new DeviceHand
            {
                Position = space.InverseTransformPoint(sensorPosition),
                Rotation = Quaternion.Inverse(space.rotation) * sensorRotation,
                Pressed = hand.Pressed,
                Trigger1D = -1f,
                Grip1D = -1f
            };
        }

        /// <summary>Мировая поза сенсора, при которой грабер руки встанет в заданную позу.</summary>
        private static bool TrySensorForGrabber(UxrHandSide side, UxrAvatar avatar, Vector3 grabberPosition,
                                                Quaternion grabberRotation, out Vector3 sensorPosition,
                                                out Quaternion sensorRotation)
        {
            sensorPosition = Vector3.zero;
            sensorRotation = Quaternion.identity;

            ScriptedControllerTracking tracking = E2EScriptedInput.Tracking;
            UxrGrabber grabber = avatar.GetGrabber(side);
            Transform wrist = side == UxrHandSide.Left ? avatar.LeftHandBone : avatar.RightHandBone;
            if (tracking == null || grabber == null || wrist == null) return false;
            if (!tracking.TryGetWristOffset(side, out Vector3 wristInSensor, out Quaternion wristInSensorRotation)) return false;

            // Грабер в пространстве запястья: постоянное смещение кости.
            Quaternion wristInverse = Quaternion.Inverse(wrist.rotation);
            Vector3 grabberInWrist = wristInverse * (grabber.transform.position - wrist.position);
            Quaternion grabberInWristRotation = wristInverse * grabber.transform.rotation;

            Quaternion wristRotation = grabberRotation * Quaternion.Inverse(grabberInWristRotation);
            Vector3 wristPosition = grabberPosition - wristRotation * grabberInWrist;

            sensorRotation = wristRotation * Quaternion.Inverse(wristInSensorRotation);
            sensorPosition = wristPosition - sensorRotation * wristInSensor;
            return true;
        }

        // ── Привязка и замер ─────────────────────────────────────────────────

        private void OnRebound(UxrAvatar avatar)
        {
            _placed = false;
            _applied = false;
            if (avatar != null) Place(avatar);
        }

        /// <summary>
        /// Новый аватар. Первый — голова над его корнем, курс по корню. Смена аватара (смерть → призрак, возрождение,
        /// смена команды) — как у шлема: устройство не двигается, поза головы в пространстве трекинга прежняя; где
        /// окажется игрок в мире, решает игра, ставя новое тело. Руки — в прежних режимах.
        /// </summary>
        private void Place(UxrAvatar avatar)
        {
            AvatarCalibrationApplier applier = avatar.GetComponent<AvatarCalibrationApplier>();
            EyeHeight = applier != null && applier.Applied.HasEyeHeight ? applier.Applied.EyeHeight : E2EHeadPose.DefaultEyeHeight;

            Transform space = avatar.CameraController;
            if (_hasTrackedHead && space != null)
            {
                _headWorld = space.TransformPoint(_trackedHeadPosition);
                Vector3 look = Vector3.ProjectOnPlane(space.rotation * _trackedHeadRotation * Vector3.forward, Vector3.up);
                if (look.sqrMagnitude > 1e-6f) _yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                _placed = true;
                return;
            }

            Transform root = avatar.transform;
            _headWorld = root.position + root.up * EyeHeight;
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            _yaw = forward.sqrMagnitude > 1e-6f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
            _left = default;
            _right = default;
            _placed = true;
        }

        private void OnStageUpdated(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.PostProcess || !_measuring) return;

            UxrAvatar avatar = E2EScriptedInput.Avatar;
            Transform camera = avatar != null ? avatar.CameraTransform : null;
            if (camera == null || !_placed || !_applied) return;

            _precision.HeadPosition = Mathf.Max(_precision.HeadPosition, (camera.position - _appliedHeadPosition).magnitude);
            _precision.HeadAngle = Mathf.Max(_precision.HeadAngle, Quaternion.Angle(camera.rotation, _appliedHeadRotation));
            MeasureWrist(UxrHandSide.Left, avatar, _left);
            MeasureWrist(UxrHandSide.Right, avatar, _right);
            _precision.Frames++;
        }

        private void MeasureWrist(UxrHandSide side, UxrAvatar avatar, HandState hand)
        {
            ScriptedControllerTracking tracking = E2EScriptedInput.Tracking;
            UxrGrabber grabber = avatar.GetGrabber(side);
            Transform wrist = side == UxrHandSide.Left ? avatar.LeftHandBone : avatar.RightHandBone;
            // Рука с предметом может следовать за ним (ограничения хвата) — точность меряется только у пустой руки.
            if (!hand.Valid || tracking == null || wrist == null || (grabber != null && grabber.GrabbedObject != null)) return;
            if (!tracking.TryGetWristOffset(side, out Vector3 offset, out Quaternion offsetRotation)) return;

            Vector3 expected = hand.SensorPosition + hand.SensorRotation * offset;
            Quaternion expectedRotation = hand.SensorRotation * offsetRotation;
            _precision.WristPosition = Mathf.Max(_precision.WristPosition, (wrist.position - expected).magnitude);
            _precision.WristAngle = Mathf.Max(_precision.WristAngle, Quaternion.Angle(wrist.rotation, expectedRotation));
        }

        private HandState Hand(UxrHandSide side) => side == UxrHandSide.Left ? _left : _right;

        private void SetHand(UxrHandSide side, HandState hand)
        {
            if (side == UxrHandSide.Left) _left = hand;
            else _right = hand;
        }
    }
}
#endif
