using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Табло лазерной сетки на настоящих картах (T-47): на каждой сцене Build Settings у каждой командной
    /// зоны есть <see cref="LaserGridScreens"/>, каждая стена арсенала принадлежит командной зоне и получает
    /// персональный кусок; общие табло — по одному на отображаемую грань (переднюю у новой проекции,
    /// четыре у старой коробки). Всё в пределах граней, не за стенами и без наложений.
    ///
    /// <para>
    /// Заодно ловит класс ошибки «стена считается вне зоны»: на картах стены — соседи зоны, а не её дети,
    /// и поиск по родителям (раздача стен T-45) не находил зону ни одной стены.
    /// </para>
    /// </summary>
    public class LaserGridMapsTests
    {
        private const string ZonePrefab = "Assets/Prefabs/Maps/TeamSpawnZone.prefab";

        [Test]
        public void Префаб_зоны_несёт_табло_сетки()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZonePrefab);
            Assert.IsNotNull(prefab, ZonePrefab);
            Assert.IsNotNull(prefab.GetComponent<LaserGridScreens>(),
                "На префабе TeamSpawnZone нет LaserGridScreens — табло в сетке не появятся ни на одной карте.");
        }

        /// <summary>
        /// Сцены Build Settings, на которых идёт матч: карта реестра с режимами (<c>MapData.supportedModes</c>).
        /// Лобби режимов не имеет — команд и экономики там нет, стены арсенала общие по замыслу, командных зон нет.
        /// </summary>
        private static IEnumerable<string> MapScenes()
        {
            MapRegistry registry = AssetDatabase.FindAssets("t:MapRegistry")
                                                .Select(AssetDatabase.GUIDToAssetPath)
                                                .Select(AssetDatabase.LoadAssetAtPath<MapRegistry>)
                                                .FirstOrDefault(r => r != null);
            Assert.IsNotNull(registry, "Нет MapRegistry");

            return EditorBuildSettings.scenes
                                      .Where(s => s.enabled)
                                      .Select(s => s.path)
                                      .Where(p =>
                                      {
                                          MapData map = registry.maps.FirstOrDefault(m => m != null &&
                                              m.sceneName == System.IO.Path.GetFileNameWithoutExtension(p));
                                          return map != null && map.supportedModes != null && map.supportedModes.Any(m => m != null);
                                      });
        }

        [TestCaseSource(nameof(MapScenes))]
        public void На_карте_у_каждой_стены_кусок_и_общие_табло_видимых_граней(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var zones = new List<TeamSpawnZone>();
                var walls = new List<ArsenalWallController>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    zones.AddRange(root.GetComponentsInChildren<TeamSpawnZone>(false));
                    walls.AddRange(root.GetComponentsInChildren<ArsenalWallController>(false));
                }

                List<TeamSpawnZone> teamZones = zones.Where(z => z.HomeTeam != null).ToList();
                if (teamZones.Count == 0 && walls.Count == 0)
                {
                    Assert.Pass($"{scenePath}: зон спавна и стен нет — табло ставить некуда.");
                    return;
                }

                var problems = new List<string>();

                foreach (ArsenalWallController wall in walls)
                {
                    // Станции отделены от масштабируемой зоны; явная связь — владелец
                    // принадлежности, как в LaserGridScreens и ArsenalOwnershipPolicy.
                    ArsenalStationAnchor anchor = wall.GetComponent<ArsenalStationAnchor>();
                    TeamSpawnZone zone = anchor != null && anchor.Zone != null
                        ? anchor.Zone : SpawnZoneMembership.ZoneOf(wall.transform, zones);
                    if (zone == null || !zones.Contains(zone) || zone.HomeTeam == null)
                        problems.Add($"стена '{Path(wall.transform)}' не стоит ни в одной командной зоне " +
                                     $"(зона: {(zone != null ? zone.name : "нет")})");
                }

                foreach (TeamSpawnZone zone in teamZones)
                {
                    if (zone.GetComponent<LaserGridScreens>() == null)
                        problems.Add($"зона '{Path(zone.transform)}' без LaserGridScreens");

                    List<ArsenalWallController> mine = LaserGridScreens.WallsOf(zone, walls, zones);
                    LaserGridBox box = LaserGridScreens.DescribeBox(zone, FloorUnder(scene, zone));
                    List<LaserGridScreenPose> poses = LaserGridScreens.Plan(box, mine);

                    // Как LaserGridScreens: принятую переднюю проекцию зоны заполняют
                    // только позы Front; остальные грани плана не создаются.
                    SpawnZoneBoundaryVisual visual = zone.GetComponent<SpawnZoneBoundaryVisual>();
                    bool frontOnly = visual != null && visual.FrontFaceOnly;
                    if (frontOnly) poses = poses.Where(p => p.Face == LaserGridFace.Front).ToList();

                    int common = poses.Count(p => p.Kind == LaserGridScreenKind.Common);
                    int expectedCommon = frontOnly ? 1 : 4;
                    if (common != expectedCommon)
                        problems.Add($"зона '{zone.name}': общих табло {common}, а не {expectedCommon}");

                    for (int i = 0; i < mine.Count; i++)
                    {
                        int personal = poses.Count(p => p.Kind == LaserGridScreenKind.Personal && p.WallId == i);
                        if (personal != 1) problems.Add($"стена '{mine[i].name}' зоны '{zone.name}': персональных кусков {personal}");
                    }

                    LaserGridLayoutTests.AssertInsideFacesAndApart(box, poses);
                    LaserGridLayoutTests.AssertClearOfWalls(box,
                        mine.Select((w, i) => LaserGridScreens.DescribeWall(w, i)).ToList(), poses);
                }

                Assert.IsEmpty(problems, $"{scenePath}:\n  " + string.Join("\n  ", problems));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCaseSource(nameof(MapScenes))]
        public void Стены_карты_раздаются_командам(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var zones = new List<TeamSpawnZone>();
                var walls = new List<ArsenalWallController>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    zones.AddRange(root.GetComponentsInChildren<TeamSpawnZone>(false));
                    walls.AddRange(root.GetComponentsInChildren<ArsenalWallController>(false));
                }

                List<string> outside = walls.Where(w => ArsenalOwnershipPolicy.TeamOf(w, zones) < 0)
                                            .Select(w => Path(w.transform)).ToList();
                Assert.IsEmpty(outside, $"{scenePath}: стены вне командных зон — их никому не раздадут (T-45):\n  " +
                                        string.Join("\n  ", outside));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Пол под центром зоны лучом сверху — как ищет его сама зона в <c>Start</c> (в превью он не идёт).
        /// Первая сверху поверхность, смотрящая вверх, не триггер; не нашлась — нижняя грань коробки.
        /// </summary>
        private static float FloorUnder(Scene scene, TeamSpawnZone zone)
        {
            PhysicsScene physics = scene.GetPhysicsScene();
            Vector3 top = new Vector3(zone.transform.position.x, zone.BorderTopY, zone.transform.position.z);
            float length = zone.BorderTopY - zone.BorderBottomY;
            var hits = new RaycastHit[16];
            int count = physics.Raycast(top, Vector3.down, hits, length, ~0, QueryTriggerInteraction.Ignore);

            System.Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));
            for (int i = 0; i < count; i++)
                if (hits[i].normal.y >= 0.7f) return hits[i].point.y;
            return zone.BorderBottomY;
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
