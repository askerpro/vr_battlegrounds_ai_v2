using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Ручное (поштучное) заряжание: оружие с профилем <see cref="WeaponAmmoCapability.FixedStoreChamber"/> принимает
    /// одиночные патроны через <see cref="CartridgeIntake"/> во встроенный запас. Префабы собирает
    /// <c>ManualLoadingMigration</c>; тест сверяет, что собранное согласовано, а не настроено руками вразнобой.
    /// </summary>
    public class ManualLoadingPrefabTests
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        private static IEnumerable<WeaponInfo> ManualLoadingWeapons() =>
            AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(info => info != null && info.ReadinessProfile != null &&
                               info.ReadinessProfile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber);

        [Test]
        public void Дробовики_переведены_на_ручное_заряжание()
        {
            var names = ManualLoadingWeapons().Select(info => info.name).ToList();
            Assert.That(names, Is.SupersetOf(new[] { "ShotgunReal_Weapon", "Herrington_Weapon" }));
        }

        [Test]
        public void Оружие_патрон_и_гнездо_согласованы()
        {
            var failures = new List<string>();
            foreach (WeaponInfo info in ManualLoadingWeapons())
            {
                string name = info.name;
                GameObject weapon = info.WeaponPrefab;
                var intake = weapon != null ? weapon.GetComponent<CartridgeIntake>() : null;
                if (intake == null) { failures.Add($"{name}: нет CartridgeIntake."); continue; }

                var cartridge = info.MagazinePrefab != null ? info.MagazinePrefab.GetComponent<Cartridge>() : null;
                if (cartridge == null) { failures.Add($"{name}: MagazinePrefab не патрон с Cartridge."); continue; }
                if (cartridge.GetComponent<UxrFirearmAmmoUnit>() == null) failures.Add($"{name}: у патрона нет UxrFirearmAmmoUnit.");
                if (cartridge.GetComponent<UxrFirearmMag>() != null) failures.Add($"{name}: патрон не должен быть магазином.");
                // Карман выдаёт патроны на столько же магазинов, сколько остальному оружию: патрон стоит 1/ёмкость магазина.
                if (cartridge.MagazineEquivalent != info.MagazineSize || info.AmmoItemsPerMagazine != info.MagazineSize)
                    failures.Add($"{name}: патрон стоит 1/{cartridge.MagazineEquivalent} магазина, выдача — {info.AmmoItemsPerMagazine} на магазин, ёмкость {info.MagazineSize}.");
                if (string.IsNullOrEmpty(intake.AmmoType) || cartridge.AmmoType != intake.AmmoType)
                    failures.Add($"{name}: тип патрона '{cartridge.AmmoType}', оружие ждёт '{intake.AmmoType}'.");

                // Карман и гнездо сходятся по тегу: MagazinePocketSelectionTests.
                string tag = cartridge.Grabbable.Tag;
                UxrGrabbableObjectAnchor slot = intake.Intake;
                if (slot == null || !slot.IsCompatibleObjectTag(tag))
                    failures.Add($"{name}: гнездо патрона не принимает тег '{tag}'.");

                var stores = weapon.GetComponentsInChildren<UxrFirearmMag>(true).Where(store => store.IsFixedAmmoStore).ToList();
                if (stores.Count != 1) failures.Add($"{name}: встроенных запасов {stores.Count}, нужен один.");
                else if (stores[0].FixedStoreWeapon != weapon.GetComponent<UxrFirearmWeapon>() ||
                         stores[0].Capacity != info.MagazineSize)
                    failures.Add($"{name}: встроенный запас не привязан к оружию или ёмкость {stores[0].Capacity} ≠ {info.MagazineSize}.");

                var controller = weapon.GetComponent<WeaponReadinessController>();
                if (controller == null || controller.Profile != info.ReadinessProfile)
                    failures.Add($"{name}: контроллер готовности не использует профиль из WeaponInfo.");

                // Без удаления принятых патронов сервер упирается в предел удержания, и после 32 патронов оружие
                // перестаёт заряжаться.
                if (!new SerializedObject(intake).FindProperty("_allowOrderedRetirement").boolValue)
                    failures.Add($"{name}: выключено удаление принятых патронов.");
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
    }
}
