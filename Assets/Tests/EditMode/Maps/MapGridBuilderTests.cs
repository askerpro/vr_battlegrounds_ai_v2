using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// <see cref="MapGridBuilder"/> на настоящих коллайдерах: окна и щели — часть контактов, и
    /// сетка их не разрешает, поэтому видимость карты — лучи по сцене. Сцена 10×12 м собирается
    /// в тесте: пол слоя <c>Ground</c>, две зоны, поперёк — стена на z 6.0–6.2 со щелью 5 см
    /// (x ≈ 2), окном 1.0–1.6 м (x 4.5–5.5) и дверью 1.2 м с перемычкой на 2.0 м (x 7.4–8.6).
    /// </summary>
    public class MapGridBuilderTests
    {
        private Scene _scene;
        private MapGridBuilder.Result _built;
        private MapGrid _grid;

        [OneTimeSetUp]
        public void BuildScene()
        {
            _scene = EditorSceneManager.NewPreviewScene();

            Box("Пол", new Vector3(5f, -0.5f, 6f), new Vector3(10f, 1f, 12f)).layer = LayerMask.NameToLayer("Ground");

            WallPiece("Стена_1", 0f, 1.975f, 0f, 2.5f);
            WallPiece("Стена_2", 2.025f, 4.5f, 0f, 2.5f);
            WallPiece("Подоконник", 4.5f, 5.5f, 0f, 1.0f);
            WallPiece("Перемычка_окна", 4.5f, 5.5f, 1.6f, 2.5f);
            WallPiece("Стена_3", 5.5f, 7.4f, 0f, 2.5f);
            WallPiece("Перемычка_двери", 7.4f, 8.6f, 2.0f, 2.5f);
            WallPiece("Стена_4", 8.6f, 10f, 0f, 2.5f);

            // Два одинаковых заборчика 0.9×0.1 м: поперёк всей арены — перешагиваемый, короткий у края — без метки.
            Box("Заборчик_перешагиваемый", new Vector3(5f, 0.45f, 3.05f), new Vector3(10f, 0.9f, 0.1f))
                .AddComponent<VaultableObstacle>();
            Box("Заборчик_без_метки", new Vector3(1f, 0.45f, 9.05f), new Vector3(2f, 0.9f, 0.1f));

            // Отдельно стоящие панели 1×2.5×0.1 м у z 4.5: Soft (фанера) и Visual (сетка). Контроль Hard — Стена_2.
            Cover("Панель_Soft", 0.8f, CoverClass.Soft);
            Cover("Панель_Visual", 8.5f, CoverClass.Visual);

            Zone("Зона_A", new Vector3(5f, 1f, 1f), "Assets/Data/Teams/CounterTerrorists_Team.asset");
            Zone("Зона_B", new Vector3(5f, 1f, 11f), "Assets/Data/Teams/Terrorists_Team.asset");

            _built = MapGridBuilder.Build(_scene);
            Assert.IsEmpty(_built.Problems, string.Join("\n", _built.Problems));
            _grid = _built.Grid;
        }

        [OneTimeTearDown]
        public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

        private GameObject Box(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private void WallPiece(string name, float x0, float x1, float y0, float y1) =>
            Box(name, new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, 6.1f), new Vector3(x1 - x0, y1 - y0, 0.2f));

        private void Cover(string name, float x, CoverClass cover) =>
            Box(name, new Vector3(x, 1.25f, 4.5f), new Vector3(1f, 2.5f, 0.1f)).AddComponent<CoverSurface>().Class = cover;

        private void Zone(string name, Vector3 center, string teamPath)
        {
            GameObject go = Box(name, center, new Vector3(10f, 2f, 2f));
            go.GetComponent<BoxCollider>().isTrigger = true;
            var zone = go.AddComponent<TeamSpawnZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("_team").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TeamData>(teamPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private bool Blocked(float x, float z) => _grid.Blocked[_grid.IndexAt(new Vector2(x, z))];

        [Test]
        public void Сквозь_щель_5_см_видно_рядом_нет()
        {
            float eye = LevelDesignRules.EyeHeight;
            Assert.IsTrue(MapAnalyzer.Visible(_grid, new Vector2(2f, 3f), eye, new Vector2(2f, 9f), eye), "Взгляд точно вдоль щели.");
            Assert.IsFalse(MapAnalyzer.Visible(_grid, new Vector2(2.3f, 3f), eye, new Vector2(2.3f, 9f), eye), "В 25 см от щели — стена.");
            Assert.IsTrue(Blocked(2f, 6.1f), "В щель 5 см не пройти.");
        }

        [Test]
        public void Окно_стоя_закрыто_присев_видно_пройти_нельзя()
        {
            Vector2 a = new Vector2(5f, 3f), b = new Vector2(5f, 9f);
            float eye = LevelDesignRules.EyeHeight, crouch = LevelDesignRules.CrouchEyeHeight;

            Assert.IsFalse(MapAnalyzer.Visible(_grid, a, eye, b, eye), "Стоя (1.7 м) — перемычка окна с 1.6 м.");
            Assert.IsTrue(MapAnalyzer.Visible(_grid, a, crouch, b, crouch), "Присев (1.1 м) — в окно.");
            Assert.IsTrue(MapAnalyzer.ThroughOpening(_grid, a, crouch, b, crouch), "Контакт через проём.");
            Assert.IsTrue(Blocked(5f, 6.1f), "Через подоконник не пройти.");
        }

        [TestCase(0.8f, 4.0f, 5.0f, true, TestName = "Soft_вид_закрыт_пуля_пробивает")]
        [TestCase(8.5f, 4.0f, 5.0f, true, TestName = "Visual_вид_закрыт_пуля_пролетает")]
        [TestCase(3.0f, 5.5f, 6.7f, false, TestName = "Hard_без_разметки_закрыт_и_вид_и_пуля")]
        public void Прострел_по_классу_укрытия(float x, float z0, float z1, bool shootable)
        {
            Vector2 a = new Vector2(x, z0), b = new Vector2(x, z1);
            float eye = LevelDesignRules.EyeHeight;

            Assert.IsFalse(MapAnalyzer.Visible(_grid, a, eye, b, eye), "Панель закрывает вид.");
            Assert.AreEqual(shootable, MapAnalyzer.CanShoot(_grid, a, b));
        }

        [Test]
        public void Перешагиваемый_заборчик_проходим_такой_же_без_метки_нет()
        {
            Assert.IsFalse(Blocked(5f, 3.05f), "Заборчик с VaultableObstacle перешагивают — клетка проходима.");
            Assert.IsTrue(Blocked(1f, 9.05f), "Такой же заборчик без метки — непроходимое препятствие.");
            Assert.AreEqual(1, _built.Vaultables.Count, "Собран ровно один перешагиваемый.");
            Assert.AreEqual(0.9f, _built.Vaultables[0].Height, 0.02f);
            Assert.AreEqual(0.1f, _built.Vaultables[0].Thickness, 0.02f);
        }

        [Test]
        public void Дверь_под_перемычкой_проходима_и_половины_связаны()
        {
            Assert.IsFalse(Blocked(8f, 6.1f), "Под перемычкой на 2.0 м проходят.");
            Assert.IsTrue(Blocked(3f, 6.1f), "Стена рядом непроходима.");
            Assert.IsEmpty(MapAnalyzer.FindUnreachable(_grid, MapAnalyzer.Clearance(_grid)),
                           "Через дверь 1.2 м обе стороны доходят до всей карты.");
        }
    }
}
