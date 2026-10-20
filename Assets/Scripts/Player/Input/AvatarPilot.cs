// Пилот ввода аватара: намерения → кадр устройства (контракт tasks/bots-input/contracts/bot-input-design.md).
// Компилируется во всех сборках: им пользуются боты на сервере. Перенос ScriptedPilot скриптового клиента сюда —
// после вливания этапа round задачи e2e-combat (до того у клиента остаётся свой пилот с той же логикой).
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Player.Input
{
    /// <summary>
    /// Пилот одного аватара: намерения («иди к точке», «смотри туда», «подведи кисть к точке хвата», «нажми grip/курок»)
    /// → кадр устройства, как его дал бы человек со шлемом. Источник ввода для <see cref="AvatarInputDevice"/>; в SDK
    /// ничего не вызывает (хват и выстрел — код человека по кнопкам).
    ///
    /// <para>
    /// <b>Ходьба — только головой</b> в пространстве трекинга (физическая ходьба): корень аватара не двигается, скорость
    /// не выше <see cref="MaxWalkSpeed"/>, без скачков. Руки в позе «опущены» идут за головой.
    /// </para>
    ///
    /// <para>
    /// <b>Кисть к цели.</b> Цель — мировая поза грабера (например, <c>ComputeRequiredGrabberTransform</c> — только чтение
    /// SDK). Пилот переводит её в позу контроллера через смещения «грабер → запястье» (кости аватара) и
    /// «сенсор → запястье» (<see cref="ScriptedControllerTracking.TryGetWristOffset"/>).
    /// </para>
    ///
    /// <para>
    /// <b>Смена тела.</b> <see cref="Bind"/> с <c>keepHead</c> сохраняет мировую позу головы и курс (физическая голова не
    /// телепортируется при смене тела на той же карте); без него голова ставится над корнем нового тела.
    /// </para>
    /// </summary>
    public sealed class AvatarPilot : IAvatarBoundInputSource
    {
        public const float MaxWalkSpeed = 1.2f;
        public const float DefaultEyeHeight = 1.6f;
        private const float TurnSpeed = 180f;

        private enum HandMode { Rest, Target }

        private struct HandState
        {
            public HandMode Mode;
            public Vector3 TargetPosition;   // мировая поза грабера
            public Quaternion TargetRotation;
            public UxrInputButtons Pressed;
        }

        private UxrAvatar _avatar;
        private Vector3 _headWorld;          // мировая точка головы (пол корня + рост)
        private float _yaw;                  // курс головы, градусы в мире
        private float _pitch;                // тангаж взгляда, градусы (вниз — плюс)
        private bool _placed;
        private HandState _left;
        private HandState _right;

        /// <summary>Аватар пилота; null — не привязан.</summary>
        public UxrAvatar Avatar => _avatar;

        /// <summary>Рост глаз над корнем, м: из калибровки аватара, иначе <see cref="DefaultEyeHeight"/>.</summary>
        public float EyeHeight { get; private set; } = DefaultEyeHeight;

        /// <summary>Мировая точка головы.</summary>
        public Vector3 HeadPosition => _headWorld;

        /// <summary>Мировая точка пола под головой.</summary>
        public Vector3 FloorPosition => _avatar != null ? _headWorld - _avatar.transform.up * EyeHeight : _headWorld;

        public float Yaw => _yaw;

        /// <summary>Мировой поворот головы, который пилот подаёт в этом кадре.</summary>
        public Quaternion HeadRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>
        /// Привязка к телу. <paramref name="keepHead"/> — продолжить с прежней мировой позы головы (новое тело на той же
        /// карте); иначе голова встаёт над корнем, курс — по корню. Руки — «опущены», кнопки отпущены.
        /// </summary>
        public void Bind(UxrAvatar avatar, bool keepHead)
        {
            bool continueHead = keepHead && _placed;
            _avatar = avatar;
            _left = default;
            _right = default;
            if (avatar == null)
            {
                _placed = false;
                return;
            }

            AvatarCalibrationApplier applier = avatar.GetComponent<AvatarCalibrationApplier>();
            EyeHeight = applier != null && applier.Applied.HasEyeHeight ? applier.Applied.EyeHeight : DefaultEyeHeight;
            if (continueHead) return;

            Transform root = avatar.transform;
            _headWorld = root.position + root.up * EyeHeight;
            Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            _yaw = forward.sqrMagnitude > 1e-6f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
            _pitch = 0f;
            _placed = true;
        }

        // ── Намерения ────────────────────────────────────────────────────────

        /// <summary>
        /// Шаг ходьбы к мировой точке пола за <paramref name="deltaTime"/>: курс поворачивается к цели, голова идёт не
        /// быстрее <paramref name="speed"/>. true — дошёл (ближе <paramref name="tolerance"/>).
        /// </summary>
        public bool StepTowards(Vector3 floorTarget, float speed, float deltaTime, float tolerance = 0.05f)
        {
            if (_avatar == null || !_placed) return false;

            Vector3 up = _avatar.transform.up;
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

        /// <summary>Взгляд: тангаж головы к мировой точке (курс не меняется). Без точки — прямо.</summary>
        public void LookAt(Vector3? worldPoint, float deltaTime)
        {
            float target = 0f;
            if (worldPoint.HasValue)
            {
                Vector3 d = worldPoint.Value - _headWorld;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > 1e-3f || Mathf.Abs(d.y) > 1e-3f) target = Mathf.Clamp(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, -60f, 60f);
            }
            _pitch = Mathf.MoveTowardsAngle(_pitch, target, TurnSpeed * deltaTime);
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
            UxrGrabber grabber = _avatar != null ? _avatar.GetGrabber(side) : null;
            HandState hand = Hand(side);
            return grabber != null && hand.Mode == HandMode.Target &&
                   (grabber.transform.position - hand.TargetPosition).magnitude <= tolerance;
        }

        // ── Источник ввода ───────────────────────────────────────────────────

        public bool TryRead(ref DeviceFrame frame)
        {
            AvatarInputDevice device = AvatarInputDevice.For(_avatar);
            Transform space = device != null ? device.TrackingSpace : null;
            if (_avatar == null || space == null) return false;
            if (!_placed) Bind(_avatar, false);

            Quaternion headWorldRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Quaternion bodyRotation = Quaternion.AngleAxis(_yaw, _avatar.transform.up);
            frame.HeadPosition = space.InverseTransformPoint(_headWorld);
            frame.HeadRotation = Quaternion.Inverse(space.rotation) * headWorldRotation;
            frame.Left = BuildHand(UxrHandSide.Left, device, space, bodyRotation, _left);
            frame.Right = BuildHand(UxrHandSide.Right, device, space, bodyRotation, _right);
            return true;
        }

        private DeviceHand BuildHand(UxrHandSide side, AvatarInputDevice device, Transform space, Quaternion bodyRotation,
                                     in HandState hand)
        {
            if (hand.Mode != HandMode.Target || !TrySensorForGrabber(side, device, hand.TargetPosition, hand.TargetRotation,
                                                                       out Vector3 sensorPosition, out Quaternion sensorRotation))
            {
                // «Опущена»: смещение от головы по корпусу (без тангажа взгляда).
                DeviceFrame rest = DeviceFrame.Rest(EyeHeight);
                DeviceHand restHand = rest[side];
                sensorPosition = _headWorld + bodyRotation * (restHand.Position - rest.HeadPosition);
                sensorRotation = bodyRotation * restHand.Rotation;
            }

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
        private bool TrySensorForGrabber(UxrHandSide side, AvatarInputDevice device, Vector3 grabberPosition,
                                         Quaternion grabberRotation, out Vector3 sensorPosition, out Quaternion sensorRotation)
        {
            sensorPosition = Vector3.zero;
            sensorRotation = Quaternion.identity;

            ScriptedControllerTracking tracking = device.Tracking;
            UxrGrabber grabber = _avatar.GetGrabber(side);
            Transform wrist = side == UxrHandSide.Left ? _avatar.LeftHandBone : _avatar.RightHandBone;
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

        private HandState Hand(UxrHandSide side) => side == UxrHandSide.Left ? _left : _right;

        private void SetHand(UxrHandSide side, HandState hand)
        {
            if (side == UxrHandSide.Left) _left = hand;
            else _right = hand;
        }
    }
}
