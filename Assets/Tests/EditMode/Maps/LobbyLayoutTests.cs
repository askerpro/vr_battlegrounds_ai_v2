using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Планировка лобби (<c>Lobby.unity</c>, принята пользователем 2026-10-13): навес над ареной,
    /// столы по пояс на северном, южном и западном краях, восточный край открыт на базу; одна
    /// неподвижная станция общей нейтральной зоны у западной стены; за северным и южным краями —
    /// стрельбища по четыре дорожки. Проём станции делит западную столешницу.
    ///
    /// <para>
    /// Сцена открывается превью-сценой, игровой цикл не нужен: всё проверяется геометрией
    /// и шагами физики, как в <c>WeaponDropPhysicsTests</c>. Арена берётся по коллайдерам
    /// слоя <c>Ground</c> — это и есть пол, по которому ходят и куда телепортируются.
    /// </para>
    /// </summary>
    public class LobbyLayoutTests
    {
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string TablesPath = "Environment/ArmoryRangeZone/InteriorDecoration/Tables";

        private const float EyeHeight = 1.4f;

        /// <summary>На сколько навес короче арены с каждого торца (север и юг).</summary>
        private const float CanopyEndInset = 2f;

        private Scene _scene;

        [OneTimeSetUp]
        public void OpenScene() => _scene = EditorSceneManager.OpenPreviewScene(LobbyPath);

        [OneTimeTearDown]
        public void CloseScene() => EditorSceneManager.ClosePreviewScene(_scene);

        private IEnumerable<T> All<T>() where T : Component =>
            _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

        /// <summary>Пол арены: объединение твёрдых коллайдеров слоя Ground.</summary>
        private Bounds ArenaFloor()
        {
            int ground = LayerMask.NameToLayer("Ground");
            Collider[] floors = All<Collider>().Where(c => !c.isTrigger && c.gameObject.layer == ground).ToArray();
            Assert.IsNotEmpty(floors, "В лобби нет пола на слое Ground.");

            Bounds b = floors[0].bounds;
            foreach (Collider c in floors) b.Encapsulate(c.bounds);
            return b;
        }

        private Transform Tables()
        {
            string[] path = TablesPath.Split('/');
            GameObject root = _scene.GetRootGameObjects().FirstOrDefault(r => r.name == path[0]);
            Assert.IsNotNull(root, $"В лобби нет корня '{path[0]}'.");
            Transform tables = root.transform.Find(string.Join("/", path.Skip(1)));
            Assert.IsNotNull(tables, $"В лобби нет '{TablesPath}'.");
            return tables;
        }

        // ── Арсенал ──────────────────────────────────────────────────────────

        [Test]
        public void Станция_арсенала_одна_принадлежит_нейтральной_зоне_и_смотрит_на_арену()
        {
            ArsenalWallController[] walls = All<ArsenalWallController>().ToArray();
            Assert.AreEqual(1, walls.Length,
                "В лобби должна быть одна станция арсенала: " + string.Join(", ", walls.Select(w => w.name)));
            TeamSpawnZone[] zones = All<TeamSpawnZone>().Where(z => z.gameObject.activeInHierarchy).ToArray();
            Assert.AreEqual(1, zones.Length, "В лобби одна нейтральная зона.");
            Assert.IsNull(zones[0].HomeTeam, "Общий арсенал лобби не принадлежит команде.");

            Bounds arena = ArenaFloor();

            foreach (ArsenalWallController wall in walls)
            {
                ArsenalStationAnchor station = wall.GetComponent<ArsenalStationAnchor>();
                Assert.IsNotNull(station, $"{wall.name}: нет разметки места экипировки.");
                Assert.AreSame(zones[0], station.Zone, $"{wall.name}: станция не связана с общей зоной.");
                Assert.IsNotNull(station.StandingPoint, $"{wall.name}: нет места экипировки.");
                Assert.IsTrue(station.HasBoardDirection, $"{wall.name}: нет направления личного табло.");
                Vector3 toArena = Vector3.ProjectOnPlane(arena.center - station.StandingPosition, Vector3.up).normalized;
                Assert.Greater(Vector3.Dot(station.BoardFacing, toArena), 0.5f,
                    $"{wall.name}: табло места экипировки направлено от арены.");
            }
        }

        [Test]
        public void У_каждой_стены_арсенала_свой_sceneId()
        {
            NetworkIdentity[] ids = All<ArsenalWallController>().Select(w => w.GetComponent<NetworkIdentity>()).ToArray();

            Assert.IsTrue(ids.All(i => i != null), "У стены арсенала нет NetworkIdentity.");
            Assert.IsTrue(ids.All(i => i.sceneId != 0), "У стены арсенала нулевой sceneId — Mirror её не заспавнит.");
            Assert.AreEqual(ids.Length, ids.Select(i => i.sceneId).Distinct().Count(), "У стен арсенала совпадают sceneId.");
        }

        // ── Край арены ───────────────────────────────────────────────────────

        [Test]
        public void Краевых_стен_нет_взгляд_уходит_за_край()
        {
            Bounds arena = ArenaFloor();
            PhysicsScene physics = _scene.GetPhysicsScene();
            var blocked = new List<string>();

            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
            {
                Vector3 side = Vector3.Scale(dir, arena.extents);
                Vector3 across = new Vector3(dir.z, 0f, dir.x);
                float halfSpan = Mathf.Abs(Vector3.Dot(across, arena.extents)) - 1f;

                for (float s = -halfSpan; s <= halfSpan; s += halfSpan / 2f)
                {
                    Vector3 origin = arena.center + side - dir * 1.5f + across * s;
                    origin.y = arena.max.y + EyeHeight + 0.2f;

                    var hits = new RaycastHit[128];
                    int count = physics.Raycast(origin, dir, hits, 5f, ~0, QueryTriggerInteraction.Ignore);
                    Assert.Less(count, hits.Length, "Недостаточная ёмкость луча проверки края арены.");
                    for (int i = 0; i < count; i++)
                    {
                        RaycastHit hit = hits[i];
                        // Сохранённые станции стоят у края арены. Они не являются краевой стеной;
                        // разрешение относится только к корпусу станции с явной нейтральной зоной.
                        ArsenalStationAnchor station = hit.collider.GetComponentInParent<ArsenalStationAnchor>();
                        if (station != null && station.Wall != null && station.Zone != null && station.Zone.HomeTeam == null)
                            continue;
                        blocked.Add($"{dir} из {origin}: {hit.collider.name} на {hit.distance:F1} м");
                    }
                }
            }

            Assert.IsEmpty(blocked, "На уровне глаз за краем арены что-то стоит:\n" + string.Join("\n", blocked));
        }

        [Test]
        public void По_краю_столы_по_пояс_восток_открыт()
        {
            Bounds arena = ArenaFloor();
            int ground = LayerMask.NameToLayer("Ground");
            Transform tables = Tables();
            var sides = new HashSet<Vector2Int>();
            var problems = new List<string>();

            foreach (Transform table in tables)
            {
                Collider[] colliders = table.GetComponentsInChildren<Collider>(true);
                if (colliders.Length == 0) { problems.Add($"{table.name}: нет коллайдера"); continue; }

                Bounds b = colliders[0].bounds;
                foreach (Collider c in colliders)
                {
                    b.Encapsulate(c.bounds);
                    if (c.isTrigger) problems.Add($"{table.name}/{c.name}: trigger — положенное провалится");
                    if (c.gameObject.layer == ground) problems.Add($"{table.name}/{c.name}: слой Ground — на стол можно телепортироваться");
                }

                float height = b.max.y - arena.max.y;
                float depth = Mathf.Min(b.size.x, b.size.z);
                if (height < 0.85f || height > 1.05f) problems.Add($"{table.name}: высота {height:F2} м, нужна по пояс (0.9–1.0)");
                if (depth < 0.35f || depth > 0.55f) problems.Add($"{table.name}: глубина {depth:F2} м, нужна 0.4–0.5");

                Vector3 d = b.center - arena.center;
                bool alongX = b.size.x > b.size.z;
                if (alongX) sides.Add(new Vector2Int(0, d.z > 0 ? 1 : -1));
                else sides.Add(new Vector2Int(d.x > 0 ? 1 : -1, 0));

                // Навес короче арены на 2 м с каждого торца: северный и южный столы стоят по его краю.
                float toEdge = alongX ? arena.extents.z - Mathf.Abs(d.z) : arena.extents.x - Mathf.Abs(d.x);
                float allowed = alongX ? CanopyEndInset + 0.5f : 1f;
                if (toEdge > allowed) problems.Add($"{table.name}: стоит в {toEdge:F1} м от края, а не по краю");
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
            var expected = new HashSet<Vector2Int> { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
            Assert.IsTrue(sides.SetEquals(expected), "Столы должны стоять по северному, южному и западному краям: " + string.Join(", ", sides));
        }

        [Test]
        public void Положенное_на_стол_оружие_и_магазин_лежат()
        {
            Transform tables = Tables();
            Transform table = tables.Cast<Transform>().First();
            // Проёмы станций оставляют пустоту в центре общего bounds стола. Кладём предмет
            // на настоящую верхнюю твёрдую часть, а не в центр прежнего цельного столешничного объёма.
            Collider[] solids = table.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
            Assert.IsNotEmpty(solids, $"{table.name}: нет твёрдой поверхности.");
            float topY = solids.Max(c => c.bounds.max.y);
            BoxCollider top = solids.OfType<BoxCollider>()
                .Where(c => Mathf.Abs(c.bounds.max.y - topY) < 0.001f)
                .OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).FirstOrDefault();
            Assert.IsNotNull(top, $"{table.name}: не найдена верхняя плоская часть столешницы.");
            Bounds b = top.bounds;

            PhysicsScene physics = _scene.GetPhysicsScene();
            var failures = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var grabbable = prefab.GetComponent<UxrGrabbableObject>();
                if (grabbable == null || prefab.GetComponent<Rigidbody>() == null || !grabbable.RigidBodyDynamicOnRelease) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
                try
                {
                    // Кладут, а не бросают: плашмя, вдоль стола, в пяти сантиметрах над ним.
                    bool alongX = b.size.x > b.size.z;
                    Quaternion lying = Quaternion.Euler(0f, alongX ? 90f : 0f, 90f);
                    instance.transform.SetPositionAndRotation(new Vector3(b.center.x, b.max.y + 0.05f, b.center.z), lying);

                    var body = instance.GetComponent<Rigidbody>();
                    foreach (Rigidbody nested in instance.GetComponentsInChildren<Rigidbody>(true))
                        if (nested != body) nested.isKinematic = true;
                    foreach (AnchoredItemCollisionIgnore ignore in instance.GetComponentsInChildren<AnchoredItemCollisionIgnore>(true))
                        ignore.SyncIgnoredCollisions();

                    body.isKinematic = false;
                    body.linearVelocity = Vector3.zero;

                    int steps = Mathf.CeilToInt(3f / Time.fixedDeltaTime);
                    for (int i = 0; i < steps; i++) physics.Simulate(Time.fixedDeltaTime);

                    checks++;
                    float y = instance.transform.position.y;
                    if (y < b.max.y - 0.15f)
                        failures.Add($"{prefab.name}: y={y:F2} при крышке стола {b.max.y:F2}");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            Assert.Greater(checks, 0, "Не нашлось ни одного динамического предмета оружия.");
            Assert.IsEmpty(failures, $"Положенное на стол '{table.name}' не удержалось:\n" + string.Join("\n", failures));
        }

        // ── Мишени ───────────────────────────────────────────────────────────

        [Test]
        public void За_краем_мишени_видимые_с_арены()
        {
            Bounds arena = ArenaFloor();
            int ground = LayerMask.NameToLayer("Ground");
            PhysicsScene physics = _scene.GetPhysicsScene();

            ShootingTarget[] targets = All<ShootingTarget>().ToArray();
            Assert.GreaterOrEqual(targets.Length, 8, "Мишеней в лобби меньше восьми.");

            var sides = new HashSet<Vector2Int>();
            var problems = new List<string>();

            foreach (ShootingTarget target in targets)
            {
                if (target.GetComponentInChildren<UxrGrabbableObject>(true) != null || target.GetComponentInParent<UxrGrabbableObject>(true) != null)
                    problems.Add($"{target.name}: хватается");

                Collider plate = target.Pivot != null
                    ? target.Pivot.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => !c.isTrigger)
                    : null;
                if (plate == null) { problems.Add($"{target.name}: у щита нет твёрдого коллайдера"); continue; }
                if (plate.gameObject.layer == ground) problems.Add($"{target.name}: щит на слое Ground");

                Vector3 p = plate.bounds.center;
                bool outside = Mathf.Abs(p.x - arena.center.x) > arena.extents.x + 1f ||
                               Mathf.Abs(p.z - arena.center.z) > arena.extents.z + 1f;
                if (!outside) problems.Add($"{target.name}: стоит на арене, а не за краем ({p})");

                // Стрелок — у ближайшего края, в метре от него, на уровне глаз.
                Vector3 shooter = new Vector3(
                    Mathf.Clamp(p.x, arena.min.x + 1f, arena.max.x - 1f),
                    arena.max.y + EyeHeight,
                    Mathf.Clamp(p.z, arena.min.z + 1f, arena.max.z - 1f));

                Vector3 toTarget = p - shooter;
                if (physics.Raycast(shooter, toTarget.normalized, out RaycastHit hit, toTarget.magnitude + 1f, 1, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.transform.IsChildOf(target.Pivot))
                        problems.Add($"{target.name}: с арены пуля упирается в '{hit.collider.name}'");
                }
                else
                {
                    problems.Add($"{target.name}: луч с арены не попал в щит");
                }

                Vector3 fromArena = p - arena.center;
                sides.Add(Mathf.Abs(fromArena.x) / arena.extents.x > Mathf.Abs(fromArena.z) / arena.extents.z
                    ? new Vector2Int(fromArena.x > 0 ? 1 : -1, 0)
                    : new Vector2Int(0, fromArena.z > 0 ? 1 : -1));
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
            var expected = new HashSet<Vector2Int> { new Vector2Int(0, 1), new Vector2Int(0, -1) };
            Assert.IsTrue(sides.SetEquals(expected), "Мишени должны стоять за северным и южным краями: " + string.Join(", ", sides));
        }

        [Test]
        public void За_краем_нельзя_телепортироваться()
        {
            PhysicalArenaDefinition arena = All<PhysicalArenaDefinition>().Single();
            Assert.IsTrue(arena.TryFloorBounds(out Bounds floor), "Нет границ пола PhysicalArenaDefinition.");
            floor.Expand(0.1f);

            int ground = LayerMask.NameToLayer("Ground");
            string[] outside = All<Collider>()
                .Where(c => !c.isTrigger && c.gameObject.layer == ground)
                .Where(c => !floor.Contains(c.bounds.min) || !floor.Contains(c.bounds.max))
                .Select(c => $"{c.name} {c.bounds.min}–{c.bounds.max}").ToArray();

            Assert.IsEmpty(outside, "Геометрия за краем на слое Ground — туда можно телепортироваться: " + string.Join(", ", outside));
        }
    }
}
