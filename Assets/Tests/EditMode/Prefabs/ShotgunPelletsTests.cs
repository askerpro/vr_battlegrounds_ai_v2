using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Дробовик стреляет дробью, а не одной пулей.
    ///
    /// <para>
    /// <b>Дефект.</b> У <c>Shotgun_real</c> — и у сэмплового дробовика UltimateXR — на выстрел
    /// вылетает один снаряд, как у пистолета: SDK дроби не умеет, у сэмпла она только нарисована
    /// широким трассером. <c>ShotgunPellets</c> добавляет дробинки на событие выстрела.
    /// </para>
    ///
    /// <para>
    /// Тест поднимает событие выстрела и считает снаряды, которые появились в
    /// <c>UxrWeaponManager</c>: сколько, в каком конусе, какой суммарный урон вблизи.
    /// </para>
    /// </summary>
    public class ShotgunPelletsTests
    {
        private const string Weapon = "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab";
        private const int MinPellets = 6;
        private const float PlayerLife = 100f;

        [Test]
        public void Выстрел_дробовика_выпускает_дробь_в_конусе()
        {
            // Свой менеджер — как в GameModeRulesTests: Awake регистрирует синглтон, OnDestroy снимает.
            // Через UxrWeaponManager.Instance + DestroyImmediate в синглтоне оставалась ссылка на
            // уничтоженный объект, и следующие тесты уже не могли зарегистрировать менеджер.
            UxrWeaponManager created = null;
            if (!UxrWeaponManager.HasInstance)
            {
                created = new GameObject("WeaponManager").AddComponent<UxrWeaponManager>();
                Call(created, "Awake");
            }

            IList projectiles = Projectiles();
            int before = projectiles.Count;
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Weapon));
            var spawned = new List<GameObject>();

            try
            {
                // Вне Play Mode Awake/OnEnable не зовутся — как в TwoHandGrabHarness.
                foreach (MonoBehaviour behaviour in weapon.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null || behaviour.GetType().Namespace?.StartsWith("VrBattlegrounds") != true) continue;
                    Call(behaviour, "Awake");
                    Call(behaviour, "OnEnable");
                }

                var firearm = weapon.GetComponent<UxrFirearmWeapon>();
                var source = weapon.GetComponent<UxrProjectileSource>();
                typeof(UxrFirearmWeapon).GetMethod("OnProjectileShot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(firearm, new object[] { 0 });

                int pellets = projectiles.Count - before + 1; // + основной снаряд, его выпускает сам SDK
                for (int i = before; i < projectiles.Count; i++)
                    spawned.Add((GameObject)projectiles[i].GetType().GetProperty("Projectile").GetValue(projectiles[i]));

                Assert.That(pellets, Is.GreaterThanOrEqualTo(MinPellets), $"{weapon.name}: на выстрел {pellets} снаряд(ов) — это пуля, не дробь.");

                var descriptor = projectiles[before].GetType().GetProperty("ShotDescriptor").GetValue(projectiles[before]) as UxrShotDescriptor;
                var pelletsComponent = weapon.GetComponent<VrBattlegrounds.Weapons.ShotgunPellets>();
                Vector3 barrel = descriptor.ShotSource.forward;

                foreach (GameObject pellet in spawned)
                {
                    float angle = Vector3.Angle(barrel, pellet.transform.forward);
                    Assert.That(angle, Is.LessThanOrEqualTo(pelletsComponent.SpreadDegrees * 1.42f + 0.1f), $"Дробинка ушла на {angle:F1}° от ствола.");
                }

                // Урон вблизи всей дробью — порядка здоровья игрока: наповал в упор, но не впятеро.
                UxrShotDescriptor main = source.ShotTypes[0];
                float total = main.ProjectileDamageNear + (pellets - 1) * descriptor.ProjectileDamageNear;
                Assert.That(total, Is.InRange(PlayerLife, PlayerLife * 1.5f), $"Вся дробь вблизи наносит {total:F0} при здоровье {PlayerLife:F0}.");
            }
            finally
            {
                while (projectiles.Count > before) projectiles.RemoveAt(projectiles.Count - 1);
                foreach (GameObject go in spawned) if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(weapon);
                if (created != null)
                {
                    Call(created, "OnDestroy");
                    Object.DestroyImmediate(created.gameObject);
                }
            }
        }

        private static IList Projectiles() =>
            (IList)typeof(UxrWeaponManager).GetField("_projectiles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(UxrWeaponManager.Instance);

        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(target, null);
    }
}
