using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Неподвижная разметка места экипировки и направления личного табло.
    /// Размещается на логическом корне стены; не двигает игрока и не ведёт сетевое состояние.
    /// Габариты задаются для поднятого корпуса и не зависят от его текущей анимации.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArsenalWallController))]
    public sealed class ArsenalStationAnchor : MonoBehaviour
    {
        [SerializeField] private ArsenalWallController _wall;
        [SerializeField] private TeamSpawnZone _zone;
        [SerializeField] private Transform _standingPoint;
        [SerializeField] private Transform _arenaFacing;
        [Tooltip("Проектные границы поднятого корпуса в координатах неподвижного корня стены.")]
        [SerializeField] private Bounds _raisedLocalBounds = new Bounds(new Vector3(0f, 1.4f, 0f), new Vector3(2.2f, 2.8f, 0.8f));

        public ArsenalWallController Wall => _wall != null ? _wall : GetComponent<ArsenalWallController>();
        public TeamSpawnZone Zone => _zone;
        public Transform StandingPoint => _standingPoint;
        public Transform ArenaFacing => _arenaFacing;
        public Vector3 StandingPosition => _standingPoint != null ? _standingPoint.position : transform.position;
        public Vector3 BoardFacing => _arenaFacing != null ? Vector3.ProjectOnPlane(_arenaFacing.forward, Vector3.up).normalized : Vector3.zero;
        public bool HasBoardDirection => _standingPoint != null && BoardFacing.sqrMagnitude > 0.0001f;

        /// <summary>Постоянные мировые границы поднятого корпуса, включая масштаб и поворот корня.</summary>
        public Bounds RaisedBoundsWorld
        {
            get
            {
                Transform root = Wall != null ? Wall.transform : transform;
                Vector3 e = _raisedLocalBounds.extents;
                Vector3 x = root.TransformVector(new Vector3(e.x, 0f, 0f));
                Vector3 y = root.TransformVector(new Vector3(0f, e.y, 0f));
                Vector3 z = root.TransformVector(new Vector3(0f, 0f, e.z));
                Vector3 extent = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                                             Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                                             Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(root.TransformPoint(_raisedLocalBounds.center), extent * 2f);
            }
        }

        /// <summary>Настраивает ссылки разметки; направления и позиции остаются у карты.</summary>
        public void Configure(ArsenalWallController wall, TeamSpawnZone zone, Transform standingPoint, Transform arenaFacing)
        {
            _wall = wall;
            _zone = zone;
            _standingPoint = standingPoint;
            _arenaFacing = arenaFacing;
        }

        /// <summary>Задаёт измеренные проектные габариты в позе поднятого корпуса.</summary>
        public void ConfigureRaisedBounds(Bounds localBounds)
        {
            _raisedLocalBounds = new Bounds(localBounds.center, new Vector3(Mathf.Abs(localBounds.size.x),
                                                                           Mathf.Abs(localBounds.size.y),
                                                                           Mathf.Abs(localBounds.size.z)));
        }
    }
}
