using Mirror;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Сетевой прогресс выдвижения оборудования. Корпус, его коллайдеры и удерживаемые предметы
    /// не меняются. Поздний клиент получает снимок движения и вычисляет позу по сетевому времени.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArsenalWallController))]
    public sealed class ArsenalDeploymentAnimator : NetworkBehaviour
    {
        public struct DeploymentMotion
        {
            public double StartedAt;
            public float From;
            public float To;
            public float Duration;
        }

        [SerializeField] private Transform _presentationRoot;
        [SerializeField, Min(0.1f)] private float _duration = 1f;
        [SyncVar] private DeploymentMotion _motion = new DeploymentMotion { From = 1f, To = 1f };
        [SerializeField] private ArsenalBoundaryWall _boundary;
        private ArsenalEquipmentPoses _equipment;

        private double Clock => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        private bool CanWrite => (!NetworkClient.active && !NetworkServer.active) || isServer;
        private DeploymentMotion CurrentMotion => _boundary != null ? _boundary.Motion : _motion;

        /// <summary>Ссылка для редакторского стенда; компонент не двигает корпус.</summary>
        public Transform PresentationRoot => _presentationRoot;
        public bool ReadyForAccess => _boundary != null ? _boundary.ReadyForAccess :
            _motion.To >= 1f && RaisedFraction >= 0.999f;
        /// <summary>Оборудование полностью убрано; неподвижный корпус остаётся на месте.</summary>
        public bool IsRetracted => _boundary != null ? _boundary.IsRetracted :
            _motion.To <= 0f && RaisedFraction <= 0.001f;
        public float RaisedFraction => Evaluate(CurrentMotion, Clock);

        public void ConfigureBoundary(ArsenalBoundaryWall boundary) => _boundary = boundary;

        public void Configure(Transform presentationRoot, float duration)
        {
            _presentationRoot = presentationRoot;
            _duration = Mathf.Max(0.1f, duration);
        }

        private void Awake() => _equipment = GetComponent<ArsenalEquipmentPoses>();

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyPose();
        }

        private void Update()
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (_boundary == null && CanWrite && mode != null)
            {
                bool needed = mode.ArsenalRules.IsDeployed;
                if (needed && _motion.To < 1f) BeginMotion(1f);
                else if (!needed && _motion.To > 0f) BeginMotion(0f);
            }
            ApplyPose();
        }

        /// <summary>Запрос подготовки меняет прогресс всех связанных станций, а не позу корпуса.</summary>
        public void PrepareForEquipment()
        {
            if (_boundary != null)
            {
                _boundary.PrepareForEquipment();
                return;
            }
            if (CanWrite && _motion.To < 1f) BeginMotion(1f);
        }

        private void BeginMotion(float target)
        {
            float current = RaisedFraction;
            _motion = new DeploymentMotion
            {
                StartedAt = Clock, From = current, To = target,
                Duration = _duration * Mathf.Abs(target - current)
            };
            GameLog.Arsenal.Info(target > 0f ? "[Arsenal] Содержимое выдвигается." : "[Arsenal] Содержимое убирается.", this);
        }

        public static float Evaluate(DeploymentMotion motion, double now)
        {
            if (motion.Duration <= 0f) return motion.To;
            float t = Mathf.Clamp01((float)((now - motion.StartedAt) / motion.Duration));
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(motion.From, motion.To, t);
        }

        private void ApplyPose()
        {
            if (_equipment == null) _equipment = GetComponent<ArsenalEquipmentPoses>();
            if (_equipment != null) _equipment.Apply(RaisedFraction);
        }
    }
}
