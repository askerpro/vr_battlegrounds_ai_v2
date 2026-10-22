using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;
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
            KinemationWeaponRecipe kinemation = KinemationWeaponBuilder.All.FirstOrDefault(k => k.Weapon.Name == root.name);
            HandsPackWeaponRecipe source = kinemation?.Weapon ?? HandsPackWeaponBuilder.T38
                .Concat(new[] { HandsPackWeaponBuilder.ShotgunReal }).FirstOrDefault(r => r.Name == root.name);
            var recipe = WeaponInteractionRecipes.For(root, source, Palms(root, kinemation));
            if (kinemation != null) recipe.AssetFolder = KinemationFixReview.InteractionFolder(root.name);
            return recipe;
        }

        /// <summary>
        /// Нейтральные точки хвата для областей: snap-точки <c>Orig</c> префаба, если есть все нужные (их читает
        /// <see cref="WeaponInteractionRecipes.For" />, здесь — null), иначе центры ладоней рук пака в кадре позы
        /// (<see cref="HandsPackWeaponBuilder.PalmPoints" />, как при сборке). Записи хвата аватаров не читаются; оружие
        /// без рецепта пака и без <c>Orig</c> — исключение.
        /// </summary>
        private static Vector3[] Palms(GameObject root, KinemationWeaponRecipe kinemation)
        {
            int points = root.GetComponent<UxrGrabbableObject>().GrabPointCount;
            if (PackOrigPoses.FindSnap(root.transform, "Grip", UltimateXR.Core.UxrHandSide.Right) != null &&
                (points < 2 || PackOrigPoses.FindSnap(root.transform, "Support", UltimateXR.Core.UxrHandSide.Left) != null))
                return null;
            Transform Body(HandsPackWeaponRecipe r) => root.transform.Find("MeshContainer/" + r.BodyObjectName) ??
                throw new InvalidOperationException($"{root.name}: нет корпуса MeshContainer/{r.BodyObjectName} для центров ладоней пака.");
            if (kinemation != null)
            {
                using var model = new KinemationWeapon(kinemation.PackPrefab, kinemation.AnimFolder, kinemation.PoseClip, kinemation.Weapon.Name,
                    kinemation.Weapon.BodyPart, kinemation.RestClip, kinemation.Attachments);
                return HandsPackWeaponBuilder.PalmPoints(model, kinemation.Weapon.PoseClip, root.transform, Body(kinemation.Weapon));
            }
            HandsPackWeaponRecipe hands = HandsPackWeaponBuilder.T38.Concat(new[] { HandsPackWeaponBuilder.ShotgunReal })
                .Concat(LegacyPalmSources).FirstOrDefault(r => r.Name == root.name);
            if (hands == null)
                throw new InvalidOperationException($"{root.name}: нет snap-точек Orig и рецепта пака — центры ладоней недоступны. " +
                                                    "Записи хвата аватаров для областей подсветки не используются: экспортируйте _orig или добавьте рецепт.");
            using var pack = new HandsPackWeapon(hands.Folder, hands.BodyPart);
            return HandsPackWeaponBuilder.PalmPoints(pack, hands.PoseClip, root.transform, Body(hands));
        }

        /// <summary>
        /// Набор HandsPackLegacyRebuild без его immutable baseline: только то, что нужно для центров ладоней — папка,
        /// корпус, клип позы и путь корпуса в префабе (те же значения, что в его рецептах и в <see cref="PackOrigPoses" />).
        /// </summary>
        private static IEnumerable<HandsPackWeaponRecipe> LegacyPalmSources => new[]
        {
            new HandsPackWeaponRecipe { Name = "BrowningHiPower", Folder = "Hands_Gun", BodyPart = "Base_mesh", PoseClip = "Aiming_Idle" },
            new HandsPackWeaponRecipe
            {
                Name = "AR15", Folder = "Hands_Automatic_Rifle03", BodyPart = "Rifle_Body_Mesh", BodyObjectName = "Rifle_Body_Mesh", PoseClip = "Aim_Idle"
            }
        };

        public static string Preflight() => Preflight(WeaponPaths());

        /// <summary>
        /// Строит все области без записи Assets и печатает по каждой: режим (island — остров меша целиком, clip — обрезка
        /// коробкой), размер и площадь острова затравки, площадь области, долю области на острове и покрытие острова.
        /// Ошибка любого оружия — исключение со всем отчётом.
        /// </summary>
        public static string Preflight(IEnumerable<string> paths)
        {
            int weapons = 0, regions = 0;
            var lines = new List<string>();
            var errors = new List<string>();
            foreach (string path in paths.Distinct())
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var recipe = Recipe(root);
                    foreach (var region in new[] { recipe.Primary, recipe.Support, recipe.Action, recipe.Insertion }.Where(r => r != null))
                    {
                        Mesh mesh = WeaponHighlightRegionBaker.Build(root.transform, region, out WeaponHighlightRegionBaker.RegionStats stats);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        lines.Add(Describe(root.name, region, stats));
                        regions++;
                    }
                    weapons++;
                }
                catch (Exception e) { errors.Add($"{path}: {e.Message}"); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            string report = string.Join("\n", lines);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Preflight FAIL: {errors.Count} оружий с ошибкой; Assets не записаны.\n" +
                                                    string.Join("\n", errors) + "\n" + report);
            return $"Preflight PASS: {weapons} оружий, {regions} непустых областей; Assets не записаны.\n{report}";
        }

        private static string Describe(string weapon, WeaponVisualRegion region, WeaponHighlightRegionBaker.RegionStats stats)
        {
            string mode = region.Triangles != null ? "island" : "clip";
            string line = $"{weapon}/{region.Name}: {mode}, область {stats.Area * 1e4f:F0} см²";
            MeshIslands.Island island = region.SeedIsland;
            if (island == null) return line;
            Vector3 size = island.Bounds.size * 1000f;
            line += $"; остров затравки {size.x:F0}×{size.y:F0}×{size.z:F0} мм, {island.Area * 1e4f:F0} см²" +
                    $"; на острове {Percent(stats.OnSeedIsland, stats.Area)} области, покрыто {Percent(stats.OnSeedIsland, island.Area)} острова";
            return region.IslandRejection == null ? line : $"{line}; остров не взят: {region.IslandRejection}";
        }

        private static string Percent(float part, float whole) => whole > 0f ? $"{100f * part / whole:F0}%" : "—";

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
            // Только своё: области (Interaction/<имя> или папка KINEMATION ствола), материал приёмной области, префабы.
            OwnAssetSave.Save(weapons.Concat(magazines).Append("Assets/Art/Weapons/Interaction")
                .Concat(weapons.Select(p => KinemationFixReview.InteractionFolder(System.IO.Path.GetFileNameWithoutExtension(p)))));
            return $"Применено: {weapons.Length} оружий, {magazines.Length} магазинов.\n{preflight}";
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
