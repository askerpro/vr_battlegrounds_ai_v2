using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.LevelDesign;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Tests.Modes;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Грубые нарушения правил левел-дизайна (<c>Docs/level-design-principles.md</c>) на всех
    /// боевых картах реестра. Карта сводится к карте высот (<see cref="MapGridBuilder"/>),
    /// проверки — <see cref="MapAnalyzer"/>, сами проверки доказаны на искусственных картах в
    /// <c>MapAnalyzerTests</c>. Здесь только жёсткие пороги; метрики для глаза (доля видимой
    /// половины, дистанции контактов, открытость спавна) — в отчёте
    /// <c>Tools/VR Battlegrounds/Level Design/Map Principles Report</c>.
    /// </summary>
    public class MapPrinciplesTests
    {
        private readonly Dictionary<string, MapGridBuilder.Result> _maps = new Dictionary<string, MapGridBuilder.Result>();

        /// <summary>Сцены открыты весь прогон: видимость сетки — лучи по коллайдерам сцены.</summary>
        private readonly List<Scene> _scenes = new List<Scene>();

        [OneTimeSetUp]
        public void BuildGrids()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(GameModeWiringTests.MapRegistryPath);
            foreach (MapData map in MapPrinciplesReport.BattleMaps(registry))
            {
                string path = AssetDatabase.FindAssets($"t:Scene {map.sceneName}")
                                           .Select(AssetDatabase.GUIDToAssetPath)
                                           .First(p => System.IO.Path.GetFileNameWithoutExtension(p) == map.sceneName);

                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                _scenes.Add(scene);
                _maps[map.sceneName] = MapGridBuilder.Build(scene);
            }
        }

        [OneTimeTearDown]
        public void CloseScenes()
        {
            foreach (Scene scene in _scenes) EditorSceneManager.ClosePreviewScene(scene);
        }

        /// <summary>Собрать нарушения по всем картам: «карта: нарушение».</summary>
        private List<string> Violations(Func<MapGridBuilder.Result, float[], IEnumerable<string>> check)
        {
            var all = new List<string>();
            foreach (KeyValuePair<string, MapGridBuilder.Result> map in _maps)
            {
                if (map.Value.Problems.Count > 0)
                {
                    all.AddRange(map.Value.Problems.Select(p => $"{map.Key}: {p}"));
                    continue;
                }
                float[] clear = MapAnalyzer.Clearance(map.Value.Grid);
                all.AddRange(check(map.Value, clear).Select(v => $"{map.Key}: {v}"));
            }
            return all;
        }

        [Test]
        public void Боевые_карты_есть()
        {
            Assert.IsNotEmpty(_maps, "В реестре нет ни одной боевой карты — проверять нечего.");
        }

        [Test]
        public void LD20_Укрытия_блокаута_только_трёх_высот()
        {
            List<string> v = Violations((m, _) => MapAnalyzer.CheckCoverHeights(m.BlockoutTops));
            Assert.IsEmpty(v, "Высота укрытия вне классов Low/Mid/Tall — игрок не угадает, спрячет ли:\n" + string.Join("\n", v));
        }

        [Test]
        public void LD48_Перешагиваемое_действительно_перешагивается()
        {
            List<string> v = Violations((m, _) => MapAnalyzer.CheckVaultables(m.Vaultables));
            Assert.IsEmpty(v, "Метка VaultableObstacle на препятствии, которое не перешагнуть, — законный проход сквозь стену:\n" +
                              string.Join("\n", v));
        }

        [Test]
        public void LD23_Нет_проходов_уже_метра()
        {
            List<string> v = Violations((m, c) => MapAnalyzer.FindNarrowPassages(m.Grid, c).Select(p => p.ToString()));
            Assert.IsEmpty(v, "Проход уже 1 м — игрок с оружием задевает стены и начинает ходить сквозь них:\n" + string.Join("\n", v));
        }

        [Test]
        public void LD25_Вся_карта_достижима_из_своей_зоны_в_обход_чужой()
        {
            List<string> v = Violations((m, c) => MapAnalyzer.FindUnreachable(m.Grid, c).Select(p => p.ToString()));
            Assert.IsEmpty(v, "Участок недостижим — туда не дойти, не нарушив правила (сквозь стену или через чужую базу):\n" + string.Join("\n", v));
        }

        [Test]
        public void LD15_Нет_прострела_от_базы_до_базы()
        {
            List<string> v = Violations((m, c) =>
            {
                var found = new List<string>();
                List<MapAnalyzer.Sightline> lines = MapAnalyzer.FindBaseToBaseSightlines(m.Grid, c);
                if (lines.Count > 0)
                    found.Add($"{lines.Count} пар точек видят друг друга, самая короткая {lines.OrderBy(l => l.Length).First()}");
                List<MapAnalyzer.Sightline> shots = MapAnalyzer.FindBaseToBaseShotlines(m.Grid, c);
                if (shots.Count > 0)
                    found.Add($"{shots.Count} пар простреливаются вслепую сквозь Soft/Visual, самая короткая {shots.OrderBy(l => l.Length).First()}");
                return found;
            });
            Assert.IsEmpty(v, "Из зоны видно зону противника — раунд решается дуэлью на старте:\n" + string.Join("\n", v));
        }
    }
}
