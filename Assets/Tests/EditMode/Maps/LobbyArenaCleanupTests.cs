using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Detail;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Чистка арены Lobby (lobby-arena-cleanup, принято пользователем 2026-10-24): проём торцевой стойки закрыт
    /// мешками не ниже стойки — она граница игровой части; точки стрельбы — пустые точки перед стойкой; надписи —
    /// TextMeshPro (legacy TextMesh на GUI/Text Shader рисовался сквозь объекты); мониторов дорожек нет —
    /// монитор попаданий в планшете; выключенного корня Scene нет, детализация на него не ссылается.
    /// </summary>
    public class LobbyArenaCleanupTests
    {
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string Perimeter = "Environment/ArmoryRangeZone/PerimeterDecoration/";
        private const string Tables = "Environment/ArmoryRangeZone/InteriorDecoration/Tables/";

        private Scene _scene;

        [OneTimeSetUp]
        public void OpenScene() => _scene = EditorSceneManager.OpenPreviewScene(LobbyPath);

        [OneTimeTearDown]
        public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

        private IEnumerable<T> All<T>() where T : Component =>
            _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

        private Transform Find(string path)
        {
            string[] parts = path.Split('/');
            GameObject root = _scene.GetRootGameObjects().FirstOrDefault(r => r.name == parts[0]);
            Assert.IsNotNull(root, path);
            Transform t = parts.Length == 1 ? root.transform : root.transform.Find(string.Join("/", parts.Skip(1)));
            Assert.IsNotNull(t, path);
            return t;
        }

        private static Bounds Bounds(Transform t)
        {
            Renderer[] rs = t.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            Assert.IsNotEmpty(rs, t.name);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        [TestCase("Table_North", "NorthLowShootingBoundary")]
        [TestCase("Table_South", "SouthLowCampBoundary")]
        public void Проём_стойки_закрыт_мешками_не_ниже_стойки(string table, string bags)
        {
            Transform counter = Find(Tables + table);
            var blocks = counter.GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds).ToArray();
            float top = blocks.Max(b => b.max.y);
            Bounds left = blocks.Where(b => b.center.x < 0).OrderByDescending(b => b.max.x).First();
            Bounds right = blocks.Where(b => b.center.x > 0).OrderBy(b => b.min.x).First();
            Bounds wall = Bounds(Find(Perimeter + bags));

            Assert.LessOrEqual(wall.min.x, left.max.x + .05f, "мешки от левого блока стойки");
            Assert.GreaterOrEqual(wall.max.x, right.min.x - .05f, "мешки до правого блока стойки");
            Assert.GreaterOrEqual(wall.max.y, top, "мешки не ниже стойки");
            Assert.Less(Mathf.Abs(wall.center.z - left.center.z), .6f, "мешки — в линии стойки");
        }

        [TestCase("Gameplay/ShootingRange/ShootingPositionNorth", 1f)]
        [TestCase("Gameplay/ShootingRange/ShootingPositionSouth", -1f)]
        public void Точка_стрельбы_пустая_перед_стойкой(string path, float side)
        {
            Transform point = Find(path);
            Assert.IsNull(point.GetComponent<TMP_Text>(), "надписи на точке стрельбы нет");
            Assert.IsNull(point.GetComponent<Renderer>());
            Bounds counter = Bounds(Find(Tables + (side > 0 ? "Table_North" : "Table_South")));
            float inside = side > 0 ? counter.min.z - point.position.z : point.position.z - counter.max.z;
            Assert.That(inside, Is.InRange(0f, 1f), "точка — перед стойкой со стороны арены");
        }

        [Test]
        public void Надписи_лобби_не_рисуются_сквозь_объекты()
        {
            Assert.IsEmpty(All<TextMesh>().Select(t => t.name), "legacy TextMesh на GUI/Text Shader виден сквозь стены");
            foreach (string name in new[] { "EntryPlaque__Text", "ArenaFacingPlaque__Text", "GuardSign__КПП" })
                Assert.IsTrue(All<TextMeshPro>().Any(t => t.name == name), name + " — TextMeshPro");
        }

        [Test]
        public void Номера_дорожек_текстом()
        {
            Transform dressing = Find("Environment/ArmoryRangeZone/ShootingRangeDressing");
            var lanes = dressing.Cast<Transform>().Where(l => l.name.StartsWith("RangeLane")).ToArray();
            Assert.AreEqual(8, lanes.Length);
            foreach (Transform lane in lanes)
            {
                string n = lane.name.Substring(lane.name.LastIndexOf('_') + 1);
                var number = lane.Find("Номер " + n)?.GetComponent<TextMeshPro>();
                Assert.IsNotNull(number, lane.name + ": номер дорожки — TextMeshPro");
                Assert.AreEqual(n, number.text);
                Assert.IsNull(lane.Find("Цифра"), lane.name + ": семисегментные кубы убраны");
            }
        }

        [Test]
        public void На_стрельбище_нет_мониторов_и_подписей_дистанций()
        {
            Assert.IsFalse(All<Transform>().Any(t => t.name.StartsWith("LaneMonitor")), "монитор попаданий — в планшете");
            Assert.IsFalse(All<Transform>().Any(t => t.name == "RangeDistanceLabel"));
            Assert.AreEqual(1, All<ShootingRangePhotoCapture>().Count(), "снимки попаданий для планшета");
        }

        [Test]
        public void Корня_Scene_нет_и_детализация_без_пустых_ссылок()
        {
            Assert.IsFalse(_scene.GetRootGameObjects().Any(r => r.name == "Scene"), "остаток сцены-примера удалён");
            SceneDetailApplier applier = All<SceneDetailApplier>().Single();
            var renderers = (System.Array)typeof(SceneDetailApplier)
                .GetField("_renderers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(applier);
            foreach (object entry in renderers)
            {
                var renderer = (Renderer)entry.GetType().GetField("renderer").GetValue(entry);
                Assert.IsTrue(renderer != null, "запись детализации ссылается на удалённый рендерер");
            }
        }
    }
}
