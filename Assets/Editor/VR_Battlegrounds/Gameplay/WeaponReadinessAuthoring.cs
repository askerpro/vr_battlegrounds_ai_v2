using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Единственный writer компонентов готовности на префабе оружия: <see cref="WeaponReadinessController"/>
    /// (профиль, Body, Action mapping, Empty rear time), <see cref="WeaponTriggerAttemptRouter"/> и
    /// <see cref="WeaponAttemptFeedback"/>. Его вызывают сборщики (рецепт) и адресная миграция, поэтому
    /// пересборка префаба воспроизводит те же bindings, а не теряет их.
    /// </summary>
    public static class WeaponReadinessAuthoring
    {
        /// <summary>Съёмный магазин + патронник, ручное досылание, затвор остаётся сзади после последнего патрона.</summary>
        public const string DetachableHoldOpenProfile = "Assets/Data/Weapons/Profiles/DetachableHoldOpenReadiness.asset";

        /// <summary>Mapping из authored source: Action-графа, Empty-клипа и хода ручки (<see cref="WeaponMechanismVisuals.TryBuildPreparedActionMapping"/>).</summary>
        public static void ConfigureFromSource(GameObject root, WeaponReadinessProfile profile)
        {
            var visuals = root != null ? root.GetComponent<WeaponMechanismVisuals>() : null;
            if (visuals == null) throw new InvalidOperationException("Native Action visuals required.");
            if (!visuals.TryBuildPreparedActionMapping(profile, out ChamberActionBinding[] bindings, out float emptyTime, out string error))
                throw new InvalidOperationException(error);
            WriteController(root, profile, visuals.Body, bindings, emptyTime);
        }

        /// <summary>Записать уже посчитанный mapping (помпа считает его сама) и компоненты попытки спуска.</summary>
        public static void WriteController(GameObject root, WeaponReadinessProfile profile, Transform body,
            ChamberActionBinding[] bindings, float emptyTime)
        {
            if (root == null || profile == null || !profile.TryValidate(out _) || profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo)
                throw new InvalidOperationException("Ledger readiness profile required.");
            var controller = GetOrAdd<WeaponReadinessController>(root);
            var settings = new SerializedObject(controller);
            settings.FindProperty("_triggerIndex").intValue = 0;
            settings.FindProperty("_profile").objectReferenceValue = profile;
            settings.FindProperty("_body").objectReferenceValue = body;
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
            GetOrAdd<WeaponTriggerAttemptRouter>(root);
            GetOrAdd<WeaponAttemptFeedback>(root);
        }

        // Не «GetComponent() ?? AddComponent()»: в редакторе отсутствующий компонент — поддельный null Unity, и ?? его не видит.
        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent<T>(out var existing) ? existing : target.AddComponent<T>();
    }
}
