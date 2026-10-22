using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Металличность выше потолка Quest (QPM-SHADER-008). URP Lit в metallic-режиме: с <c>_METALLICSPECGLOSSMAP</c>
    /// металличность — канал R карты <c>_MetallicGlossMap</c> (как его видит шейдер: при sRGB-импорте с гамма-декодированием),
    /// <c>_Metallic</c> игнорируется; без карты — <c>_Metallic</c>. Карта читается с GPU (импортированная текстура не читаема
    /// с CPU), оценка — 95-й перцентиль R.
    /// </summary>
    public sealed class MetallicHighRule : AuditRule, IMaterialAuditCheck
    {
        [AuditThreshold("Потолок металличности на Quest (решение пользователя 2026-10-22)")]
        public float MaxMetallic = 0.3f;

        [AuditThreshold("Допуск сверх потолка (сжатие и фильтрация карты)")]
        public float Tolerance = 0.03f;

        private readonly Dictionary<Texture, MetallicMapStats> _maps = new Dictionary<Texture, MetallicMapStats>();

        public override string Id => "material.metallic-high";
        public override string Title => "Металличность выше потолка Quest";
        public override string Category => "material";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Цвет металла (Metallic ≈ 1) — почти целиком отражение окружения: без запечённой пробы отражений в каждой зоне, " +
            "ночью и в помещении металл чернеет рядом с освещёнными руками. Рассеянный свет из лайтмап и проб металл почти " +
            "не принимает. С металличностью ≤ 0,3 поверхность освещается лампами как остальные, а металл читается цветом и бликами.";

        public override string Evidence => AuditEvidence.LobbyWeaponReflection +
            " Замер оружия 2026-10-22: у 14 из 84 материалов средняя металличность 0,57–0,86, карты пака Hands почти бинарные " +
            "(медиана R 1.0) при альбедо металла 0,08–0,30; пользователь в шлеме: револьвер «слишком металлический».";

        public override string FixHint =>
            "Фиксер MetallicCapFixer: копия карты металличности с R × потолок (гладкость в A сохраняется, импорт линейный) " +
            "или _Metallic = потолок для материалов без карты. Исходные текстуры пака не меняются.";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            if (material.shader == null || !material.HasProperty("_Metallic"))
                return;
            // URP Lit: _WorkflowMode 0 — specular, 1 — metallic.
            if (material.HasProperty("_WorkflowMode") && material.GetFloat("_WorkflowMode") < 0.5f)
                return;
            string label = AuditAssetUtil.AssetLabel(material);
            Texture map = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
            if (map != null && material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
            {
                if (!_maps.TryGetValue(map, out MetallicMapStats stats))
                    _maps[map] = stats = MetallicMapStats.Measure(map);
                if (stats.P95 <= MaxMetallic + Tolerance)
                    return;
                string texturePath = AssetDatabase.GetAssetPath(map);
                var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                context.Report(this, label, "металличность по карте до " + stats.P95.ToString("0.00") + " (потолок " + MaxMetallic.ToString("0.0#") + ")")
                    .WithGroup("metallic-map")
                    .WithItem(label + " ← " + texturePath)
                    .WithFixTarget(assetPath)
                    .WithMetric("p95", stats.P95)
                    .WithMetric("mean", stats.Mean)
                    .WithMetric("map", texturePath)
                    .WithMetric("mapSrgb", importer != null && importer.sRGBTexture);
                return;
            }
            float metallic = material.GetFloat("_Metallic");
            if (metallic <= MaxMetallic + Tolerance)
                return;
            context.Report(this, label, "_Metallic " + metallic.ToString("0.00") + " (потолок " + MaxMetallic.ToString("0.0#") + ")")
                .WithGroup("metallic-value")
                .WithItem(label)
                .WithFixTarget(assetPath)
                .WithMetric("metallic", metallic);
        }
    }

    /// <summary>
    /// Каналы карты металличности так, как их читает шейдер: текстура рисуется в линейный RT (sRGB-импорт декодируется),
    /// пиксели читаются с GPU. Используется правилом (статистика) и фиксером (пиксели копии).
    /// </summary>
    internal readonly struct MetallicMapStats
    {
        private MetallicMapStats(float mean, float p95)
        {
            Mean = mean;
            P95 = p95;
        }

        public float Mean { get; }
        public float P95 { get; }

        public static MetallicMapStats Measure(Texture map)
        {
            Color[] pixels = ReadLinear(map, 256);
            var r = new float[pixels.Length];
            double sum = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                r[i] = pixels[i].r;
                sum += r[i];
            }
            Array.Sort(r);
            return new MetallicMapStats((float)(sum / Math.Max(1, r.Length)), r.Length > 0 ? r[(int)(r.Length * 0.95f)] : 0f);
        }

        /// <summary>Пиксели текстуры в линейных значениях шейдера, размер не больше <paramref name="maxSize"/>.</summary>
        public static Color[] ReadLinear(Texture map, int maxSize) => ReadLinear(map, maxSize, out _, out _);

        public static Color[] ReadLinear(Texture map, int maxSize, out int width, out int height)
        {
            float scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(map.width, map.height));
            width = Mathf.Max(1, Mathf.RoundToInt(map.width * scale));
            height = Mathf.Max(1, Mathf.RoundToInt(map.height * scale));
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var readback = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            try
            {
                Graphics.Blit(map, rt);
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply(false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }
    }
}
