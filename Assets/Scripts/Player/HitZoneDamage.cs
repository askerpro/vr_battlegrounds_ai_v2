using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Множитель урона пули по зоне попадания (T-38): голова ×3 (не ×4, как в CS2: в VR попасть в голову
    /// труднее, чем мышью — решение пользователя), торс и руки ×1, ноги ×0,75. Живота как отдельной зоны у
    /// хитбоксов нет (<see cref="HitZone"/>), он — торс.
    ///
    /// <para>
    /// Применяется в SDK до события урона (<c>UxrActor.ImpactDamageModifier</c>, патч 31): урон у события
    /// неизменяемый, а «смертельный ли» SDK считает при его создании. Ставится при загрузке — и в игре, и в
    /// редакторе (тесты). Одинаково на каждой машине: зона — по коллайдеру того же луча.
    /// </para>
    /// </summary>
    public static class HitZoneDamage
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install() => UxrActor.ImpactDamageModifier = (actor, hit, damage) => Apply(hit, damage);

        public const float Head = 3f;
        public const float Leg = 0.75f;

        public static float Multiplier(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return Head;
                case HitZone.Leg: return Leg;
                default: return 1f;
            }
        }

        /// <summary>Урон пули с учётом зоны: попадание не в хитбокс игрока (мишень, предмет) — как есть.</summary>
        public static float Apply(RaycastHit hit, float damage) =>
            Hitbox.TryGetPart(hit.collider, out HitZone zone) ? damage * Multiplier(zone) : damage;
    }
}
