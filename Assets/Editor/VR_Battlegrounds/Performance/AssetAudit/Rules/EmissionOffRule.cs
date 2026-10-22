using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Эмиссия задана, но не рисуется (QPM-SHADER-006). Проверяет сохранённый ассет, а не намерение инструмента:
    /// (а) шейдер с эмиссией, <c>_EmissionColor</c> не чёрный, а GI-флаг без AnyEmissive (None/EmissiveIsBlack) — URP при
    /// импорте/валидации выключает <c>_EMISSION</c>; EmissiveIsBlack с белым цветом без карты — остаток конвертации HDRP→URP,
    /// не находка; (б) шейдер без эмиссии, а в сохранённых свойствах остались не чёрный
    /// <c>_EmissionColor</c> и карта <c>_EmissionMap</c> — свечение потеряно при смене шейдера (Unlit не в счёт: он и так не освещается).
    /// </summary>
    public sealed class EmissionOffRule : AuditRule, IMaterialAuditCheck
    {
        [AuditThreshold("Минимальная компонента _EmissionColor, с которой эмиссия считается заданной")]
        public float MinEmission = 0.001f;

        public override string Id => "material.emission-off";
        public override string Title => "Эмиссия задана, но выключена";
        public override string Category => "material";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Не цена кадра, а скрытая ошибка картинки: светильник рисуется тёмным. URP (BaseShaderGUI.SetMaterialKeywords) " +
            "включает _EMISSION только при GI-флаге AnyEmissive (RealtimeEmissive/BakedEmissive); с None или EmissiveIsBlack " +
            "эмиссия молча выключается при импорте. Следом инструменты, судящие по _EMISSION, считают материал неэмиссивным " +
            "и переводят его на шейдер без эмиссии.";

        public override string Evidence =>
            "Lobby 2026-10-22 (задача scene-detail-followups): LobbyNightDoorLamp создан с EmissiveIsBlack, _EMISSION выключился, " +
            "ярус материалов scene-detail принял его за неэмиссивный и перевёл в Baked Lit — дверные лампы не светятся; " +
            "сохранённый ассет: m_LightmapFlags 4, _EmissionColor (1, 0.74, 0.46), _EmissionMap T_Door_lamp_EM.";

        public override string FixHint =>
            "Шейдер с эмиссией — GI-флаг RealtimeEmissive и _EMISSION (realtime GI выключен: свечение видно, в свет не идёт; " +
            "BakedEmissive добавил бы вклад в лайтмапы). Шейдер без эмиссии — Simple Lit без бликов с картой и цветом эмиссии. " +
            "Фиксер правила — EmissionOffFixer; инструмент, создавший материал, тоже должен ставить AnyEmissive.";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            EmissionState state = EmissionState.Read(material);
            if (state.Color.maxColorComponent <= MinEmission)
                return;
            string label = AuditAssetUtil.AssetLabel(material);
            string shaderName = material.shader != null ? material.shader.name : "(нет шейдера)";
            if (state.ShaderHasEmission)
            {
                if ((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0)
                    return;
                // EmissiveIsBlack — так URP хранит снятую галочку Emission. Белый цвет без карты — остаток конвертации HDRP→URP
                // (у HDRP Lit _EmissionColor по умолчанию белый), а не потерянное свечение: M_Glass_Inst пака PostApocalypticTown,
                // в HDRP-источнике _EmissiveColor чёрный. Подобранный цвет (WarmLampDiffuser, OwnerIndicator) или карта
                // (дверная лампа, T_Door_lamp_EM) — признак задуманного свечения, такие материалы ловятся.
                if ((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0 && !state.HasMap &&
                    IsConversionWhite(state.Color))
                    return;
                context.Report(this, label, "_EmissionColor не чёрный, GI-флаг " + material.globalIlluminationFlags +
                                            " без AnyEmissive — URP выключает _EMISSION")
                    .WithGroup("emission-flag:" + material.globalIlluminationFlags)
                    .WithItem(label)
                    .WithFixTarget(assetPath)
                    .WithMetric("shader", shaderName)
                    .WithMetric("giFlags", material.globalIlluminationFlags.ToString())
                    .WithMetric("emissionKeyword", material.IsKeywordEnabled("_EMISSION"));
                return;
            }
            // Unlit сам по себе «светится» основным цветом: остаток карты эмиссии там не потеря (GlowMat_URP сэмплов UXR).
            if (!state.HasMap || shaderName.Contains("Unlit"))
                return;
            context.Report(this, label, "шейдер «" + shaderName + "» без эмиссии, а в материале остались цвет и карта эмиссии — свечение потеряно")
                .WithGroup("emission-lost:" + shaderName)
                .WithItem(label)
                .WithFixTarget(assetPath)
                .WithMetric("shader", shaderName)
                .WithMetric("giFlags", material.globalIlluminationFlags.ToString());
        }

        private static bool IsConversionWhite(Color color) =>
            Mathf.Abs(color.r - 1f) < 0.001f && Mathf.Abs(color.g - 1f) < 0.001f && Mathf.Abs(color.b - 1f) < 0.001f;
    }

    /// <summary>Эмиссия материала по сохранённым свойствам (они переживают смену шейдера) и по самому шейдеру.</summary>
    internal readonly struct EmissionState
    {
        private EmissionState(bool shaderHasEmission, Color color, Texture map)
        {
            ShaderHasEmission = shaderHasEmission;
            Color = color;
            Map = map;
        }

        public bool ShaderHasEmission { get; }
        public Color Color { get; }
        public Texture Map { get; }
        public bool HasMap => Map != null;

        public static EmissionState Read(Material material)
        {
            bool shaderHas = material.shader != null && material.HasProperty("_EmissionColor") &&
                             material.shader.keywordSpace.FindKeyword("_EMISSION").isValid;
            Color color = Color.black;
            Texture map = null;
            var serialized = new SerializedObject(material);
            SerializedProperty colors = serialized.FindProperty("m_SavedProperties.m_Colors");
            for (int i = 0; colors != null && i < colors.arraySize; i++)
            {
                SerializedProperty entry = colors.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first")?.stringValue == "_EmissionColor")
                    color = entry.FindPropertyRelative("second").colorValue;
            }
            SerializedProperty textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; textures != null && i < textures.arraySize; i++)
            {
                SerializedProperty entry = textures.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first")?.stringValue == "_EmissionMap")
                    map = entry.FindPropertyRelative("second.m_Texture")?.objectReferenceValue as Texture;
            }
            return new EmissionState(shaderHas, color, map);
        }
    }
}
