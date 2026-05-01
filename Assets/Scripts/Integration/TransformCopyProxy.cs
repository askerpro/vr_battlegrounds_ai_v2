using UnityEngine;

namespace VRBattlegrounds.Integration
{
    /// <summary>
    /// Копирует трансформ источника в указанный target-объект.
    /// Подходит для проксирования позы кости рига в отдельный вспомогательный объект.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VR Battlegrounds/Integration/Transform Copy Proxy")]
    public sealed class TransformCopyProxy : MonoBehaviour
    {
        public enum CopySpace
        {
            WorldToWorld,
            LocalToLocal
        }

        public enum UpdatePhase
        {
            Update,
            LateUpdate,
            FixedUpdate
        }

        [Header("References")]
        [SerializeField] private Transform _source;
        [SerializeField] private Transform _target;

        [Header("Behavior")]
        [SerializeField] private CopySpace _copySpace = CopySpace.WorldToWorld;
        [SerializeField] private UpdatePhase _updatePhase = UpdatePhase.LateUpdate;

        [Header("Channels")]
        [SerializeField] private bool _copyPosition = true;
        [SerializeField] private bool _copyRotation = true;
        [SerializeField] private bool _copyScale;

        [Header("Offsets")]
        [Tooltip("Для WorldToWorld интерпретируется в локальной системе source.")]
        [SerializeField] private Vector3 _positionOffset = Vector3.zero;
        [SerializeField] private Vector3 _rotationOffsetEuler = Vector3.zero;
        [SerializeField] private Vector3 _scaleMultiplier = Vector3.one;

        public Transform Source
        {
            get => _source;
            set => _source = value;
        }

        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        private void Reset()
        {
            _target = transform;
        }

        private void Update()
        {
            if (_updatePhase == UpdatePhase.Update)
            {
                CopyNow();
            }
        }

        private void LateUpdate()
        {
            if (_updatePhase == UpdatePhase.LateUpdate)
            {
                CopyNow();
            }
        }

        private void FixedUpdate()
        {
            if (_updatePhase == UpdatePhase.FixedUpdate)
            {
                CopyNow();
            }
        }

        /// <summary>
        /// Выполняет копирование немедленно.
        /// </summary>
        public void CopyNow()
        {
            if (_source == null)
            {
                return;
            }

            Transform target = _target != null ? _target : transform;
            Quaternion rotationOffset = Quaternion.Euler(_rotationOffsetEuler);

            if (_copySpace == CopySpace.WorldToWorld)
            {
                if (_copyPosition)
                {
                    target.position = _source.TransformPoint(_positionOffset);
                }

                if (_copyRotation)
                {
                    target.rotation = _source.rotation * rotationOffset;
                }
            }
            else
            {
                if (_copyPosition)
                {
                    target.localPosition = _source.localPosition + _positionOffset;
                }

                if (_copyRotation)
                {
                    target.localRotation = _source.localRotation * rotationOffset;
                }
            }

            if (_copyScale)
            {
                target.localScale = Vector3.Scale(_source.localScale, _scaleMultiplier);
            }
        }
    }
}
