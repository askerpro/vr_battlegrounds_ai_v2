using System;
using System.Linq;
using Mirror;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Явная authoring политика two opt-in recipes; никаких поисков по имени Shotgun.</summary>
    public static class ManualLoadingAuthoring
    {
        public static void ConfigureCartridge(GameObject root, string ammoType, int capacity)
        {
            if (root == null || string.IsNullOrWhiteSpace(ammoType) || root.GetComponent<UxrGrabbableObject>() == null ||
                root.GetComponent<NetworkIdentity>() == null || root.GetComponent<Rigidbody>() == null || capacity <= 0)
                throw new InvalidOperationException("Cartridge requires a networked physical source.");
            foreach (var magazine in root.GetComponentsInChildren<UxrFirearmMag>(true)) Object.DestroyImmediate(magazine);
            var unit = GetOrAdd<UxrFirearmAmmoUnit>(root.gameObject);
            var cartridge = GetOrAdd<Cartridge>(root.gameObject);
            var settings = new SerializedObject(cartridge);
            settings.FindProperty("_ammoType").stringValue = ammoType;
            settings.FindProperty("_magazineEquivalent").intValue = capacity;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var grab = new SerializedObject(root.GetComponent<UxrGrabbableObject>());
            grab.FindProperty("_tag").stringValue = "Cartridge:" + ammoType;
            grab.FindProperty("_startAnchor").objectReferenceValue = null;
            grab.FindProperty("_autoCreateStartAnchor").boolValue = false;
            grab.ApplyModifiedPropertiesWithoutUndo();
            if (root.GetComponent<Rigidbody>().collisionDetectionMode == CollisionDetectionMode.Discrete)
                root.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (root.GetComponent<OutOfWorldGuard>() == null) root.AddComponent<OutOfWorldGuard>();
            if (!root.GetComponentsInChildren<Collider>(true).Any(collider => !collider.isTrigger))
                throw new InvalidOperationException("Cartridge requires a solid collider.");
            EditorUtility.SetDirty(unit); EditorUtility.SetDirty(cartridge);
        }

        public static void ConfigureWeapon(GameObject root, string ammoType, int totalCapacity, WeaponReadinessProfile profile,
            AudioClip insertClip)
        {
            var weapon = root != null ? root.GetComponent<UxrFirearmWeapon>() : null;
            if (weapon == null || totalCapacity <= 0 || profile == null || profile.AmmoCapability != WeaponAmmoCapability.FixedStoreChamber ||
                profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn ||
                !weapon.TryGetTriggerMagazineAnchor(0, out var internalAnchor) || internalAnchor == null)
                throw new InvalidOperationException("Explicit fixed tube/profile/anchor required.");
            // Authoring source уже содержит свой initial object; новый cartridge intake не занимает trigger anchor.
            var counters = root.GetComponentsInChildren<UxrFirearmMag>(true);
            var counter = counters.FirstOrDefault(candidate => candidate.GetComponent<UxrGrabbableObject>()?.StartAnchor == internalAnchor);
            if (counter == null)
            {
                // Rebuild из cartridge source: clone вложенного item превращается в независимый counter до сохранения.
                var old = root.GetComponentsInChildren<UxrGrabbableObject>(true).FirstOrDefault(candidate => candidate.StartAnchor == internalAnchor);
                if (old == null) throw new InvalidOperationException("No authored initial tube item.");
                counter = old.gameObject.AddComponent<UxrFirearmMag>();
            }
            var counterObject = counter.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(counterObject))
                PrefabUtility.UnpackPrefabInstance(counterObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var cartridge in counterObject.GetComponents<Cartridge>()) Object.DestroyImmediate(cartridge);
            foreach (var unit in counterObject.GetComponents<UxrFirearmAmmoUnit>()) Object.DestroyImmediate(unit);
            var counterSettings = new SerializedObject(counter);
            counterSettings.FindProperty("_capacity").intValue = totalCapacity;
            counterSettings.FindProperty("_rounds").intValue = totalCapacity;
            counterSettings.ApplyModifiedPropertiesWithoutUndo();
            if (!counter.ConfigureFixedStore(weapon, 0)) throw new InvalidOperationException("Invalid fixed store binding.");
            var counterGrab = counterObject.GetComponent<UxrGrabbableObject>();
            var grabSettings = new SerializedObject(counterGrab);
            grabSettings.FindProperty("_tag").stringValue = "Tube:" + ammoType;
            grabSettings.FindProperty("_startAnchor").objectReferenceValue = internalAnchor;
            grabSettings.FindProperty("_autoCreateStartAnchor").boolValue = false;
            grabSettings.ApplyModifiedPropertiesWithoutUndo();
            counterGrab.enabled = false;
            // Встроенный reservoir живёт с оружием; guards физической выдаваемой единицы ему не принадлежат.
            if (counterObject.GetComponent<OutOfWorldGuard>() != null) Object.DestroyImmediate(counterObject.GetComponent<OutOfWorldGuard>());
            if (counterObject.GetComponent<VrBattlegrounds.Arsenal.MagazineManipulationHistory>() != null)
                Object.DestroyImmediate(counterObject.GetComponent<VrBattlegrounds.Arsenal.MagazineManipulationHistory>());
            foreach (var renderer in counterObject.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var collider in counterObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            if (counterObject.GetComponent<NetworkIdentity>() != null) Object.DestroyImmediate(counterObject.GetComponent<NetworkIdentity>());
            if (counterObject.GetComponent<Rigidbody>() != null) counterObject.GetComponent<Rigidbody>().isKinematic = true;
            SetAnchorTag(internalAnchor, "Tube:" + ammoType);

            Transform intakeTransform = root.transform.Find("CartridgeIntake");
            if (intakeTransform == null)
            {
                intakeTransform = new GameObject("CartridgeIntake").transform;
                intakeTransform.SetParent(root.transform, false);
                // Реальная наружная proximity source, отдельная от скрытого fixed store alignment.
                intakeTransform.SetPositionAndRotation(internalAnchor.DropProximityTransform.position, internalAnchor.DropProximityTransform.rotation);
            }
            var intake = GetOrAdd<UxrGrabbableObjectAnchor>(intakeTransform.gameObject);
            SetAnchorTag(intake, "Cartridge:" + ammoType);
            ConfigureIntakeSound(internalAnchor, intake, insertClip);
            var receiver = GetOrAdd<CartridgeIntake>(root.gameObject);
            var receiverSettings = new SerializedObject(receiver);
            receiverSettings.FindProperty("_triggerIndex").intValue = 0;
            receiverSettings.FindProperty("_ammoType").stringValue = ammoType;
            receiverSettings.FindProperty("_intake").objectReferenceValue = intake;
            receiverSettings.FindProperty("_allowOrderedRetirement").boolValue = true;
            receiverSettings.ApplyModifiedPropertiesWithoutUndo();

            // Хост оружия, механизм, звуки и подсказка — единственный writer (помпа, затвор или уже переведённый ствол).
            WeaponSystemAuthoring.Configure(root, profile);
            // Перенос только ссылки владельца insertion highlight, не новые материалы/геометрия.
            var highlight = internalAnchor.GetComponent<WeaponMagazineAnchorHighlight>();
            if (highlight != null)
            {
                var highlightSettings = new SerializedObject(highlight);
                highlightSettings.FindProperty("_anchor").objectReferenceValue = intake;
                highlightSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Звук окна приёма: настройки источника и звук выпадения — от гнезда магазина того же оружия,
        /// звук вставки — свой у оружия (insertClip), пусто — общий MagazineAnchorSoundDefaults (AnchorActivationAudioTests).
        /// Источник — на самом окне, не под ActivateOnPlaced, иначе звук выпадения оборвётся.
        /// </summary>
        private static void ConfigureIntakeSound(UxrGrabbableObjectAnchor from, UxrGrabbableObjectAnchor to, AudioClip insertClip)
        {
            var donor = from.GetComponent<AnchorSound>();
            if (donor == null || donor.Source == null || donor.TakeOutClip == null)
                throw new InvalidOperationException("Гнездо магазина без AnchorSound — нечего перенести на окно приёма.");
            var source = GetOrAdd<AudioSource>(to.gameObject);
            EditorUtility.CopySerialized(donor.Source, source);
            source.clip = insertClip;
            var sound = GetOrAdd<AnchorSound>(to.gameObject);
            EditorUtility.CopySerialized(donor, sound);
            var settings = new SerializedObject(sound);
            settings.FindProperty("_source").objectReferenceValue = source;
            settings.FindProperty("_takeOutOnlyByHand").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        // Не «GetComponent() ?? AddComponent()»: в редакторе отсутствующий компонент — поддельный null Unity, и ?? его не видит.
        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent<T>(out var existing) ? existing : target.AddComponent<T>();

        private static void SetAnchorTag(UxrGrabbableObjectAnchor anchor, string tag)
        {
            var settings = new SerializedObject(anchor);
            var tags = settings.FindProperty("_compatibleTags"); tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = tag;
            settings.FindProperty("_allowSwap").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

    }
}
