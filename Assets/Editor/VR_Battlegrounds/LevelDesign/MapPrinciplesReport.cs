using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using static VrBattlegrounds.Editor.LevelDesign.LevelDesignRules;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Level Design/Map Principles Report</c>: по каждой боевой карте
    /// реестра — текст с нарушениями и метриками и две картинки вида сверху
    /// (<see cref="MapReportImage"/>) в <c>Temp/LevelDesign/</c>.
    ///
    /// <para>
    /// Тест (<c>MapPrinciplesTests</c>) отвечает «есть ли грубые нарушения»; отчёт — «где и
    /// почему» и метрики, у которых нет жёсткого порога: LD-09 (доля чужой половины из одной
    /// точки), LD-14 (дистанции контактов), LD-26 (открытость спавна). Агент, строящий карту,
    /// смотрит картинку сам. Из кода — <see cref="Run"/>.
    /// </para>
    /// </summary>
    public static class MapPrinciplesReport
    {
        public const string OutputFolder = "Temp/LevelDesign";

        /// <summary>
        /// Карты, к которым применяются правила боя: все карты реестра, кроме лобби и стендов для отладки
        /// (<see cref="MapData.debugOnly"/>, <c>TestMap3</c> — стенд блоков).
        /// </summary>
        public static IEnumerable<MapData> BattleMaps(MapRegistry registry) =>
            registry.maps.Where(m => m != null && m != registry.lobby && !m.debugOnly);

        [MenuItem("Tools/VR Battlegrounds/Level Design/Map Principles Report")]
        private static void RunFromMenu()
        {
            string summary = Run();
            GameLog.Debug.Info(summary);
            EditorUtility.RevealInFinder(OutputFolder);
        }

        /// <summary>Отчёт по всем боевым картам или по одной (<paramref name="sceneName"/>). Возвращает текст отчёта.</summary>
        public static string Run(string sceneName = null)
        {
            Directory.CreateDirectory(OutputFolder);
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(
                AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:MapRegistry").First()));

            var all = new StringBuilder();
            // Названная карта — любая из реестра (и стенд тоже); без имени — все боевые.
            IEnumerable<MapData> maps = sceneName != null
                ? registry.maps.Where(m => m != null && m.sceneName == sceneName)
                : BattleMaps(registry);
            foreach (MapData map in maps)
            {

                string path = AssetDatabase.FindAssets($"t:Scene {map.sceneName}")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .First(p => Path.GetFileNameWithoutExtension(p) == map.sceneName);
                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    all.AppendLine(Report(map.sceneName, MapGridBuilder.Build(scene)));
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            return all.ToString();
        }

        private static string Report(string name, MapGridBuilder.Result built)
        {
            var text = new StringBuilder();
            text.AppendLine($"══ {name} ══");
            if (built.Problems.Count > 0)
            {
                foreach (string p in built.Problems) text.AppendLine("  ! " + p);
                return text.ToString();
            }

            MapGrid g = built.Grid;
            float[] clear = MapAnalyzer.Clearance(g);
            List<string> heights = MapAnalyzer.CheckCoverHeights(built.BlockoutTops);
            List<MapAnalyzer.NarrowPassage> passages = MapAnalyzer.FindNarrowPassages(g, clear);
            List<MapAnalyzer.Pocket> pockets = MapAnalyzer.FindUnreachable(g, clear);
            List<MapAnalyzer.Sightline> sightlines = MapAnalyzer.FindBaseToBaseSightlines(g, clear);
            MapAnalyzer.VisibilityStats vis = MapAnalyzer.Visibility(g, clear);

            Section(text, "LD-20 высоты укрытий", heights);
            Section(text, "LD-48 перешагиваемое перешагивается", MapAnalyzer.CheckVaultables(built.Vaultables));
            Section(text, "LD-23 проходы уже 1 м", passages.Select(p => p.ToString()));
            Section(text, "LD-25 недостижимые участки", pockets.Select(p => p.ToString()));
            List<MapAnalyzer.Sightline> shotlines = MapAnalyzer.FindBaseToBaseShotlines(g, clear);
            Section(text, "LD-15 прострел база—база сквозь Soft/Visual (не видя)",
                    shotlines.OrderBy(l => l.Length).Take(5).Select(l => l.ToString()));
            Section(text, "LD-15 прострел база—база",
                    sightlines.Count == 0 ? new string[0] : new[]
                    {
                        $"{sightlines.Count} пар точек видят друг друга; кратчайшие:",
                    }.Concat(sightlines.OrderBy(s => s.Length).Take(5).Select(s => "  " + s)));

            int pairs = vis.Close + vis.Medium + vis.Long;
            text.AppendLine("  Метрики (порогов нет — для глаза):");
            text.AppendLine($"    LD-09 самая «всевидящая» точка ({vis.MaxEnemyShareAt.x:F1}; {vis.MaxEnemyShareAt.y:F1}) видит {vis.MaxEnemyShare:P0} чужой половины");
            text.AppendLine(pairs == 0
                ? "    LD-14 контактов между половинами нет"
                : $"    LD-14 видимые пары между половинами: ближний {Pct(vis.Close, pairs)}, средний {Pct(vis.Medium, pairs)}, дальний {Pct(vis.Long, pairs)}");
            text.AppendLine($"    LD-26 из чужой половины видно зоны A {vis.ZoneAExposure:P0}, зоны B {vis.ZoneBExposure:P0}");
            if (pairs > 0)
                text.AppendLine($"    Контакты через проёмы (окна, щели, бойницы, двери): {Pct(vis.ThroughOpenings, pairs)}");
            text.AppendLine(vis.BlindShots == 0
                ? "    Прострел вслепую (сквозь Soft/Visual, не видя): нет — оси S в каталоге контактов нет (LD-39)"
                : $"    Прострел вслепую (сквозь Soft/Visual, не видя): {vis.BlindShots} пар — ось S (LD-39)");
            text.AppendLine("    Видят друг друга — хоть в одной паре поз: стоя (1.7 м) или присев (1.1 м)");

            string layout = $"{OutputFolder}/{name}_layout.png";
            string visibility = $"{OutputFolder}/{name}_visibility.png";
            File.WriteAllBytes(layout, MapReportImage.Layout(g, clear, passages, sightlines, pockets));
            File.WriteAllBytes(visibility, MapReportImage.Visibility(g, clear, vis));
            File.WriteAllText($"{OutputFolder}/{name}.txt", text.ToString());
            text.AppendLine($"  Картинки: {layout}, {visibility}");
            return text.ToString();
        }

        private static void Section(StringBuilder text, string title, IEnumerable<string> lines)
        {
            List<string> list = lines.ToList();
            text.AppendLine(list.Count == 0 ? $"  ✔ {title}" : $"  ✘ {title}:");
            foreach (string l in list) text.AppendLine("    " + l);
        }

        private static string Pct(int part, int total) => $"{100f * part / total:F0} %";
    }
}
