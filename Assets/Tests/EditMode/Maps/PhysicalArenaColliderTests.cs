using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.LevelDesign;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>Коллайдеры общей физической арены: опора, реальные препятствия и визуальные метки.</summary>
    public class PhysicalArenaColliderTests
    {
        private const string ArenaPath = "Assets/Prefabs/Arenas/Nalchick/Tolstogo_street/Environment.prefab";
        private Scene scene;
        private GameObject arena;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaPath);
            Assert.That(prefab, Is.Not.Null, "Нет общего префаба физической арены.");
            scene = EditorSceneManager.NewPreviewScene();
            arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            // Имена импортированных объектов не являются API: весь набор работает после переименования.
            int index = 0;
            foreach (var transform in arena.GetComponentsInChildren<Transform>(true))
                transform.name = "renamed-object-" + index++;
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void Floor_HasOneSolidSupport_MatchingVisibleSurface()
        {
            var supports = arena.GetComponentsInChildren<Collider>(true)
                .Where(c => c.enabled && !c.isTrigger && c.gameObject.layer == LayerMask.NameToLayer("Ground")).ToArray();
            Assert.That(supports.Length, Is.EqualTo(1), "Дублирующий пол ошибочно участвует в поиске препятствий.");
            Assert.That(supports[0], Is.TypeOf<BoxCollider>());
            var visual = arena.GetComponentsInChildren<MeshRenderer>(true)
                .Single(r => r.gameObject.layer == LayerMask.NameToLayer("Ground")).bounds;
            var support = supports[0].bounds;
            Assert.That(support.max.y, Is.EqualTo(visual.max.y).Within(.001f));
            Assert.That(support.min.x, Is.EqualTo(visual.min.x).Within(.001f));
            Assert.That(support.max.x, Is.EqualTo(visual.max.x).Within(.001f));
            Assert.That(support.min.z, Is.EqualTo(visual.min.z).Within(.001f));
            Assert.That(support.max.z, Is.EqualTo(visual.max.z).Within(.001f));
            Assert.That(support.size.y, Is.GreaterThan(.1f), "Пол должен иметь объём для физики выпавших предметов.");
        }

        [Test]
        public void PillarsAndEnclosure_CollidersMatchVisibleGeometry()
        {
            var solids = arena.GetComponentsInChildren<BoxCollider>(true)
                .Where(c => !c.isTrigger && c.gameObject.layer != LayerMask.NameToLayer("Ground")
                    && c.GetComponentInParent<PhysicalSpaceAnchor>() == null
                    && c.GetComponentInParent<TeamSpawnZone>() == null).ToArray();
            Assert.That(solids.Length, Is.EqualTo(9), "Ожидаются четыре столба, четыре стены и потолок.");
            foreach (var collider in solids)
            {
                Assert.That(collider.enabled && !collider.isTrigger, Is.True, collider.name);
                var renderer = collider.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null, collider.name);
                Assert.That(Vector3.Distance(collider.bounds.center, renderer.bounds.center), Is.LessThan(.001f), collider.name);
                Assert.That(Vector3.Distance(collider.bounds.size, renderer.bounds.size), Is.LessThan(.001f), collider.name);
                Assert.That(collider.bounds.size.x * collider.bounds.size.y * collider.bounds.size.z, Is.GreaterThan(0f), collider.name);
            }
        }

        [Test]
        public void CalibrationDecals_DoNotCreateSolidObstacles()
        {
            var anchors = arena.GetComponentsInChildren<PhysicalSpaceAnchor>(true);
            Assert.That(anchors.Length, Is.EqualTo(2), "Две визуальные метки калибровки должны сохраниться.");
            foreach (var anchor in anchors)
                Assert.That(anchor.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger), Is.False,
                    "Визуальная метка калибровки не должна становиться физическим препятствием.");
        }

        [TestCase("Assets/Scenes/Lobby.unity", 1)]
        [TestCase("Assets/Scenes/Maps/TestMap1.unity", 2)]
        [TestCase("Assets/Scenes/Maps/TestMap2.unity", 2)]
        [TestCase("Assets/Scenes/Maps/TestMap3.unity", 2)]
        [TestCase("Assets/Scenes/Maps/ReferenceMap04.unity", 2)]
        [TestCase("Assets/Scenes/Maps/ServiceYard.unity", 2)]
        public void SpawnZones_RemainTriggers(string scenePath, int expectedZones)
        {
            // Общий Environment больше не владеет спавнами: они принадлежат Gameplay каждой карты.
            Assert.That(arena.GetComponentsInChildren<TeamSpawnZone>(true), Is.Empty,
                "Спавны карты не должны возвращаться в общий префаб окружения.");
            var mapScene = EditorSceneManager.OpenPreviewScene(scenePath);
            try
            {
                var roots = mapScene.GetRootGameObjects();
                var gameplayRoots = roots.Where(root => root.name == "Gameplay").ToArray();
                Assert.That(gameplayRoots.Length, Is.EqualTo(1), scenePath + ": нужен один корень Gameplay.");
                var zones = roots.SelectMany(root => root.GetComponentsInChildren<TeamSpawnZone>(true)).ToArray();
                Assert.That(zones.Length, Is.EqualTo(expectedZones), scenePath);
                foreach (var zone in zones)
                {
                    Assert.That(zone.transform.IsChildOf(gameplayRoots[0].transform), Is.True,
                        scenePath + ": зона должна принадлежать Gameplay, а не Environment или PhysicalArenaLayout.");
                    var colliders = zone.GetComponentsInChildren<Collider>(true);
                    Assert.That(colliders.Length, Is.EqualTo(1), scenePath + ": у каждой зоны должен быть один коллайдер.");
                    Assert.That(colliders.All(c => c.enabled && c.isTrigger), Is.True, scenePath + ": " + zone.name);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(mapScene);
            }
        }

        [TestCase("support", true)]
        [TestCase("raised", false)]
        [TestCase("trigger", false)]
        [TestCase("disabled", false)]
        [TestCase("marker", false)]
        [TestCase("protection", false)]
        [TestCase("block", false)]
        public void SupportRole_RequiresFloorLevelAndExcludesGameplayObstacles(string role, bool expected)
        {
            var definition = arena.AddComponent<PhysicalArenaDefinition>();
            definition.arenaId = "collider-test";
            definition.floor = arena.GetComponentsInChildren<BoxCollider>(true)
                .Single(c => c.enabled && !c.isTrigger && c.gameObject.layer == LayerMask.NameToLayer("Ground"));
            var candidate = new GameObject("arbitrary-obstacle-name");
            candidate.transform.SetParent(arena.transform, false);
            candidate.layer = LayerMask.NameToLayer("Ground");
            var collider = candidate.AddComponent<BoxCollider>();
            collider.size = new Vector3(1, .1f, 1);
            candidate.transform.position = new Vector3(0, definition.floor.bounds.max.y - .05f, 0);
            if (role == "raised") candidate.transform.position += Vector3.up;
            if (role == "trigger") collider.isTrigger = true;
            if (role == "disabled") collider.enabled = false;
            if (role == "marker") candidate.AddComponent<PhysicalObstacleMarker>();
            if (role == "protection") candidate.AddComponent<PhysicalObstacleProtection>();
            if (role == "block") candidate.AddComponent<BlockoutBlockInstance>();
            Physics.SyncTransforms();
            PhysicalArenaPanel.Invalidate(scene);
            Assert.That(BlockoutSupportSurfaces.IsSupportSurface(scene, collider), Is.EqualTo(expected), role);
            Assert.That(BlockoutSupportSurfaces.IsSupportSurface(scene, definition.floor), Is.True,
                "Явный опорный пол не должен блокировать размещение оболочки столба.");
        }
    }
}
