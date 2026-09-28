using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// У каждой карты и лобби из Build Settings запечён occlusion culling.
    ///
    /// <para>
    /// Без запечённых данных на Quest 3 рисуется всё, что попало во frustum: аватар
    /// за стеной (~100k треугольников) стоит столько же, сколько видимый, а кадр упирается
    /// в GPU. Данные запекает <c>Tools/VR Battlegrounds/Gameplay/Bake Occlusion (all maps)</c>
    /// (<c>OcclusionBakeTool</c>). Тест — страж: новая карта без запекания или сцена,
    /// потерявшая ссылку на данные, краснеет здесь, а не на шлеме.
    /// </para>
    /// <para>
    /// Ссылка читается из YAML сцены (<c>OcclusionCullingSettings.m_OcclusionCullingData</c>) —
    /// открывать сцену не нужно. Отбор сцен повторяет <c>OcclusionBakeTool.IsTargetScene</c>:
    /// редакторная сборка из тестовой недоступна.
    /// </para>
    /// </summary>
    public class OcclusionCullingBakedTests
    {
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string MapsFolder     = "Assets/Scenes/Maps/";
        private const string OcclusionDataFileId = "36300000";

        private static readonly Regex DataRef = new Regex(
            @"m_OcclusionCullingData:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?");

        [Test]
        public void MapAndLobbyScenes_HaveBakedOcclusionData()
        {
            var missing = new List<string>();
            int checks  = 0;

            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                if (s.path != LobbyScenePath && !s.path.StartsWith(MapsFolder)) continue;

                checks++;
                string problem = Problem(s.path);
                if (problem != null) missing.Add($"{s.path} → {problem}");
            }

            Assert.That(checks, Is.GreaterThan(0), "В Build Settings нет ни лобби, ни карт — тест ничего не проверил.");
            Assert.IsEmpty(missing,
                "Сцены без запечённого occlusion culling (Tools/VR Battlegrounds/Gameplay/Bake Occlusion (all maps)):\n" +
                string.Join("\n", missing));
        }

        private static string Problem(string scenePath)
        {
            Match m = DataRef.Match(File.ReadAllText(scenePath));
            if (!m.Success) return "в сцене нет OcclusionCullingSettings";
            if (m.Groups[1].Value == "0" || !m.Groups[2].Success) return "m_OcclusionCullingData не задан";

            string dataPath = AssetDatabase.GUIDToAssetPath(m.Groups[2].Value);
            if (string.IsNullOrEmpty(dataPath)) return $"ассет данных {m.Groups[2].Value} не найден";

            // OcclusionCullingData в скриптовом API не представлен (грузится как Object),
            // поэтому тип проверяется по fileID: 363 — его ClassID, 36300000 — главный объект.
            if (m.Groups[1].Value != OcclusionDataFileId) return $"{dataPath} — не OcclusionCullingData";
            if (!File.Exists(dataPath) || new FileInfo(dataPath).Length == 0) return $"{dataPath} пуст или удалён";
            return null;
        }
    }
}
