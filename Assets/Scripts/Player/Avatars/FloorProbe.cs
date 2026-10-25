using UnityEngine;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Пол карты под точкой: луч вниз по слою <c>Ground</c> (на картах это только полы; блоки и укрытия —
    /// <c>Default</c>). Луч начинается на <see cref="ProbeUp"/> выше точки, поэтому корень, провалившийся
    /// под пол, тоже находит свой пол, а не этаж ниже.
    /// </summary>
    public static class FloorProbe
    {
        /// <summary>Насколько выше точки начинается луч.</summary>
        public const float ProbeUp = 2f;

        /// <summary>Длина луча вниз от его начала.</summary>
        public const float ProbeDown = 8f;

        /// <summary>Расхождение корня с полом, начиная с которого корень считается «не на полу».</summary>
        public const float Tolerance = 0.1f;

        private static int s_groundMask = -1;

        private static int GroundMask
        {
            get
            {
                if (s_groundMask == -1) s_groundMask = LayerMask.GetMask("Ground");
                return s_groundMask;
            }
        }

        public static bool TryFindFloor(Vector3 position, out float floorY, out Collider floor)
        {
            Vector3 origin = position + Vector3.up * ProbeUp;
            if (GroundMask != 0 &&
                Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeDown, GroundMask, QueryTriggerInteraction.Ignore))
            {
                floorY = hit.point.y;
                floor = hit.collider;
                return true;
            }

            floorY = position.y;
            floor = null;
            return false;
        }
    }
}
