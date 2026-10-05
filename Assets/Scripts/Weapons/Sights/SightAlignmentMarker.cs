using UnityEngine;

namespace VrBattlegrounds.Weapons.Sights
{
    public enum SightAlignmentRole { Rear, Front }

    /// <summary>Точка фактического наведения внутри детали целика или мушки; игровыми действиями не управляет.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VR Battlegrounds/Weapons/Sight Alignment Marker")]
    public sealed class SightAlignmentMarker : MonoBehaviour
    {
        [Tooltip("Rear — точка целика; Front — точка мушки. Направление задаётся двумя позициями, не вращением Empty.")]
        public SightAlignmentRole Role;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Role == SightAlignmentRole.Rear ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .001f);
        }
    }
}
