using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Fixers
{
    /// <summary>
    /// Фиксер правила <c>material.emission-off</c> (QPM-SHADER-006): возвращает свечение, не добавляя его в запечённый свет.
    /// Шейдер с эмиссией — GI-флаг RealtimeEmissive и <c>_EMISSION</c> (realtime GI выключен: свечение видно, в свет не идёт).
    /// Шейдер без эмиссии (свечение потеряно при смене шейдера) — URP Simple Lit без бликов с сохранёнными картой и цветом
    /// эмиссии, основной текстурой и тайлингом. Меняются только материалы в корнях окружения
    /// (<see cref="BakedLitMaterialFixer.EnvironmentRoots"/>): общий игровой контент и сэмплы SDK — решение их владельцев. Плафоны LobbyNightLampLong приняты и правятся
    /// вручную — не трогаются: у материала один писатель (QPM-SHADER-007).
    /// </summary>
    public sealed class EmissionOffFixer : AuditFixer
    {
        public const string SimpleLitName = "Universal Render Pipeline/Simple Lit";

        /// <summary>Материалы с другим писателем — ручной правкой (по префиксу имени).</summary>
        public static readonly string[] OwnedElsewhere = { "LobbyNightLampLong" };

        public override string RuleId => "material.emission-off";
        public override string Title => "Включить эмиссию (RealtimeEmissive)";
        public override string WhatChanges =>
            "Материал с заданной эмиссией: GI-флаг RealtimeEmissive и _EMISSION; если шейдер без эмиссии — URP Simple Lit без бликов " +
            "с прежними картой/цветом эмиссии и основной текстурой. В запечённый свет эмиссия не идёт.";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuditFinding finding in findings)
            {
                foreach (string path in finding.FixTargets.Count > 0 ? (IEnumerable<string>)finding.FixTargets : new[] { finding.Asset })
                {
                    if (!seen.Add(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                        !BakedLitMaterialFixer.EnvironmentRoots.Any(r => path.StartsWith(r, StringComparison.Ordinal)))
                        continue;
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null || OwnedElsewhere.Any(p => material.name.StartsWith(p, StringComparison.Ordinal)))
                        continue;
                    EmissionState state = EmissionState.Read(material);
                    string before = material.shader.name + ", GI " + material.globalIlluminationFlags +
                                    (material.IsKeywordEnabled("_EMISSION") ? ", _EMISSION" : ", без _EMISSION");
                    string after = (state.ShaderHasEmission ? material.shader.name : SimpleLitName + " без бликов") +
                                   ", GI RealtimeEmissive, _EMISSION";
                    actions.Add(new AuditFixAction(path, before, after));
                }
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            Shader simpleLit = Shader.Find(SimpleLitName);
            foreach (AuditFixAction action in actions)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(action.AssetPath);
                if (material == null)
                {
                    action.MarkFailed("нет материала");
                    continue;
                }
                EmissionState state = EmissionState.Read(material);
                if (!state.ShaderHasEmission)
                {
                    if (simpleLit == null)
                    {
                        action.MarkFailed("нет шейдера " + SimpleLitName);
                        continue;
                    }
                    ToSimpleLit(material, simpleLit, state);
                }
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                // Проверка сохранённого ассета: URP валидирует keywords при импорте (QPM-SHADER-006).
                AssetDatabase.ImportAsset(action.AssetPath, ImportAssetOptions.ForceUpdate);
                material = AssetDatabase.LoadAssetAtPath<Material>(action.AssetPath);
                if (material.IsKeywordEnabled("_EMISSION") &&
                    (material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.AnyEmissive) != 0)
                    action.MarkApplied();
                else
                    action.MarkFailed("после импорта _EMISSION=" + material.IsKeywordEnabled("_EMISSION") + ", GI " + material.globalIlluminationFlags);
            }
            GameLog.Perf.Info("[EmissionOffFixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count);
        }

        private static void ToSimpleLit(Material m, Shader simpleLit, EmissionState state)
        {
            Texture baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            Vector2 scale = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = m.HasProperty("_BaseMap") ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
            Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            color.a = 1f;
            // Alpha clip сохраняется: без него у cutout-материала пропало бы отсечение.
            bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON");
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            m.shader = simpleLit;
            m.shaderKeywords = Array.Empty<string>();
            m.SetTexture("_BaseMap", baseMap);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            if (cutout)
            {
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            m.SetFloat("_ReceiveShadows", 0f);
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            m.SetTexture("_BumpMap", null);
            // SimpleLitGUI.SpecularSource: 1 — NoSpecular (QPM-SHADER-005).
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetTexture("_EmissionMap", state.Map);
            m.SetColor("_EmissionColor", state.Color);
        }
    }
}
