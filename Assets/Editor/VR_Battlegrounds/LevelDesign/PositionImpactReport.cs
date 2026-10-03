using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Читает JSON разметки и активную сцену; не открывает сцены и не меняет геометрию.</summary>
    public static class PositionImpactReport
    {
        [MenuItem("Tools/VR Battlegrounds/Level Design/Art Pass/Check/Position Impact Report (Active Scene)", false, 200)]
        private static void RunFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            { GameLog.Debug.Warning("Отчёт позиций доступен в Edit Mode после компиляции."); return; }
            string input = EditorUtility.OpenFilePanel("JSON разметки позиций", "Docs/level-design/maps", "json");
            if (string.IsNullOrEmpty(input)) return;
            try { GameLog.Debug.Info(Run(SceneManager.GetActiveScene(), input)); }
            catch (Exception e) { GameLog.Error("Не удалось построить отчёт позиций: " + e.Message); }
        }

        public static string Run(Scene scene, string layoutPath)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Нужна загруженная сцена.");
            PositionImpactLayout layout = JsonUtility.FromJson<PositionImpactLayout>(File.ReadAllText(layoutPath, Encoding.UTF8));
            if (layout == null || layout.map != scene.name)
                throw new ArgumentException("Имя map в разметке должно совпадать с активной сценой.");
            MapEvaluationResult evaluation = MapEvaluationScene.Evaluate(scene, layoutPath);
            string folder = "Temp/LevelDesign";
            Directory.CreateDirectory(folder);
            string stem = Path.Combine(folder, scene.name + "_position-impact");
            string text = MapEvaluationScene.Write(evaluation, stem);
            File.WriteAllText(stem + "_input.json", JsonUtility.ToJson(layout, true), new UTF8Encoding(false));
            return stem + ".json\n" + text;
        }
    }
}
