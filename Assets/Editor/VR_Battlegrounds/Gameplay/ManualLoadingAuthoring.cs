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

            var pump = root.GetComponent<UxrShotgunPump>();
            var feedback = root.GetComponent<AutomaticWeaponSlideFeedback>();
            var visuals = root.GetComponent<WeaponMechanismVisuals>();
            if (visuals == null) throw new InvalidOperationException("Native Action visuals required.");
            if (pump != null)
            {
                var pumpSettings = new SerializedObject(pump);
                var pumpGrip = pumpSettings.FindProperty("_pump").objectReferenceValue as UxrGrabbableObject;
                if (pumpGrip == null || root.GetComponent<PumpGrabFollow>()?.Pump != pumpGrip)
                    throw new InvalidOperationException("Native pump/follow binding mismatch.");
                if (feedback == null) feedback = root.AddComponent<AutomaticWeaponSlideFeedback>();
                var feedbackSettings = new SerializedObject(feedback);
                feedbackSettings.FindProperty("_slide").objectReferenceValue = pumpGrip;
                feedbackSettings.FindProperty("_slideThreshold").floatValue = pumpSettings.FindProperty("_slideThreshold").floatValue;
                feedbackSettings.FindProperty("_autoReturnOnRelease").boolValue = false;
                foreach (var pair in new[] {
                    new[] { "_audioSlide", "_audioSlideForward" }, new[] { "_audioSlideBack", "_audioSlideBack" },
                    new[] { "_audioSlideAlreadyLoaded", "_audioSlideForwardWhenLoaded" }, new[] { "_audioSlideBackAlreadyLoaded", "_audioSlideBackWhenLoaded" },
                    new[] { "_hapticClipSlide", "_hapticForward" }, new[] { "_hapticClipSlideBack", "_hapticBack" },
                    new[] { "_hapticClipSlideAlreadyLoaded", "_hapticForwardWhenLoaded" }, new[] { "_hapticClipSlideBackAlreadyLoaded", "_hapticBackWhenLoaded" } })
                    CopySample(pumpSettings, feedbackSettings, pair[0], pair[1]);
                feedbackSettings.ApplyModifiedPropertiesWithoutUndo();
                pump.enabled = false; // Controller owns all physical ledger commands, follow only solves hand pose.
            }
            if (feedback == null) throw new InvalidOperationException("Action feedback required.");

            ChamberActionBinding[] bindings; float emptyTime;
            if (pump != null)
            {
                var action = feedback.Slide;
                var targets = visuals.GetRequiredActionTargets();
                if (targets.Length != 1 || targets[0] != action.transform || visuals.ContactPart == null ||
                    !visuals.ContactPart.IsChildOf(action.transform) || visuals.Body == null ||
                    !AutomaticWeaponSlideFeedback.TryGetSlideTravel(action, out var direction, out float length))
                    throw new InvalidOperationException("Native pump must have a single rigid Action graph.");
                var body = visuals.Body;
                // Общий authored root, как runtime source-rest validator: без cancellation мировых координат.
                Matrix4x4 bodyFrame = MatrixToRoot(body, root.transform);
                Matrix4x4 restFrame = bodyFrame.inverse * MatrixToRoot(action.transform, root.transform);
                Vector3 rest = restFrame.MultiplyPoint3x4(Vector3.zero);
                Quaternion rotation = restFrame.rotation;
                Vector3 fullTravel = bodyFrame.inverse.MultiplyVector(MatrixToRoot(action.transform.parent, root.transform).MultiplyVector(direction * length));
                bindings = new[] { new ChamberActionBinding { Target = action.transform, RestPosition = rest, RearPosition = rest + fullTravel,
                    RestRotation = rotation, RearRotation = rotation, AnimateRotation = false } };
                emptyTime = -1f;
            }
            else if (!visuals.TryBuildPreparedActionMapping(profile, out bindings, out emptyTime, out string error))
                throw new InvalidOperationException(error);
            var controller = GetOrAdd<WeaponReadinessController>(root.gameObject);
            var settings = new SerializedObject(controller);
            settings.FindProperty("_profile").objectReferenceValue = profile;
            settings.FindProperty("_body").objectReferenceValue = visuals.Body;
            settings.FindProperty("_emptyRearTime").floatValue = emptyTime;
            var required = settings.FindProperty("_requiredBindings"); required.arraySize = bindings.Length;
            for (int i = 0; i < bindings.Length; i++)
            {
                var item = required.GetArrayElementAtIndex(i); var binding = bindings[i];
                item.FindPropertyRelative("Target").objectReferenceValue = binding.Target;
                item.FindPropertyRelative("RestPosition").vector3Value = binding.RestPosition;
                item.FindPropertyRelative("RearPosition").vector3Value = binding.RearPosition;
                item.FindPropertyRelative("RestRotation").quaternionValue = binding.RestRotation;
                item.FindPropertyRelative("RearRotation").quaternionValue = binding.RearRotation;
                item.FindPropertyRelative("AnimateRotation").boolValue = binding.AnimateRotation;
            }
            settings.ApplyModifiedPropertiesWithoutUndo();
            if (root.GetComponent<WeaponTriggerAttemptRouter>() == null) root.AddComponent<WeaponTriggerAttemptRouter>();
            if (root.GetComponent<WeaponAttemptFeedback>() == null) root.AddComponent<WeaponAttemptFeedback>();
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
        private static Matrix4x4 MatrixToRoot(Transform target, Transform root)
        {
            Matrix4x4 result = Matrix4x4.identity;
            for (Transform current = target; current != root; current = current.parent)
            {
                if (current == null) throw new InvalidOperationException("Native Action has a foreign root.");
                result = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * result;
            }
            return result;
        }
        private static void CopySample(SerializedObject source, SerializedObject target, string sourceName, string targetName)
        {
            var sample = source.FindProperty(sourceName);
            if (sample == null || target.FindProperty(targetName) == null) throw new InvalidOperationException("Native pump feedback schema mismatch.");
            var property = sample.Copy(); var end = sample.GetEndProperty();
            while (property.Next(true) && !SerializedProperty.EqualContents(property, end))
            {
                string relative = property.propertyPath.Substring(sourceName.Length);
                var output = target.FindProperty(targetName + relative);
                if (output == null) throw new InvalidOperationException("Native feedback property missing: " + relative);
                if (property.isArray && property.propertyType != SerializedPropertyType.String) output.arraySize = property.arraySize;
                switch (property.propertyType)
                {
                    case SerializedPropertyType.ObjectReference: output.objectReferenceValue = property.objectReferenceValue; break;
                    case SerializedPropertyType.Integer: output.longValue = property.longValue; break;
                    case SerializedPropertyType.Enum: output.intValue = property.intValue; break;
                    case SerializedPropertyType.Boolean: output.boolValue = property.boolValue; break;
                    case SerializedPropertyType.Float: output.floatValue = property.floatValue; break;
                    case SerializedPropertyType.String: output.stringValue = property.stringValue; break;
                    case SerializedPropertyType.Vector2: output.vector2Value = property.vector2Value; break;
                    case SerializedPropertyType.Vector3: output.vector3Value = property.vector3Value; break;
                    case SerializedPropertyType.Vector4: output.vector4Value = property.vector4Value; break;
                    case SerializedPropertyType.Color: output.colorValue = property.colorValue; break;
                    case SerializedPropertyType.Quaternion: output.quaternionValue = property.quaternionValue; break;
                    case SerializedPropertyType.AnimationCurve: output.animationCurveValue = property.animationCurveValue; break;
                }
            }
        }
    }
}
