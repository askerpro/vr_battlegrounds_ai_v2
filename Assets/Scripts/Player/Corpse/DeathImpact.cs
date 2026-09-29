using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Толчок, с которым тело падает (T-35): точка приложения и импульс. Считает сервер из
    /// смертельного урона и рассылает с командой «стань трупом» — у каждого клиента рэгдолл
    /// падает от одного и того же толчка.
    ///
    /// <para>
    /// Направление — от стрелка к точке попадания (у пули), от центра взрыва к телу (у взрыва).
    /// Сила растёт с уроном и ограничена: попадание в конечность лёгкого рэгдолла иначе
    /// закрутило бы его. Урон без источника (падение, отладка) — без толчка, тело просто оседает.
    /// </para>
    /// </summary>
    public readonly struct DeathImpact
    {
        public const float BaseImpulse = 15f;
        public const float ImpulsePerDamage = 0.6f;
        public const float MaxImpulse = 90f;

        public readonly Vector3 Point;
        public readonly Vector3 Impulse;

        public DeathImpact(Vector3 point, Vector3 impulse)
        {
            Point = point;
            Impulse = impulse;
        }

        public static DeathImpact None => new DeathImpact(Vector3.zero, Vector3.zero);

        public bool HasImpulse => Impulse.sqrMagnitude > 0f;

        /// <summary>Сила толчка по урону.</summary>
        public static float Magnitude(float damage) =>
            Mathf.Min(MaxImpulse, BaseImpulse + Mathf.Max(0f, damage) * ImpulsePerDamage);

        /// <summary>
        /// Толчок по попаданию. <paramref name="bodyCenter"/> — центр тела жертвы (взрыв, пуля без стрелка).
        /// </summary>
        public static DeathImpact From(UxrDamageEventArgs e, Vector3 bodyCenter)
        {
            if (e == null) return None;

            if (e.DamageType == UxrDamageType.Explosive)
                return Along(bodyCenter, bodyCenter - e.ExplosionPosition, e.Damage);

            if (e.RaycastHit.collider == null) return None;

            Vector3 point = e.RaycastHit.point;
            Vector3 direction = e.ActorSource != null
                ? point - e.ActorSource.transform.position
                : -e.RaycastHit.normal;

            return Along(point, direction, e.Damage);
        }

        private static DeathImpact Along(Vector3 point, Vector3 direction, float damage)
        {
            if (direction.sqrMagnitude < 1e-6f) return new DeathImpact(point, Vector3.zero);
            return new DeathImpact(point, direction.normalized * Magnitude(damage));
        }
    }
}
