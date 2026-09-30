using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Баланс оружия (T-38): числа — по Counter-Strike 2, единая точка правды — <see cref="WeaponInfo" />,
    /// префабы получают их командой <c>Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance</c>.
    ///
    /// <para>
    /// Класс ошибки, который ловит тест: урон, темп и магазин жили в трёх местах префаба (дескриптор выстрела,
    /// спуск, магазин) и правились руками — число в арсенале расходилось с тем, чем стреляет оружие.
    /// </para>
    /// </summary>
    public class WeaponBalanceTests
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        // Роль ствола в CS2: урон пули, спад на 500 юнитов, пробитие стен (T-41), выстрелов в минуту, патронов, цена,
        // дробинок. Пробитие CS2: пистолеты, SMG и дробовики — 1, Desert Eagle и винтовки — 2, снайперские — 2.5.
        private struct Cs2
        {
            public string Role;
            public float Damage, RangeModifier, Penetration;
            public int FireRate, Magazine, Price, Pellets;

            public Cs2(string role, float damage, float rangeModifier, float penetration, int fireRate, int magazine, int price, int pellets = 1)
            {
                Role = role; Damage = damage; RangeModifier = rangeModifier; Penetration = penetration; FireRate = fireRate;
                Magazine = magazine; Price = price; Pellets = pellets;
            }
        }

        // Ключ — имя ассета WeaponInfo. Новый ствол арсенала — строка сюда.
        private static readonly Dictionary<string, Cs2> Roster = new Dictionary<string, Cs2>
        {
            { "Gun_Weapon",         new Cs2("P250", 38f, 0.90f, 1f, 400, 13, 300) },
            { "M16_Weapon",         new Cs2("M4A4", 33f, 0.97f, 2f, 666, 30, 3100) },
            // Nova в CS2 — 26 × 9. В VR вся дробь вблизи попадает (разброса мыши нет), 234 — впятеро больше
            // нужного; урон дробинки снижен до 16 (144 всей дробью), см. ShotgunPelletsTests.
            { "ShotgunReal_Weapon", new Cs2("Nova", 16f, 0.70f, 1f, 68, 8, 1050, 9) },
            { "Scar_Weapon",        new Cs2("AK-47", 36f, 0.98f, 2f, 600, 30, 2700) },
            { "Uzi_Weapon",         new Cs2("MAC-10", 29f, 0.80f, 1f, 800, 30, 1050) },
            { "MP5K_Weapon",        new Cs2("MP9", 26f, 0.87f, 1f, 857, 30, 1250) },
            { "PPK_Weapon",         new Cs2("Glock-18", 30f, 0.85f, 1f, 400, 20, 200) },
            // Тяжёлый пистолет — револьвер пака в роли Deagle (решение пользователя); барабан на 6, а не 7.
            { "Revolver_Weapon",    new Cs2("Desert Eagle", 53f, 0.81f, 2f, 267, 6, 700) },
            { "SniperRifle_Weapon", new Cs2("SSG 08", 88f, 0.98f, 2.5f, 48, 10, 1700) },
            // T-39: скаут из пака KINEMATION (Desert Tech SRS). Магазин 5, а не 10 как у SSG 08: модель магазина пака
            // на 5 патронов .338, а в VR смена магазина — движение руки, не штраф; запасных магазинов больше.
            { "SRM12_Weapon",       new Cs2("SSG 08", 88f, 0.98f, 2.5f, 48, 5, 1700) },
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
                if (!Mathf.Approximately(info.Damage, cs.Damage)) failures.Add($"{pair.Key} ({cs.Role}): урон {info.Damage}, в CS2 {cs.Damage}.");
                if (!Mathf.Approximately(info.RangeModifier, cs.RangeModifier)) failures.Add($"{pair.Key} ({cs.Role}): спад {info.RangeModifier}, в CS2 {cs.RangeModifier}.");
                if (!Mathf.Approximately(info.Penetration, cs.Penetration)) failures.Add($"{pair.Key} ({cs.Role}): пробитие {info.Penetration}, в CS2 {cs.Penetration}.");
                if (info.FireRate != cs.FireRate) failures.Add($"{pair.Key} ({cs.Role}): темп {info.FireRate}, в CS2 {cs.FireRate}.");
                if (info.MagazineSize != cs.Magazine) failures.Add($"{pair.Key} ({cs.Role}): магазин {info.MagazineSize}, в CS2 {cs.Magazine}.");
                if (info.Price != cs.Price) failures.Add($"{pair.Key} ({cs.Role}): цена {info.Price}, в CS2 {cs.Price}.");
                if (info.Pellets != cs.Pellets) failures.Add($"{pair.Key} ({cs.Role}): дробинок {info.Pellets}, в CS2 {cs.Pellets}.");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// Префаб стреляет тем, что записано в <see cref="WeaponInfo" />: урон вблизи и на предельной дистанции
        /// (SDK интерполирует линейно между ними), частота спуска, ёмкость и заряд магазина, число дробинок.
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

                var recoil = info.WeaponPrefab.GetComponent<RecoilAccumulator>();
                if (recoil == null) failures.Add($"{name}: нет RecoilAccumulator — очередь не уводит ствол.");
                else
                {
                    RecoilPattern want = info.Recoil, got = recoil.Pattern;
                    if (!Mathf.Approximately(want.KickDegrees, got.KickDegrees) || !Mathf.Approximately(want.MaxPitchDegrees, got.MaxPitchDegrees) ||
                        !Mathf.Approximately(want.MaxYawDegrees, got.MaxYawDegrees) || !Mathf.Approximately(want.RecoveryTime, got.RecoveryTime) ||
                        !Mathf.Approximately(want.OneHandMultiplier, got.OneHandMultiplier))
                        failures.Add($"{name}: картина отдачи префаба не совпадает с балансом.");

                    var axes = new SerializedObject(recoil).FindProperty("_axes").objectReferenceValue;
                    var weaponAxes = new SerializedObject(weapon).FindProperty("_recoilAxes").objectReferenceValue;
                    if (axes != weaponAxes) failures.Add($"{name}: оси отдачи RecoilAccumulator не те, что у UxrFirearmWeapon.");
                }
                if (info.Recoil.KickDegrees <= 0f || info.Recoil.MaxPitchDegrees <= 0f)
                    failures.Add($"{name}: картина отдачи в балансе не задана.");

                var mag = info.MagazinePrefab != null ? info.MagazinePrefab.GetComponentInChildren<UxrFirearmMag>(true) : null;
                if (mag == null) failures.Add($"{name}: нет магазина с UxrFirearmMag.");
                else if (mag.Capacity != info.MagazineSize || mag.Rounds != info.MagazineSize)
                    failures.Add($"{name}: магазин {mag.Rounds}/{mag.Capacity}, по балансу {info.MagazineSize}.");
            }

            Assert.Greater(checks, 0, "Ни у одного WeaponInfo не задан баланс.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
