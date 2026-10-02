using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Единый серверный снимок выдвижения оборудования нескольких станций.
    /// Историческое имя сохранено для ссылок префабов; границы зоны, корпус и коллайдеры не управляются здесь.
    /// NetworkIdentity координатора и станций остаются отдельными корнями.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class ArsenalBoundaryWall : NetworkBehaviour
    {
        [SerializeField] private ArsenalDeploymentAnimator[] _stations = new ArsenalDeploymentAnimator[0];
        [SerializeField, Min(0.1f)] private float _duration = 1f;
        [SyncVar] private ArsenalDeploymentAnimator.DeploymentMotion _motion =
            new ArsenalDeploymentAnimator.DeploymentMotion { From = 1f, To = 1f };

        private bool CanWrite => (!NetworkClient.active && !NetworkServer.active) || isServer;
        private double Clock => NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.timeAsDouble;
        public ArsenalDeploymentAnimator.DeploymentMotion Motion => _motion;
        public float RaisedFraction => ArsenalDeploymentAnimator.Evaluate(_motion, Clock);
        public bool ReadyForAccess => _motion.To >= 1f && RaisedFraction >= 0.999f;
        public bool IsRetracted => _motion.To <= 0f && RaisedFraction <= 0.001f;

        public void Configure(ArsenalDeploymentAnimator[] stations, float duration)
        {
            _stations = stations ?? new ArsenalDeploymentAnimator[0];
            _duration = Mathf.Max(0.1f, duration);
            BindStations();
        }

        private void Awake() => BindStations();

        public override void OnStartClient()
        {
            base.OnStartClient();
            BindStations();
        }

        private void BindStations()
        {
            foreach (ArsenalDeploymentAnimator station in _stations)
                if (station != null) station.ConfigureBoundary(this);
        }

        private void Update()
        {
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null && referee.gameObject.scene == gameObject.scene ? referee.ActiveGameMode : null;
            if (!CanWrite || mode == null) return;
            bool needed = mode.ArsenalRules.IsDeployed;
            if (needed && _motion.To < 1f) BeginMotion(1f);
            else if (!needed && _motion.To > 0f) BeginMotion(0f);
        }

        public void PrepareForEquipment()
        {
            if (CanWrite && _motion.To < 1f) BeginMotion(1f);
        }

        private void BeginMotion(float target)
        {
            float from = RaisedFraction;
            _motion = new ArsenalDeploymentAnimator.DeploymentMotion
            {
                StartedAt = Clock, From = from, To = target,
                Duration = _duration * Mathf.Abs(target - from)
            };
            GameLog.Arsenal.Info(target > 0f ? "[Arsenal] Оборудование станций выдвигается." :
                "[Arsenal] Оборудование станций убирается.", this);
        }
    }
}
