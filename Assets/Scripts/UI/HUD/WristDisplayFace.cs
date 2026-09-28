using UnityEngine;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Чистая математика табло на часах: доля и цвет ХП, формат времени.
    /// Вынесена из <see cref="WristDisplay"/>, чтобы проверяться юнит-тестом.
    /// </summary>
    public static class WristDisplayFace
    {
        /// <summary>Тон зелёного в HSV. Красный — 0.</summary>
        private const float GreenHue = 1f / 3f;

        public static float HealthFraction(float health, float maxHealth)
        {
            if (maxHealth <= 0f) return 0f;
            return Mathf.Clamp01(health / maxHealth);
        }

        /// <summary>
        /// Зелёный → жёлтый → красный по тону HSV. Линейный RGB между зелёным и красным
        /// в середине даёт грязно-бурый, по тону — чистый жёлтый.
        /// </summary>
        public static Color HealthColor(float fraction)
        {
            return Color.HSVToRGB(Mathf.Clamp01(fraction) * GreenHue, 0.9f, 1f);
        }

        /// <summary>
        /// «мм:сс». Секунды округляются вверх: пока идёт последняя секунда, видно 00:01,
        /// а 00:00 — только когда время действительно вышло.
        /// </summary>
        public static string FormatTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "--:--";

            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
