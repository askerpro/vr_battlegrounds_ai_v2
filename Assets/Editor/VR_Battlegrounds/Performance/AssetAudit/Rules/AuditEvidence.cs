namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Общие ссылки на замеры, которыми правила обосновывают пороги. Новый замер — новая константа
    /// с условиями (сборка, устройство, сцена, вид, дата), правило ссылается на неё в <see cref="AuditRule.Evidence"/>.
    /// </summary>
    internal static class AuditEvidence
    {
        /// <summary>Per-draw замер Lobby на Quest 3 (задача scene-detail).</summary>
        public const string LobbyQuest3PerDraw =
            "Quest 3, Lobby, худший вид A, Meta RenderDoc per-draw, 2026-10-20 (задача scene-detail): " +
            "tasks/scene-detail/reports/q3-meta-lobby-A-20261020/ranking.json в worktree scene-detail; " +
            "отчёт «Lobby на Quest 3: замеры GPU и план оптимизаций» (Claude Docs). GPU кадра 10,8 мс при бюджете 7 мс, кадр ограничен пикселями.";

        /// <summary>Normal A/B замены S_VPWorldAligned → URP Baked Lit на шлеме.</summary>
        public const string LobbyFloorBakedLitAB =
            "Normal A/B на Quest 3 (scene-detail, 2026-10-20): пол Lobby S_VPWorldAligned → URP Baked Lit даёт −1,87 мс кадра.";

        /// <summary>Правила «Replace before optimise» и «Standard Quest tricks».</summary>
        public const string PerformanceExpertTricks =
            "experts/performance/AGENT.md, «Standard Quest tricks» (решение пользователя 2026-10-20).";
    }
}
