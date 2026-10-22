using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Fixers
{
    /// <summary>
    /// Фиксер правила <c>material.metallic-high</c> (QPM-SHADER-008): металличность не выше потолка Quest.
    /// Материал с картой металличности получает копию карты: R × потолок (металл 1 → потолок, градиенты сохраняются),
    /// G/B/A без изменений (в A — гладкость), импорт линейный (данные, не цвет). Копия одна на исходную карту и лежит в
    /// <see cref="OutputFolder"/>; исходные текстуры паков не меняются. Материал без карты — <c>_Metallic</c> = потолок.
    /// </summary>
    public sealed class MetallicCapFixer : AuditFixer
    {
        public const string OutputFolder = "Assets/Art/QuestMaterials/MetallicCap";

        /// <summary>Потолок металличности — как порог правила (QPM-SHADER-008).</summary>
        public static float Cap => new MetallicHighRule().MaxMetallic;

        public override string RuleId => "material.metallic-high";
        public override string Title => "Ограничить металличность (копия карты)";
        public override string WhatChanges =>
            "Материал с картой металличности: _MetallicGlossMap → копия в " + OutputFolder + " с R × " + Cap.ToString("0.0#") +
            " (гладкость в A сохранена, импорт линейный); исходная карта не меняется. Без карты — _Metallic = " + Cap.ToString("0.0#") + ".";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuditFinding finding in findings)
            {
                foreach (string path in finding.FixTargets)
                {
                    if (!seen.Add(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                        continue;
                    Texture map = MapOf(material);
                    if (map != null)
                    {
                        string source = AssetDatabase.GetAssetPath(map);
                        actions.Add(new AuditFixAction(path, "_MetallicGlossMap " + source, "_MetallicGlossMap " + CopyPath(source)));
                    }
                    else
                    {
                        actions.Add(new AuditFixAction(path, "_Metallic " + material.GetFloat("_Metallic").ToString("0.00"),
                            "_Metallic " + Cap.ToString("0.00")));
                    }
                }
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            float cap = Cap;
            var copies = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            foreach (AuditFixAction action in actions)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(action.AssetPath);
                if (material == null)
                {
                    action.MarkFailed("нет материала");
                    continue;
                }
                try
                {
                    Texture map = MapOf(material);
                    if (map != null)
                    {
                        string source = AssetDatabase.GetAssetPath(map);
                        if (!copies.TryGetValue(source, out Texture2D copy))
                            copies[source] = copy = WriteCappedCopy(map, source, cap);
                        material.SetTexture("_MetallicGlossMap", copy);
                    }
                    else
                    {
                        material.SetFloat("_Metallic", Mathf.Min(material.GetFloat("_Metallic"), cap));
                    }
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    action.MarkApplied();
                }
                catch (Exception exception)
                {
                    action.MarkFailed(exception.GetBaseException().Message);
                }
            }
            GameLog.Perf.Info("[MetallicCapFixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count +
                              ", копий карт " + copies.Count + " в " + OutputFolder);
        }

        private static Texture MapOf(Material material) =>
            material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") && material.HasProperty("_MetallicGlossMap")
                ? material.GetTexture("_MetallicGlossMap")
                : null;

        private static string CopyPath(string sourcePath) =>
            OutputFolder + "/" + Path.GetFileNameWithoutExtension(sourcePath) + "_Metal" + Mathf.RoundToInt(Cap * 100) + ".png";

        // Пиксели — как их видит шейдер (sRGB-импорт исходника уже декодирован), размер — не больше maxTextureSize исходника.
        private static Texture2D WriteCappedCopy(Texture map, string sourcePath, float cap)
        {
            var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            int maxSize = sourceImporter != null ? sourceImporter.maxTextureSize : 2048;
            Color[] pixels = MetallicMapStats.ReadLinear(map, maxSize, out int width, out int height);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i].r = Mathf.Clamp01(pixels[i].r) * cap;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.SetPixels(pixels);
            texture.Apply(false);
            string path = CopyPath(sourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = maxSize;
            if (sourceImporter != null)
            {
                foreach (string platform in new[] { "Android", "Standalone" })
                {
                    TextureImporterPlatformSettings settings = sourceImporter.GetPlatformTextureSettings(platform);
                    if (settings.overridden)
                        importer.SetPlatformTextureSettings(settings);
                }
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
