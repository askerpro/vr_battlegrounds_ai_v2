using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Разметка зоны тела на щите мишени стрельбища: коробка — единичный куб в осях этого объекта
    /// (масштаб задаёт размер). Монитор попаданий берёт множитель урона зоны из <see cref="HitZoneDamage"/> —
    /// как у хитбоксов игрока; вне разметки — торс (×1). Физики и сетевых действий нет.
    /// </summary>
    public sealed class ShootingTargetZone : MonoBehaviour
    {
        [SerializeField] private HitZone _zone = HitZone.Head;

        public HitZone Zone => _zone;

        /// <summary>Точка мира внутри коробки зоны (глубина щита не важна — проверяются только его плоскость и ±0,5 по оси).</summary>
        public bool Contains(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= .5f && Mathf.Abs(local.y) <= .5f && Mathf.Abs(local.z) <= .5f;
        }

        /// <summary>Зона попадания по щиту: первая размеченная зона, в которую попала точка; иначе торс.</summary>
        public static HitZone ZoneAt(Transform pivot, Vector3 world)
        {
            if (pivot != null)
                foreach (ShootingTargetZone zone in pivot.GetComponentsInChildren<ShootingTargetZone>(false))
                    if (zone.Contains(world)) return zone.Zone;
            return HitZone.Torso;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = _zone == HitZone.Head ? Color.red : Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
