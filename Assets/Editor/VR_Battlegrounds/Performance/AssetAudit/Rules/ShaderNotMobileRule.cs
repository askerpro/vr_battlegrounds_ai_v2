using System;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>Материал на шейдере не из мобильного набора URP (Simple Lit / Baked Lit / Unlit). Группа — шейдер.</summary>
    public sealed class ShaderNotMobileRule : AuditRule, IMaterialAuditCheck
    {
        [AuditThreshold("Разрешённые шейдеры: точное имя или префикс с '/' в конце")]
        public string[] AllowedShaders =
        {
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Baked Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Simple Lit",
            "Universal Render Pipeline/Particles/Unlit",
            "TextMeshPro/Mobile/",
            "UI/",
            "Sprites/",
            "Skybox/",
            "Hidden/",
        };

        [AuditThreshold("Шейдеры, дающие «Критично» (полный PBR/сложные модели освещения)")]
        public string[] CriticalShaders = { "Universal Render Pipeline/Complex Lit", "Universal Render Pipeline/Terrain/Lit" };

        public override string Id => "shader.non-mobile";
        public override string Title => "Шейдер не из мобильного набора URP";
        public override string Category => "shader";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Quest упирается в пиксели: каждый фрагмент считается дважды (multiview) на тайловом мобильном GPU. " +
            "URP Lit и графы паков считают полный PBR (несколько выборок, нормали, отражения окружения) на каждый пиксель; " +
            "Simple Lit / Baked Lit / Unlit — дешёвые модели для декора и больших поверхностей.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " Доля кадра: URP Lit ≈27 %, S_BasicTextured/S_Foliage ≈49 %, S_VPWorldAligned 11,8 %.";

        public override string FixHint =>
            "Статичный декор и большие поверхности — URP Baked Lit + лайтмапа (1–2 выборки); освещаемое динамикой — Simple Lit; " +
            "далёкое/эмиссивное — Unlit. Сначала «замена прежде оптимизации»: дешёвый шейдер вместо настройки дорогого. " +
            "Фиксер правила — BakedLitMaterialFixer (S_BasicTextured, S_Foliage, URP Lit → Baked Lit / Simple Lit).";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            Shader shader = material.shader;
            string label = AuditAssetUtil.AssetLabel(material);
            if (shader == null || shader.name == "Hidden/InternalErrorShader")
            {
                // HDRP Lit узнаётся по сохранённой _BaseColorMap: в URP-проекте шейдера HDRP нет (паки «HDRP+URP»).
                bool hdrp = material.HasProperty("_BaseColorMap") || new SerializedObject(material)
                    .FindProperty("m_SavedProperties.m_TexEnvs") is SerializedProperty envs && HasEntry(envs, "_BaseColorMap");
                context.Report(this, label, (hdrp ? "материал HDRP Lit, шейдера HDRP в проекте нет" : "шейдер отсутствует или с ошибкой") +
                                            " — там, где назначен, рисуется розовым", AuditSeverity.Critical)
                    .WithGroup("shader:(ошибка)")
                    .WithItem(label)
                    .WithFixTarget(assetPath)
                    .WithMetric("shader", shader != null ? shader.name : "(нет)")
                    .WithMetric("hdrp", hdrp)
                    .WithFix(hdrp
                        ? "BakedLitMaterialFixer: HDRP Lit → Baked Lit по сохранённым свойствам (_BaseColorMap, _BaseColor)."
                        : "Назначить мобильный шейдер URP или восстановить исходный шейдер пака.");
                return;
            }
            if (IsAllowed(shader.name))
                return;

            bool critical = Array.IndexOf(CriticalShaders, shader.name) >= 0;
            string shaderAsset = AuditAssetUtil.AssetLabel(shader);
            ShaderGraphInfo graph = context.GetShaderGraphInfo(shader);
            string kind = graph != null ? "Shader Graph" : shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ? "URP" : "сторонний";
            context.Report(this, shaderAsset, kind + " «" + shader.name + "» вместо Simple Lit / Baked Lit / Unlit",
                    critical ? AuditSeverity.Critical : DefaultSeverity)
                .WithGroup("shader:" + shader.name)
                .WithItem(label)
                .WithFixTarget(assetPath)
                .WithMetric("shader", shader.name)
                .WithMetric("passes", shader.passCount);
        }

        private static bool HasEntry(SerializedProperty entries, string name)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("first")?.stringValue == name)
                    return true;
            }
            return false;
        }

        private bool IsAllowed(string shaderName)
        {
            foreach (string allowed in AllowedShaders)
            {
                if (allowed.EndsWith("/", StringComparison.Ordinal)
                        ? shaderName.StartsWith(allowed, StringComparison.Ordinal)
                        : shaderName == allowed)
                    return true;
            }
            return false;
        }
    }
}
