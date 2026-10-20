using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Суммарная alpha-test геометрия охвата (листва, сетки). В сцене — видимые рендереры (LOD0 или вне LOD)
    /// и их треугольники; в паке/выборке — сколько alpha-test материалов.
    /// </summary>
    public sealed class AlphaTestFoliageRule : AuditRule, IMaterialAuditCheck, IGameObjectAuditCheck, IAuditFinishCheck
    {
        [AuditThreshold("Рендереров с alpha-test в сцене, выше — «Предупреждение» (×2 — «Критично»)")]
        public int WarningRenderers = 30;

        [AuditThreshold("Треугольников alpha-test в сцене, выше — «Предупреждение» (×2 — «Критично»)")]
        public int WarningTriangles = 30000;

        [AuditThreshold("Alpha-test материалов в паке, от этого — «Инфо»")]
        public int InfoMaterials = 1;

        private readonly List<string> _materials = new List<string>();
        private readonly Dictionary<string, (int renderers, long triangles)> _byMesh = new Dictionary<string, (int, long)>();
        private int _renderers;
        private long _triangles;
        private long _farTriangles;

        public override string Id => "foliage.alpha-test";
        public override string Title => "Много alpha-test геометрии (листва)";
        public override string Category => "material";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Alpha-test (clip/discard) выключает раннее отсечение глубины на тайловом GPU Quest (LRZ/HSR): перекрытые листья " +
            "всё равно считают фрагменты, перерисовка множится слоями кроны и обоими глазами. Плюс MSAA-алиасинг краёв.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " S_Foliage/S_BasicTextured (скалы, ели) ≈49 % кадра; SM_SilverFir_M_01_LOD3 — 10 % на 52 draw.";

        public override string FixHint =>
            "Дальние деревья — импостор/billboard без alpha-test или непрозрачная «крона-оболочка»; ближние — меньше слоёв карт, " +
            "плотнее контур карты (меньше пустой площади); ограничить число экземпляров в кадре.";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            if (AuditAssetUtil.IsAlphaTested(material))
                _materials.Add(AuditAssetUtil.AssetLabel(material));
        }

        public void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context)
        {
            if (!source.InScene)
                return;
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled || !renderer.sharedMaterials.Any(AuditAssetUtil.IsAlphaTested))
                return;
            // В кадре виден один LOD: LOD0 — верхняя оценка (вблизи), последний LOD — нижняя (вдали).
            LODGroup group = AuditAssetUtil.FindOwningLodGroup(renderer, out int lodIndex);
            long triangles = AuditAssetUtil.TriangleCount(renderer);
            bool isFirst = group == null || lodIndex == 0;
            bool isLast = group == null || lodIndex == group.lodCount - 1;
            if (isLast)
                _farTriangles += triangles;
            if (!isFirst)
                return;
            _renderers++;
            _triangles += triangles;
            string mesh = AuditAssetUtil.AssetLabel(AuditAssetUtil.GetMesh(renderer));
            _byMesh.TryGetValue(mesh, out (int renderers, long triangles) entry);
            _byMesh[mesh] = (entry.renderers + 1, entry.triangles + triangles);
        }

        public void Finish(AuditContext context)
        {
            if (context.IsSceneScope)
            {
                // Порог по треугольникам — по нижней оценке (дальние LOD): так не пугаем LOD0, который вдали не рисуется.
                if (_renderers <= WarningRenderers && _farTriangles <= WarningTriangles)
                    return;
                bool critical = _renderers > WarningRenderers * 2 || _farTriangles > WarningTriangles * 2L;
                AuditFinding finding = context.Report(this, context.ScenePath,
                        _renderers + " alpha-test рендереров, треуг. от " + AuditAssetUtil.FormatCount(_farTriangles) +
                        " (последние LOD) до " + AuditAssetUtil.FormatCount(_triangles) + " (LOD0)",
                        critical ? AuditSeverity.Critical : AuditSeverity.Warning)
                    .WithMetric("renderers", _renderers)
                    .WithMetric("trianglesLod0", _triangles)
                    .WithMetric("trianglesLastLod", _farTriangles)
                    .WithMetric("materials", _materials.Count);
                foreach (KeyValuePair<string, (int renderers, long triangles)> mesh in _byMesh.OrderByDescending(p => p.Value.triangles).Take(15))
                    finding.WithItem(mesh.Key + " ×" + mesh.Value.renderers + " (" + AuditAssetUtil.FormatCount(mesh.Value.triangles) + ")");
                return;
            }

            if (_materials.Count < InfoMaterials)
                return;
            AuditFinding packFinding = context.Report(this, context.Request.Paths.FirstOrDefault(),
                    _materials.Count + " alpha-test материалов в охвате", AuditSeverity.Info)
                .WithMetric("materials", _materials.Count);
            foreach (string material in _materials)
                packFinding.WithItem(material);
        }
    }
}
