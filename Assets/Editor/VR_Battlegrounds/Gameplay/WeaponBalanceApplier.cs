using System.Collections.Generic;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Переносит баланс из <see cref="WeaponInfo" /> в префабы оружия и магазинов (T-38): урон вблизи и на
    /// предельной дистанции каждого выстрела (основного и дробинок), частоту спуска, число дробинок, ёмкость и
    /// заряд магазина, картину накопленной отдачи (<see cref="RecoilAccumulator" />, ставится, если нет). Руками эти поля в префабах не правятся — расхождение ловит <c>WeaponBalanceTests</c>.
    /// Оружие без баланса (<see cref="WeaponInfo.HasBalance" /> — сэмплы SDK) не трогается.
    /// </summary>
    public static class WeaponBalanceApplier
    {
        private const string WeaponsFolder = "Assets/Data/Weapons";

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Apply Weapon Balance")]
        public static void ApplyAll()
        {
            var changed = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { WeaponsFolder }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || !info.HasBalance || info.WeaponPrefab == null) continue;

                if (ApplyWeapon(info)) changed.Add(info.WeaponPrefab.name);
                if (ApplyMagazine(info)) changed.Add(info.MagazinePrefab.name);
            }

            AssetDatabase.SaveAssets();
            GameLog.WeaponSystem.Info(changed.Count == 0
                ? "Баланс оружия: префабы уже совпадают с WeaponInfo."
                : $"Баланс оружия применён: {string.Join(", ", changed)}.");
        }

        private static bool ApplyWeapon(WeaponInfo info)
        {
            GameObject prefab = info.WeaponPrefab;
            var weapon = prefab.GetComponent<UxrFirearmWeapon>();
            var source = prefab.GetComponent<UxrProjectileSource>();
            if (weapon == null || source == null)
            {
                GameLog.WeaponSystem.Warning($"{info.name}: префаб {prefab.name} без UxrFirearmWeapon/UxrProjectileSource — баланс не применён.", prefab);
                return false;
            }

            // Компонент — первым: LoadPrefabContents читает префаб с диска, несохранённые правки ниже потерялись бы.
            bool dirty = EnsureRecoilAccumulator(prefab);
            prefab = info.WeaponPrefab;
            weapon = prefab.GetComponent<UxrFirearmWeapon>();
            source = prefab.GetComponent<UxrProjectileSource>();
            var shotIndices = new List<int>();

            var weaponSo = new SerializedObject(weapon);
            SerializedProperty triggers = weaponSo.FindProperty("_triggers");
            int frequency = WeaponInfo.ShotFrequency(info.FireRate);
            for (int i = 0; i < triggers.arraySize; i++)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(i);
                shotIndices.Add(trigger.FindPropertyRelative("_projectileShotIndex").intValue);
                dirty |= SetInt(trigger.FindPropertyRelative("_maxShotFrequency"), frequency);
            }
            weaponSo.ApplyModifiedPropertiesWithoutUndo();

            var pellets = prefab.GetComponent<ShotgunPellets>();
            if (pellets != null)
            {
                var pelletsSo = new SerializedObject(pellets);
                dirty |= SetInt(pelletsSo.FindProperty("_pellets"), info.Pellets);
                pelletsSo.ApplyModifiedPropertiesWithoutUndo();
                shotIndices.Add(pellets.PelletShotIndex);
            }
            else if (info.Pellets > 1)
            {
                GameLog.WeaponSystem.Warning($"{info.name}: {info.Pellets} дробинок по балансу, а у {prefab.name} нет ShotgunPellets.", prefab);
            }

            var sourceSo = new SerializedObject(source);
            SerializedProperty shots = sourceSo.FindProperty("_shotTypes");
            foreach (int index in new HashSet<int>(shotIndices))
            {
                if (index < 0 || index >= shots.arraySize) continue;
                SerializedProperty shot = shots.GetArrayElementAtIndex(index);
                float maxDistance = shot.FindPropertyRelative("_projectileMaxDistance").floatValue;
                dirty |= SetFloat(shot.FindPropertyRelative("_projectileDamageNear"), info.Damage);
                dirty |= SetFloat(shot.FindPropertyRelative("_projectileDamageFar"),
                                  WeaponInfo.DamageAt(info.Damage, info.RangeModifier, maxDistance));
            }
            sourceSo.ApplyModifiedPropertiesWithoutUndo();

            dirty |= ApplyRecoil(info, weaponSo.FindProperty("_recoilAxes").objectReferenceValue);

            if (dirty) PrefabUtility.SavePrefabAsset(prefab);
            return dirty;
        }

        private static bool EnsureRecoilAccumulator(GameObject prefab)
        {
            if (prefab.GetComponent<RecoilAccumulator>() != null) return false;

            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.AddComponent<RecoilAccumulator>();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return true;
        }

        private static bool ApplyRecoil(WeaponInfo info, Object axes)
        {
            GameObject prefab = info.WeaponPrefab;
            bool dirty = false;
            var so = new SerializedObject(prefab.GetComponent<RecoilAccumulator>());
            SerializedProperty target = so.FindProperty("_pattern");
            SerializedProperty source = new SerializedObject(info).FindProperty("_recoil");
            foreach (string field in new[] { "_kickDegrees", "_maxPitchDegrees", "_maxYawDegrees", "_recoveryTime", "_oneHandMultiplier" })
                dirty |= SetFloat(target.FindPropertyRelative(field), source.FindPropertyRelative(field).floatValue);

            SerializedProperty axesProperty = so.FindProperty("_axes");
            if (axesProperty.objectReferenceValue != axes)
            {
                axesProperty.objectReferenceValue = axes;
                dirty = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return dirty;
        }

        private static bool ApplyMagazine(WeaponInfo info)
        {
            if (info.MagazinePrefab == null) return false;
            var mag = info.MagazinePrefab.GetComponentInChildren<UxrFirearmMag>(true);
            if (mag == null) return false;

            var so = new SerializedObject(mag);
            bool dirty = SetInt(so.FindProperty("_capacity"), info.MagazineSize);
            dirty |= SetInt(so.FindProperty("_rounds"), info.MagazineSize);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (dirty) PrefabUtility.SavePrefabAsset(info.MagazinePrefab);
            return dirty;
        }

        private static bool SetInt(SerializedProperty property, int value)
        {
            if (property == null || property.intValue == value) return false;
            property.intValue = value;
            return true;
        }

        private static bool SetFloat(SerializedProperty property, float value)
        {
            if (property == null || Mathf.Approximately(property.floatValue, value)) return false;
            property.floatValue = value;
            return true;
        }
    }
}
