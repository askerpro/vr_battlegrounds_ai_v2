using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Толчок, с которым тело падает (T-35): точка приложения и импульс. Считает сервер из
    /// смертельного попадания и рассылает с командой «стань трупом» — у каждого клиента рэгдолл
    /// падает от одного и того же толчка.
    ///
    /// <para>
    /// <b>Сила — от оружия и урона.</b> Пуля несёт силу (<c>UxrDamageEventArgs.ImpactForce</c>, Патч 30):
    /// скорость пули × <c>ProjectileImpactForceMultiplier</c> описания выстрела — ту же, которой SDK толкает
    /// физические предметы, приложенную на один шаг физики. К ней добавляются вклад урона и база, итог
    /// ограничен <see cref="MaxImpulse"/>: у дробовика множитель 50, и одна дробина иначе отправила бы
    /// тело в полёт. Дробины одного выстрела складываются (<see cref="Combine"/>).
    /// </para>
    ///
    /// <para>
    /// Направление — полёт пули (сила), без силы — от стрелка к точке попадания; у взрыва — от центра
    /// взрыва к телу. Урон без источника (падение, отладка) — без толчка, тело просто оседает.
    /// </para>
    /// </summary>
    public readonly struct DeathImpact
    {
        public const float BaseImpulse = 15f;
        public const float ImpulsePerDamage = 0.3f;

        /// <summary>Сила пули приложена SDK на один шаг физики — это и есть её импульс.</summary>
        public const float PhysicsStep = 0.02f;

        /// <summary>Во сколько раз импульс пули сильнее для рэгдолла (лёгкие кости, заметное падение).</summary>
        public const float ForceImpulseScale = 2.5f;

        public const float MaxImpulse = 90f;

        public readonly Vector3 Point;
        public readonly Vector3 Impulse;

        /// <summary>Тип смертельного урона — только для лога сервера, по сети не едет; null — неизвестен.</summary>
        public readonly UxrDamageType? DamageType;

        public DeathImpact(Vector3 point, Vector3 impulse, UxrDamageType? damageType = null)
        {
            Point = point;
            Impulse = impulse;
            DamageType = damageType;
        }

        public static DeathImpact None => new DeathImpact(Vector3.zero, Vector3.zero);

        public bool HasImpulse => Impulse.sqrMagnitude > 0f;

        /// <summary>Сила толчка по урону и силе пули (Н), с пределом.</summary>
        public static float Magnitude(float damage, float impactForce) =>
            Mathf.Min(MaxImpulse,
                      BaseImpulse + Mathf.Max(0f, damage) * ImpulsePerDamage + Mathf.Max(0f, impactForce) * PhysicsStep * ForceImpulseScale);

        /// <summary>
        /// Толчок по попаданию. <paramref name="bodyCenter"/> — центр тела жертвы для взрыва: точка толчка
        /// и направление от центра взрыва. Брать <see cref="PlayerBodyCenter.Of"/>, не корень аватара — при
        /// физической ходьбе корень стоит в стороне от тела.
        /// </summary>
        public static DeathImpact From(UxrDamageEventArgs e, Vector3 bodyCenter)
        {
            if (e == null) return None;

            if (e.DamageType == UxrDamageType.Explosive)
                return Along(bodyCenter, bodyCenter - e.ExplosionPosition, Magnitude(e.Damage, 0f), e.DamageType);

            if (e.RaycastHit.collider == null) return new DeathImpact(Vector3.zero, Vector3.zero, e.DamageType);

            Vector3 point = e.RaycastHit.point;
            Vector3 direction = e.ImpactForce.sqrMagnitude > 0f ? e.ImpactForce
                : e.ActorSource != null ? point - e.ActorSource.transform.position
                : -e.RaycastHit.normal;

            return Along(point, direction, Magnitude(e.Damage, e.ImpactForce.magnitude), e.DamageType);
        }

        /// <summary>
        /// Два попадания одного выстрела (дробины): импульсы складываются, точка — последнего, итог с пределом.
        /// </summary>
        public static DeathImpact Combine(DeathImpact first, DeathImpact second)
        {
            if (!first.HasImpulse) return second;
            if (!second.HasImpulse) return first;
            return new DeathImpact(second.Point, Vector3.ClampMagnitude(first.Impulse + second.Impulse, MaxImpulse),
                                   second.DamageType ?? first.DamageType);
        }

        private static DeathImpact Along(Vector3 point, Vector3 direction, float magnitude, UxrDamageType damageType)
        {
            if (direction.sqrMagnitude < 1e-6f) return new DeathImpact(point, Vector3.zero, damageType);
            return new DeathImpact(point, direction.normalized * magnitude, damageType);
        }
    }
}
