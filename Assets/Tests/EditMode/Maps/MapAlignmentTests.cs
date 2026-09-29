using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Tests.Modes;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Арены всех карт стоят одинаково: мировые координаты везде значат одно и то же место
    /// в физической комнате.
    ///
    /// <para>
    /// Что доказывает. Игроки ходят по арене ногами, и неоткалиброванный игрок при смене
    /// карты остаётся в тех же мировых координатах (<c>SpawnPlaceRegistry</c>). Это верно,
    /// только если арена на новой карте стоит там же и повёрнута так же. Раньше в
    /// <c>TestMap1</c> арена была повёрнута на 90°, и мировая точка на ней оказывалась
    /// в стене или на чужой базе. Метки физической комнаты — якоря
    /// (<see cref="PhysicalSpaceAnchor"/>): у всех карт реестра они обязаны совпадать
    /// с лобби.
    /// </para>
    /// </summary>
    public class MapAlignmentTests
    {
        /// <summary>Допуск на положение якоря: сантиметр с запасом на округление сцен.</summary>
        private const float Tolerance = 0.02f;

        private static IEnumerable<MapData> RegisteredMaps()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(GameModeWiringTests.MapRegistryPath);
            return registry.maps.Where(m => m != null);
        }

        /// <summary>Мировые позиции якорей сцены по возрастанию id.</summary>
        private static Vector3[] Anchors(string sceneName)
        {
            string path = AssetDatabase.FindAssets($"t:Scene {sceneName}")
                                       .Select(AssetDatabase.GUIDToAssetPath)
                                       .First(p => System.IO.Path.GetFileNameWithoutExtension(p) == sceneName);

            Scene scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                return scene.GetRootGameObjects()
                            .SelectMany(r => r.GetComponentsInChildren<PhysicalSpaceAnchor>(true))
                            .OrderBy(a => a.id)
                            .Select(a => a.transform.position)
                            .ToArray();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void Якоря_всех_карт_совпадают_с_лобби()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(GameModeWiringTests.MapRegistryPath);
            Vector3[] reference = Anchors(registry.lobby.sceneName);
            Assert.AreEqual(2, reference.Length, "В лобби не два якоря — сравнивать не с чем.");

            var wrong = new List<string>();
            foreach (MapData map in RegisteredMaps())
            {
                Vector3[] anchors = Anchors(map.sceneName);
                if (anchors.Length != 2)
                {
                    wrong.Add($"{map.sceneName}: якорей {anchors.Length}, нужно два");
                    continue;
                }

                for (int i = 0; i < 2; i++)
                {
                    float off = Vector3.Distance(reference[i], anchors[i]);
                    if (off > Tolerance)
                        wrong.Add($"{map.sceneName}: якорь {i} в {anchors[i]}, в лобби {reference[i]} (сдвиг {off:F2} м)");
                }
            }

            Assert.IsEmpty(wrong,
                "Арена на карте стоит не так, как в лобби — неоткалиброванный игрок после смены карты окажется " +
                "не там, где стоит в комнате:\n" + string.Join("\n", wrong));
        }
    }
}
