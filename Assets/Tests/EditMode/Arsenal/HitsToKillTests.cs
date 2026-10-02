using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Матрица «попаданий до убийства» (T-38, этап 2) — главный артефакт баланса: оружие реестра × зона × дистанция →
    /// сколько попаданий убивают игрока со 100 жизни. Менять числа баланса — менять матрицу осознанно.
    ///
    /// <para>
    /// Считается так, как считает игра: урон выстрела — линейно между уроном вблизи и на предельной дистанции
    /// дескриптора SDK (<c>ProjectileDamageNear/Far</c>, их пишет <c>Apply Weapon Balance</c>), множитель зоны —
    /// <see cref="HitZoneDamage" /> (голова ×3, ноги ×0,75), брони нет. У дробовика — вся дробь в цели.
    /// </para>
    ///
    /// <para>
    /// Отличия от CS2 (без брони, голова ×4) — осознанные: голова ×3 — решение пользователя, поэтому Glock, MAC-10 и MP9
    /// убивают в голову с двух, а не с одного; линейный спад SDK на средней дистанции мягче степенного CS. Полное
    /// совпадение HTK с CS2 (броня, живот) — T-43.
    /// </para>
    /// </summary>
    public class HitsToKillTests
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";
        private const float Life = 100f;
        private static readonly float[] Distances = { 0f, 10f, 25f };

        // Голова / торс / ноги на 0, 10 и 25 м.
        private static readonly Dictionary<string, int[,]> Expected = new Dictionary<string, int[,]>
        {
            { "Viper_Weapon",       new[,] { { 2, 4, 5 }, { 2, 4, 5 }, { 2, 4, 6 } } },
            { "Gun_Weapon",         new[,] { { 1, 3, 4 }, { 1, 3, 4 }, { 2, 4, 5 } } },
            { "Revolver_Weapon",    new[,] { { 1, 2, 3 }, { 1, 3, 3 }, { 1, 3, 4 } } },
            { "Uzi_Weapon",         new[,] { { 2, 4, 5 }, { 2, 4, 5 }, { 2, 5, 6 } } },
            { "MKR9_Weapon",        new[,] { { 2, 4, 6 }, { 2, 5, 6 }, { 2, 5, 7 } } },
            { "ShotgunReal_Weapon", new[,] { { 1, 1, 1 }, { 1, 1, 2 }, { 1, 2, 2 } } },
            { "Herrington_Weapon",  new[,] { { 1, 2, 2 }, { 1, 2, 3 }, { 1, 3, 3 } } },
            { "TR15_Weapon",        new[,] { { 1, 3, 4 }, { 1, 3, 4 }, { 1, 3, 4 } } },
            { "SRM12_Weapon",       new[,] { { 1, 2, 2 }, { 1, 2, 2 }, { 1, 2, 2 } } },
            { "Mk14_Weapon",        new[,] { { 1, 2, 2 }, { 1, 2, 2 }, { 1, 2, 2 } } },
        };

        private static readonly HitZone[] Zones = { HitZone.Head, HitZone.Torso, HitZone.Leg };

        [Test]
        public void Каждый_ствол_реестра_в_матрице()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>($"{WeaponsFolder}/Resources/WeaponRegistry.asset");
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");
            var missing = new List<string>();
            foreach (WeaponInfo info in registry.Weapons)
                if (info != null && !Expected.ContainsKey(info.name)) missing.Add(info.name);
            Assert.IsEmpty(missing, "Стволы реестра без строки в матрице попаданий до убийства: " + string.Join(", ", missing));
        }

        [Test]
        public void Попаданий_до_убийства_по_матрице()
        {
            var failures = new List<string>();
            foreach (var pair in Expected)
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>($"{WeaponsFolder}/{pair.Key}.asset");
                if (info == null || info.WeaponPrefab == null) { failures.Add($"{pair.Key}: нет ассета или префаба."); continue; }

                var weapon = info.WeaponPrefab.GetComponent<UxrFirearmWeapon>();
                var source = info.WeaponPrefab.GetComponent<UxrProjectileSource>();
                int shotIndex = WeaponBalanceTests.TriggersOf(weapon)[0].shotIndex;
                UxrShotDescriptor shot = source.ShotTypes[shotIndex];

                for (int d = 0; d < Distances.Length; d++)
                {
                    float t = Mathf.Clamp01(Distances[d] / shot.ProjectileMaxDistance);
                    float perShot = Mathf.Lerp(shot.ProjectileDamageNear, shot.ProjectileDamageFar, t) * info.Pellets;
                    for (int z = 0; z < Zones.Length; z++)
                    {
                        int hits = Mathf.CeilToInt(Life / (perShot * HitZoneDamage.Multiplier(Zones[z])) - 1e-4f);
                        int want = pair.Value[d, z];
                        if (hits != want)
                            failures.Add($"{pair.Key}: {Zones[z]} на {Distances[d]} м — {hits} попаданий, по матрице {want} (выстрел {perShot:F1}).");
                    }
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
