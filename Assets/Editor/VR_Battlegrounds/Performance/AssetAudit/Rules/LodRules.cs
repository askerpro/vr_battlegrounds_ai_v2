using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>LODGroup, у которого дальний LOD почти не упрощён (или не отсекается).</summary>
    public sealed class LodFarNotReducedRule : AuditRule, IGameObjectAuditCheck
    {
        [AuditThreshold("Треугольников в последнем LOD, выше — «Предупреждение» (ориентир Quest: сотни)")]
        public int WarningLastLodTriangles = 1000;

        [AuditThreshold("Треугольников в последнем LOD, выше — «Критично»")]
        public int CriticalLastLodTriangles = 5000;

        [AuditThreshold("Доля последнего LOD от LOD0, выше — «Инфо» (если в нём больше InfoMinTriangles)")]
        public float InfoLastLodRatio = 0.2f;

        [AuditThreshold("Минимум треугольников последнего LOD для «Инфо» по доле")]
        public int InfoMinTriangles = 300;

        public override string Id => "lod.far-not-reduced";
        public override string Title => "Дальний LOD почти не упрощён";
        public override string Category => "mesh";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Дальний LOD рисуется чаще всего и занимает мало пикселей: тысячи треугольников в нём дают микротреугольники, " +
            "столл вершин и плохое заполнение квадов GPU. Последний LOD должен быть в сотни треугольников или отсекаться.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " SM_WatchTower_01: LOD0 ≈142k, LOD3 17,8k треугольников, столл вершин 58 %.";

        public override string FixHint =>
            "Перегенерировать последний LOD (децимация до сотен треугольников, импостор/billboard) или задать отсечение (Culled) раньше.";

        public void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context)
        {
            LODGroup group = gameObject.GetComponent<LODGroup>();
            if (group == null)
                return;
            LOD[] lods = group.GetLODs();
            if (lods.Length < 2)
                return;

            var triangles = new List<long>(lods.Length);
            foreach (LOD lod in lods)
                triangles.Add(lod.renderers.Where(r => r != null).Distinct().Sum(r => AuditAssetUtil.TriangleCount(r)));
            long lod0 = triangles[0];
            long last = triangles[triangles.Count - 1];
            float ratio = lod0 > 0 ? (float)last / lod0 : 0f;
            bool culled = lods[lods.Length - 1].screenRelativeTransitionHeight > 0.0001f;

            AuditSeverity? severity = null;
            if (last > CriticalLastLodTriangles)
                severity = AuditSeverity.Critical;
            else if (last > WarningLastLodTriangles)
                severity = AuditSeverity.Warning;
            else if (ratio > InfoLastLodRatio && last > InfoMinTriangles)
                severity = AuditSeverity.Info;
            if (severity == null)
                return;

            string message = "последний LOD" + (lods.Length - 1) + ": " + AuditAssetUtil.FormatCount(last) + " треуг. (" +
                             Mathf.RoundToInt(ratio * 100f) + " % от LOD0); цепочка " + AuditAssetUtil.JoinCounts(triangles) +
                             (culled ? string.Empty : ", без отсечения");
            context.Report(this, source.GroupAsset, message, severity)
                .WithGroup(source.GroupAsset + "|" + gameObject.name + "|" + message)
                .WithItem(source.InScene ? source.HierarchyPath : source.AssetPath + ":" + source.HierarchyPath)
                .WithMetric("lodTriangles", AuditAssetUtil.JoinCounts(triangles))
                .WithMetric("lastLodTriangles", last)
                .WithMetric("lod0Triangles", lod0)
                .WithMetric("lastLodRatio", ratio)
                .WithMetric("culled", culled);
        }
    }

    /// <summary>Тяжёлый меш вне LODGroup.</summary>
    public sealed class MeshWithoutLodRule : AuditRule, IGameObjectAuditCheck
    {
        [AuditThreshold("Треугольников у меша без LOD, выше — «Предупреждение»")]
        public int WarningTriangles = 5000;

        [AuditThreshold("Треугольников у меша без LOD, выше — «Критично»")]
        public int CriticalTriangles = 20000;

        public override string Id => "mesh.no-lod";
        public override string Title => "Тяжёлый меш без LOD";
        public override string Category => "mesh";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Меш без LOD рисуется полной детализацией на любом расстоянии: вдали это микротреугольники и столл вершин, " +
            "а в Lobby весь декор виден одновременно.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " Ориентир сцены Lobby: видимых треугольников ≤ ~150k (experts/performance, 2026-10-13).";

        public override string FixHint => "Добавить LODGroup (LOD1–2 + отсечение) или заменить упрощённым мешем; мелочь — отсекать по дистанции.";

        public void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context)
        {
            var renderer = gameObject.GetComponent<MeshRenderer>();
            if (renderer == null)
                return;
            Mesh mesh = AuditAssetUtil.GetMesh(renderer);
            long triangles = AuditAssetUtil.TriangleCount(mesh);
            if (triangles <= WarningTriangles)
                return;
            if (AuditAssetUtil.FindOwningLodGroup(renderer, out _) != null)
                return;

            AuditSeverity severity = triangles > CriticalTriangles ? AuditSeverity.Critical : DefaultSeverity;
            string meshLabel = AuditAssetUtil.AssetLabel(mesh);
            context.Report(this, meshLabel, AuditAssetUtil.FormatCount(triangles) + " треуг. без LODGroup", severity)
                .WithGroup("mesh:" + meshLabel)
                .WithItem(source.InScene ? source.HierarchyPath : source.AssetPath + ":" + source.HierarchyPath)
                .WithMetric("triangles", triangles);
        }
    }
}
