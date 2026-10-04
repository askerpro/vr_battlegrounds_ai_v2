using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Локальный объём разметки; не Collider и никогда не участвует в физике.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalArenaShape : MonoBehaviour
    {
        public Vector3 center;
        public Vector3 size = Vector3.one;
        public bool TryBounds(out Bounds bounds) => PhysicalArenaGeometry.TryBox(transform, center, size, out bounds);
    }
}
