// ЧЕРНОВИК v1 (2026-10-11). Целевое место после приёмки плана:
// Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyCanopyBaysBuilder.cs
// Не запускался в Unity; проверена только офлайн-компиляция (reports/implementation/canopy-bays/offline-compile.txt).
// Габариты источников измерены в Unity 2026-10-11 (reports/implementation/canopy-bays/prefab-measurements.txt).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Пролёты восточной стороны навеса (OpenTent/EastBays) и детализация западной стены и периметра навеса
    /// (OpenTent/WestWallDetail). Позы — в мировых метрах: ArmoryRangeZone совмещена с PhysicalArenaLayout в нуле,
    /// OpenTent без поворота и масштаба. Восток делится на 4 пролёта по tent.size.z/4 (север → юг): 1 и 4 закрыты
    /// статичным складом (ящики, шкафы, стол с мелким снаряжением; оружия нет), 2 и 3 открыты на базу; шторы подвязаны
    /// к стойкам, у карниза свёрнутые валики; над закрытыми пролётами — плафоны и пустые сокеты света.
    /// Запад: кабели и щитки по краям единственной станции арсенала, перфопанель, подвесные плафоны, ящик на шкафу;
    /// по периметру крыши — валики торцов и угловые висящие шторы (x&lt;0 — WestWallDetail, x&gt;0 — EastBays).
    /// Только MeshFilter/MeshRenderer/LODGroup; Light/Collider/скрипты источников удаляются. Освещение, пробы и GI
    /// задаёт ночной пресет; здесь только выключаются тени и добавляются (|) статичные флаги.
    /// </summary>
    public static class LobbyCanopyBaysBuilder
    {
        public const string TentName = "OpenTent";
        public const string EastGroupName = "EastBays";
        public const string WestGroupName = "WestWallDetail";
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/canopy-bays/latest-build.json";

        // MilitaryCamp (LobbyMilitaryCampSources.Root). Не брать по бюджету: SM_MilitaryCrate_01 (4.7k), SM_AmmoCase_02 (3k).
        // SM_MilitaryCrate_03 не брать: силуэт оружейного кейса (оружие — только в арсенале).
        public static readonly string[] RequiredSources = {
            "SM_ClothesCabinet", "SM_cabinet_a", "SM_Table",
            "SM_Palete_03", "SM_Palete_04", "SM_TarpCrate_01", "SM_MilitaryCrate_02",
            "SM_WoodCrate_big_01", "SM_WoodCrate_big_02", "SM_WoodCrate_small_01",
            "SM_AmmoCase_01", "SM_AmmoCartridgeBoxes_01", "SM_AmmoCartridgeBoxes_02", "SM_AmmoCartridgeBoxes_03",
            "SM_PropaneTank_02", "SM_FuelCanister", "SM_SandBarrier_04", "SM_SandBarrier_06",
            "SM_Lamp_long", "SM_ToolRack"
        };
        // Подвесной плафон на цепи (RuralTown, только чтение): верх y=0, низ −0.555 м, LOD0 4056 tris.
        private const string PendantPath = "Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Prefabs/SM_TopLight.prefab";
        public static readonly string[] RequiredExternalSources = { PendantPath };
        public static readonly string[] RequiredMaterials = {
            "Assets/Materials/Lobby/CanvasOlive.mat",          // как WestCurtain
            "Assets/Materials/Lobby/FrameDark.mat",            // как «Внешняя стойка»
            "Assets/Materials/Lobby/Exterior/DarkRubber.mat",  // кабели, подвязки
            "Assets/Materials/Lobby/Exterior/CampSteel.mat"    // щитки, подвесы
        };

        // ---------- Согласованная геометрия (мир, м) ----------
        private const float Tol = .035f;
        private const float ExpectedFloorHalfX = 7.75f, ExpectedFloorHalfZ = 9.48f, ExpectedTentHalfZ = 7.48f;
        // Промежуточные стойки — на линии угловых «Внешняя стойка» (X 7.59…7.75) под карнизной балкой (X 7.65…7.73).
        private const float PostX = 7.67f, PostHalf = .08f, PostTopY = 4.19f;
        private const float Gap = .06f, CheckGap = .05f;
        // Восточная сторона: 5 пролётов с севера на юг (индексы 0…4); закрыты 2-й и 4-й, остальные открыты на базу.
        private const int BayCount = 5;
        private static readonly int[] ClosedBays = { 1, 3 };
        // Закрытый пролёт: штора опущена по линии стоек, предметы перед ней внутри навеса у кромки пола.
        // Открытый пролёт: штора подвязана, предметы снаружи за линией мешков.
        private const float CurtainX = 7.735f, CurtainHalf = .015f, InsideMaxX = 7.70f, InsideDepth = 1.0f;
        private const float OutsideMinX = 9.0f, OutsideFarX = 10.6f;
        private const float DecorMinX = 7.70f, MaxFloorIntrusion = .10f, ClosedMaxHeight = 2.45f, ClosedFarX = 10.2f;
        // Торцевые столы Table_North/Table_South — граница арены (SpawnZoneBoundaryBlock), не трогаются:
        // внутренняя грань |Z| 7.02, восточный край X 7.75. Вплотную к ним (|Z| > 6.97) декор только за X >= 7.80.
        private const float BoundaryTableInnerZ = 7.02f, OuterStripMinX = 7.80f, GasMinX = 7.82f;
        public const string EastBoundaryPath = "PerimeterDecoration/EastLowBaseBoundary";
        // Чужие объекты, которые не являются препятствием: плоские тропы и дороги на земле.
        private static readonly string[] IgnoredForeignRoots = { "SharedBase/Paths", "SharedBase/Roads" };
        private const float FlatRendererMaxHeight = .10f;
        // Валик у карниза: под балкой (низ 4.05), выше коридора открытых пролётов.
        private const float EaveBeamBottomY = 4.05f, RollX = 7.69f, RollY = 3.93f, RollD = .20f;
        private const float CorridorBottom = .50f, CorridorTop = 3.80f;
        // Кровля TentRoof_LOD0: карниз y 4.19 при |x|=7.75, конёк y 5.59 при x=0 (измерено по вершинам).
        private const float RoofEaveY = 4.19f, RoofRidgeY = 5.59f, RoofHalfWidth = 7.75f;
        private const float BayLampX = 7.30f, BayLampTopY = 3.55f;
        private const float WallCableX = -7.735f, WallCableY = 3.95f, WallCableD = .026f;
        private const float GableRollZ = 7.38f, GableRollD = .16f;
        private static readonly Bounds StationDefault = MinMax(new Vector3(-7.67f, -.1f, -4.13f), new Vector3(-6.09f, 4.05f, 3.55f));
        private static readonly Bounds MirrorFloor = MinMax(new Vector3(-7.75f, -.1f, 3.5f), new Vector3(-6.0f, 2.6f, 5.2f));
        private static readonly Vector2[] ColumnCenters = { new Vector2(2.95f, 3.38f), new Vector2(2.95f, -3.38f), new Vector2(-2.95f, 3.38f), new Vector2(-2.95f, -3.38f) };
        private const float ColumnHalf = .22f + CheckGap;
        // Сектор стрельбы на мишени (Z≈22.5, X −6…6) с запасом до краёв пола: выше 0.5 м ничего.
        private static readonly Bounds FireSector = MinMax(new Vector3(-7.75f, .5f, 7.5f), new Vector3(7.75f, 12f, 30f));

        // Ориентация источников: фасад префабов — +Z (как у западного шкафа SidewallDecoration, yaw 92). Проверить на превью.
        private const float FaceWestYaw = 270f;

        [Serializable] public sealed class Item
        {
            public string role, source, group;
            public Bounds bounds;
            public int lodLevels, lod0Triangles;
        }
        [Serializable] public sealed class Socket { public string path; public Vector3 position; }
        [Serializable] public sealed class RemovedSegment { public string path; public int siblingIndex; public Bounds bounds; }
        [Serializable] public sealed class Report
        {
            public bool layoutValidated;
            public string error;
            public Bounds floor, tent, station;
            public float bayLength;
            public List<float> postZ = new List<float>();
            public List<Bounds> closedReservations = new List<Bounds>(), openCorridors = new List<Bounds>();
            public List<string> checks = new List<string>(), warnings = new List<string>();
            public List<Item> items = new List<Item>();
            public List<RemovedSegment> removedBoundarySegments = new List<RemovedSegment>();
            public List<Socket> sockets = new List<Socket>();
            public Bounds eastBounds, westBounds;
            public int prefabInstances, renderers, colliders, lights, scripts, audioSources, animators, particleSystems;
            public int lod0TrianglesEast, lod0TrianglesWest, lod0TrianglesTotal;
        }
        public static Report LastReport { get; private set; }
        private const int InstanceBudget = 40, TriangleBudget = 45000;

        private sealed class Ctx
        {
            public Transform East, West;
            public Dictionary<string, GameObject> Sources;
            public GameObject Pendant;
            public Material Canvas, Frame, Rubber, Steel;
            public float FloorY, FloorEdgeX, BayLength;
            public Bounds Tent, Station;
            public float[] PostZ;                       // север → юг: 0 и BayCount — угловые, остальные — промежуточные
            public Bounds[] Closed, Corridors;
            public readonly List<KeyValuePair<Transform, Bounds>> Removals = new List<KeyValuePair<Transform, Bounds>>();
            public float? KeptEdgeNorth, KeptEdgeSouth;   // ближние к закрытым пролётам грани оставшихся мешков открытых пролётов
            public readonly List<KeyValuePair<string, Bounds>> Ground = new List<KeyValuePair<string, Bounds>>();
            public readonly List<KeyValuePair<string, Bounds>> ClosedGround = new List<KeyValuePair<string, Bounds>>();
            public readonly HashSet<Transform> Sloped = new HashSet<Transform>();
            public GameObject Src(string stem) => Sources[stem];
        }

        // ---------- Точка входа ----------

        public static void Build(Transform armoryRangeZone, Bounds floor)
        {
            LastReport = new Report { floor = floor };
            if (armoryRangeZone == null || !armoryRangeZone.gameObject.scene.IsValid()) throw new ArgumentException("Нужен корень ArmoryRangeZone в сцене.");
            var tent = armoryRangeZone.Find(TentName) ?? throw new InvalidOperationException("Нет " + TentName + " в ArmoryRangeZone.");
            if (tent.Find(EastGroupName) != null || tent.Find(WestGroupName) != null)
                throw new InvalidOperationException(EastGroupName + "/" + WestGroupName + " уже существует; повторная сборка запрещена.");
            if (Quaternion.Angle(tent.rotation, Quaternion.identity) > .1f || (tent.lossyScale - Vector3.one).sqrMagnitude > 1e-6f || tent.position.magnitude > .05f)
                throw new InvalidOperationException("OpenTent должен стоять в мировом нуле без поворота и масштаба.");

            var ctx = Prepare(armoryRangeZone, tent, floor);
            PlanBoundaryRemovals(armoryRangeZone, ctx);
            // Сегменты восточной границы в закрытых пролётах на время сборки выключаются; удаляются только после успешной проверки,
            // при любой ошибке включаются обратно.
            foreach (var r in ctx.Removals) r.Key.gameObject.SetActive(false);
            try
            {
                var conflicts = ClosedBayConflicts(armoryRangeZone, ctx);
                if (conflicts.Count != 0)
                    throw new InvalidOperationException("Закрытые пролёты заняты чужими объектами: " + string.Join("; ", conflicts));
                ctx.East = NewRoot(tent, EastGroupName);
                ctx.West = NewRoot(tent, WestGroupName);
                BuildEast(ctx);
                BuildWest(ctx, armoryRangeZone);
                Validate(ctx, armoryRangeZone);
                foreach (var r in ctx.Removals)
                {
                    LastReport.removedBoundarySegments.Add(new RemovedSegment { path = HierarchyPath(r.Key, armoryRangeZone),
                        siblingIndex = r.Key.GetSiblingIndex(), bounds = r.Value });
                    UnityEngine.Object.DestroyImmediate(r.Key.gameObject);
                }
                LastReport.checks.Add("EastLowBaseBoundary: удалено сегментов в закрытых пролётах " + ctx.Removals.Count);
                LastReport.layoutValidated = true;
            }
            catch (Exception e)
            {
                LastReport.error = e.Message;
                if (ctx.East != null) UnityEngine.Object.DestroyImmediate(ctx.East.gameObject);
                if (ctx.West != null) UnityEngine.Object.DestroyImmediate(ctx.West.gameObject);
                foreach (var r in ctx.Removals) if (r.Key != null) r.Key.gameObject.SetActive(true);
                throw;
            }
            finally
            {
                var file = Path.Combine(Application.dataPath, "../" + ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, JsonUtility.ToJson(LastReport, true));
            }
            GameLog.Debug.Info($"[LobbyDecoration] Пролёты навеса: экземпляров {LastReport.prefabInstances}/{InstanceBudget}, LOD0 tris " +
                $"{LastReport.lod0TrianglesTotal}/{TriangleBudget} (восток {LastReport.lod0TrianglesEast}, запад {LastReport.lod0TrianglesWest}); удалено сегментов EastLowBaseBoundary " +
                $"{LastReport.removedBoundarySegments.Count}; {ReportPath}");
        }

        /// <summary>Только чтение: неразрешимые конфликты закрытых пролётов (без сегментов границы, которые Build удалит сам).</summary>
        public static List<string> ListClosedBayConflicts(Transform armoryRangeZone, Bounds floor)
        {
            var tent = armoryRangeZone.Find(TentName) ?? throw new InvalidOperationException("Нет " + TentName);
            LastReport = new Report { floor = floor };
            var ctx = Prepare(armoryRangeZone, tent, floor);
            PlanBoundaryRemovals(armoryRangeZone, ctx);
            return ClosedBayConflicts(armoryRangeZone, ctx);
        }

        /// <summary>Только чтение: сегменты EastLowBaseBoundary, которые Build удалит (по координатам, не по имени).</summary>
        public static List<string> ListEastBoundaryRemovals(Transform armoryRangeZone, Bounds floor)
        {
            var tent = armoryRangeZone.Find(TentName) ?? throw new InvalidOperationException("Нет " + TentName);
            LastReport = new Report { floor = floor };
            var ctx = Prepare(armoryRangeZone, tent, floor);
            PlanBoundaryRemovals(armoryRangeZone, ctx);
            return ctx.Removals.Select(r => HierarchyPath(r.Key, armoryRangeZone) + "#" + r.Key.GetSiblingIndex() + " "
                + r.Value.min.ToString("F2") + ".." + r.Value.max.ToString("F2")).ToList();
        }

        // Сегменты восточной границы: имена одинаковые, поэтому выбор только по координатам. Удаляются те, чьи bounds
        // заходят в Z-диапазон закрытых пролётов (|Z| между промежуточной и угловой стойкой); сегменты открытых остаются.
        private static void PlanBoundaryRemovals(Transform zone, Ctx ctx)
        {
            ctx.Removals.Clear(); ctx.KeptEdgeNorth = ctx.KeptEdgeSouth = null;
            var boundary = zone.Find(EastBoundaryPath);
            if (boundary == null) { LastReport.warnings.Add("Нет " + EastBoundaryPath + "; удалять нечего, стыки мешков не ставятся."); return; }
            float lo = ctx.PostZ[ClosedBays[0] + 1], hi = ctx.PostZ[ClosedBays[0]];
            foreach (Transform segment in boundary)
            {
                if (!segment.gameObject.activeInHierarchy) continue;
                var renderers = segment.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
                // Касание стойки не считается: сегмент уходит, только если заходит в закрытый пролёт больше чем на 0.2 м.
                bool closed = ClosedBays.Any(k => Math.Min(b.max.z, ctx.PostZ[k]) - Math.Max(b.min.z, ctx.PostZ[k + 1]) > .2f);
                if (closed)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(segment.gameObject) && !PrefabUtility.IsOutermostPrefabInstanceRoot(segment.gameObject))
                        throw new InvalidOperationException("Сегмент границы внутри чужого префаба, удалить нельзя: " + HierarchyPath(segment, zone));
                    ctx.Removals.Add(new KeyValuePair<Transform, Bounds>(segment, b));
                }
                else if (b.min.z > 0 && b.max.z <= lo) ctx.KeptEdgeNorth = Math.Max(ctx.KeptEdgeNorth ?? float.MinValue, b.max.z);
                else if (b.max.z < 0 && b.min.z >= -lo) ctx.KeptEdgeSouth = Math.Min(ctx.KeptEdgeSouth ?? float.MaxValue, b.min.z);
            }
        }

        private static Ctx Prepare(Transform zone, Transform tent, Bounds floor)
        {
            if (!Near(floor.max.x, ExpectedFloorHalfX) || !Near(floor.min.x, -ExpectedFloorHalfX) || !Near(floor.max.z, ExpectedFloorHalfZ) || !Near(floor.min.z, -ExpectedFloorHalfZ))
                throw new InvalidOperationException("Изменился физический пол (ожидался X±7.75, Z±9.48): " + floor);
            var tentBounds = LobbyMilitaryCampSources.TentBounds(floor);
            if (!Near(tentBounds.max.z, ExpectedTentHalfZ)) throw new InvalidOperationException("Изменилась длина навеса: " + tentBounds);
            var ctx = new Ctx { FloorY = floor.max.y, FloorEdgeX = floor.max.x, Tent = tentBounds, BayLength = tentBounds.size.z / BayCount };
            ctx.PostZ = Enumerable.Range(0, BayCount + 1).Select(k => tentBounds.max.z - ctx.BayLength * k).ToArray();
            ctx.Sources = RequiredSources.ToDictionary(s => s, s => LobbyMilitaryCampSources.Prefab(s));
            if (ctx.Sources.Any(p => p.Value == null)) throw new InvalidOperationException("Не импортирован источник пролётов навеса.");
            ctx.Pendant = AssetDatabase.LoadAssetAtPath<GameObject>(PendantPath) ?? throw new InvalidOperationException("Нет внешнего источника: " + PendantPath);
            ctx.Canvas = LoadMaterial(RequiredMaterials[0]); ctx.Frame = LoadMaterial(RequiredMaterials[1]);
            ctx.Rubber = LoadMaterial(RequiredMaterials[2]); ctx.Steel = LoadMaterial(RequiredMaterials[3]);
            ctx.Station = MeasureStation();

            // Закрытые пролёты (ClosedBays) — между двумя промежуточными стойками, с зазором от их граней.
            float y0 = ctx.FloorY - .03f, y1 = ctx.FloorY + ClosedMaxHeight;
            ctx.Closed = ClosedBays.Select(k => MinMax(
                new Vector3(ctx.FloorEdgeX - InsideDepth, y0, ctx.PostZ[k + 1] + PostHalf + Gap - .05f),
                new Vector3(InsideMaxX + .001f, y1, ctx.PostZ[k] - PostHalf - Gap + .05f))).ToArray();
            // Открытые: предметы только снаружи, за мешками кромки (X >= 9.0).
            ctx.Corridors = Enumerable.Range(0, BayCount).Where(k => !ClosedBays.Contains(k)).Select(k => MinMax(
                new Vector3(OutsideMinX - .001f, y0 - .05f, ctx.PostZ[k + 1] + 2 * PostHalf + Gap - .05f),
                new Vector3(OutsideFarX, y1, ctx.PostZ[k] - 2 * PostHalf - Gap + .05f))).ToArray();
            LastReport.tent = tentBounds; LastReport.bayLength = ctx.BayLength; LastReport.postZ = ctx.PostZ.ToList();
            LastReport.closedReservations = ctx.Closed.ToList(); LastReport.openCorridors = ctx.Corridors.ToList();
            LastReport.station = ctx.Station;
            return ctx;
        }

        // ---------- Восток ----------

        private static void BuildEast(Ctx ctx)
        {
            var posts = Group(ctx.East, "Posts");
            for (int k = 1; k < BayCount; k++)
                Box(ctx, posts, "EastPost_" + k, new Vector3(PostX, (ctx.FloorY + PostTopY) / 2, ctx.PostZ[k]),
                    new Vector3(2 * PostHalf, PostTopY - ctx.FloorY, 2 * PostHalf), Quaternion.identity, ctx.Frame);

            var rig = Group(ctx.East, "CurtainRig");
            for (int bay = 0; bay < BayCount; bay++)
            {
                float a = ctx.PostZ[bay] - (bay == 0 ? 2 * PostHalf : PostHalf) - .02f;
                float b = ctx.PostZ[bay + 1] + (bay == BayCount - 1 ? 2 * PostHalf : PostHalf) + .02f;
                // Закрытый пролёт — штора опущена до земли; открытый — свёрнута валиком у карниза.
                if (ClosedBays.Contains(bay))
                    Box(ctx, rig, "LoweredCurtain_" + (bay + 1), new Vector3(CurtainX, (ctx.FloorY + EaveBeamBottomY) / 2, (a + b) / 2),
                        new Vector3(2 * CurtainHalf, EaveBeamBottomY - ctx.FloorY, a - b), Quaternion.identity, ctx.Canvas);
                else EaveRoll(ctx, rig, "EaveRoll_" + (bay + 1), a, b);
            }
            // Подвязанные шторы — у каждой промежуточной стойки со стороны соседнего открытого пролёта.
            for (int k = 1; k < BayCount; k++)
            {
                if (!ClosedBays.Contains(k - 1)) TiedCurtain(ctx, rig, "TiedCurtain_Post" + k + "_North", ctx.PostZ[k], +1);
                if (!ClosedBays.Contains(k)) TiedCurtain(ctx, rig, "TiedCurtain_Post" + k + "_South", ctx.PostZ[k], -1);
            }
            GableRoll(ctx, rig, "GableRoll_NE", +1, +1); GableRoll(ctx, rig, "GableRoll_SE", +1, -1);
            CornerDrape(ctx, rig, "CornerDrape_NE", +1, +1); CornerDrape(ctx, rig, "CornerDrape_SE", +1, -1);

            var lamps = Group(ctx.East, "Lamps");
            int closedIndex = 0, openIndex = 0;
            for (int k = 0; k < BayCount; k++)
            {
                bool closed = ClosedBays.Contains(k);
                float north = ctx.PostZ[k] - (k == 0 || !closed ? 2 * PostHalf : PostHalf) - Gap;
                float south = ctx.PostZ[k + 1] + (k == BayCount - 1 || !closed ? 2 * PostHalf : PostHalf) + Gap;
                var g = Group(ctx.East, "Bay" + (k + 1) + (closed ? "_InsideStores" : "_OutsideStores"));
                if (closed)
                {
                    if (closedIndex++ % 2 == 0) TableStores(ctx, g, north, south); else CabinetStores(ctx, g, north, south);
                    BayLamp(ctx, lamps, "BayLamp_" + (k + 1), (ctx.PostZ[k] + ctx.PostZ[k + 1]) / 2);
                }
                else OutsideStores(ctx, g, north, south, openIndex++);
            }
        }

        // Ставит предмет вплотную к опущенной шторе изнутри (правая грань X = InsideMaxX).
        private static Bounds PlaceInside(Ctx ctx, Transform t, float? nearZ, int s, float? centerZ)
        {
            var b = Place(ctx, t, 0, nearZ, s, centerZ);
            t.position += new Vector3(InsideMaxX - b.max.x, 0, 0);
            return Measure(t);
        }

        // Внутри, от северной стойки на юг: полевой стол со снаряжением и штабель ящиков.
        private static void TableStores(Ctx ctx, Transform g, float north, float south)
        {
            const int s = -1;
            var table = Spawn(ctx.Src("SM_Table"), g, "FieldTable", 90);
            var tb = PlaceInside(ctx, table.transform, north, s, null);
            Closed(ctx, "FieldTable", tb); Record(ctx, table, g.name);
            TableKit(ctx, g, tb);
            var stack = StackUp(ctx, g, "CrateStack", P("SM_MilitaryCrate_02", 90), P("SM_MilitaryCrate_02", 90));
            var sb = PlaceInside(ctx, stack, tb.min.z - Gap, s, null);
            if (sb.min.z < south - .001f) { UnityEngine.Object.DestroyImmediate(stack.gameObject); LastReport.warnings.Add(g.name + ": штабель не поместился"); }
            else Closed(ctx, g.name + "/CrateStack", sb);
        }

        // Внутри, от южной стойки на север: железный шкаф с ящиками сверху и высокий шкаф.
        private static void CabinetStores(Ctx ctx, Transform g, float north, float south)
        {
            const int s = 1;
            var cabinet = Spawn(ctx.Src("SM_cabinet_a"), g, "SteelCabinet", FaceWestYaw);
            var cb = PlaceInside(ctx, cabinet.transform, south, s, null);
            Closed(ctx, "SteelCabinet", cb); Record(ctx, cabinet, g.name);
            var crate = Prop(ctx, g, "SM_WoodCrate_small_01", "CabinetTopCrate", new Vector3(cb.center.x, cb.max.y, cb.min.z + .45f), 0, cb);
            var cr = Measure(crate.transform);
            Prop(ctx, g, "SM_AmmoCase_01", "CabinetTopCase", new Vector3(cb.center.x, cb.max.y, cb.max.z - .25f), 0, cb);
            Prop(ctx, g, "SM_AmmoCartridgeBoxes_01", "CabinetTopBoxes", new Vector3(cr.center.x, cr.max.y, cr.center.z), 0, cr);
            var locker = Spawn(ctx.Src("SM_ClothesCabinet"), g, "SupplyLocker", FaceWestYaw);
            var lb = PlaceInside(ctx, locker.transform, cb.max.z + Gap, s, null);
            if (lb.max.z > north + .001f) { UnityEngine.Object.DestroyImmediate(locker); LastReport.warnings.Add(g.name + ": высокий шкаф не поместился"); }
            else { Closed(ctx, "SupplyLocker", lb); Record(ctx, locker, g.name); }
        }

        // Снаружи открытого пролёта за мешками: штабели и грузы по краям, середина пролёта остаётся просветом.
        private static void OutsideStores(Ctx ctx, Transform g, float north, float south, int variant)
        {
            switch (variant % 3)
            {
                case 0:
                {
                    // Северный: поддон с ящиком и зелёным кейсом у северной стойки, баллон с канистрой у южной.
                    var back = StackUp(ctx, g, "PalletStack", P("SM_Palete_03", 0), P("SM_WoodCrate_big_02", 0), P("SM_MilitaryCrate_02", 90));
                    Closed(ctx, g.name + "/PalletStack", Place(ctx, back, OutsideMinX, north, -1, null));
                    OutsideGas(ctx, g, south, +1);
                    break;
                }
                case 1:
                {
                    // Средний (выход к развилке): по штабелю у каждой стойки, просвет посередине.
                    var a = StackUp(ctx, g, "TarpStack", P("SM_Palete_04", 0), P("SM_TarpCrate_01", 0));
                    Closed(ctx, g.name + "/TarpStack", Place(ctx, a, OutsideMinX, north, -1, null));
                    var b = StackUp(ctx, g, "CaseStack", P("SM_MilitaryCrate_02", 90), P("SM_MilitaryCrate_02", 90));
                    Closed(ctx, g.name + "/CaseStack", Place(ctx, b, OutsideMinX, south, +1, null));
                    break;
                }
                default:
                {
                    // Южный: ящик-башня у южной стойки, баллон с канистрой у северной.
                    var tower = StackUp(ctx, g, "CrateTower", P("SM_WoodCrate_big_01", 0), P("SM_MilitaryCrate_02", 90));
                    Closed(ctx, g.name + "/CrateTower", Place(ctx, tower, OutsideMinX, south, +1, null));
                    OutsideGas(ctx, g, north, -1);
                    break;
                }
            }
        }

        private static void OutsideGas(Ctx ctx, Transform parent, float zNear, int s)
        {
            var gas = Group(parent, "GasCorner");
            Prop(ctx, gas, "SM_PropaneTank_02", "GasBottle", new Vector3(OutsideMinX + .17f, ctx.FloorY, zNear + s * .17f), 0, null);
            Prop(ctx, gas, "SM_FuelCanister", "FuelCanister", new Vector3(OutsideMinX + .55f, ctx.FloorY, zNear + s * .20f), 0, null);
            Closed(ctx, parent.name + "/GasCorner", Measure(gas));
        }

        // Мелкое снаряжение на столе — патронные коробки и закрытый кейс; без оружия.
        private static void TableKit(Ctx ctx, Transform g, Bounds tb)
        {
            Prop(ctx, g, "SM_AmmoCase_01", "TableCase", new Vector3(tb.center.x + .15f, tb.max.y, tb.min.z + .25f), 90, tb);
            Prop(ctx, g, "SM_AmmoCartridgeBoxes_01", "TableBoxesA", new Vector3(tb.center.x - .20f, tb.max.y, tb.center.z - .05f), 0, tb);
            Prop(ctx, g, "SM_AmmoCartridgeBoxes_02", "TableBoxesB", new Vector3(tb.center.x - .20f, tb.max.y, tb.center.z + .22f), 90, tb);
            Prop(ctx, g, "SM_AmmoCartridgeBoxes_03", "TableBoxesC", new Vector3(tb.center.x + .15f, tb.max.y, tb.center.z + .30f), 0, tb);
        }

        // Плафон SM_Lamp_long вдоль пролёта у карниза, два подвеса до кровли, сокет под плафоном.
        private static void BayLamp(Ctx ctx, Transform parent, string name, float zCenter)
        {
            var g = Group(parent, name);
            var lamp = Spawn(ctx.Src("SM_Lamp_long"), g, "Fixture", 0);
            var b = Measure(lamp.transform);
            if (b.size.z < 2f || b.size.x > .4f || b.size.y > .35f) throw new InvalidOperationException("SM_Lamp_long неожиданного габарита: " + b.size);
            lamp.transform.position += new Vector3(BayLampX - b.center.x, BayLampTopY - b.max.y, zCenter - b.center.z);
            b = Measure(lamp.transform);
            Record(ctx, lamp, g.name);
            float roof = RoofY(BayLampX);
            foreach (var dz in new[] { -.85f, .85f })
                Box(ctx, g, "HangerRod", new Vector3(BayLampX, (b.max.y + roof) / 2, zCenter + dz), new Vector3(.012f, roof - b.max.y, .012f), Quaternion.identity, ctx.Steel);
            AddSocket(ctx, g, "LightingSocket__" + name + "__CommonLightingOwner", new Vector3(b.center.x, b.min.y - .05f, b.center.z));
        }

        private static void EaveRoll(Ctx ctx, Transform parent, string name, float zA, float zB)
        {
            var g = Group(parent, name);
            Cyl(ctx, g, "Roll", new Vector3(RollX, RollY, zA), new Vector3(RollX, RollY, zB), RollD, RollD, ctx.Canvas);
            float strapBottom = RollY - RollD / 2 - .015f;
            foreach (var t in new[] { .2f, .5f, .8f })
                Box(ctx, g, "Strap", new Vector3(RollX, (strapBottom + EaveBeamBottomY) / 2, Mathf.Lerp(zA, zB, t)),
                    new Vector3(RollD + .03f, EaveBeamBottomY - strapBottom, .035f), Quaternion.identity, ctx.Rubber);
        }

        // Штора, собранная у стойки: верхний сборок до валика, подвязка, нижний распуск; dir — в сторону открытого пролёта.
        private static void TiedCurtain(Ctx ctx, Transform parent, string name, float postZ, int dir)
        {
            var g = Group(parent, name);
            float x = PostX + PostHalf, z = postZ + dir * (PostHalf + .02f + .12f);
            Cyl(ctx, g, "Gathered", new Vector3(x, 1.32f, z), new Vector3(x, CorridorTop, z), .16f, .22f, ctx.Canvas);
            Cyl(ctx, g, "Tie", new Vector3(x, 1.23f, z), new Vector3(x, 1.31f, z), .19f, .25f, ctx.Rubber);
            Cyl(ctx, g, "Skirt", new Vector3(x, .50f, z), new Vector3(x, 1.22f, z), .18f, .24f, ctx.Canvas);
        }

        // Заполняет зазор между последним оставшимся сегментом открытого пролёта и закрытым пролётом (s: +1 север, -1 юг).
        private static void EdgeFill(Ctx ctx, Transform parent, string name, float? keptEdge, int s, float closedNearZ)
        {
            if (!keptEdge.HasValue) { LastReport.warnings.Add(name + " пропущен: нет оставшихся сегментов границы в открытом пролёте."); return; }
            var stack = StackUp(ctx, parent, name, P("SM_SandBarrier_04", 90), P("SM_SandBarrier_06", 90));
            float length = Measure(stack).size.z, start = keptEdge.Value + s * (CheckGap + .005f);
            var b = Place(ctx, stack, OuterStripMinX, null, s, start + s * length / 2);
            if (b.max.y - ctx.FloorY > .45f) throw new InvalidOperationException(name + " выше ограждения: " + b.size);
            if (s * (Far(b, s) - closedNearZ) > -CheckGap + .001f) throw new InvalidOperationException(name + " не помещается до закрытого пролёта: " + b);
            Ground(ctx, name, b);
        }

        // ---------- Запад и периметр навеса ----------

        private static void BuildWest(Ctx ctx, Transform zone)
        {
            var perimeter = Group(ctx.West, "CanopyPerimeter");
            GableRoll(ctx, perimeter, "GableRoll_NW", -1, +1); GableRoll(ctx, perimeter, "GableRoll_SW", -1, -1);
            CornerDrape(ctx, perimeter, "CornerDrape_NW", -1, +1); CornerDrape(ctx, perimeter, "CornerDrape_SW", -1, -1);

            var services = Group(ctx.West, "WallServices");
            float stationS = ctx.Station.min.z - Gap - .06f, stationN = ctx.Station.max.z + Gap + .04f;
            float southEnd = ctx.PostZ[BayCount] + 2 * PostHalf + .07f, northEnd = ctx.PostZ[0] - 2 * PostHalf - .07f;
            WallCable(ctx, services, "WallCableSouth", southEnd, stationS);
            WallCable(ctx, services, "WallCableNorth", stationN, northEnd);
            JunctionBox(ctx, services, "JunctionBoxSouth", -6.90f, new Vector3(.12f, .40f, .30f), 1.60f);
            JunctionBox(ctx, services, "JunctionBoxNorth", 6.55f, new Vector3(.12f, .30f, .24f), 1.65f);
            // Перфопанель над западным полевым столом (SidewallDecoration, Z −6.48…−5.38).
            var board = Spawn(ctx.Src("SM_ToolRack"), services, "ToolBoard", 0);
            var bd = Measure(board.transform);
            board.transform.position += new Vector3(-ctx.FloorEdgeX + .002f - bd.min.x, 1.55f - bd.center.y, -5.93f - bd.center.z);
            Record(ctx, board, services.name);

            var pendants = Group(ctx.West, "Pendants");
            Pendant(ctx, pendants, "Pendant_NW", -7.00f, 6.30f);
            Pendant(ctx, pendants, "Pendant_SW", -7.05f, -5.95f);

            LockerTop(ctx, Group(ctx.West, "LockerTop"), zone);
        }

        private static void WallCable(Ctx ctx, Transform parent, string name, float zA, float zB)
        {
            if (zB - zA < .3f) throw new InvalidOperationException(name + ": нет места для кабеля");
            Box(ctx, parent, name + "__DarkRubber", new Vector3(WallCableX, WallCableY, (zA + zB) / 2), new Vector3(WallCableD, WallCableD, zB - zA), Quaternion.identity, ctx.Rubber);
        }

        private static void JunctionBox(Ctx ctx, Transform parent, string name, float z, Vector3 size, float centerY)
        {
            var g = Group(parent, name);
            Box(ctx, g, "Box__CampSteel", new Vector3(-ctx.FloorEdgeX + size.x / 2 + .002f, centerY, z), size, Quaternion.identity, ctx.Steel);
            float top = centerY + size.y / 2;
            Box(ctx, g, "Drop__DarkRubber", new Vector3(WallCableX, (top + WallCableY) / 2, z), new Vector3(WallCableD, WallCableY - top, WallCableD), Quaternion.identity, ctx.Rubber);
        }

        // SM_TopLight чашкой под кровлю; фидер от настенного кабеля по скату; сокет под плафоном.
        private static void Pendant(Ctx ctx, Transform parent, string name, float x, float z)
        {
            var g = Group(parent, name);
            var p = Spawn(ctx.Pendant, g, "Shade", 0);
            var b = Measure(p.transform);
            if (b.size.y > .6f || b.size.x > .3f) throw new InvalidOperationException("SM_TopLight неожиданного габарита: " + b.size);
            p.transform.position += new Vector3(x - b.center.x, RoofY(x) - .005f - b.max.y, z - b.center.z);
            b = Measure(p.transform);
            Record(ctx, p, PendantPath, g.name);
            // Фидер Г-образно: по стене на высоте настенного кабеля (ниже карнизной балки 4.05) и вверх к чашке.
            // Наклонный отрезок дал бы AABB, задевающий балку при проверке пересечений.
            float wallX = WallCableX + WallCableD / 2, riserX = b.min.x - .02f, top = b.max.y - .02f;
            Box(ctx, g, "Feed__DarkRubber", new Vector3((wallX + riserX) / 2, WallCableY, z), new Vector3(riserX - wallX, .02f, .02f), Quaternion.identity, ctx.Rubber);
            Box(ctx, g, "FeedRiser__DarkRubber", new Vector3(riserX, (WallCableY + top) / 2, z), new Vector3(.02f, top - WallCableY, .02f), Quaternion.identity, ctx.Rubber);
            AddSocket(ctx, g, "LightingSocket__" + name + "__CommonLightingOwner", new Vector3(b.center.x, b.min.y - .05f, b.center.z));
        }

        // Ящик и патронные коробки на западном шкафу одежды (SidewallDecoration). Опора чужая — без неё элемент пропускается.
        private static void LockerTop(Ctx ctx, Transform g, Transform zone)
        {
            var support = zone.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.gameObject.activeInHierarchy && r.name.StartsWith("SM_ClothesCabinet") && r.name.EndsWith("_LOD0")
                    && r.bounds.center.x < -6.5f && r.bounds.center.z > ctx.Station.min.z - 1.5f && r.bounds.center.z < ctx.Station.min.z)
                .Select(r => r.bounds).ToList();
            if (support.Count != 1)
            {
                LastReport.warnings.Add("LockerTop пропущен: опора SM_ClothesCabinet у южного края станции найдена " + support.Count + " раз.");
                return;
            }
            var sb = support[0];
            var crate = Prop(ctx, g, "SM_WoodCrate_small_01", "LockerTopCrate", new Vector3(sb.center.x, sb.max.y, sb.center.z), 0, sb);
            var cr = Measure(crate.transform);
            Prop(ctx, g, "SM_AmmoCartridgeBoxes_02", "LockerTopBoxes", new Vector3(cr.center.x, cr.max.y, cr.center.z), 90, cr);
        }

        // Свёрнутая штора вдоль ската торца (sx: −1 запад/+1 восток, sz: +1 север/−1 юг); от конька 0.2 м до угла.
        private static void GableRoll(Ctx ctx, Transform parent, string name, int sx, int sz)
        {
            float r = GableRollD / 2, x0 = .20f, x1 = 7.45f;
            var a = new Vector3(sx * x0, RoofY(x0) - .02f - r, sz * GableRollZ);
            var b = new Vector3(sx * x1, RoofY(x1) - .02f - r, sz * GableRollZ);
            var roll = Cyl(ctx, parent, name, a, b, GableRollD, GableRollD, ctx.Canvas);
            ctx.Sloped.Add(roll.transform);
        }

        // Угловая висящая штора на открытом торце у стойки: полотно + складка; низ 1.5 м (над столами торцов).
        private static void CornerDrape(Ctx ctx, Transform parent, string name, int sx, int sz)
        {
            var g = Group(parent, name);
            Cyl(ctx, g, "Panel", new Vector3(sx * 7.27f, 1.50f, sz * 7.40f), new Vector3(sx * 7.27f, 4.00f, sz * 7.40f), .45f, .07f, ctx.Canvas);
            Cyl(ctx, g, "Fold", new Vector3(sx * 7.12f, 1.62f, sz * 7.41f), new Vector3(sx * 7.12f, 3.95f, sz * 7.41f), .22f, .09f, ctx.Canvas);
        }

        // ---------- Размещение ----------

        private static (string stem, float yaw) P(string stem, float yaw) => (stem, yaw);

        // Штабель: первый элемент на грунте, каждый следующий — по центру на предыдущем; свес не больше 6 см.
        // Повороты кратны 90°, чтобы габариты не раздувались.
        private static Transform StackUp(Ctx ctx, Transform parent, string role, params (string stem, float yaw)[] layers)
        {
            var stems = layers.Select(l => l.stem).ToArray();
            var g = Group(parent, role);
            Bounds support = default;
            for (int i = 0; i < stems.Length; i++)
            {
                var go = Spawn(ctx.Src(stems[i]), g, (i == 0 ? "Base" : "Top" + i), layers[i].yaw);
                var foot = i == 0 ? new Vector3(0, ctx.FloorY, 0) : new Vector3(support.center.x, support.max.y, support.center.z);
                SetFoot(go, foot);
                var b = Measure(go.transform);
                if (i > 0 && (b.min.x < support.min.x - .06f || b.max.x > support.max.x + .06f || b.min.z < support.min.z - .06f || b.max.z > support.max.z + .06f))
                    throw new InvalidOperationException(role + ": " + stems[i] + " свисает с опоры " + b.size);
                support = b;
                Record(ctx, go, g.name);
            }
            return g;
        }

        // Сдвиг поддерева: min.x → minX, низ → пол; по Z либо ближняя к стойке грань (s=+1 — min.z, s=−1 — max.z) → nearZ, либо центр → centerZ.
        private static Bounds Place(Ctx ctx, Transform t, float minX, float? nearZ, int s, float? centerZ)
        {
            var b = Measure(t);
            float dz = nearZ.HasValue ? nearZ.Value - (s > 0 ? b.min.z : b.max.z) : centerZ.Value - b.center.z;
            t.position += new Vector3(minX - b.min.x, ctx.FloorY - b.min.y, dz);
            return Measure(t);
        }

        private static float Far(Bounds b, int s) => s > 0 ? b.max.z : b.min.z;

        // Предмет на грунте/опоре: центр XZ — в foot, низ — на foot.y; при опоре проверяется, что не свисает.
        private static GameObject Prop(Ctx ctx, Transform parent, string stem, string role, Vector3 foot, float yaw, Bounds? support)
        {
            var go = Spawn(ctx.Src(stem), parent, role, yaw);
            SetFoot(go, foot);
            var b = Measure(go.transform);
            if (support.HasValue)
            {
                var s = support.Value;
                if (b.min.x < s.min.x - .002f || b.max.x > s.max.x + .002f || b.min.z < s.min.z - .002f || b.max.z > s.max.z + .002f)
                    throw new InvalidOperationException(role + " не помещается на опоре: " + b + " / " + s);
            }
            Record(ctx, go, parent.name);
            return go;
        }

        private static void SetFoot(GameObject go, Vector3 foot)
        {
            var b = Measure(go.transform);
            go.transform.position += foot - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        private static GameObject Spawn(GameObject source, Transform parent, string role, float yaw)
        {
            if (source == null) throw new InvalidOperationException("Нет источника для " + role);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            if (go == null) throw new InvalidOperationException("Не удалось создать экземпляр " + source.name);
            go.name = role + "__" + source.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go, StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static GameObject Box(Ctx ctx, Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = center; go.transform.rotation = rotation;
            go.transform.localScale = FitScale(go, size); go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Sanitize(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            Record(ctx, go, "primitive_cube_" + material.name, parent.name);
            return go;
        }

        // Цилиндр от a до b; dx — поперечник по локальной X, dz — по локальной Z (для вертикали — мировые X и Z).
        private static GameObject Cyl(Ctx ctx, Transform parent, string name, Vector3 a, Vector3 b, float dx, float dz, Material material)
        {
            var axis = b - a;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = name;
            go.transform.SetParent(parent, false); go.transform.position = (a + b) / 2;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            go.transform.localScale = FitScale(go, new Vector3(dx, axis.magnitude, dz));
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Sanitize(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            Record(ctx, go, "primitive_cylinder_" + material.name, parent.name);
            return go;
        }

        // Масштаб под фактический меш примитива (встроенный Cylinder.fbx в 6000.4 имеет габарит 2×2×2, а не 1×2×1).
        private static Vector3 FitScale(GameObject primitive, Vector3 size)
        {
            var mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            var native = mesh != null ? mesh.bounds.size : Vector3.zero;
            if (native.x <= 0 || native.y <= 0 || native.z <= 0) throw new InvalidOperationException("Пустой меш примитива " + primitive.name);
            return new Vector3(size.x / native.x, size.y / native.y, size.z / native.z);
        }

        private static void AddSocket(Ctx ctx, Transform parent, string name, Vector3 position)
        {
            var socket = new GameObject(name).transform; socket.SetParent(parent, false); socket.position = position;
            LastReport.sockets.Add(new Socket { path = HierarchyPath(socket, parent.root), position = position });
        }

        private static Transform NewRoot(Transform tent, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(tent, false);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            return t;
        }

        private static Transform Group(Transform parent, string name) => NewRoot(parent, name);

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("Неверный материал: " + path);
            return material;
        }

        // ---------- Проверки ----------

        private static Bounds MeasureStation()
        {
            var stations = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                .Where(m => m != null && m.GetType().Name == "ArsenalWallController" && m.gameObject.scene.IsValid()).ToList();
            if (stations.Count > 1) throw new InvalidOperationException("Ожидалась одна станция арсенала, найдено " + stations.Count);
            var b = StationDefault;
            if (stations.Count == 0) { LastReport.warnings.Add("Станция арсенала не найдена; использован согласованный габарит."); return b; }
            // Только активные renderer'ы: у неактивных bounds пустые в нуле и раздули бы габарит до центра арены.
            foreach (var r in stations[0].GetComponentsInChildren<Renderer>(false)) if (r.enabled) b.Encapsulate(r.bounds);
            if (b.max.x > -5.5f) throw new InvalidOperationException("Станция арсенала не у западной стены: " + b);
            b.max = new Vector3(b.max.x, Mathf.Max(b.max.y, EaveBeamBottomY), b.max.z);   // подъём станции свободен до карниза
            return b;
        }

        private static List<string> ClosedBayConflicts(Transform zone, Ctx ctx)
        {
            var result = new List<string>();
            foreach (var r in ForeignRenderers(zone, ctx))
                foreach (var res in ctx.Closed)
                    if (Intersects(Shrink(r.bounds, .004f), res)) { result.Add(HierarchyPath(r.transform, zone) + " " + r.bounds.min.ToString("F2") + ".." + r.bounds.max.ToString("F2")); break; }
            return result;
        }

        // Чужие видимые renderer'ы окружения (Environment): без наших корней, MapShell, полотна кровли и гигантского фона.
        private static IEnumerable<Renderer> ForeignRenderers(Transform zone, Ctx ctx)
        {
            var environment = zone.parent != null ? zone.parent : zone;
            var shell = zone.Find("MapShell");
            var ignored = IgnoredForeignRoots.Select(p => environment.Find(p)).Where(t => t != null).ToArray();
            foreach (var r in environment.GetComponentsInChildren<Renderer>(false))
            {
                var t = r.transform;
                if ((ctx.East != null && t.IsChildOf(ctx.East)) || (ctx.West != null && t.IsChildOf(ctx.West))) continue;
                if (shell != null && t.IsChildOf(shell)) continue;
                // Каркас крыши — опора для стоек, валиков и подвесов; касание с ним задумано.
                if (r.name.StartsWith("TentRoof_LOD") || (zone.Find("OpenTent/TentRoof") is Transform roof && t.IsChildOf(roof))) continue;
                if (r.bounds.size.x > 40f || r.bounds.size.z > 40f) continue;
                if (r.bounds.size.y < FlatRendererMaxHeight) continue;            // плоские тропы и площадки на земле
                if (ignored.Any(t.IsChildOf)) continue;
                if (ctx.Removals.Any(x => x.Key != null && t.IsChildOf(x.Key))) continue;
                if (IsHigherLod(r)) continue;
                yield return r;
            }
        }

        private static void Validate(Ctx ctx, Transform zone)
        {
            var foreign = ForeignRenderers(zone, ctx).Select(r => new KeyValuePair<string, Bounds>(HierarchyPath(r.transform, zone), r.bounds)).ToList();
            var stationZone = ctx.Station; stationZone.Expand(new Vector3(2 * CheckGap, 0, 2 * CheckGap));
            foreach (var root in new[] { ctx.East, ctx.West })
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (IsHigherLod(r)) continue;
                    var b = Measure(r.transform, false);
                    string path = HierarchyPath(r.transform, root.parent);
                    if (Intersects(b, stationZone)) throw new InvalidOperationException("Перекрыта станция арсенала или её подъём: " + path);
                    if (Intersects(b, MirrorFloor)) throw new InvalidOperationException("Занят пол перед зеркалом: " + path);
                    foreach (var c in ColumnCenters)
                        if (Intersects(b, MinMax(new Vector3(c.x - ColumnHalf, -1, c.y - ColumnHalf), new Vector3(c.x + ColumnHalf, 6, c.y + ColumnHalf))))
                            throw new InvalidOperationException("Задета физическая колонна: " + path);
                    if (Intersects(b, FireSector)) throw new InvalidOperationException("Выше 0.5 м в секторе стрельбы: " + path);
                    if (!ctx.Sloped.Contains(r.transform) && Math.Abs(b.center.x) <= RoofHalfWidth && b.max.y > RoofY(b.center.x) + .02f)
                        throw new InvalidOperationException("Выходит за кровлю: " + path);
                    if (b.max.y > RoofRidgeY) throw new InvalidOperationException("Выше конька: " + path);
                    var shrunk = Shrink(b, .004f);
                    foreach (var f in foreign)
                        if (Intersects(shrunk, f.Value)) throw new InvalidOperationException("Пересечение с " + f.Key + ": " + path);
                }
            LastReport.checks.Add("station/rise, mirror floor, columns, fire sector, open corridors, roof, foreign renderers: passed");

            foreach (var g in ctx.ClosedGround)
            {
                if (!ctx.Closed.Concat(ctx.Corridors).Any(c => Inside(g.Value, c))) throw new InvalidOperationException("Вышло из резерва пролёта: " + g.Key + " " + g.Value);
                if (g.Value.min.x < ctx.FloorEdgeX - InsideDepth - .001f) throw new InvalidOperationException("Заходит на пол больше 1 м: " + g.Key);
            }
            var ground = ctx.Ground.Concat(ctx.ClosedGround).ToList();
            for (int i = 0; i < ground.Count; i++)
            {
                if (Math.Abs(ground[i].Value.min.y - ctx.FloorY) > .01f) throw new InvalidOperationException("Не стоит на полу: " + ground[i].Key);
                for (int j = i + 1; j < ground.Count; j++)
                    if (HorizontalOverlap(ground[i].Value, ground[j].Value, CheckGap - .001f))
                        throw new InvalidOperationException("Зазор меньше 5 см: " + ground[i].Key + " / " + ground[j].Key);
            }
            LastReport.checks.Add("closed reservations, floor intrusion ≤0.1 м, ground gaps ≥5 см: passed");

            var all = new[] { ctx.East, ctx.West };
            LastReport.eastBounds = Measure(ctx.East); LastReport.westBounds = Measure(ctx.West);
            LastReport.renderers = all.Sum(r => r.GetComponentsInChildren<Renderer>(true).Length);
            LastReport.prefabInstances = all.Sum(r => r.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)));
            LastReport.colliders = all.Sum(r => r.GetComponentsInChildren<Collider>(true).Length);
            LastReport.lights = all.Sum(r => r.GetComponentsInChildren<Light>(true).Length);
            LastReport.scripts = all.Sum(r => r.GetComponentsInChildren<MonoBehaviour>(true).Length);
            LastReport.audioSources = all.Sum(r => r.GetComponentsInChildren<AudioSource>(true).Length);
            LastReport.animators = all.Sum(r => r.GetComponentsInChildren<Animator>(true).Length);
            LastReport.particleSystems = all.Sum(r => r.GetComponentsInChildren<ParticleSystem>(true).Length);
            if (LastReport.colliders + LastReport.lights + LastReport.scripts + LastReport.audioSources + LastReport.animators + LastReport.particleSystems != 0)
                throw new InvalidOperationException("Пролёты навеса содержат запрещённые компоненты.");
            LastReport.lod0TrianglesEast = Lod0Triangles(ctx.East);
            LastReport.lod0TrianglesWest = Lod0Triangles(ctx.West);
            LastReport.lod0TrianglesTotal = LastReport.lod0TrianglesEast + LastReport.lod0TrianglesWest;
            if (LastReport.lod0TrianglesTotal > TriangleBudget) throw new InvalidOperationException("Превышен бюджет LOD0: " + LastReport.lod0TrianglesTotal);
            if (LastReport.prefabInstances > InstanceBudget) throw new InvalidOperationException("Превышен бюджет экземпляров: " + LastReport.prefabInstances);
            LastReport.checks.Add("components 0, LOD0 ≤ " + TriangleBudget + ", instances ≤ " + InstanceBudget + ": passed");
        }

        private static bool IsHigherLod(Renderer r)
        {
            var group = r.GetComponentInParent<LODGroup>(true);
            if (group == null) return false;
            var lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++) if (lods[i].renderers.Contains(r) && !lods[0].renderers.Contains(r)) return true;
            return false;
        }

        private static int Lod0Triangles(Transform root)
        {
            int sum = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsHigherLod(r)) continue;
                var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++) sum += (int)mesh.GetIndexCount(s) / 3;
            }
            return sum;
        }

        private static void CheckEnvelope(string role, Bounds b, float maxX, float maxY, float maxZ)
        {
            if (b.size.x > maxX || b.size.y > maxY || b.size.z > maxZ) throw new InvalidOperationException(role + " неожиданного габарита: " + b.size);
        }

        private static void Closed(Ctx ctx, string role, Bounds b) => ctx.ClosedGround.Add(new KeyValuePair<string, Bounds>(role, b));
        private static void Ground(Ctx ctx, string role, Bounds b) => ctx.Ground.Add(new KeyValuePair<string, Bounds>(role, b));

        private static void Record(Ctx ctx, GameObject go, string group) => Record(ctx, go, SourcePath(go), group);
        private static void Record(Ctx ctx, GameObject go, string source, string group)
            => LastReport.items.Add(new Item { role = go.name, source = source, group = group, bounds = Measure(go.transform),
                lodLevels = go.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length), lod0Triangles = Lod0Triangles(go.transform) });
        private static string SourcePath(GameObject go)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            return source != null ? AssetDatabase.GetAssetPath(source) : go.name;
        }

        // ---------- Геометрия и очистка (как в LobbyWarehouseEntranceBuilder; пространство — мир) ----------

        private static float RoofY(float x) => RoofRidgeY - (RoofRidgeY - RoofEaveY) * Mathf.Clamp01(Math.Abs(x) / RoofHalfWidth);

        private static Bounds Measure(Transform subtree, bool children = true)
        {
            var result = new Bounds(); var has = false;
            var filters = children ? subtree.GetComponentsInChildren<MeshFilter>(true) : subtree.GetComponents<MeshFilter>();
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) throw new InvalidOperationException("Пустой mesh: " + filter.name);
                var b = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!has) { result = new Bounds(p, Vector3.zero); has = true; } else result.Encapsulate(p);
                }
            }
            if (!has) throw new InvalidOperationException("Нет геометрии: " + subtree.name);
            return result;
        }

        private static void Sanitize(GameObject go, StaticEditorFlags flags)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Статичный декор не принимает skinned-источник: " + go.name);
            // Сначала MonoBehaviour: RequireComponent иначе удерживает Light/Collider источника.
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) throw new InvalidOperationException("Missing script в источнике: " + go.name);
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is LODGroup)) UnityEngine.Object.DestroyImmediate(c);
            }
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
            {
                if (lod.GetLODs().Any(l => l.renderers.Length == 0 || l.renderers.Any(r => r == null)))
                    throw new InvalidOperationException("Неполная LOD-цепочка: " + go.name);
                lod.RecalculateBounds();
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterials.Length == 0 || r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                    throw new InvalidOperationException("Отсутствует материал/шейдер: " + r.name);
                // Пробы, отражения и GI настраивает только ночной пресет освещения.
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject) | flags);
        }

        private static Bounds MinMax(Vector3 min, Vector3 max) { var b = new Bounds(); b.SetMinMax(min, max); return b; }
        private static Bounds Shrink(Bounds b, float d) { b.Expand(-2 * d); return b; }
        private static bool Near(float a, float b) => Math.Abs(a - b) < Tol;
        private static bool Inside(Bounds a, Bounds outer) => a.min.x >= outer.min.x && a.max.x <= outer.max.x && a.min.y >= outer.min.y
            && a.max.y <= outer.max.y && a.min.z >= outer.min.z && a.max.z <= outer.max.z;
        private static bool Intersects(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y
            && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z;
        private static bool HorizontalOverlap(Bounds a, Bounds b, float gap) => a.min.x < b.max.x + gap && a.max.x > b.min.x - gap
            && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;
        private static string HierarchyPath(Transform t, Transform root) => t == root || t.parent == null ? t.name : HierarchyPath(t.parent, root) + "/" + t.name;
    }
}
