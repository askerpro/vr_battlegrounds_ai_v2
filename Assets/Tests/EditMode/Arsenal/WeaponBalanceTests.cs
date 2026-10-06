using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Баланс оружия (T-38): числа — по Counter-Strike 2 (урон, экономика, разброс, сила отдачи), единая точка правды —
    /// <see cref="WeaponInfo" />, префабы получают их командой <c>Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance</c>.
    ///
    /// <para>
    /// Классы ошибок, которые ловит тест: урон, темп и магазин жили в трёх местах префаба (дескриптор выстрела,
    /// спуск, магазин) и правились руками — число в арсенале расходилось с тем, чем стреляет оружие; разброс и отдача
    /// стояли «по памяти» и донорскими значениями SDK, одинаковыми у пистолета и ПП («разброс MP5K»).
    /// </para>
    /// </summary>
    public class WeaponBalanceTests
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        // Роль ствола в CS2 — числа из weapons.vdata CS2 (SteamDatabase/GameTracking-CS2, сверено 2026-10-01; таблица и
        // ссылки — Docs/tasks/T-38-weapon-roster-and-balance.md). Отступления от CS2 — с комментарием в строке.
        // Разброс — единицы скрипта CS (мрад = vdata × 1000); у снайперских — значения в прицеле (…Alt), у M4A1-S — с
        // глушителем (в CS это Secondary_Mode и режим по умолчанию).
        private struct Cs2
        {
            public string Role;
            public float Damage, RangeModifier, Penetration, ArmorRatio, RecoilMagnitude;
            public int FireRate, Magazine, Reserve, Price, KillAward, Pellets;
            public bool FullAuto;
            public SpreadPattern Spread;

            public Cs2(string role, float damage, float rangeModifier, float penetration, float armorRatio, int fireRate, bool fullAuto,
                       int magazine, int reserve, int price, int killAward, SpreadPattern spread, float recoilMagnitude, int pellets = 1)
            {
                Role = role; Damage = damage; RangeModifier = rangeModifier; Penetration = penetration; ArmorRatio = armorRatio;
                FireRate = fireRate; FullAuto = fullAuto; Magazine = magazine; Reserve = reserve; Price = price; KillAward = killAward;
                Spread = spread; RecoilMagnitude = recoilMagnitude; Pellets = pellets;
            }
        }

        // spread, inaccuracy_stand, inaccuracy_move, inaccuracy_fire (мрад), recovery_time_stand (с).
        private static SpreadPattern S(float spread, float stand, float move, float fire, float recovery) =>
            new SpreadPattern(spread, stand, move, fire, recovery);

        private static readonly Cs2 Glock = new Cs2("Glock-18", 30f, 0.85f, 1f, 0.94f, 400, false, 20, 60, 200, 300, S(2f, 5.6f, 10f, 56f, 0.2f), 18f);
        private static readonly Cs2 Mp9 = new Cs2("MP9", 26f, 0.87f, 1f, 1.2f, 857, true, 30, 60, 1250, 600, S(0.6f, 9f, 29.04f, 3.7f, 0.25789f), 21f);
        private static readonly Cs2 Ssg08 = new Cs2("SSG 08", 88f, 0.98f, 2.5f, 1.7f, 48, false, 10, 20, 1700, 300, S(0.23f, 3f, 123.45f, 22.92f, 0.142096f), 25f);

        private static Cs2 WithMagazine(Cs2 cs, int magazine)
        {
            cs.Magazine = magazine;
            return cs;
        }

        // Ключ — имя ассета WeaponInfo. Новый ствол арсенала — строка сюда.
        private static readonly Dictionary<string, Cs2> Roster = new Dictionary<string, Cs2>
        {
            // ── Реестр (стена арсенала), T-39 ──
            { "Viper_Weapon",       Glock },
            { "Gun_Weapon",         new Cs2("P250", 38f, 0.90f, 1f, 1.28f, 400, false, 13, 39, 300, 300, S(2f, 9.1f, 20f, 52.45f, 0.345388f), 26f) },
            // Тяжёлый пистолет — револьвер пака в роли Deagle (решение пользователя); барабан на 6, а не 7.
            { "Revolver_Weapon",    new Cs2("Desert Eagle", 53f, 0.85f, 2f, 1.864f, 267, false, 6, 21, 700, 300, S(2f, 4.2f, 48.1f, 72.23f, 0.8112f), 48.2f) },
            { "Uzi_Weapon",         new Cs2("MAC-10", 29f, 0.80f, 1f, 1.15f, 800, true, 30, 90, 1050, 600, S(0.6f, 13.3f, 13.99f, 4.76f, 0.399729f), 18f) },
            { "MKR9_Weapon",        Mp9 },
            // Nova в CS2 — 26 × 9. В VR вся дробь вблизи попадает (разброса мыши нет), 234 — впятеро больше
            // нужного; урон дробинки снижен до 16 (144 всей дробью), см. ShotgunPelletsTests.
            { "ShotgunReal_Weapon", new Cs2("Nova", 16f, 0.70f, 1f, 1f, 68, false, 8, 32, 1050, 900, S(40f, 7f, 36.75f, 9.72f, 0.460517f), 143f, 9) },
            // XM1014 в CS2 — 20 × 6. Как у Nova, дробина снижена в той же пропорции (16/26): в VR дробь вблизи вся в цели.
            { "Herrington_Weapon",  new Cs2("XM1014", 12f, 0.70f, 1f, 1.6f, 171, true, 7, 32, 2000, 600, S(38f, 7f, 36.03f, 8.83f, 0.506569f), 80f, 6) },
            // M4A1-S с глушителем (режим CS по умолчанию): spread 0.5, move 122, fire 7, отдача 21.
            { "TR15_Weapon",        new Cs2("M4A1-S", 38f, 0.94f, 2f, 1.4f, 600, true, 20, 60, 2900, 300, S(0.5f, 4.9f, 122f, 7f, 0.338941f), 21f) },
            // T-39: скаут из пака KINEMATION (Desert Tech SRS). Магазин 5, а не 10 как у SSG 08: модель магазина пака
            // на 5 патронов .338, а в VR смена магазина — движение руки, не штраф; запасных магазинов больше.
            { "SRM12_Weapon",       WithMagazine(Ssg08, 5) },
            { "Mk14_Weapon",        new Cs2("SCAR-20", 80f, 0.98f, 2.5f, 1.65f, 240, true, 20, 40, 5000, 300, S(0.3f, 2f, 150.48f, 18.61f, 0.544331f), 31f) },

            // ── Вне реестра (префабы оставлены, баланс поддерживается) ──
            { "M16_Weapon",         new Cs2("M4A4", 33f, 0.97f, 2f, 1.4f, 666, true, 30, 120, 2900, 300, S(0.6f, 4.9f, 137.88f, 7f, 0.338941f), 23f) },
            { "Scar_Weapon",        new Cs2("AK-47", 36f, 0.98f, 2f, 1.55f, 600, true, 30, 90, 2700, 300, S(0.6f, 6.41f, 175.06f, 7.8f, 0.368f), 30f) },
            { "MP5K_Weapon",        Mp9 },
            { "PPK_Weapon",         Glock },
            { "SniperRifle_Weapon", Ssg08 },
        };

        private static IEnumerable<WeaponInfo> Weapons()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsFolder }))
                yield return AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
        }

        // Спуск SDK (UxrFirearmTrigger) — internal-класс: частота и индекс выстрела читаются сериализацией.
        internal static List<(int shotIndex, int frequency)> TriggersOf(UxrFirearmWeapon weapon)
        {
            var result = new List<(int, int)>();
            SerializedProperty triggers = new SerializedObject(weapon).FindProperty("_triggers");
            for (int i = 0; i < triggers.arraySize; i++)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
                result.Add((trigger.FindPropertyRelative("_projectileShotIndex").intValue,
                            trigger.FindPropertyRelative("_maxShotFrequency").intValue));
            }
            return result;
        }

        /// <summary>
        /// Толчок отдачи SDK у каждого спуска — из баланса и гаснет до следующего выстрела. Класс ошибки «разброс MP5K»:
        /// донорский толчок 6°/2° на 0,3 с стоял у всех стволов одинаково; при интервале очереди 0,07 с он не успевал
        /// погаснуть — ствол ПП постоянно задран и дрожит, причём тем сильнее, чем выше темп.
        /// </summary>
        private static IEnumerable<string> SdkRecoilProblems(UxrFirearmWeapon weapon, RecoilPattern want, int frequency)
        {
            float interval = 1f / frequency;
            SerializedProperty triggers = new SerializedObject(weapon).FindProperty("_triggers");
            for (int i = 0; i < triggers.arraySize; i++)
            {
                SerializedProperty t = triggers.GetArrayElementAtIndex(i);
                float duration = t.FindPropertyRelative("_recoilDurationSeconds").floatValue;
                if (duration >= interval)
                    yield return $"спуск {i}: толчок SDK {duration:F3} с не короче интервала очереди {interval:F3} с — не гаснет, ствол задран.";
                else if (!Mathf.Approximately(duration, RecoilPattern.SdkDuration(interval)))
                    yield return $"спуск {i}: длительность толчка SDK {duration:F3} с, по балансу {RecoilPattern.SdkDuration(interval):F3} с.";
                if (!Mathf.Approximately(t.FindPropertyRelative("_recoilAngleTwoHands").floatValue, want.SdkAngle(false)) ||
                    !Mathf.Approximately(t.FindPropertyRelative("_recoilAngleOneHand").floatValue, want.SdkAngle(true)))
                    yield return $"спуск {i}: угол толчка SDK не по балансу ({want.SdkAngle(false):F2}° / {want.SdkAngle(true):F2}°).";
                if (Mathf.Abs(t.FindPropertyRelative("_recoilOffsetTwoHands").vector3Value.z + want.SdkOffset(false)) > 1e-4f ||
                    Mathf.Abs(t.FindPropertyRelative("_recoilOffsetOneHand").vector3Value.z + want.SdkOffset(true)) > 1e-4f)
                    yield return $"спуск {i}: отдача назад SDK не по балансу.";
            }
        }

        /// <summary>
        /// Каждый ствол реестра (то, что продаёт стена) — в таблице ролей CS2. Иначе новый ствол уходит на стену с числами
        /// донора, и никто этого не видит: проверка значений выше ходит только по строкам таблицы.
        /// </summary>
        [Test]
        public void Каждый_ствол_реестра_в_таблице_CS2()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>($"{WeaponsFolder}/Resources/WeaponRegistry.asset");
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");
            var missing = new List<string>();
            foreach (WeaponInfo info in registry.Weapons)
                if (info != null && !Roster.ContainsKey(info.name)) missing.Add(info.name);
            Assert.IsEmpty(missing, "Стволы реестра без роли CS2 в WeaponBalanceTests.Roster: " + string.Join(", ", missing));
        }

        [Test]
        public void Спад_урона_как_в_CS2()
        {
            Assert.AreEqual(36f * 0.98f, WeaponInfo.DamageAt(36f, 0.98f, 12.7f), 0.01f, "500 юнитов (12,7 м) — один множитель спада.");
            Assert.AreEqual(36f, WeaponInfo.DamageAt(36f, 0.98f, 0f), 0.001f);
            Assert.AreEqual(50f, WeaponInfo.DamageAt(50f, 1f, 300f), 0.001f, "Множитель 1 — без спада.");
            Assert.AreEqual(10, WeaponInfo.ShotFrequency(600));
            Assert.AreEqual(1, WeaponInfo.ShotFrequency(48), "Болтовка — не реже раза в секунду по SDK (частота целая).");
        }

        [Test]
        public void Баланс_стволов_как_в_CS2()
        {
            var failures = new List<string>();
            foreach (var pair in Roster)
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>($"{WeaponsFolder}/{pair.Key}.asset");
                if (info == null) { failures.Add($"{pair.Key}: нет ассета."); continue; }

                Cs2 cs = pair.Value;
                void Check(string what, float got, float want)
                {
                    if (!Mathf.Approximately(got, want)) failures.Add($"{pair.Key} ({cs.Role}): {what} {got}, в CS2 {want}.");
                }

                Check("урон", info.Damage, cs.Damage);
                Check("спад", info.RangeModifier, cs.RangeModifier);
                Check("пробитие", info.Penetration, cs.Penetration);
                Check("armor_ratio", info.ArmorRatio, cs.ArmorRatio);
                Check("темп", info.FireRate, cs.FireRate);
                Check("магазин", info.MagazineSize, cs.Magazine);
                Check("запас", info.ReserveAmmo, cs.Reserve);
                Check("цена", info.Price, cs.Price);
                Check("награда за убийство", info.KillAward, cs.KillAward);
                Check("дробинок", info.Pellets, cs.Pellets);
                Check("recoil_magnitude", info.RecoilMagnitude, cs.RecoilMagnitude);
                if (info.FullAuto != cs.FullAuto) failures.Add($"{pair.Key} ({cs.Role}): автоматический огонь {info.FullAuto}, в CS2 {cs.FullAuto}.");
                if (!cs.Spread.SameAs(info.Spread)) failures.Add($"{pair.Key} ({cs.Role}): разброс [{info.Spread}], в CS2 [{cs.Spread}].");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Префаб стреляет тем, что записано в <see cref="WeaponInfo" />: урон вблизи и на предельной дистанции
        /// (SDK интерполирует линейно между ними), частота спуска, толчок отдачи SDK, ёмкость и заряд магазина, число
        /// дробинок и их конус, накопленная отдача и разброс CS2.
        /// </summary>
        [Test]
        public void Префаб_стреляет_по_балансу()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (WeaponInfo info in Weapons())
            {
                if (!info.HasBalance || info.WeaponPrefab == null) continue;
                checks++;

                string name = info.name;
                var weapon = info.WeaponPrefab.GetComponent<UxrFirearmWeapon>();
                var source = info.WeaponPrefab.GetComponent<UxrProjectileSource>();
                if (weapon == null || source == null) { failures.Add($"{name}: префаб без UxrFirearmWeapon/UxrProjectileSource."); continue; }

                var pellets = info.WeaponPrefab.GetComponent<ShotgunPellets>();
                int pelletCount = pellets != null ? pellets.Pellets : 1;
                if (pelletCount != info.Pellets) failures.Add($"{name}: дробинок в префабе {pelletCount}, в балансе {info.Pellets}.");

                var shotIndices = new List<int>();
                foreach (var trigger in TriggersOf(weapon))
                {
                    shotIndices.Add(trigger.shotIndex);
                    int frequency = WeaponInfo.ShotFrequency(info.FireRate);
                    if (trigger.frequency != frequency)
                        failures.Add($"{name}: частота спуска {trigger.frequency}/с, по балансу {frequency}/с ({info.FireRate} в минуту).");
                }
                if (pellets != null) shotIndices.Add(pellets.PelletShotIndex);

                foreach (int index in shotIndices)
                {
                    UxrShotDescriptor shot = source.ShotTypes[index];
                    float far = WeaponInfo.DamageAt(info.Damage, info.RangeModifier, shot.ProjectileMaxDistance);
                    if (Mathf.Abs(shot.ProjectileDamageNear - info.Damage) > 0.01f)
                        failures.Add($"{name}: выстрел {index} — урон вблизи {shot.ProjectileDamageNear}, по балансу {info.Damage}.");
                    if (Mathf.Abs(shot.ProjectileDamageFar - far) > 0.05f)
                        failures.Add($"{name}: выстрел {index} — урон на {shot.ProjectileMaxDistance} м {shot.ProjectileDamageFar}, по балансу {far:F2}.");
                    if (!Mathf.Approximately(shot.PenetrationPower, info.Penetration))
                        failures.Add($"{name}: выстрел {index} — пробитие {shot.PenetrationPower}, по балансу {info.Penetration}.");
                }

                RecoilPattern want = info.Recoil;
                foreach (string problem in SdkRecoilProblems(weapon, want, WeaponInfo.ShotFrequency(info.FireRate)))
                    failures.Add($"{name}: {problem}");

                var spread = info.WeaponPrefab.GetComponent<WeaponSpread>();
                // Решение 2026-10-02: у пуль только физическая отдача ствола, у дроби собственный spread.
                if (info.Pellets <= 1)
                {
                    if (spread != null) failures.Add($"{name}: у пулевого оружия остался WeaponSpread.");
                }
                else if (spread == null) failures.Add($"{name}: нет разлёта первой дробины.");
                else if (!Mathf.Approximately(spread.Pattern.Spread, info.Spread.Spread) ||
                         spread.Pattern.InaccuracyStand != 0f || spread.Pattern.InaccuracyMove != 0f ||
                         spread.Pattern.InaccuracyFire != 0f)
                    failures.Add($"{name}: дробь должна сохранять spread без общей неточности: [{spread.Pattern}].");
                if (pellets != null && Mathf.Abs(pellets.SpreadDegrees - WeaponAccuracy.ToDegrees(info.Spread.Spread)) > 0.001f)
                    failures.Add($"{name}: конус дроби {pellets.SpreadDegrees}°, по балансу {WeaponAccuracy.ToDegrees(info.Spread.Spread):F3}°.");

                var recoil = info.WeaponPrefab.GetComponent<RecoilAccumulator>();
                if (recoil == null) failures.Add($"{name}: нет RecoilAccumulator — очередь не уводит ствол.");
                else
                {
                    if (!want.SameAs(recoil.Pattern))
                        failures.Add($"{name}: картина отдачи префаба не совпадает с балансом.");

                    var axes = new SerializedObject(recoil).FindProperty("_axes").objectReferenceValue;
                    var weaponAxes = new SerializedObject(weapon).FindProperty("_recoilAxes").objectReferenceValue;
                    if (axes != weaponAxes) failures.Add($"{name}: оси отдачи RecoilAccumulator не те, что у UxrFirearmWeapon.");
                }
                if (want.KickDegrees <= 0f || want.MaxPitchDegrees <= 0f)
                    failures.Add($"{name}: сила отдачи CS2 (recoil_magnitude) в балансе не задана.");

                // Ручное заряжание: «магазин» — встроенный запас в оружии, MagazinePrefab — одиночный патрон.
                bool manualLoading = info.ReadinessProfile != null &&
                                     info.ReadinessProfile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber;
                if (manualLoading && (info.MagazinePrefab == null || info.MagazinePrefab.GetComponent<Cartridge>() == null))
                    failures.Add($"{name}: ручное заряжание, а MagazinePrefab не патрон с Cartridge.");
                var mag = manualLoading
                    ? info.WeaponPrefab.GetComponentsInChildren<UxrFirearmMag>(true).FirstOrDefault(store => store.IsFixedAmmoStore)
                    : info.MagazinePrefab != null ? info.MagazinePrefab.GetComponentInChildren<UxrFirearmMag>(true) : null;
                if (mag == null) failures.Add(manualLoading ? $"{name}: нет встроенного запаса (fixed store)." : $"{name}: нет магазина с UxrFirearmMag.");
                else if (mag.Capacity != info.MagazineSize || mag.Rounds != info.MagazineSize)
                    failures.Add($"{name}: магазин {mag.Rounds}/{mag.Capacity}, по балансу {info.MagazineSize}.");
            }

            Assert.Greater(checks, 0, "Ни у одного WeaponInfo не задан баланс.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// ПП управляемы, как в CS2: подброс первого выстрела и потолок очереди у ПП не больше, чем у винтовок, а конус
        /// очереди не шире пары градусов. Ловит класс «разброс MP5K» на уровне таблицы, а не префаба.
        /// </summary>
        [Test]
        public void Пистолеты_пулемёты_управляемы()
        {
            var failures = new List<string>();
            RecoilPattern rifle = RecoilPattern.FromCs2(Roster["TR15_Weapon"].RecoilMagnitude, true);
            foreach (string key in new[] { "Uzi_Weapon", "MKR9_Weapon" })
            {
                Cs2 cs = Roster[key];
                RecoilPattern smg = RecoilPattern.FromCs2(cs.RecoilMagnitude, cs.FullAuto);
                if (smg.KickDegrees > rifle.KickDegrees + 1e-4f) failures.Add($"{key}: подброс {smg.KickDegrees:F2}° больше винтовки {rifle.KickDegrees:F2}°.");
                if (smg.MaxPitchDegrees > rifle.MaxPitchDegrees + 1e-4f) failures.Add($"{key}: потолок {smg.MaxPitchDegrees:F2}° выше винтовки {rifle.MaxPitchDegrees:F2}°.");

                var accuracy = new WeaponAccuracy(cs.Spread);
                float cone = WeaponAccuracy.ToDegrees(cs.Spread.InaccuracyStand + accuracy.SteadyPenalty(60f / cs.FireRate) + cs.Spread.Spread);
                if (cone > 2f) failures.Add($"{key}: конус длинной очереди {cone:F2}° — шире 2°.");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
