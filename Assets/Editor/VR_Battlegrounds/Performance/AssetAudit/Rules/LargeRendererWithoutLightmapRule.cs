using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Крупный рендерер без лайтмапы: освещается одной интерполированной пробой (или вообще не запекается — нет UV2).
    /// В сцене проверяет фактическую лайтмапу; в префабе/модели — только наличие UV2 у крупного меша.
    /// </summary>
    public sealed class LargeRendererWithoutLightmapRule : AuditRule, IGameObjectAuditCheck
    {
        [AuditThreshold("Площадь наибольшей грани габаритов (м²), от которой рендерер считается крупным")]
        public float MinFaceArea = 24f;

        [AuditThreshold("Пропускать рендереры, все материалы которых на шейдерах с этими подстроками (свет не нужен)")]
        public string[] UnlitShaderMarkers = { "Unlit", "Skybox" };

        public override string Id => "lighting.large-without-lightmap";
        public override string Title => "Крупная поверхность без лайтмапы";
        public override string Category => "lighting";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Без лайтмапы большая поверхность берёт свет одной пробы в центре габаритов: пол на 20 м освещён как точка, " +
            "свет лампы «плывёт» или пропадает. Дешёвый Baked Lit требует лайтмапу (UV2); без неё остаются только дорогие " +
            "реалтайм-модели света на всю площадь экрана.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " Пол Lobby берёт пробу в ≈8 м от лампы; пол — 11,8 % кадра. " + AuditEvidence.PerformanceExpertTricks;

        public override string FixHint =>
            "Включить Generate Lightmap UVs в импорте модели (или box-UV/UV2 фиксером), ContributeGI + Receive GI: Lightmaps, " +
            "запечь свет; затем перевести материал на Baked Lit.";

        public void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context)
        {
            var renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                return;
            Mesh mesh = AuditAssetUtil.GetMesh(renderer);
            if (mesh == null || AllUnlit(renderer))
                return;
            // Листва и сетки (alpha-test) штатно живут на пробах; дальние LOD повторяют находку LOD0.
            foreach (Material material in renderer.sharedMaterials)
            {
                if (AuditAssetUtil.IsAlphaTested(material))
                    return;
            }
            if (AuditAssetUtil.FindOwningLodGroup(renderer, out int lodIndex) != null && lodIndex > 0)
                return;
            Bounds bounds = AuditAssetUtil.WorldBounds(renderer);
            float area = AuditAssetUtil.LargestFaceArea(bounds);
            if (area < MinFaceArea)
                return;

            bool hasUv2 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
            string meshLabel = AuditAssetUtil.AssetLabel(mesh);

            if (!source.InScene)
            {
                if (hasUv2)
                    return;
                context.Report(this, meshLabel, "крупный меш без UV2 — не запечётся в лайтмапу", AuditSeverity.Info)
                    .WithGroup("uv2:" + meshLabel)
                    .WithItem(source.AssetPath + ":" + source.HierarchyPath)
                    .WithMetric("faceAreaM2", area);
                return;
            }

            int index = renderer.lightmapIndex;
            bool lightmapped = index >= 0 && index < 0xFFFE && index < LightmapSettings.lightmaps.Length;
            if (lightmapped)
                return;

            var reasons = new List<string>();
            if (!hasUv2)
                reasons.Add("нет UV2");
            if ((GameObjectUtility.GetStaticEditorFlags(gameObject) & StaticEditorFlags.ContributeGI) == 0)
                reasons.Add("не ContributeGI");
            else if (renderer.receiveGI == ReceiveGI.LightProbes)
                reasons.Add("Receive GI: Light Probes");
            if (LightmapSettings.lightmaps.Length == 0)
                reasons.Add("в сцене нет лайтмап");
            if (reasons.Count == 0)
                reasons.Add("лайтмапа не назначена (перезапечь)");

            AuditFinding finding = context.Report(this, source.GroupAsset == source.AssetPath ? meshLabel : source.GroupAsset,
                    "крупная поверхность " + Mathf.RoundToInt(area) + " м² на пробах: " + string.Join(", ", reasons))
                .WithGroup("lm:" + meshLabel + "|" + string.Join(",", reasons))
                .WithItem(source.HierarchyPath)
                .WithMetric("faceAreaM2", area)
                .WithMetric("size", bounds.size.ToString("F1"))
                .WithMetric("hasUv2", hasUv2)
                .WithMetric("probeAnchor", renderer.probeAnchor != null ? renderer.probeAnchor.name : null);
            float nearest = NearestProbeDistance(bounds.center);
            if (nearest >= 0f)
                finding.WithMetric("nearestProbeM", nearest);
        }

        private bool AllUnlit(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                return false;
            foreach (Material material in materials)
            {
                if (material == null || material.shader == null)
                    return false;
                bool unlit = false;
                foreach (string marker in UnlitShaderMarkers)
                    unlit |= material.shader.name.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!unlit)
                    return false;
            }
            return true;
        }

        private static float NearestProbeDistance(Vector3 point)
        {
            LightProbes probes = LightmapSettings.lightProbes;
            Vector3[] positions = probes != null ? probes.positions : null;
            if (positions == null || positions.Length == 0)
                return -1f;
            float best = float.MaxValue;
            foreach (Vector3 position in positions)
                best = Mathf.Min(best, (position - point).sqrMagnitude);
            return Mathf.Sqrt(best);
        }
    }
}
