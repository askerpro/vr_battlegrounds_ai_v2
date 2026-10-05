using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Пакетное узкое применение к готовым игровым префабам, с предварительной проверкой всего набора.</summary>
    public static class WeaponInteractionReview
    {
        private const string ReportFolder = "tmp/weapon-interaction-extension";

        public static string[] WeaponPaths() => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)
                .GetComponent<UxrFirearmWeapon>() != null).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        private static WeaponInteractionRecipe Recipe(GameObject root)
        {
            HandsPackWeaponRecipe source = KinemationWeaponBuilder.All.Select(r => r.Weapon)
                .Concat(HandsPackWeaponBuilder.T38).Concat(new[] { HandsPackWeaponBuilder.ShotgunReal })
                .FirstOrDefault(r => r.Name == root.name);
            var recipe = WeaponInteractionRecipes.For(root, source);
            if (KinemationWeaponBuilder.All.Any(r => r.Weapon.Name == root.name))
                recipe.AssetFolder = $"{KinemationWeapon.ArtRoot}/{root.name}/Interaction";
            return recipe;
        }

        public static string Preflight() => Preflight(WeaponPaths());

        public static string Preflight(IEnumerable<string> paths)
        {
            int weapons = 0, regions = 0;
            foreach (string path in paths.Distinct())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var recipe = Recipe(root);
                    foreach (var region in new[] { recipe.Primary, recipe.Support, recipe.Action, recipe.Insertion }.Where(r => r != null))
                    {
                        Mesh mesh = WeaponHighlightRegionBaker.Build(root.transform, region);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        regions++;
                    }
                    weapons++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return $"Preflight PASS: {weapons} оружий, {regions} непустых областей; Assets не записаны.";
        }

        private static void ApplyMenu() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenTab(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Interaction);

        public static string ApplyAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Нельзя менять префабы в Play Mode.");
            return Apply(WeaponPaths(), AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)
                    .GetComponent<UxrFirearmMag>() != null));
        }

        /// <summary>Узкое применение к объявленным оружию и магазинам; проверка всего набора до первой записи.</summary>
        public static string Apply(IEnumerable<string> weaponPaths, IEnumerable<string> magazinePaths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Нельзя менять префабы в Play Mode.");
            string[] weapons = weaponPaths.Distinct().ToArray();
            string preflight = Preflight(weapons);
            string[] magazines = magazinePaths.Distinct().ToArray();
            foreach (string path in magazines)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<UxrFirearmMag>() == null)
                    throw new InvalidOperationException("Нет магазина: " + path);
            foreach (string path in magazines)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { WeaponInteractionInstaller.ApplyMagazine(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (string path in weapons)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { WeaponInteractionInstaller.Apply(root, Recipe(root)); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return $"{preflight} Применено: {weapons.Length} оружий, {magazines.Length} магазинов.";
        }

        /// <summary>Измерение исходной геометрии и настроек до/после, без prefab-записи.</summary>
        public static string Capture(string label)
        {
            var rows = new List<object>();
            foreach (string path in WeaponPaths())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    string Relative(Transform t) => AnimationUtility.CalculateTransformPath(t, root.transform);
                    var firearm = root.GetComponent<UxrFirearmWeapon>();
                    var settings = new List<string>();
                    var so = new SerializedObject(firearm);
                    SerializedProperty p = so.GetIterator();
                    while (p.Next(true))
                    {
                        if (!p.propertyPath.StartsWith("_triggers") && p.propertyPath != "_collisionLayerMask") continue;
                        if (p.propertyPath.EndsWith(".m_FileID")) continue; // ID экземпляра preview меняется между загрузками.
                        string value = null;
                        if (p.propertyType == SerializedPropertyType.Boolean) value = p.boolValue.ToString();
                        else if (p.propertyType == SerializedPropertyType.Float) value = p.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        else if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) value = p.intValue.ToString();
                        else if (p.propertyType == SerializedPropertyType.Vector3) value = p.vector3Value.ToString("F6");
                        else if (p.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var obj = p.objectReferenceValue;
                            if (obj is Component c && c.transform.IsChildOf(root.transform)) value = Relative(c.transform);
                            else if (obj is GameObject g && g.transform.IsChildOf(root.transform)) value = Relative(g.transform);
                            else value = obj == null ? "null" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj)) + ":" + obj.name;
                        }
                        if (value != null) settings.Add(p.propertyPath + "=" + value);
                    }
                    var geometry = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null &&
                        !f.name.StartsWith("GrabHighlight") && !Relative(f.transform).Contains("/GrabHighlight"))
                        .Select(f => {
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f.sharedMesh, out string guid, out long id);
                            return new { path = Relative(f.transform), mesh = guid + ":" + id,
                                position = root.transform.InverseTransformPoint(f.transform.position).ToString("F6"),
                                rotation = (Quaternion.Inverse(root.transform.rotation) * f.transform.rotation).ToString("F6"),
                                scale = f.transform.lossyScale.ToString("F6"), enabled = f.GetComponent<MeshRenderer>()?.enabled };
                        }).ToArray();
                    Rigidbody rb = root.GetComponent<Rigidbody>();
                    rows.Add(new { path, geometry, settings, mass = rb.mass, collision = rb.collisionDetectionMode.ToString(),
                        rootScale = root.transform.localScale.ToString("F6"), anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true)?.name });
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Directory.CreateDirectory(ReportFolder);
            string report = $"{ReportFolder}/{label}.json";
            File.WriteAllText(report, Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
            return report;
        }
    }
}
