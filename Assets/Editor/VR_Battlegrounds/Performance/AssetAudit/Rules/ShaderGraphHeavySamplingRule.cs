using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Shader Graph с трипланарной/world-aligned проекцией или с большим числом выборок текстур.
    /// Разбор графа — эвристика по узлам (<see cref="ShaderGraphAnalyzer"/>): число выборок — верхняя оценка.
    /// </summary>
    public sealed class ShaderGraphHeavySamplingRule : AuditRule, IShaderAuditCheck
    {
        [AuditThreshold("Выборок текстур на пиксель, выше — «Предупреждение»")]
        public int WarningSamples = 4;

        [AuditThreshold("Выборок текстур на пиксель, выше — «Критично»")]
        public int CriticalSamples = 8;

        [AuditThreshold("Трипланар/world-aligned всегда не ниже «Предупреждения»; с выборками выше этого — «Критично»")]
        public int CriticalProjectedSamples = 6;

        public override string Id => "shadergraph.heavy-sampling";
        public override string Title => "Shader Graph: трипланар/world-aligned или много выборок";
        public override string Category => "shader";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Каждая выборка текстуры на пиксель — нагрузка на текстурный блок и кэш тайлового GPU; трипланар/world-aligned " +
            "делает по 3 выборки на слой и ещё смешивание. На большом полу это умножается на всю площадь экрана и на оба глаза. " +
            "Branch по свойству (не keyword) в Shader Graph вычисляет обе ветви — GPU платит за выборки и «выключенной» ветви.";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " S_VPWorldAligned (3 слоя по vertex paint, 24 выборки): пол — 11,8 % кадра на 7 draw; " +
            "скалы на S_BasicTextured — столл текстур 40–55 %. " + AuditEvidence.LobbyFloorBakedLitAB;

        public override string FixHint =>
            "Большие поверхности — URP Baked Lit с одной текстурой (детали запечь в текстуру/vertex color) и box-UV вместо world-aligned " +
            "(фиксеры scene-detail: BoxUvModelPostprocessor, BakedLitMaterialFixer). В графе: Branch по свойству → Keyword, " +
            "убрать неиспользуемые слои, нормали вдали.";

        public void CheckShader(Shader shader, string assetPath, AuditContext context)
        {
            ShaderGraphInfo graph = context.GetShaderGraphInfo(assetPath);
            if (graph == null)
                return;

            bool projected = graph.IsWorldProjected;
            if (!projected && graph.TotalSamples <= WarningSamples)
                return;

            AuditSeverity severity = DefaultSeverity;
            if (graph.TotalSamples > CriticalSamples || (projected && graph.TotalSamples > CriticalProjectedSamples))
                severity = AuditSeverity.Critical;

            string message = (projected ? "трипланар/world-aligned, " : string.Empty) + "≈" + graph.TotalSamples + " выборок текстур на пиксель";
            if (graph.BranchNodes > 0)
                message += ", Branch ×" + graph.BranchNodes + " (обе ветви считаются)";

            AuditFinding finding = context.Report(this, assetPath, message, severity)
                .WithMetric("samples", graph.TotalSamples)
                .WithMetric("directSamples", graph.DirectSamples)
                .WithMetric("subGraphUses", graph.SubGraphUses)
                .WithMetric("branchNodes", graph.BranchNodes)
                .WithMetric("customFunctions", graph.CustomFunctionNodes)
                .WithMetric("projection", string.Join("; ", graph.ProjectionReasons))
                .WithMetric("materials", context.MaterialsUsing(shader).Count);
            foreach (string material in context.MaterialsUsing(shader))
                finding.WithItem(material);
        }
    }
}
