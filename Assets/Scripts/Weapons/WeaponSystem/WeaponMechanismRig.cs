using System;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Механизм одного ствола (этап D WeaponSystem): ссылки на детали, позы покоя и заднего упора, клипы и числа хода.
    /// Только данные и геометрия без решений. Пишет только <c>WeaponSystemAuthoring</c> (единственный writer);
    /// читают датчик хода (<see cref="Sensors.WeaponActionSensor"/>) и исполнитель позы (<see cref="WeaponPoseExecutor"/>).
    ///
    /// <para>
    /// <b>Action</b> — ручка (<see cref="Handle"/>, граббабл затвора/помпы) и связанные с ней детали (<see cref="ActionBindings"/>,
    /// дети ручки, включая её саму). У каждой — поза покоя и проверенная задняя поза в пространстве корпуса
    /// (<see cref="Body"/>) и локальная поза покоя (<see cref="ActionLocalRest"/>). <b>Детали клипов</b> (<see cref="Parts"/>) —
    /// дорожки <see cref="Motion"/>: Action-детали (дети ручки) и нерычажные (курок, барабан; <c>InMagazine</c> — в магазине).
    /// </para>
    /// <para>
    /// Допуск хода <see cref="Epsilon"/> — численный запас float над координатой, а не физический порог (как прежний
    /// <c>AutomaticWeaponSlideFeedback.FrontPositionEpsilon</c>): покой и задний упор проверяются точно.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WeaponMechanismRig
    {
        /// <summary>Локальная поза детали относительно родителя.</summary>
        [Serializable]
        public struct LocalPose
        {
            public Vector3 Position;
            public Quaternion Rotation;

            public LocalPose(Vector3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
        }

        /// <summary>Деталь клипа выстрела: дорожка <see cref="Part"/> из <see cref="WeaponMechanismMotion"/>.</summary>
        [Serializable]
        public sealed class Part
        {
            public string Name;
            public Transform Target;
            public Vector3 RestPosition;
            public Quaternion RestRotation = Quaternion.identity;
            public bool Rotate;
            /// <summary>Деталь находится в магазине (барабан): цель ищется по имени в текущем магазине гнезда.</summary>
            public bool InMagazine;
            [NonSerialized] internal Transform RuntimeTarget;
        }

        [SerializeField] private Transform _body;
        [SerializeField] private UxrGrabbableObject _handle;
        [SerializeField] private Transform _contactPart;
        [SerializeField] private ChamberActionBinding[] _actionBindings = Array.Empty<ChamberActionBinding>();
        [SerializeField] private LocalPose[] _actionLocalRest = Array.Empty<LocalPose>();
        [SerializeField, Range(0f, 1f)] private float _extractionGate = 0.7f;
        [SerializeField, Min(0f)] private float _springReturnSpeed = 1.5f;
        [SerializeField, Min(0f)] private float _autoReturnSpeed = 1.5f;
        [SerializeField] private WeaponReleasedAction _releasedAction = WeaponReleasedAction.Spring;
        [SerializeField] private float _emptyRearTime = -1f;
        [SerializeField] private WeaponMechanismMotion _motion;
        [SerializeField] private Part[] _parts = Array.Empty<Part>();
        [SerializeField] private UxrGrabbableObjectAnchor _magazineAnchor;

        // Кэш проверенной геометрии (TryPrepare). Не состояние механизма.
        [NonSerialized] private ChamberActionBinding[] _bindings = Array.Empty<ChamberActionBinding>();
        [NonSerialized] private int[] _parentFirst = Array.Empty<int>();
        [NonSerialized] private Vector3 _travelDirection;
        [NonSerialized] private float _travelLength, _epsilon;
        [NonSerialized] private bool _prepared;
        [NonSerialized] private UxrGrabbableObject _currentMagazine;

        public Transform Body => _body;
        public UxrGrabbableObject Handle => _handle;
        public Transform ContactPart => _contactPart;
        public float ExtractionGate => _extractionGate;
        public float SpringReturnSpeed => _springReturnSpeed;
        public float AutoReturnSpeed => _autoReturnSpeed;
        public WeaponReleasedAction ReleasedAction => _releasedAction;
        public float EmptyRearTime => _emptyRearTime;
        public WeaponMechanismMotion Motion => _motion;
        public UxrGrabbableObjectAnchor MagazineAnchor => _magazineAnchor;

        /// <summary>Сериализованные Action-привязки (для writer и тестов); runtime-копия — после <see cref="TryPrepare"/>.</summary>
        public ChamberActionBinding[] ActionBindings => _actionBindings;
        public LocalPose[] ActionLocalRest => _actionLocalRest;
        public Part[] Parts => _parts;

        /// <summary>Ручной ход есть: ручка и проверенные привязки.</summary>
        public bool HasAction => _handle != null && _actionBindings != null && _actionBindings.Length > 0;

        public bool IsPrepared => _prepared;
        public Vector3 TravelDirection => _travelDirection;
        public float TravelLength => _travelLength;
        public float Epsilon => _epsilon;

        internal ChamberActionBinding[] RuntimeBindings => _bindings;
        internal int[] ParentFirstOrder => _parentFirst;

        /// <summary>Ось и длина полного хода ручки — из <c>Translation Limits</c> её граббабла (Restrict Local Offset).</summary>
        public static bool TryGetTravel(UxrGrabbableObject handle, out Vector3 direction, out float length)
        {
            direction = Vector3.zero;
            length = 0f;
            if (handle == null || handle.TranslationConstraint != UxrTranslationConstraintMode.RestrictLocalOffset) return false;
            Vector3 min = handle.TranslationLimitsMin, max = handle.TranslationLimitsMax;
            Vector3 travel = min.sqrMagnitude >= max.sqrMagnitude ? min : max;
            length = travel.magnitude;
            if (length < 1e-5f) return false;
            direction = travel / length;
            return true;
        }

        /// <summary>Численный запас конечной точки (8 ULP координаты), как у прежнего контроллера.</summary>
        public static float NumericEpsilon(Vector3 restLocal, float length) =>
            8f * 1.192092896e-7f * Mathf.Max(Mathf.Abs(restLocal.x), Mathf.Abs(restLocal.y), Mathf.Abs(restLocal.z), length);

        /// <summary>
        /// Проверить данные и подготовить runtime-копию привязок (родитель захватывается сейчас: смена родителя детали —
        /// конфигурация больше не текущая, <see cref="IsCurrent"/>). Без ручного хода проверяется только отсутствие Action.
        /// </summary>
        public bool TryPrepare(bool actionTravel, bool holdsOpen, out string error)
        {
            _prepared = false;
            error = null;
            _bindings = Array.Empty<ChamberActionBinding>();
            _parentFirst = Array.Empty<int>();
            if (!actionTravel)
            {
                if (_handle != null || (_actionBindings != null && _actionBindings.Length != 0))
                { error = "NoAction не принимает ручку и Action-привязки."; return false; }
                _prepared = true;
                return true;
            }
            if (_body == null || _handle == null || _actionBindings == null || _actionBindings.Length == 0 ||
                _actionLocalRest == null || _actionLocalRest.Length != _actionBindings.Length)
            { error = "Нет корпуса, ручки или Action-привязок."; return false; }
            if (!HasFixedRotation(_handle) || !TryGetTravel(_handle, out _travelDirection, out _travelLength))
            { error = "Ручка без фиксированного поворота или без хода (Restrict Local Offset)."; return false; }
            int handleIndex = Array.FindIndex(_actionBindings, binding => binding != null && binding.Target == _handle.transform);
            if (handleIndex < 0) { error = "Среди Action-привязок нет самой ручки."; return false; }
            _epsilon = NumericEpsilon(_actionLocalRest[handleIndex].Position, _travelLength);
            if (!Finite(_epsilon) || _epsilon <= 0f || _epsilon >= _travelLength)
            { error = "Численный диапазон не различает покой и задний упор."; return false; }
            if (!Finite(_extractionGate) || _extractionGate <= 0f || _extractionGate > 1f ||
                !Finite(_springReturnSpeed) || _springReturnSpeed <= 0f || !Finite(_autoReturnSpeed) || _autoReturnSpeed <= 0f)
            { error = "Порог извлечения или скорости возврата вне диапазона."; return false; }
            var targets = new System.Collections.Generic.HashSet<Transform>();
            var copy = new ChamberActionBinding[_actionBindings.Length];
            for (int index = 0; index < _actionBindings.Length; index++)
            {
                ChamberActionBinding binding = _actionBindings[index];
                LocalPose rest = _actionLocalRest[index];
                if (binding == null || binding.Target == null || !binding.Target.IsChildOf(_handle.transform) ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body) || !targets.Add(binding.Target) ||
                    !Finite(binding.RestPosition) || !Finite(binding.RearPosition) || !Finite(rest.Position) ||
                    !Unit(binding.RestRotation) || !Unit(binding.RearRotation) || !Unit(rest.Rotation))
                { error = "Невалидная или повторная Action-привязка."; return false; }
                bool translation = (binding.RearPosition - binding.RestPosition).sqrMagnitude > _epsilon * _epsilon;
                bool rotation = binding.AnimateRotation && !ChamberActionBinding.RotationNear(binding.RestRotation, binding.RearRotation);
                if (rotation && Mathf.Abs(Quaternion.Dot(binding.RestRotation.normalized, binding.RearRotation.normalized)) <= 8f * 1.192092896e-7f)
                { error = "Поворот на 180° неоднозначен."; return false; }
                if (!translation && !rotation) { error = "Action-привязка без проверяемого хода."; return false; }
                copy[index] = new ChamberActionBinding
                {
                    Target = binding.Target, RestPosition = binding.RestPosition, RestRotation = binding.RestRotation.normalized,
                    RearPosition = binding.RearPosition, RearRotation = binding.RearRotation.normalized,
                    AnimateRotation = binding.AnimateRotation, CapturedParent = binding.Target.parent
                };
            }
            if (holdsOpen)
            {
                if (_motion == null || _motion.Empty == null || !Finite(_emptyRearTime) || _emptyRearTime < 0f || _emptyRearTime > _motion.Empty.Duration)
                { error = "HoldOpen требует Empty-клипа и проверенного времени задней позы."; return false; }
                foreach (ChamberActionBinding binding in copy)
                    if (!binding.TryGetPoseProgress(new Pose(binding.RearPosition, binding.RearRotation), _epsilon, out float progress) || progress <= 0f)
                    { error = "Задняя поза HoldOpen не лежит на ходе Action."; return false; }
            }
            int[] order = new int[copy.Length];
            for (int index = 0; index < order.Length; index++) order[index] = index;
            Array.Sort(order, (left, right) => Depth(copy[left].Target).CompareTo(Depth(copy[right].Target)));
            _bindings = copy;
            _parentFirst = order;
            _prepared = true;
            return true;
        }

        /// <summary>Подготовленная геометрия всё ещё описывает сцену (родители деталей и масштабы не сменились).</summary>
        public bool IsCurrent()
        {
            if (!_prepared) return false;
            foreach (ChamberActionBinding binding in _bindings)
                if (binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !ChamberActionBinding.HasSupportedLocalFrames(binding.Target, _body)) return false;
            return true;
        }

        /// <summary>Ход ручки в долях полного хода (0 — покой, 1 — зад), со знаком.</summary>
        public float HandleProgress
        {
            get
            {
                if (!_prepared || _handle == null || _travelLength <= 0f) return 0f;
                return Vector3.Dot(_handle.transform.localPosition - HandleRest.Position, _travelDirection) / _travelLength;
            }
        }

        /// <summary>Локальная поза покоя самой ручки.</summary>
        public LocalPose HandleRest
        {
            get
            {
                for (int index = 0; index < _bindings.Length; index++)
                    if (_bindings[index].Target == _handle.transform) return _actionLocalRest[index];
                return new LocalPose(_handle.transform.localPosition, _handle.transform.localRotation);
            }
        }

        public bool IsHandleHeld => _handle != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_handle);

        public bool IsHandleHeldBy(UxrAvatar avatar) =>
            avatar != null && _handle != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbedBy(_handle, avatar);

        /// <summary>Минимальный прогресс всех обязательных деталей — порог извлечения (как прежний контроллер).</summary>
        public bool TryGetMinimumProgress(out float minimum)
        {
            minimum = HandleProgress;
            if (!_prepared || _bindings.Length == 0) return false;
            foreach (ChamberActionBinding binding in _bindings)
            {
                if (binding.Target == null || binding.Target.parent != binding.CapturedParent ||
                    !binding.TryGetProgress(_body, _epsilon, out float current)) return false;
                minimum = Mathf.Min(minimum, current);
            }
            return true;
        }

        /// <summary>Action физически в покое: ручка и каждая деталь в своей локальной позе покоя.</summary>
        public bool IsAtRest()
        {
            if (!_prepared) return false;
            if (_bindings.Length == 0) return true;
            for (int index = 0; index < _bindings.Length; index++)
            {
                ChamberActionBinding binding = _bindings[index];
                LocalPose rest = _actionLocalRest[index];
                Transform target = binding.Target;
                if (target == null || target.parent != binding.CapturedParent || !Finite(target.localPosition) ||
                    (target.localPosition - rest.Position).sqrMagnitude > _epsilon * _epsilon ||
                    !ChamberActionBinding.RotationNear(target.localRotation, rest.Rotation)) return false;
            }
            return true;
        }

        /// <summary>Action стоит в проверенной задней позе HoldOpen (доказательство для толчка вперёд, T22).</summary>
        public bool IsAtValidatedRear()
        {
            if (!_prepared || _bindings.Length == 0 || _emptyRearTime < 0f) return false;
            foreach (ChamberActionBinding binding in _bindings)
            {
                if (!ChamberActionBinding.TryGetBodyPose(binding.Target, _body, out Pose actual) ||
                    (actual.position - binding.RearPosition).sqrMagnitude > _epsilon * _epsilon ||
                    !ChamberActionBinding.RotationNear(actual.rotation, binding.RearRotation)) return false;
            }
            return true;
        }

        /// <summary>Деталь клипа — Action (ребёнок ручки): ею владеет рука, когда ручку держат.</summary>
        public bool IsActionPart(Part part) => part != null && part.Target != null && _handle != null && part.Target.IsChildOf(_handle.transform);

        /// <summary>Цель детали клипа: обычная — сериализованная; в магазине — найденная в текущем магазине гнезда.</summary>
        internal Transform TargetOf(Part part)
        {
            if (part == null) return null;
            if (!part.InMagazine) return part.Target;
            RefreshMagazineParts();
            return part.RuntimeTarget != null && _currentMagazine != null && _currentMagazine.CurrentAnchor == _magazineAnchor ? part.RuntimeTarget : null;
        }

        private void RefreshMagazineParts()
        {
            UxrGrabbableObject magazine = _magazineAnchor != null ? _magazineAnchor.CurrentPlacedObject : null;
            if (magazine == _currentMagazine) return;
            _currentMagazine = magazine;
            foreach (Part part in _parts)
            {
                if (part == null || !part.InMagazine) continue;
                part.RuntimeTarget = null;
                if (magazine == null) continue;
                string name = part.Name == "Cylinder" ? "MechanismVisual" : part.Name;
                foreach (MeshFilter filter in magazine.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name == name) { part.RuntimeTarget = filter.transform; break; }
            }
        }

        private static bool HasFixedRotation(UxrGrabbableObject handle)
        {
            if (handle.RotationConstraint == UxrRotationConstraintMode.Locked) return true;
            // Сборщик ставит Restrict с нулевыми пределами — физически тот же запрет вращения.
            return handle.RotationConstraint == UxrRotationConstraintMode.RestrictLocalRotation &&
                   handle.RotationAngleLimitsMin.Equals(Vector3.zero) && handle.RotationAngleLimitsMax.Equals(Vector3.zero);
        }

        private static int Depth(Transform target)
        {
            int depth = 0;
            for (Transform current = target; current != null; current = current.parent) depth++;
            return depth;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Unit(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
            Mathf.Abs(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w - 1f) <= 1e-5f;
    }
}
