using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Прострел стен по формуле Counter-Strike (T-41): пробивает ли пуля препятствие и сколько урона теряет.
    ///
    /// <para>
    /// Ставится в SDK (<c>UxrWeaponManager.ProjectilePenetration</c>, патч 32) при загрузке — в игре и в редакторе.
    /// Класс препятствия — <see cref="CoverSurface"/> на коллайдере или выше; без разметки — Hard, пуля
    /// останавливается (в CS пробиваемо всё, у нас Hard — осознанное «никогда», LD-27). Visual пуля пролетает без потерь.
    /// Soft считается по CS.
    /// </para>
    ///
    /// <para>
    /// <b>Формула</b> (CS:GO, восстановлена по коду игры; CS2 по открытым данным та же):
    /// <c>потеря = (1/pm)·t²/24 + урон·0.16 + (3.75/пробитие)·3·(1/pm)</c>, где <c>t</c> — толщина в юнитах CS
    /// (дюймах, <see cref="WeaponInfo.CsUnitsPerMeter"/>), <c>pm</c> — <see cref="CoverSurface.PenetrationModifier"/>,
    /// пробитие — <see cref="UxrShotDescriptor.PenetrationPower"/>. Потеря больше урона или остаток меньше
    /// <see cref="MinDamage"/> — пуля застряла. Первое слагаемое — толщина (квадратом), второе — доля текущего урона,
    /// третье — «плата за вход», слабое оружие платит больше. Константы — только здесь: если замер в CS2 разойдётся,
    /// правится одно место (<c>WallPenetrationTests</c> держит числа).
    /// </para>
    ///
    /// <para>
    /// <b>Толщина</b> — обратным лучом по тому же коллайдеру: из точки на <see cref="MaxThickness"/> за входом назад к
    /// нему; первая грань — выходная (как <c>TraceToExit</c> CS, до 90 юнитов). Плоский одногранный коллайдер (Quad)
    /// выхода не имеет — Soft должен быть объёмом реальной толщины (LD-30).
    /// </para>
    ///
    /// <para>
    /// <b>Урон дальше.</b> SDK хранит у пули множитель урона и передаёт текущий урон (спад по дистанции × множитель);
    /// новый множитель = старый × остаток / текущий. После стены спад продолжается от остатка — как в CS.
    /// </para>
    ///
    /// <para>
    /// <b>Сеть.</b> Пулю ведёт каждая машина (повтор <c>Shoot</c>), урон вычитает только сервер. Геометрия карты
    /// одинакова везде, поэтому и решение одинаково; новых сообщений нет.
    /// </para>
    /// </summary>
    public static class WallPenetration
    {
        /// <summary>Слагаемое толщины: (1/pm)·t²/<see cref="ThicknessDivisor"/>.</summary>
        public const float ThicknessDivisor = 24f;

        /// <summary>Доля текущего урона, теряемая на каждом препятствии.</summary>
        public const float DamageFractionLost = 0.16f;

        /// <summary>«Плата за вход»: (<see cref="EntryCost"/>/пробитие)·<see cref="EntryCostScale"/>·(1/pm).</summary>
        public const float EntryCost = 3.75f;

        public const float EntryCostScale = 3f;

        /// <summary>Сколько препятствий пуля пробивает за полёт (в CS — 4).</summary>
        public const int MaxPenetrations = 4;

        /// <summary>Урон, ниже которого пуля застревает.</summary>
        public const float MinDamage = 1f;

        /// <summary>pm ниже этого — материал не пробивается (как в CS).</summary>
        public const float MinPenetrationModifier = 0.1f;

        /// <summary>Дальше всего ищется выход, юнитов CS (TraceToExit).</summary>
        public const float MaxExitSearchUnits = 90f;

        /// <summary>Самое толстое пробиваемое препятствие, м (90 юнитов = 2.29 м).</summary>
        public static float MaxThickness => MaxExitSearchUnits / WeaponInfo.CsUnitsPerMeter;

        /// <summary>На сколько пуля выходит за грань, чтобы следующий луч не поймал то же препятствие, м.</summary>
        public const float ExitOffset = 0.01f;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install() => UxrWeaponManager.ProjectilePenetration = Evaluate;

        public static UxrPenetrationResult Evaluate(RaycastHit hit, Vector3 direction, UxrShotDescriptor shot, int penetrations,
                                                    float currentDamage, float damageMultiplier) =>
            Evaluate(hit, direction, shot != null ? shot.PenetrationPower : 0f, penetrations, currentDamage, damageMultiplier);

        public static UxrPenetrationResult Evaluate(RaycastHit hit, Vector3 direction, float power, int penetrations,
                                                    float currentDamage, float damageMultiplier)
        {
            CoverSurface surface = CoverSurface.Of(hit.collider);
            CoverClass coverClass = surface != null ? surface.Class : CoverClass.Hard;
            direction = direction.normalized;

            if (coverClass == CoverClass.Visual)
            {
                return new UxrPenetrationResult
                {
                    Kind = UxrPenetrationKind.PassThrough,
                    ExitPoint = hit.point + direction * ExitOffset,
                    DamageMultiplier = damageMultiplier
                };
            }

            if (coverClass != CoverClass.Soft || penetrations >= MaxPenetrations || currentDamage <= 0f) return UxrPenetrationResult.Stop;
            if (!TryFindExit(hit.collider, hit.point, direction, out RaycastHit exit)) return UxrPenetrationResult.Stop;

            float thickness = Vector3.Distance(hit.point, exit.point);
            float loss = Loss(currentDamage, thickness, surface.PenetrationModifier, power);
            float remaining = currentDamage - loss;
            if (loss > currentDamage || remaining < MinDamage) return UxrPenetrationResult.Stop;

            return new UxrPenetrationResult
            {
                Kind = UxrPenetrationKind.Penetrate,
                ExitPoint = exit.point + direction * ExitOffset,
                ExitHit = exit,
                DamageMultiplier = damageMultiplier * remaining / currentDamage
            };
        }

        /// <summary>
        /// Потеря урона на препятствии по CS; бесконечность — не пробивается вовсе (пробитие 0 или pm &lt; 0.1).
        /// </summary>
        public static float Loss(float damage, float thicknessMeters, float penetrationModifier, float power)
        {
            if (power <= 0f || penetrationModifier < MinPenetrationModifier) return float.PositiveInfinity;

            float modifier = 1f / penetrationModifier;
            float units = thicknessMeters * WeaponInfo.CsUnitsPerMeter;
            return modifier * units * units / ThicknessDivisor
                 + damage * DamageFractionLost
                 + EntryCost / power * EntryCostScale * modifier;
        }

        /// <summary>Выходная грань препятствия не дальше <see cref="MaxThickness"/> от входа.</summary>
        public static bool TryFindExit(Collider collider, Vector3 entry, Vector3 direction, out RaycastHit exit)
        {
            exit = default;
            if (collider == null) return false;
            float reach = MaxThickness;
            var back = new Ray(entry + direction * reach, -direction);
            return collider.Raycast(back, out exit, reach);
        }
    }
}
