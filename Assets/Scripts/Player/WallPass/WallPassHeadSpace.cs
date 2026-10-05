using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>Единая угловая маска относительно центра головы, без viewport конкретного глаза.</summary>
    public static class WallPassHeadSpace
    {
        public static float PeripheralAlpha(Vector3 position, float width)
        {
            if (position.sqrMagnitude < 0.000001f) return 0f;
            width = Mathf.Clamp(WallPassVisualSettings.Safe(width, 0.28f), 0.08f, 0.5f);
            float innerAngle = Mathf.Lerp(48f, 18f, (width - 0.08f) / 0.42f);
            float angle = Mathf.Acos(Mathf.Clamp(position.z / position.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.SmoothStep(0f, 1f, (angle - innerAngle) / 22f);
        }
    }
}
