using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Следы HDRP в материале: keywords и выключенные проходы HDRP, HDRP-свойства. Сами по себе почти бесплатны,
    /// но метят пак, собранный под ПК: шейдер, текстуры и альфа-отсечение надо пересмотреть под Quest.
    /// Отдельно ловит потерянное альфа-отсечение: в HDRP было включено, в URP — нет.
    /// </summary>
    public sealed class HdrpLegacyMaterialRule : AuditRule, IMaterialAuditCheck
    {
        [AuditThreshold("Keywords HDRP")]
        public string[] HdrpKeywords = { "_DISABLE_SSR_TRANSPARENT", "_DISABLE_SSR", "_DISABLE_DECALS", "_ENABLE_FOG_ON_TRANSPARENT" };

        [AuditThreshold("Проходы HDRP в disabledShaderPasses")]
        public string[] HdrpPasses = { "RayTracingPrepass", "TransparentDepthPrepass", "TransparentDepthPostpass", "TransparentBackface" };

        [AuditThreshold("Свойства, существующие только в HDRP")]
        public string[] HdrpProperties = { "_AlphaCutoffEnable", "_StencilRefGBuffer", "_DiffusionProfileHash", "_SurfaceType" };

        public override string Id => "material.hdrp-legacy";
        public override string Title => "HDRP-наследие в материале";
        public override string Category => "material";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Info;

        public override string WhyBadOnQuest =>
            "Материал сделан под HDRP (ПК): параметры, размеры текстур и модель освещения рассчитаны на десктопный GPU. " +
            "Мёртвые keywords/проходы безвредны, но это маркер: такой материал и его текстуры ещё не адаптированы под Quest. " +
            "Если HDRP-альфа-отсечение не перенесено в URP — листва/сетки рисуются сплошными картами.";

        public override string Evidence =>
            "Пак MilitaryCamp (Lobby): 82 из 165 материалов несут _DISABLE_SSR_TRANSPARENT и RayTracingPrepass в disabledShaderPasses " +
            "(аудит текстов .mat, 2026-10-20). " + AuditEvidence.LobbyQuest3PerDraw;

        public override string FixHint =>
            "Пересобрать материал на мобильный шейдер URP (фиксер-конвертер), проверить альфа-отсечение и размеры текстур; " +
            "мёртвые keywords уйдут при смене шейдера.";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            var serialized = new SerializedObject(material);
            var markers = new List<string>();
            CollectStrings(serialized.FindProperty("m_ValidKeywords"), HdrpKeywords, markers, "keyword ");
            CollectStrings(serialized.FindProperty("m_InvalidKeywords"), HdrpKeywords, markers, "keyword ");
            CollectStrings(serialized.FindProperty("disabledShaderPasses"), HdrpPasses, markers, "pass ");

            bool hdrpAlphaCutoff = false;
            SerializedProperty floats = serialized.FindProperty("m_SavedProperties.m_Floats");
            if (floats != null && floats.isArray)
            {
                for (int i = 0; i < floats.arraySize; i++)
                {
                    SerializedProperty entry = floats.GetArrayElementAtIndex(i);
                    string name = entry.FindPropertyRelative("first")?.stringValue;
                    if (name == null || Array.IndexOf(HdrpProperties, name) < 0)
                        continue;
                    if (!markers.Contains("prop " + name))
                        markers.Add("prop " + name);
                    if (name == "_AlphaCutoffEnable" && entry.FindPropertyRelative("second")?.floatValue > 0.5f)
                        hdrpAlphaCutoff = true;
                }
            }
            if (markers.Count == 0)
                return;

            string label = AuditAssetUtil.AssetLabel(material);
            string shaderName = material.shader != null ? material.shader.name : "(нет шейдера)";
            if (hdrpAlphaCutoff && !AuditAssetUtil.IsAlphaTested(material))
            {
                context.Report(this, label, "в HDRP было альфа-отсечение (_AlphaCutoffEnable=1), в URP оно выключено — прозрачные места рисуются сплошными",
                        AuditSeverity.Warning)
                    .WithMetric("shader", shaderName)
                    .WithFix("Включить Alpha Clipping в материале/графе или убедиться, что текстура без прозрачных мест.");
            }

            markers.Sort(StringComparer.Ordinal);
            string signature = string.Join(", ", markers);
            context.Report(this, AuditAssetUtil.AssetLabel(material.shader), "HDRP-наследие: " + signature)
                .WithGroup("hdrp:" + shaderName + "|" + signature)
                .WithItem(label)
                .WithMetric("shader", shaderName);
        }

        private static void CollectStrings(SerializedProperty array, string[] wanted, List<string> markers, string prefix)
        {
            if (array == null || !array.isArray)
                return;
            for (int i = 0; i < array.arraySize; i++)
            {
                string value = array.GetArrayElementAtIndex(i).stringValue;
                if (Array.IndexOf(wanted, value) >= 0 && !markers.Contains(prefix + value))
                    markers.Add(prefix + value);
            }
        }
    }
}
