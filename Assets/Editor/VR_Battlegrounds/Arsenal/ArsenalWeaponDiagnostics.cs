using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Gameplay;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Читает готовые KIN-префабы. Не сэмплирует источник, не импортирует модели и не исправляет ассеты.</summary>
    public static class ArsenalWeaponDiagnostics
    {
        public const string ExportPath = "tmp/arsenal-weapon-diagnostics/kinemation.json";

        [Serializable]
        public sealed class Row
        {
            public string Name, Path, Guid, Motion;
            public int GrabPoints, Meshes, Bindings, MissingScripts;
            public bool PrimarySupportSeparate;
            public readonly List<string> Problems = new List<string>();
            public readonly List<BindingRow> BindingDetails = new List<BindingRow>();
            public readonly List<RegionRow> Regions = new List<RegionRow>();
        }

        [Serializable]
        public sealed class BindingRow
        {
            public string Part, Target;
            public bool InMagazine;
        }

        [Serializable]
        public sealed class RegionRow
        {
            public string Name, Mesh, Size;
            public int Vertices;
        }

        [Serializable]
        public sealed class Report
        {
            public readonly string CapturedUtc = DateTime.UtcNow.ToString("o");
            public readonly List<Row> Rows = new List<Row>();
            public string Summary => string.Join("\n", Rows.Select(r => r.Name + " [" + r.Path + "]: " +
                (r.Problems.Count == 0 ? "структура motion/grab/mesh OK" : string.Join("; ", r.Problems)))) +
                "\nСнимок " + CapturedUtc + ". Префабы прочитаны без исправлений и импорта. Поведение, баланс и readiness этим отчётом не проверены.";
        }

        /// <summary>Проверяет только объявленные поддержанные рецепты, без source constructor/EnsureReadable.</summary>
        public static Report Capture(IEnumerable<KinemationWeaponRecipe> recipes)
        {
            CheckEditor();
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));
            var report = new Report();
            foreach (var recipe in recipes)
            {
                if (recipe?.Weapon == null)
                {
                    report.Rows.Add(Inspect("Пустой рецепт", "", null));
                    continue;
                }
                string path = KinemationWeaponBuilder.PrefabPath(recipe);
                report.Rows.Add(Inspect(recipe.Weapon.Name, path, AssetDatabase.LoadAssetAtPath<GameObject>(path)));
            }
            return report;
        }

        /// <summary>Чистая диагностика существующего объекта; отсутствие обязательных данных возвращается строками.</summary>
        public static Row Inspect(string name, string path, GameObject root)
        {
            CheckEditor();
            var row = new Row { Name = name ?? "Без имени", Path = path ?? "", Guid =
                string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path) };
            if (root == null) { row.Problems.Add("Нет префаба"); return row; }
            if (root.GetComponent<UxrFirearmWeapon>() == null) row.Problems.Add("Нет UxrFirearmWeapon");
            var grab = root.GetComponent<UxrGrabbableObject>();
            if (grab == null) row.Problems.Add("Нет UxrGrabbableObject");
            else
            {
                row.GrabPoints = grab.GrabPointCount;
                row.PrimarySupportSeparate = row.GrabPoints < 2 || grab.GetGrabPoint(0).EnableOnHandNear != grab.GetGrabPoint(1).EnableOnHandNear;
                if (row.GrabPoints == 0) row.Problems.Add("Нет точек хвата");
                if (!row.PrimarySupportSeparate) row.Problems.Add("Основной и опорный хват используют одну подсветку");
            }
            var mechanism = root.GetComponent<WeaponMechanismVisuals>();
            if (mechanism == null) row.Problems.Add("Нет WeaponMechanismVisuals");
            else
            {
                row.Motion = mechanism.Motion == null ? "" : AssetDatabase.GetAssetPath(mechanism.Motion);
                if (mechanism.Motion == null) row.Problems.Add("Нет motion asset");
                foreach (var binding in mechanism.Bindings ?? Array.Empty<WeaponMechanismVisuals.Binding>())
                {
                    row.Bindings++;
                    if (binding == null) { row.Problems.Add("Пустой motion binding"); continue; }
                    if (binding.Target == null) row.Problems.Add("Нет binding target: " + binding.Part);
                    row.BindingDetails.Add(new BindingRow { Part = binding.Part, InMagazine = binding.InMagazine,
                        Target = binding.Target == null ? "" : AnimationUtility.CalculateTransformPath(binding.Target, root.transform) });
                }
                if (row.Bindings == 0) row.Problems.Add("Нет motion bindings");
            }
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            row.Meshes = filters.Length;
            if (row.Meshes == 0) row.Problems.Add("Нет MeshFilter");
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) row.Problems.Add("Нет mesh: " + AnimationUtility.CalculateTransformPath(filter.transform, root.transform));
                else if (filter.sharedMesh.vertexCount == 0) row.Problems.Add("Пустой mesh: " + filter.name);
                if (filter.name.StartsWith("GrabHighlight", StringComparison.Ordinal) || filter.transform.parent != null &&
                    filter.transform.parent.name.StartsWith("GrabHighlight", StringComparison.Ordinal))
                    row.Regions.Add(new RegionRow { Name = AnimationUtility.CalculateTransformPath(filter.transform, root.transform),
                        Mesh = filter.sharedMesh == null ? "" : AssetDatabase.GetAssetPath(filter.sharedMesh),
                        Vertices = filter.sharedMesh == null ? 0 : filter.sharedMesh.vertexCount,
                        Size = filter.sharedMesh == null ? "" : filter.sharedMesh.bounds.size.ToString("F4") });
            }
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                row.MissingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (row.MissingScripts != 0) row.Problems.Add("Missing scripts: " + row.MissingScripts);
            return row;
        }

        /// <summary>Явный экспорт уже полученного снимка. Вызывающий writer держит own lease и объявляет ExportPath.</summary>
        public static string Export(Report report)
        {
            CheckEditor();
            if (report == null) throw new ArgumentNullException(nameof(report));
            string workspace = Directory.GetParent(Application.dataPath).FullName;
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(workspace, ExportPath));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            return "Экспортирован снимок " + report.CapturedUtc + ": " + ExportPath + ". Assets не записаны.";
        }

        private static void CheckEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Диагностика доступна только в свободном Edit Mode.");
        }
    }
}
