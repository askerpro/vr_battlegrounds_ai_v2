using System;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Один визуальный цикл на выстрел; ручная тяга имеет приоритет. Косметика не вызывает Reload/Shoot.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(200)]
    public sealed class WeaponMechanismVisuals : MonoBehaviour
    {
        [Serializable] public sealed class Binding
        {
            public string Part;
            public Transform Target;
            public Vector3 RestPosition;
            public Quaternion RestRotation;
            public bool Rotate;
            public bool InMagazine;
            [NonSerialized] public Vector3 FrozenPosition;
            [NonSerialized] public Quaternion FrozenRotation;
            [NonSerialized] public Vector3 FrozenBodyPosition;
            [NonSerialized] public Quaternion FrozenBodyRotation;
        }
        [SerializeField] private WeaponMechanismMotion _motion;
        [SerializeField] private Transform _body;
        [SerializeField] private UxrGrabbableObject _slide;
        [SerializeField] private Transform _contactPart;
        [SerializeField] private UxrGrabbableObjectAnchor _magazineAnchor;
        [SerializeField] private Binding[] _bindings;
        [SerializeField] private float _originalSlideLength;
        private UxrFirearmWeapon _weapon;
        private AutomaticWeaponSlideFeedback _feedback;
        private WeaponMechanismMotion.Cycle _cycle;
        private float _started;
        private bool _manual;
        private bool _emptyHeld;
        private bool _catchRequired;
        private float _catchProgress;
        private UxrGrabbableObject _currentMagazine;

        public WeaponMechanismMotion Motion => _motion;
        public Binding[] Bindings => _bindings;

        private void OnEnable()
        {
            _weapon = GetComponent<UxrFirearmWeapon>();
            _feedback = GetComponent<AutomaticWeaponSlideFeedback>();
            if (_weapon != null) { _weapon.ProjectileShot += HandleShot; _weapon.ProjectileShotReplayed += HandleShot; }
            if (_slide != null) _slide.Grabbing += HandleGrabbing;
            if (_feedback != null) _feedback.ManualCycleCompleted += HandleManualCompleted;
        }

        private void OnDisable()
        {
            if (_weapon != null) { _weapon.ProjectileShot -= HandleShot; _weapon.ProjectileShotReplayed -= HandleShot; }
            if (_slide != null) _slide.Grabbing -= HandleGrabbing;
            if (_feedback != null) _feedback.ManualCycleCompleted -= HandleManualCompleted;
            if (_feedback != null) _feedback.CancelVisualHold();
            ResetVisuals();
        }

        private void HandleShot(int trigger) => PlayShot(trigger, _weapon.GetAmmoLeft(trigger) == 0);

        public void PlayShot(int triggerIndex, bool emptyAfterShot)
        {
            if (triggerIndex != 0 || _motion == null || _manual ||
                (_slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide))) return;
            _cycle = emptyAfterShot && _motion.Empty != null ? _motion.Empty : _motion.Fire;
            _emptyHeld = false;
            _catchRequired = false;
            _started = Time.time;
        }

        private void LateUpdate()
        {
            UpdateMagazineBinding();
            bool held = _slide != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_slide);
            if (_manual)
            {
                float progress = _feedback != null ? _feedback.GetSlideProgress() : 0f;
                if (_catchRequired && progress >= _catchProgress - 0.0005f)
                {
                    _catchRequired = false;
                    _emptyHeld = false;
                }
                foreach (Binding binding in _bindings)
                {
                    if (!IsAction(binding) || binding.Target == null) continue;
                    if (_catchRequired && binding.Target != _contactPart)
                    {
                        binding.Target.SetPositionAndRotation(_body.TransformPoint(binding.FrozenBodyPosition), _body.rotation * binding.FrozenBodyRotation);
                    }
                    else
                    {
                        // Контактная система UXR не меняется под держащей рукой.
                        if (held && binding.Target == _contactPart)
                        {
                            binding.Target.localPosition = binding.FrozenPosition;
                            if (binding.Rotate) binding.Target.localRotation = binding.FrozenRotation;
                        }
                        else if (held)
                        {
                            binding.Target.localPosition = binding.RestPosition;
                            if (binding.Rotate) binding.Target.localRotation = binding.RestRotation;
                        }
                        else
                        {
                            binding.Target.localPosition = Vector3.MoveTowards(binding.Target.localPosition, binding.RestPosition, 1.5f * Time.deltaTime);
                            if (binding.Rotate) binding.Target.localRotation = Quaternion.RotateTowards(binding.Target.localRotation, binding.RestRotation, 720f * Time.deltaTime);
                        }
                    }
                }
                if (!held && (_slide == null || (_slide.transform.localPosition - _slide.InitialLocalPosition).sqrMagnitude < 1e-8f))
                {
                    if (_catchRequired)
                    {
                        // Отпускание до зацепления не закрывает открытый внутренний затвор.
                        _manual = false;
                        _emptyHeld = true;
                    }
                    else ResetVisuals();
                }
                return;
            }
            if (_cycle == null) return;
            float time = Mathf.Min(Time.time - _started, _cycle.Duration);
            foreach (Binding binding in _bindings) ApplyTrack(binding, _cycle, time);
            if (time < _cycle.Duration) return;
            if (_cycle.HoldEnd)
            {
                if (!_emptyHeld) FinishEmpty();
            }
            else ResetVisuals();
        }

        private void FinishEmpty()
        {
            _emptyHeld = true;
            // У TR15 ручка неподвижна при Fire_Out: внутренний Bolt остаётся отдельным каналом.
            if (_feedback == null || _contactPart == null || !AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 dir, out float length)) return;
            Binding contact = Array.Find(_bindings, b => b.Target == _contactPart);
            if (contact == null) return;
            Vector3 delta = _slide.transform.InverseTransformVector(_contactPart.parent.TransformVector(_contactPart.localPosition - contact.RestPosition));
            float along = Vector3.Dot(delta, dir);
            if (Mathf.Abs(along) < 0.001f) return;
            Vector3[] positions = new Vector3[_bindings.Length];
            Quaternion[] rotations = new Quaternion[_bindings.Length];
            for (int i = 0; i < _bindings.Length; i++) if (_bindings[i].Target != null) { positions[i] = _bindings[i].Target.position; rotations[i] = _bindings[i].Target.rotation; }
            _feedback.HoldVisualPose(dir * along);
            // Мировая поза каждого независимого канала и контакта остаётся прежней.
            for (int i = 0; i < _bindings.Length; i++) if (IsAction(_bindings[i]) && _bindings[i].Target != null)
                _bindings[i].Target.SetPositionAndRotation(positions[i], rotations[i]);
            _cycle = null;
        }

        private void HandleGrabbing(object sender, UxrManipulationEventArgs e)
        {
            Binding contact = Array.Find(_bindings, b => b.Target == _contactPart);
            if (_feedback != null && contact != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 direction, out _))
            {
                Vector3 residual = _slide.transform.InverseTransformVector(_contactPart.parent.TransformVector(_contactPart.localPosition - contact.RestPosition));
                var poses = new Pose[_bindings.Length];
                for (int i = 0; i < _bindings.Length; i++) if (_bindings[i].Target != null) poses[i] = new Pose(_bindings[i].Target.position, _bindings[i].Target.rotation);
                _feedback.BeginVisualHandoff(direction * Vector3.Dot(residual, direction));
                for (int i = 0; i < _bindings.Length; i++) if (IsAction(_bindings[i]) && _bindings[i].Target != null)
                    _bindings[i].Target.SetPositionAndRotation(poses[i].position, poses[i].rotation);
                UxrGrabManager.CaptureGrabbingObjectPose(e);
            }
            _manual = true;
            _cycle = null;
            _catchRequired = false;
            foreach (Binding b in _bindings) if (b.Target != null)
            {
                b.FrozenPosition = b.Target.localPosition; b.FrozenRotation = b.Target.localRotation;
                b.FrozenBodyPosition = _body.InverseTransformPoint(b.Target.position);
                b.FrozenBodyRotation = Quaternion.Inverse(_body.rotation) * b.Target.rotation;
            }
            // Открытый внутренний затвор возможен уже в первом кадре Empty и в середине обычного Fire.
            if (_feedback != null && AutomaticWeaponSlideFeedback.TryGetSlideTravel(_slide, out Vector3 dir, out float length))
            {
                float far = 0f;
                foreach (Binding b in _bindings) if (IsAction(b) && b.Target != _contactPart)
                    far = Mathf.Max(far, Vector3.Dot(_slide.transform.InverseTransformVector(b.Target.parent.TransformVector(b.Target.localPosition - b.RestPosition)), dir));
                if (far > 0.001f)
                {
                    _catchRequired = true;
                    _catchProgress = _feedback.GetSlideProgress() + far / length;
                    _feedback.RequireManualCatch(_catchProgress - 0.0005f);
                }
            }
        }

        private void HandleManualCompleted() { _emptyHeld = false; }

        private bool IsAction(Binding b) => b.Target != null && _slide != null && b.Target.IsChildOf(_slide.transform);

        private void ApplyTrack(Binding binding, WeaponMechanismMotion.Cycle cycle, float time)
        {
            if (binding.Target == null || cycle == null || (binding.InMagazine && (_currentMagazine == null || _currentMagazine.CurrentAnchor != _magazineAnchor))) return;
            WeaponMechanismMotion.Track track = Array.Find(cycle.Tracks, t => t.Part == binding.Part);
            if (track == null) return;
            Pose pose = track.Evaluate(time);
            binding.Target.position = _body.TransformPoint(pose.position);
            if (track.AnimateRotation && binding.Rotate) binding.Target.rotation = _body.rotation * pose.rotation;
        }

        private void UpdateMagazineBinding()
        {
            var magazine = _magazineAnchor != null ? _magazineAnchor.CurrentPlacedObject : null;
            if (magazine == _currentMagazine) return;
            _currentMagazine = magazine;
            foreach (Binding b in _bindings) if (b.InMagazine)
            {
                b.Target = null;
                if (magazine == null) continue;
                foreach (MeshFilter filter in magazine.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.name == (b.Part == "Cylinder" ? "MechanismVisual" : b.Part)) { b.Target = filter.transform; break; }
            }
        }

        public void ResetVisuals()
        {
            _cycle = null; _manual = false; _emptyHeld = false; _catchRequired = false;
            if (_bindings == null) return;
            foreach (Binding binding in _bindings)
            {
                if (binding.Target == null || (binding.InMagazine && (_currentMagazine == null || _currentMagazine.CurrentAnchor != _magazineAnchor))) continue;
                binding.Target.localPosition = binding.RestPosition;
                if (binding.Rotate) binding.Target.localRotation = binding.RestRotation;
            }
        }
    }
}
