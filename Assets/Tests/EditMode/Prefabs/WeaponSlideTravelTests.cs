using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Затвор оружия физически дотягивается до порога перезарядки.
    ///
    /// <para>
    /// Раньше длина хода для порога задавалась в <see cref="AutomaticWeaponSlideFeedback" />
    /// отдельно от <c>Translation Limits</c> затвора, и у <c>Gun_real</c> они разошлись: эталон
    /// 6 см от M16 при ходе 1.89 см — максимум 31% против порога 85.5%, перезарядка не
    /// срабатывала никогда. Теперь ход берётся из лимитов затвора; тест требует, чтобы у каждого
    /// затвора он вычислялся.
    /// </para>
    /// </summary>
    public class WeaponSlideTravelTests
    {
        private static readonly string[] WeaponRoots = { "Assets/Prefabs/Weapons" };

        [Test]
        public void WeaponPrefabs_SlideTravelComesFromLimits()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", WeaponRoots))
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (var feedback in prefab.GetComponentsInChildren<AutomaticWeaponSlideFeedback>(true))
                {
                    checks++;
                    var slide = new SerializedObject(feedback).FindProperty("_slide").objectReferenceValue as UxrGrabbableObject;

                    if (slide == null)
                        failures.Add($"{path}: не назначен _slide");
                    else if (!AutomaticWeaponSlideFeedback.TryGetSlideTravel(slide, out _, out _))
                        failures.Add($"{path}: у '{slide.name}' нет хода — нужен Restrict Local Offset и ненулевые Translation Limits");
                }

                // Этап drive (Herrington, FABARM): AutomaticWeaponSlideFeedback снят, порог извлечения считает хост WeaponSystem
                // по ходу ручки Rig.Handle — тот же контракт «ход из Translation Limits». Пустой Rig — ствол ещё на старом коде.
                foreach (var host in prefab.GetComponentsInChildren<WeaponSystem>(true))
                {
                    if (!host.Rig.HasAction) continue;
                    checks++;
                    if (!WeaponMechanismRig.TryGetTravel(host.Rig.Handle, out _, out _))
                        failures.Add($"{path}: у ручки '{host.Rig.Handle.name}' (WeaponSystem) нет хода — нужен Restrict Local Offset и ненулевые Translation Limits");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного затвора — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Затвор не может засчитать перезарядку:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Магазин, который арсенал выдаёт к оружию, встаёт в его якорь магазина. У <c>Gun_real</c>
        /// якорь принимал только <c>M16_Mag</c> (скопирован с M16), а выдавался <c>MagGun</c>:
        /// затвор щёлкал, но стрелять было нечем.
        /// </summary>
        [Test]
        public void ArsenalMagazine_FitsWeaponMagAnchor()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || info.MagazinePrefab == null) continue;

                var magGrabbable = info.MagazinePrefab.GetComponent<UxrGrabbableObject>();
                var firearm      = info.WeaponPrefab.GetComponent<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>();
                if (magGrabbable == null || firearm == null) continue;

                var triggers = new SerializedObject(firearm).FindProperty("_triggers");
                for (int i = 0; i < triggers.arraySize; i++)
                {
                    var anchor = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
                    if (anchor == null) continue;

                    checks++;
                    var fixedStores = info.WeaponPrefab.GetComponentsInChildren<UltimateXR.Mechanics.Weapons.UxrFirearmMag>(true)
                        .Where(store => store.IsFixedAmmoStore).ToArray();
                    bool fixedLoading = info.ReadinessProfile != null &&
                        info.ReadinessProfile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber;
                    if (fixedLoading || fixedStores.Length > 0)
                    {
                        // Поштучный боезапас вставляется в CartridgeIntake, а не в скрытый
                        // ammunition anchor встроенного запаса. Оба контракта проверяем явно.
                        var cartridge = info.MagazinePrefab.GetComponent<Cartridge>();
                        var intake = info.WeaponPrefab.GetComponent<CartridgeIntake>();
                        if (!fixedLoading || fixedStores.Length != 1 || fixedStores[0].FixedStoreWeapon != firearm ||
                            fixedStores[0].FixedStoreTrigger != i || !fixedStores[0].transform.IsChildOf(anchor.transform) ||
                            fixedStores[0].Capacity != info.MagazineSize)
                            failures.Add($"{info.name}: профиль, скрытый встроенный запас и ammunition anchor не согласованы.");
                        if (cartridge == null || cartridge.Unit == null || cartridge.Grabbable != magGrabbable ||
                            info.MagazinePrefab.GetComponent<UltimateXR.Mechanics.Weapons.UxrFirearmMag>() != null ||
                            cartridge.MagazineEquivalent != info.MagazineSize)
                            failures.Add($"{info.name}: выдаваемый боезапас не одиночный патрон согласованной ёмкости.");
                        if (intake == null || intake.Intake == null || intake.Intake == anchor ||
                            !intake.Intake.transform.IsChildOf(info.WeaponPrefab.transform) ||
                            new SerializedObject(intake).FindProperty("_triggerIndex").intValue != i ||
                            cartridge == null || string.IsNullOrEmpty(intake.AmmoType) || intake.AmmoType != cartridge.AmmoType ||
                            !intake.Intake.IsCompatibleObjectTag(magGrabbable.Tag))
                            failures.Add($"{info.name}: приёмник патрона не согласован с типом, тегом или спуском оружия.");
                        continue;
                    }
                    var tags = new SerializedObject(anchor).FindProperty("_compatibleTags");
                    var list = new List<string>();
                    for (int t = 0; t < tags.arraySize; t++) list.Add(tags.GetArrayElementAtIndex(t).stringValue);

                    // Как UxrGrabbableObjectAnchor.IsCompatibleObjectTag: пустой список принимает только предметы без тега.
                    bool fits = list.Count == 0 ? string.IsNullOrEmpty(magGrabbable.Tag) : list.Contains(magGrabbable.Tag);
                    if (!fits)
                        failures.Add($"{info.name}: магазин {info.MagazinePrefab.name} (тег '{magGrabbable.Tag}') не встаёт в " +
                                     $"{info.WeaponPrefab.name}/{anchor.name} (принимает: {string.Join(", ", list)})");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного оружия с якорем магазина — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Выданный арсеналом магазин не вставляется в оружие:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Оружие арсенала приходит со стены заряженным: в якоре магазина лежит вложенный
        /// магазин, который UltimateXR вставляет в <c>Awake</c> (нужны <c>Start Anchor</c> и
        /// <c>Rigid Body Source</c>). Декоративный магазин на стене не берётся, а запасные выдаёт
        /// только <c>PlayerLoadoutManager</c> в начале раунда — без вложенного магазина
        /// у <c>Gun_real</c> затвор щёлкал впустую.
        /// </summary>
        [Test]
        public void ArsenalFirearms_ComeLoaded()
        {
            var failures = new List<string>();
            int checks   = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || info.MagazinePrefab == null) continue;

                var firearm = info.WeaponPrefab.GetComponent<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>();
                if (firearm == null) continue;

                var triggers = new SerializedObject(firearm).FindProperty("_triggers");
                for (int i = 0; i < triggers.arraySize; i++)
                {
                    var anchor = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
                    if (anchor == null) continue;

                    checks++;
                    var mag = anchor.GetComponentInChildren<UltimateXR.Mechanics.Weapons.UxrFirearmMag>(true);
                    if (mag == null)
                    {
                        failures.Add($"{info.WeaponPrefab.name}: в {anchor.name} нет магазина");
                        continue;
                    }

                    var so = new SerializedObject(mag.GetComponent<UxrGrabbableObject>());
                    if (so.FindProperty("_startAnchor").objectReferenceValue != anchor)
                        failures.Add($"{info.WeaponPrefab.name}: у {mag.name} Start Anchor не {anchor.name} — в Awake не вставится");
                    if (so.FindProperty("_rigidBodySource").objectReferenceValue == null)
                        failures.Add($"{info.WeaponPrefab.name}: у {mag.name} нет Rigid Body Source — в Awake не вставится");
                    if (mag.GetComponent<Mirror.NetworkIdentity>() != null)
                        failures.Add($"{info.WeaponPrefab.name}: у вложенного {mag.name} NetworkIdentity — Mirror не допускает вложенных");

                    // Ручное заряжание: в гнезде встроенный запас, выдаются одиночные патроны — «заряжено» значит
                    // полный запас. Состав патрона и окна приёма — ManualLoadingPrefabTests.
                    if (mag.IsFixedAmmoStore)
                    {
                        if (mag.Rounds != mag.Capacity)
                            failures.Add($"{info.WeaponPrefab.name}: встроенный запас {mag.Rounds}/{mag.Capacity} — приходит недозаряженным");
                        continue;
                    }

                    // Вставленный и запасные магазины — один префаб, иначе у оружия два разных магазина.
                    // Выдаваемый может быть вариантом вложенного (оружие-вариант сэмпла UltimateXR).
                    string nested = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mag.gameObject);
                    if (!MagazineChain(info.MagazinePrefab).Contains(nested))
                        failures.Add($"{info.WeaponPrefab.name}: вложен {nested}, а {info.name} выдаёт {AssetDatabase.GetAssetPath(info.MagazinePrefab)}");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Не найдено ни одного оружия с якорем магазина — тест ничего не проверил.");
            Assert.IsEmpty(failures, "Оружие арсенала приходит незаряженным:\n" + string.Join("\n", failures));
        }

        [Test]
        public void TryGetSlideTravel_TakesLongerLimitAsFullTravel()
        {
            var slide = CreateSlide(UxrTranslationConstraintMode.RestrictLocalOffset, new Vector3(0f, 0f, -0.0189f), Vector3.zero);

            try
            {
                Assert.IsTrue(AutomaticWeaponSlideFeedback.TryGetSlideTravel(slide, out Vector3 direction, out float length));
                Assert.That(length, Is.EqualTo(0.0189f).Within(1e-6f));
                Assert.That(Vector3.Distance(direction, Vector3.back), Is.LessThan(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(slide.gameObject);
            }
        }

        [Test]
        public void TryGetSlideTravel_RejectsUnconstrainedOrZeroTravel()
        {
            var free = CreateSlide(UxrTranslationConstraintMode.Free, new Vector3(0f, 0f, -0.05f), Vector3.zero);
            var zero = CreateSlide(UxrTranslationConstraintMode.RestrictLocalOffset, Vector3.zero, Vector3.zero);

            try
            {
                Assert.IsFalse(AutomaticWeaponSlideFeedback.TryGetSlideTravel(free, out _, out _));
                Assert.IsFalse(AutomaticWeaponSlideFeedback.TryGetSlideTravel(zero, out _, out _));
                Assert.IsFalse(AutomaticWeaponSlideFeedback.TryGetSlideTravel(null, out _, out _));
            }
            finally
            {
                Object.DestroyImmediate(free.gameObject);
                Object.DestroyImmediate(zero.gameObject);
            }
        }

        /// <summary>Путь префаба и всех его баз по цепочке вариантов.</summary>
        private static List<string> MagazineChain(GameObject prefab)
        {
            var chain = new List<string>();
            for (GameObject current = prefab; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
                chain.Add(AssetDatabase.GetAssetPath(current));
            return chain;
        }

        private static UxrGrabbableObject CreateSlide(UxrTranslationConstraintMode mode, Vector3 min, Vector3 max)
        {
            var slide = new GameObject("Slide").AddComponent<UxrGrabbableObject>();
            slide.TranslationConstraint = mode;
            slide.TranslationLimitsMin  = min;
            slide.TranslationLimitsMax  = max;
            return slide;
        }
    }
}
