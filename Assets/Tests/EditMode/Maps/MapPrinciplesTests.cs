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
        private readonly Dictionary<string, MapEvaluationResult> _maps = new Dictionary<string, MapEvaluationResult>();

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
                _maps[map.sceneName] = MapEvaluation.Evaluate(map.sceneName, MapGridBuilder.Build(scene), null,
                    MapEvaluationProfile.Unspecified, false);
            }
        }

        [OneTimeTearDown]
        public void CloseScenes()
        {
            foreach (Scene scene in _scenes) EditorSceneManager.ClosePreviewScene(scene);
        }

        /// <summary>Собрать нарушения по всем картам: «карта: нарушение».</summary>
        private List<string> Violations(string rule)
        {
            var all = new List<string>();
            foreach (KeyValuePair<string, MapEvaluationResult> map in _maps)
            {
                if (map.Value.grid == null)
                {
                    all.AddRange(map.Value.uncheckedRequirements.Where(p => p.rule == "GEOMETRY").Select(p => $"{map.Key}: {p.message}"));
                    continue;
                }
                all.AddRange(map.Value.violations.Where(v => v.rule == rule).Select(v => $"{map.Key}: {v.message}"));
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
            List<string> v = Violations("LD-20");
            Assert.IsEmpty(v, "Высота укрытия вне классов Low/Mid/Tall — игрок не угадает, спрячет ли:\n" + string.Join("\n", v));
        }

        [Test]
        public void LD48_Перешагиваемое_действительно_перешагивается()
        {
            List<string> v = Violations("LD-48");
            Assert.IsEmpty(v, "Метка VaultableObstacle на препятствии, которое не перешагнуть, — законный проход сквозь стену:\n" +
                              string.Join("\n", v));
        }

        [Test]
        public void LD23_Нет_проходов_уже_метра()
        {
            List<string> v = Violations("LD-23");
            Assert.IsEmpty(v, "Проход уже 1 м — игрок с оружием задевает стены и начинает ходить сквозь них:\n" + string.Join("\n", v));
        }

        [Test]
        public void LD25_Вся_карта_достижима_из_своей_зоны_в_обход_чужой()
        {
            List<string> v = Violations("LD-25");
            Assert.IsEmpty(v, "Участок недостижим — туда не дойти, не нарушив правила (сквозь стену или через чужую базу):\n" + string.Join("\n", v));
        }

        // LD-15 больше не запрет: решение пользователя 2026-10-02 разрешает стартовые дуэли.
        // Классификацию и сохранение измеренных линий проверяет MapEvaluationTests.
    }
}
