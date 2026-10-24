using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.Performance.AssetAudit;

namespace VrBattlegrounds.Tests.AssetAudit
{
    /// <summary>
    /// Общие правила света на каждой карте Build Settings (Lobby и Assets/Scenes/Maps): нет реалтайм-света, свет запечён,
    /// подвижное вне GI, UV2 у мешей в лайтмапе, пробы в игровых зонах (решение пользователя 2026-10-24, задача
    /// lobby-decor-builders). Пока карты в разработке, находки — предупреждение: Inconclusive с маркером «[Warn:lighting]»,
    /// который heavy выносит в warnings и заметки verdict.json. Сбой самого правила — провал теста.
    /// </summary>
    public sealed class MapLightingAuditTests
    {
        public const string WarnMarker = "[Warn:lighting]";

        private static readonly string[] LightingRules =
        {
            "lighting.realtime-light", "lighting.scene-not-baked", "lighting.dynamic-in-gi",
            "mesh.lightmapped-without-uv2", "lighting.zone-without-probes",
        };

        private static IEnumerable<string> MapScenes() =>
            EditorBuildSettings.scenes.Select(s => s.path)
                .Where(p => p == "Assets/Scenes/Lobby.unity" || p.StartsWith("Assets/Scenes/Maps/", System.StringComparison.Ordinal));

        [Test]
        public void BuildSettings_ListsLobbyAndMaps() =>
            Assert.That(MapScenes().ToList(), Has.Member("Assets/Scenes/Lobby.unity").And.Count.GreaterThan(1));

        [TestCaseSource(nameof(MapScenes))]
        public void Map_FollowsBakedLightingRules(string scenePath)
        {
            AuditRequest request = AuditRequest.Scene(scenePath);
            request.OutputPath = string.Empty;
            request.IncludeDependencies = false;
            foreach (AuditRule rule in AuditRuleCatalog.CreateRules(null, new List<string>()))
            {
                if (!LightingRules.Contains(rule.Id))
                    request.DisabledRules.Add(rule.Id);
            }

            // Сцена открывается до прогона: открытие раннером помечает SceneVisibilityState, и его выгрузка пишет Error в лог.
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            AuditReport report;
            try { report = AssetAuditRunner.Run(request); }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }

            Assert.That(report.Errors.Where(e => LightingRules.Any(e.Contains)), Is.Empty, "сбой правила света");
            List<AuditFinding> findings = report.Findings.Where(f => f.Severity >= AuditSeverity.Warning).ToList();
            if (findings.Count == 0)
                return;
            string details = string.Join("\n", findings.Take(10).Select(f =>
                f.RuleId + " ×" + f.Count + ": " + f.Message + (f.Items.Count > 0 ? " | " + string.Join("; ", f.Items.Take(3)) : string.Empty)));
            Assert.Inconclusive(WarnMarker + " " + scenePath + ": находок " + findings.Count + "\n" + details);
        }
    }
}
