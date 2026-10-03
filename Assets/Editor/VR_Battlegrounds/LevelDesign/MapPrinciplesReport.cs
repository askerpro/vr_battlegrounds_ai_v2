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
    /// Раздел проверок редактора блокаута и <c>Art Pass/Check</c>: по каждой боевой карте
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

        [MenuItem("Tools/VR Battlegrounds/Level Design/Art Pass/Check/Map Principles Report", false, 200)]
        private static void RunFromMenu()
        {
            string summary = Run();
            GameLog.Debug.Info(summary);
            EditorUtility.RevealInFinder(OutputFolder);
        }

        /// <summary>Отчёт по всем боевым картам или по одной (<paramref name="sceneName"/>). Возвращает текст отчёта.</summary>
        public static string Run(string sceneName = null, string layoutPath = null,
            MapEvaluationProfile profile = MapEvaluationProfile.Unspecified)
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
                    all.AppendLine(Report(map.sceneName, MapEvaluationScene.Evaluate(scene, layoutPath, profile)));
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            return all.ToString();
        }

        private static string Report(string name, MapEvaluationResult result)
        {
            var text = new StringBuilder(MapEvaluationScene.Write(result, $"{OutputFolder}/{name}"));
            if (result.grid == null) return text.ToString();
            string layout = $"{OutputFolder}/{name}_layout.png";
            string visibility = $"{OutputFolder}/{name}_visibility.png";
            File.WriteAllBytes(layout, MapReportImage.Layout(result.grid, result.clearance, result.passages, result.sightlines, result.pockets));
            File.WriteAllBytes(visibility, MapReportImage.Visibility(result.grid, result.clearance, result.visibility));
            text.AppendLine($"  Картинки: {layout}, {visibility}");
            return text.ToString();
        }

    }
}
