using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Явная публикация активных форм. Не меняет геометрию источников, экземпляры и сохранённые сцены.</summary>
    public static class BlockoutCanonicalRegistry
    {
        private const string Folder = "Assets/Settings/LevelDesign/BlockoutBlocks";
        private const string Sources = "Assets/Prefabs/LevelDesign/LD_Alphabet/";
        private static readonly string[] Names = { "LD_Wall_Mid", "LD_Block_Low", "LD_Crate", "LD_Dorito_Mid", "LD_Can_Mid", HalfCylinderSourceBuilder.SourceKey };
        private static readonly string[] Titles = { "Wall", "Block", "Cube", "Step", "Cylinder", "HalfCylinder" };
        private static readonly string[] Russian = { "Стена", "Блок", "Куб", "Ступенчатое укрытие", "Цилиндр", "Полуцилиндр" };
        public static int ActiveFormCount => Names.Length;
        public static bool IsActiveDefinition(BlockoutBlockDefinition definition) => definition != null && Names.Contains(definition.dimensionsSourceKey);
        public static bool IsHalfCylinder(BlockoutBlockDefinition definition) => definition != null && definition.dimensionsSourceKey == HalfCylinderSourceBuilder.SourceKey;
        private static readonly string[] Descriptions =
        {
            "Прямоугольная стена с тонким профилем. Высота, класс материала и реальные сквозные щели выбираются отдельно.",
            "Прямоугольный блок с глубоким профилем. Высота, класс материала и реальные сквозные щели выбираются отдельно.",
            "Сплошной блок с квадратным основанием. Арт вписывается в его габариты; высота и класс материала выбираются отдельно.",
            "Широкий низ высотой 1,2 м и более узкий верх. При общей высоте 1,2 м остаётся только низ. Класс материала выбирается отдельно.",
            "Сплошная цилиндрическая форма с круговым профилем. Высота и класс материала выбираются отдельно.",
            "Вертикальная половина цилиндра с плоской задней гранью. Высота и класс материала выбираются отдельно; связь с укрытием задаётся вручную."
        };

        /// <summary>Старые паспорта доступны только для разрешения существующих ссылок, а не для размещения из палитры.</summary>
        public static IEnumerable<BlockoutBlockDefinition> DefinitionsIncludingLegacy() =>
            AssetDatabase.FindAssets("t:BlockoutBlockDefinition", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<BlockoutBlockDefinition>).Where(d => d != null);

        private static BlockoutBlockDefinition[] Selected()
        {
            var all = DefinitionsIncludingLegacy().ToArray();
            var result = new BlockoutBlockDefinition[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Sources + Names[i] + ".prefab");
                if (source == null) throw new InvalidOperationException("Не найден канонический источник: " + Names[i]);
                result[i] = all.FirstOrDefault(d => d.geometryPrefab == source || d.materialVariants.Any(v => v != null && v.sourcePrefab == source));
                if (result[i] == null || string.IsNullOrWhiteSpace(result[i].shapeId))
                    throw new InvalidOperationException("Не найден существующий паспорт с ShapeID: " + Names[i]);
            }
            if (result.Distinct().Count() != Names.Length || result.Select(d => d.shapeId).Distinct().Count() != Names.Length)
                throw new InvalidOperationException("Канонические формы должны иметь различные ShapeID.");
            return result;
        }

        public static bool TryLegacyDefinition(GameObject go, out BlockoutBlockDefinition definition)
        {
            definition = go != null ? go.GetComponent<BlockoutBlockInstance>()?.definition : null;
            if (definition != null) return true;
            if (go == null) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source == null && EditorUtility.IsPersistent(go)) source = go;
            var all = DefinitionsIncludingLegacy().ToArray();
            if (source != null)
                definition = all.FirstOrDefault(d => d.geometryPrefab == source || d.materialVariants.Any(v => v != null && v.sourcePrefab == source));
            if (definition == null)
            {
                string original = go.name.Replace("(Clone)", "").Trim();
                definition = all.FirstOrDefault(d => d.geometryPrefab != null && MatchesAlias(original, d.geometryPrefab.name) ||
                    d.materialVariants.Any(v => v?.sourcePrefab != null && MatchesAlias(original, v.sourcePrefab.name)));
                if (definition == null)
                {
                    string name = AliasName(original);
                    var candidates = all.Where(d => d.geometryPrefab != null && MatchesAlias(name, AliasName(d.geometryPrefab.name)) ||
                        d.materialVariants.Any(v => v?.sourcePrefab != null && MatchesAlias(name, AliasName(v.sourcePrefab.name)))).ToArray();
                    // Полый и сплошной источники нельзя угадывать по имени после утраты prefab-связи.
                    if (candidates.Length == 1) definition = candidates[0];
                }
            }
            return definition != null;
        }

        private static string AliasName(string name) => Regex.Replace(name.Replace("(Clone)", "").Trim(), "_(Hard|Soft|Visual)(?=$|[_\\s(.])", "");
        private static bool MatchesAlias(string name, string alias) => name == alias || name.StartsWith(alias + " [", StringComparison.Ordinal) ||
            name.StartsWith(alias + " (", StringComparison.Ordinal);

        /// <summary>Под замком Unity сохраняет только активные паспорта и реестр. Старые GUID остаются разрешимыми.</summary>
        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Публикация палитры выполняется вне Play Mode после компиляции.");
            var registry = BlockoutRegistryFactory.Current;
            if (registry == null) throw new InvalidOperationException("Существующий реестр не найден; старый генератор алфавита не запускается автоматически.");
            var selected = Selected();
            if (selected[3].geometryPrefab.GetComponent<BlockoutSteppedGeometry>() == null)
                throw new InvalidOperationException("Ступенчатый источник ещё не применён; пирамиду нельзя публиковать в новой палитре.");
            string backup = "Temp/LevelDesign/CanonicalRegistry/Before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (var asset in selected.Cast<UnityEngine.Object>().Append(registry))
            {
                string path = AssetDatabase.GetAssetPath(asset);
                File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"), false);
            }
            for (int i = 0; i < selected.Length; i++)
            {
                var d = selected[i];
                d.geometryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Sources + Names[i] + ".prefab");
                d.dimensionsSourceKey = Names[i];
                d.title = Titles[i]; d.displayName = Russian[i]; d.description = Descriptions[i];
                d.gameplayGeometry = true; d.heightEditable = true;
                d.supportsCellWall = i < 3; d.supportsOpenings = i < 3; d.editableDimensions = i < 2;
                d.openingDisabledReason = i < 3 ? "" : "Для этой формы генератор сквозных отверстий пока не реализован.";
                d.defaultMaterial = CoverClass.Hard; d.allowedMaterials = new[] { CoverClass.Hard, CoverClass.Soft };
                d.minDimensions.y = 1.2f; d.maxDimensions.y = 2.5f; d.vaultShapeSuitable = false;
                SetPlanCaps(d, BlockoutGridSettings.Dimensions(d.dimensionsSourceKey));
                EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d);
            }
            registry.SetDefinitions(selected); EditorUtility.SetDirty(registry); AssetDatabase.SaveAssetIfDirty(registry);
            return backup;
        }

        public static Dictionary<string, object> Probe()
        {
            var registry = BlockoutRegistryFactory.Current;
            var active = registry != null ? registry.Definitions.Where(d => d != null).ToArray() : Array.Empty<BlockoutBlockDefinition>();
            var expected = Selected();
            return new Dictionary<string, object>
            {
                { "activeCount", active.Length }, { "exactActiveSources", active.SequenceEqual(expected) },
                { "titles", active.Select(d => d.title).ToArray() }, { "shapeIDs", active.Select(d => d.shapeId).ToArray() },
                { "allHeightEditable", active.Length == Names.Length && active.All(d => d.heightEditable && Mathf.Abs(d.minDimensions.y - 1.2f) < .0001f && Mathf.Abs(d.maxDimensions.y - 2.5f) < .0001f) },
                { "fixedPlanCaps", active.Length == Names.Length && FixedPlanCaps(expected) },
                { "hardSoftIndependent", active.Length == Names.Length && active.All(d => d.defaultMaterial == CoverClass.Hard && d.allowedMaterials.SequenceEqual(new[] { CoverClass.Hard, CoverClass.Soft })) },
                { "legacyDefinitionsOutsidePalette", DefinitionsIncludingLegacy().Count(d => !active.Contains(d)) },
                { "footprints", expected.Select(BlockoutRegistryFactory.DefaultDimensions).ToArray() },
                { "sceneMigration", "Экземпляры не переписываются: сохранены паспорта, ShapeID, GUID, pose, высота, класс материала, роли и смысловые ID." }
            };
        }

        private static bool FixedPlanCaps(BlockoutBlockDefinition[] definitions)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                var d = definitions[i];
                if (d.editableDimensions != (i < 2) || d.supportsCellWall != (i < 3) || d.supportsOpenings != (i < 3)) return false;
                Vector2 plan = BlockoutGridSettings.Dimensions(d.dimensionsSourceKey);
                if (Mathf.Abs(d.minDimensions.z - plan.y) > .0001f || Mathf.Abs(d.maxDimensions.z - plan.y) > .0001f) return false;
                if (i >= 2 && (Mathf.Abs(d.minDimensions.x - plan.x) > .0001f || Mathf.Abs(d.maxDimensions.x - plan.x) > .0001f)) return false;
            }
            return true;
        }

        /// <summary>Ограничения формы следуют опубликованному паспорту размеров, а не повторным числовым константам.</summary>
        public static void SetPlanCaps(BlockoutBlockDefinition definition, Vector2 plan)
        {
            definition.minDimensions.z = definition.maxDimensions.z = plan.y;
            if (!definition.editableDimensions) definition.minDimensions.x = definition.maxDimensions.x = plan.x;
        }

        /// <summary>Прямые сериализованные зависимости; удаление старых ассетов требует отдельной доказанной миграции.</summary>
        public static Dictionary<string, string[]> ReferenceInventory()
        {
            var active = new HashSet<BlockoutBlockDefinition>(Selected());
            var legacy = DefinitionsIncludingLegacy().Where(d => !active.Contains(d)).ToArray();
            var targets = new HashSet<string>(legacy.Select(AssetDatabase.GetAssetPath));
            foreach (var d in legacy)
            {
                if (d.geometryPrefab != null) targets.Add(AssetDatabase.GetAssetPath(d.geometryPrefab));
                foreach (var v in d.materialVariants) if (v?.sourcePrefab != null) targets.Add(AssetDatabase.GetAssetPath(v.sourcePrefab));
            }
            var result = new Dictionary<string, string[]>();
            foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                (p.EndsWith(".unity", StringComparison.Ordinal) || p.EndsWith(".prefab", StringComparison.Ordinal) || p.EndsWith(".asset", StringComparison.Ordinal))))
            {
                var dependencies = AssetDatabase.GetDependencies(path, false).Where(p => p != path && targets.Contains(p)).ToArray();
                if (dependencies.Length > 0) result[path] = dependencies;
            }
            return result;
        }
    }
}
