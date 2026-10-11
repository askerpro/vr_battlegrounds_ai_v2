// ЧЕРНОВИК v2 (2026-10-11). Целевое место после приёмки плана:
// Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyWarehouseEntranceBuilder.cs
// Не запускался в Unity; проверена только офлайн-компиляция (см. reports/implementation/warehouse-entrance).
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
    /// Оформление входа склада-мастерской. Все позы — в локальных координатах корня WarehouseZone
    /// (центр корпуса 8×12 м, +Z — север, вход — южный фасад z=-6, проём x∈[-4,-1]), поэтому зону
    /// можно двигать и вращать целиком. Только статичные экземпляры без Collider/Light/скриптов;
    /// свет — пустые сокеты, ExteriorWarmLight зоны не трогается (перенос делает ночной пресет).
    /// </summary>
    public static class LobbyWarehouseEntranceBuilder
    {
        public const string GroupName = "EntranceDressing";
        public const string ShellName = "Склад и мастерская";
        private const string ReportPath = "tasks/lobby-decoration/reports/implementation/warehouse-entrance/latest-build.json";

        // Все модели — из LobbyMilitaryCampSources.Root (импорт MilitaryCamp 2026-10-11).
        // Не использовать по бюджету треугольников: SM_Generator_02, SM_MobileGenerator_01, SM_CinderBlocks_02/03.
        public static readonly string[] RequiredSources = {
            "SM_cabinet_a", "SM_ToolRack", "SM_Door_lamp_a", "SM_AmmoCase_01", "SM_pipe_15",
            "SM_Palete_01", "SM_Palete_02", "SM_Palete_03", "SM_Palete_04",
            "SM_TarpCrate_01", "SM_SandBarrier_03",
            "SM_MilitaryCrate_01", "SM_MilitaryCrate_02", "SM_MilitaryCrate_03",
            "SM_WoodCrate_big_01", "SM_WoodCrate_big_02", "SM_WoodCrate_small_01", "SM_WoodCrate_small_02",
            "SM_MobileGenerator_02", "SM_FuelCanister", "SM_Barrel_01", "SM_Barrel_02",
            "SM_PropaneTank_01", "SM_PropaneTank_02"
        };
        // Переносная рабочая лампа вне MilitaryCamp (только чтение).
        private const string WorkLampPath = "Assets/Art/Lobby/Imported/Catalog/PostApocalypticTown/URP/Art/Prefabs/SM_Light2.prefab";
        public static readonly string[] RequiredExternalSources = { WorkLampPath };

        private const string MaterialFolder = "Assets/Materials/Lobby/Exterior/";
        public static readonly string[] RequiredMaterials = { "Truck_wood", "CampSteel", "DarkRubber", "WornSoil" };

        // Согласованная геометрия корпуса в локальных координатах зоны (LobbyBaseBuildingsBuilder, 8×12, южный вход).
        private const float Facade = -6f, GateMinX = -4f, GateMaxX = -1f, WestFace = -4f, EastFace = 4f, NorthFace = 6f;
        private const float GateCenterX = (GateMinX + GateMaxX) / 2;
        private const int InstanceBudget = 30;
        // Резерв оформления: SW-угол, фасад, SE-угол и восточная стена до z=-2.4 (дорожка к навесу отдыха свободна).
        private static readonly Bounds Reservation = MinMax(new Vector3(-7.4f, -.012f, -9.9f), new Vector3(6.3f, 3.2f, -1.95f));
        // Свободный проход 1.5 м от проёма до края площадки и полный створ проёма у порога.
        private static readonly Bounds Corridor = MinMax(new Vector3(GateCenterX - .75f, .06f, -9.9f), new Vector3(GateCenterX + .75f, 2.2f, Facade));
        private static readonly Bounds GateMouth = MinMax(new Vector3(GateMinX + .3f, .06f, Facade - 1.2f), new Vector3(GateMaxX - .3f, 2.2f, Facade));
        // Глухой простенок западной стены у SW-угла (окна z∈[-6,-4] и [-2,0]).
        private const float WestPierMinZ = -4f, WestPierMaxZ = -2f;

        // Ориентация источников без подтверждённого «фасада» — проверить на первом превью и при необходимости развернуть на 180°.
        private const float BenchYaw = 180f;     // открытые полки SM_cabinet_a должны смотреть на площадку (-Z)
        private const float WorkLampYaw = 200f;  // сетка/стекло SM_Light2 — на проём и площадку
        private const float GeneratorYaw = -8f;  // длинная сторона SM_MobileGenerator_02 вдоль фасада

        [Serializable] public sealed class Item
        {
            public string role, source, group;
            public Bounds bounds;
            public int lodLevels;
        }
        [Serializable] public sealed class Socket { public string path; public Vector3 localPosition; }
        [Serializable] public sealed class Report
        {
            public bool layoutValidated;
            public Bounds shell, entrance, reservation, corridor, actualBounds;
            public List<Item> items = new List<Item>();
            public List<Socket> sockets = new List<Socket>();
            public int prefabInstances, renderers, colliders, lights, scripts, audioSources, animators, particleSystems;
        }
        public static Report LastReport { get; private set; }

        private sealed class Ctx
        {
            public Transform Root;
            public Dictionary<string, GameObject> Sources;
            public Material Wood, Steel, Rubber, Soil;
            public float RoofTop;
            public readonly List<KeyValuePair<string, Bounds>> Ground = new List<KeyValuePair<string, Bounds>>();
        }

        public static void Build(Transform warehouseZone)
        {
            LastReport = null;
            if (warehouseZone == null || !warehouseZone.gameObject.scene.IsValid()) throw new ArgumentException("Нужен корень WarehouseZone в сцене.");
            if (warehouseZone.Find(GroupName) != null) throw new InvalidOperationException(GroupName + " уже существует; повторная сборка запрещена.");
            if ((warehouseZone.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) throw new InvalidOperationException("WarehouseZone должен иметь мировой scale=1.");
            var shell = warehouseZone.Find(ShellName) ?? throw new InvalidOperationException("Нет корпуса: " + ShellName);
            var entry = shell.Find("SM_Building_wall_entrance_2m_A") ?? throw new InvalidOperationException("Нет модуля входа склада.");

            var ctx = new Ctx();
            ctx.Sources = RequiredSources.ToDictionary(s => s, s => LobbyMilitaryCampSources.Prefab(s));
            if (ctx.Sources.Any(p => p.Value == null)) throw new InvalidOperationException("Не импортирован источник входа мастерской.");
            foreach (var path in RequiredExternalSources)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException("Нет внешнего источника: " + path);
            ctx.Wood = LoadMaterial("Truck_wood"); ctx.Steel = LoadMaterial("CampSteel");
            ctx.Rubber = LoadMaterial("DarkRubber"); ctx.Soil = LoadMaterial("WornSoil");

            LastReport = new Report { reservation = Reservation, corridor = Corridor };
            var root = new GameObject(GroupName).transform;
            root.SetParent(warehouseZone, false);
            root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;
            ctx.Root = root;
            try
            {
                ValidateShell(ctx, shell, entry);
                MaterialsYard(ctx, Group(ctx, "MaterialsYard"));
                var gate = Group(ctx, "GateApproach");
                GateApproach(ctx, gate);
                var bench = Workbench(ctx, Group(ctx, "Workbench"));
                var generator = PowerAndFuel(ctx, Group(ctx, "PowerAndFuel"));
                CrateStacks(ctx, Group(ctx, "CrateStacks"));
                Cable(ctx, gate, generator, bench);
                Validate(ctx);
                LastReport.layoutValidated = true;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                throw;
            }
            finally
            {
                var file = Path.Combine(Application.dataPath, "../" + ReportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, JsonUtility.ToJson(LastReport, true));
            }
            GameLog.Debug.Info($"[LobbyDecoration] Вход мастерской: экземпляров {LastReport.prefabInstances}/{InstanceBudget}, " +
                $"renderer {LastReport.renderers}; {ReportPath}");
        }

        // ---------- Группы композиции ----------

        // SW-угол: поддоны у глухого простенка западной стены (вместо лестницы — в паке её нет), штабель поддонов
        // с листами, трубы, доски, брезент и мешки.
        private static void MaterialsYard(Ctx ctx, Transform g)
        {
            // Простенок Z[−4;−2]: две ширины 0.8/0.82 м с зазором ~0.2 м.
            LeaningPallet(ctx, g, "SM_Palete_01", "LeaningPalletA", -3.5f, 10f);
            LeaningPallet(ctx, g, "SM_Palete_02", "LeaningPalletB", -2.45f, 14f);
            var pallets = Stack(ctx, g, "PalletStack", new Vector3(-5.6f, 0, -5.55f), new Vector2(1.35f, 1.35f), .6f, false,
                new[] { P(ctx, "SM_Palete_04", 0) }, new[] { P(ctx, "SM_Palete_03", 4), P(ctx, "SM_Palete_04", -3) }, false);
            Block(ctx, g, "SheetsOnPallets__CampSteel", new Vector3(pallets.center.x, pallets.max.y + .0175f, pallets.center.z),
                new Vector3(1.0f, .035f, 1.6f), Quaternion.Euler(0, 3, 0), ctx.Steel);
            PipeBundle(ctx, g, new Vector3(-6.75f, 0, -5.5f));
            BoardStack(ctx, g, new Vector3(-5.75f, 0, -7.25f), 4);
            Prop(ctx, g, "SM_TarpCrate_01", "TarpCoveredCrate", new Vector3(-6.35f, 0, -8.85f), 12, ground: true);
            Prop(ctx, g, "SM_SandBarrier_03", "SackStack", new Vector3(-4.95f, 0, -8.75f), -10, ground: true);
        }

        // Проём: утоптанная площадка, светильник над воротами + сокет, перфопанель с инструментом на простенке.
        private static void GateApproach(Ctx ctx, Transform g)
        {
            var apron = GameObject.CreatePrimitive(PrimitiveType.Quad);
            apron.name = "ApproachApron__WornSoil";
            apron.transform.SetParent(g, false);
            apron.transform.localPosition = new Vector3(-1.0f, .006f, Facade - .01f - 1.75f);
            apron.transform.localRotation = Quaternion.Euler(90, 0, 0);
            apron.transform.localScale = new Vector3(7.4f, 3.5f, 1);
            apron.GetComponent<MeshRenderer>().sharedMaterial = ctx.Soil;
            Sanitize(apron); Record(ctx, apron, "read_only_WornSoil", g.name);

            var fixture = WallProp(ctx, g, "SM_Door_lamp_a", "GateFixture", 0, GateCenterX, 2.75f, Facade - .006f);
            var fb = Measure(fixture.transform, ctx.Root);
            if (fb.min.y < 2.55f || fb.max.y > ctx.RoofTop) throw new InvalidOperationException("Светильник не помещается над проёмом.");
            AddSocket(ctx, g, "LightingSocket__WarehouseGate__CommonLightingOwner", new Vector3(fb.center.x, fb.min.y - .045f, fb.min.z - .07f));

            // Простенок x∈[-1,0] между проёмом и окном — единственная глухая часть южного фасада под панель.
            var rack = WallProp(ctx, g, "SM_ToolRack", "ToolBoard", 90, (GateMaxX + 0f) / 2, 1.45f, Facade - .004f);
            var rb = Measure(rack.transform, ctx.Root);
            if (rb.min.x < GateMaxX - .01f || rb.max.x > .01f) throw new InvalidOperationException("Перфопанель вышла за простенок между проёмом и окном.");
        }

        // Верстак под окном, переносная рабочая лампа на нём (сокет), ящик с инструментом, высокий баллон.
        private static Bounds Workbench(Ctx ctx, Transform g)
        {
            var bench = Spawn(ctx.Sources["SM_cabinet_a"], g, "Workbench", BenchYaw);
            var b = Measure(bench.transform, ctx.Root);
            bench.transform.localPosition += new Vector3(1.55f - b.center.x, -b.min.y, Facade - .06f - b.max.z);
            b = Measure(bench.transform, ctx.Root);
            Register(ctx, "Workbench", b);
            Record(ctx, bench, AssetDatabase.GetAssetPath(ctx.Sources["SM_cabinet_a"]), g.name);
            var lamp = Spawn(AssetDatabase.LoadAssetAtPath<GameObject>(WorkLampPath), g, "PortableWorkLamp", WorkLampYaw);
            Ground(ctx, lamp, new Vector3(b.min.x + .3f, b.max.y, b.center.z + .05f));
            Record(ctx, lamp, WorkLampPath, g.name);
            var lb = Measure(lamp.transform, ctx.Root);
            AddSocket(ctx, g, "LightingSocket__WorkLamp__CommonLightingOwner", new Vector3(lb.center.x - .05f, lb.center.y + .04f, lb.min.z - .12f));
            Prop(ctx, g, "SM_AmmoCase_01", "ToolCase", new Vector3(b.max.x - .32f, b.max.y, b.center.z), 90, ground: false);
            Prop(ctx, g, "SM_PropaneTank_02", "WeldingGas", new Vector3(.45f, 0, Facade - .3f), 0, ground: true);
            return b;
        }

        // SE: SM_MobileGenerator_02 (2.2k tris, 3 LOD), канистры, бочки у угла, приземистый баллон.
        private static Bounds PowerAndFuel(Ctx ctx, Transform g)
        {
            var generator = Prop(ctx, g, "SM_MobileGenerator_02", "Generator", new Vector3(4.35f, 0, -8.35f), GeneratorYaw, ground: true);
            var gb = Measure(generator.transform, ctx.Root);
            if (gb.size.x > 2.1f || gb.size.z > 1.2f || gb.size.y < .9f || gb.size.y > 1.4f)
                throw new InvalidOperationException("SM_MobileGenerator_02 неожиданного габарита: " + gb.size);
            Prop(ctx, g, "SM_FuelCanister", "FuelCanisterA", new Vector3(gb.min.x - .32f, 0, -8.6f), 80, ground: true);
            Prop(ctx, g, "SM_FuelCanister", "FuelCanisterB", new Vector3(gb.min.x - .38f, 0, -8.98f), 104, ground: true);
            Prop(ctx, g, "SM_PropaneTank_01", "GeneratorGas", new Vector3(gb.max.x + .38f, 0, -8.05f), 0, ground: true);
            // Бочки 0.6×0.6 м: повороты кратны 90°, чтобы габарит не раздувался; центры не ближе 0.75 м.
            Prop(ctx, g, "SM_Barrel_01", "BarrelA", new Vector3(4.6f, 0, -5.4f), 0, ground: true);
            Prop(ctx, g, "SM_Barrel_02", "BarrelB", new Vector3(5.35f, 0, -5.4f), 90, ground: true);
            Prop(ctx, g, "SM_Barrel_01", "BarrelC", new Vector3(4.97f, 0, -6.2f), 180, ground: true);
            return gb;
        }

        // Ящики штабелями, не россыпью: военный у верстака, деревянный на краю площадки, высокий деревянный у восточной стены.
        private static void CrateStacks(Ctx ctx, Transform g)
        {
            Stack(ctx, g, "CrateStackEast", new Vector3(3.1f, 0, Facade - .07f), new Vector2(1.25f, .8f), 1.3f, true,
                new[] { P(ctx, "SM_MilitaryCrate_02", 0) }, new[] { P(ctx, "SM_MilitaryCrate_01", 90) }, false);
            var south = Stack(ctx, g, "CrateStackSouth", new Vector3(1.75f, 0, -8.95f), new Vector2(1.4f, 1.4f), 1.7f, false,
                new[] { P(ctx, "SM_WoodCrate_big_02", 0) }, new[] { P(ctx, "SM_WoodCrate_small_01", 7) }, false);
            Prop(ctx, g, "SM_MilitaryCrate_03", "RifleCaseBySouthStack", new Vector3(south.min.x - .5f, 0, south.center.z), 0, ground: true);
            Stack(ctx, g, "CrateStackEastWall", new Vector3(5.55f, 0, -3.55f), new Vector2(1.45f, 2.2f), 2.0f, false,
                new[] { P(ctx, "SM_WoodCrate_big_01", 0) }, new[] { P(ctx, "SM_WoodCrate_small_02", -4) }, false);
        }

        // Кабель генератора по земле вдоль верстака к восточному откосу проёма (вне прохода 1.5 м).
        private static void Cable(Ctx ctx, Transform g, Bounds generator, Bounds bench)
        {
            var points = new[] {
                new Vector3(generator.min.x + .05f, .018f, generator.center.z),
                new Vector3(.2f, .018f, bench.min.z - .4f),
                new Vector3(GateMaxX - .45f, .018f, Facade - .1f)
            };
            for (int i = 0; i < points.Length - 1; i++)
            {
                var d = points[i + 1] - points[i];
                Block(ctx, g, "PowerCable_" + i + "__DarkRubber", (points[i] + points[i + 1]) / 2,
                    new Vector3(.035f, .03f, d.magnitude + .03f), Quaternion.LookRotation(d.normalized, Vector3.up), ctx.Rubber);
            }
        }

        // ---------- Составные элементы ----------

        // Поддон стоит торцом на грунте и опирается верхней кромкой на глухой простенок западной стены.
        private static void LeaningPallet(Ctx ctx, Transform parent, string stem, string role, float zCenter, float leanDegrees)
        {
            // Родная ось: x — ширина 0.8, y — толщина, z — длина 1.2. Длина → вверх, толщина → от стены (X), ширина → вдоль стены (Z);
            // поворот вокруг Z наклоняет верх к стене (+X).
            var rotation = Quaternion.AngleAxis(-leanDegrees, Vector3.forward) * Quaternion.LookRotation(Vector3.up, Vector3.right);
            var go = Spawn(ctx.Sources[stem], parent, role, 0);
            go.transform.localRotation = rotation;
            var b = Measure(go.transform, ctx.Root);
            go.transform.localPosition += new Vector3(WestFace - .01f - b.max.x, -b.min.y, zCenter - b.center.z);
            b = Measure(go.transform, ctx.Root);
            if (b.size.y < 1f || b.min.z < WestPierMinZ + .02f || b.max.z > WestPierMaxZ - .02f)
                throw new InvalidOperationException(role + " не стоит на глухом простенке: " + b);
            Register(ctx, role, b);
            Record(ctx, go, AssetDatabase.GetAssetPath(ctx.Sources[stem]), parent.name);
        }

        private static void PipeBundle(Ctx ctx, Transform parent, Vector3 foot)
        {
            var g = new GameObject("PipeBundle").transform; g.SetParent(parent, false);
            var lying = Quaternion.Euler(90, 0, 0);
            var offsets = new[] { new Vector3(-.095f, 0, 0), new Vector3(.095f, 0, .04f), new Vector3(0, .155f, -.05f) };
            foreach (var o in offsets)
            {
                var pipe = Spawn(ctx.Sources["SM_pipe_15"], g, "Pipe", 0, lying);
                var b = Measure(pipe.transform, ctx.Root);
                if (b.size.z < 1.3f || b.size.y > .25f) throw new InvalidOperationException("SM_pipe_15 не лёг горизонтально: " + b.size);
                Ground(ctx, pipe, foot + o);
                Record(ctx, pipe, AssetDatabase.GetAssetPath(ctx.Sources["SM_pipe_15"]), parent.name);
            }
            Register(ctx, "PipeBundle", Measure(g, ctx.Root));
        }

        private static void BoardStack(Ctx ctx, Transform parent, Vector3 foot, float yaw)
        {
            var g = new GameObject("BoardStack__Truck_wood").transform; g.SetParent(parent, false);
            var rotation = Quaternion.Euler(0, yaw, 0);
            foreach (float x in new[] { -.8f, .8f })
                Block(ctx, g, "Bearer", foot + rotation * new Vector3(x, .04f, 0), new Vector3(.1f, .08f, 1f), rotation, ctx.Wood);
            for (int layer = 0; layer < 3; layer++)
                Block(ctx, g, "BoardLayer" + layer, foot + rotation * new Vector3(.04f * layer, .08f + .04f + layer * .08f, -.03f * layer),
                    new Vector3(2.4f - .12f * layer, .08f, .85f - .14f * layer), rotation * Quaternion.Euler(0, layer == 1 ? -2 : 1.5f, 0), ctx.Wood);
            Register(ctx, "BoardStack", Measure(g, ctx.Root));
        }

        // Штабель: нижний ряд вдоль X с зазором 4 см; верхние — цепочкой (первый на весь ряд, следующий на предыдущий)
        // или perBase (i-й верхний на i-й нижний). Верхний ящик не свисает с опоры больше чем на 6 см.
        private static Bounds Stack(Ctx ctx, Transform parent, string role, Vector3 foot, Vector2 envelope, float maxHeight, bool alignBackToFacade,
            (GameObject source, float yaw)[] baseRow, (GameObject source, float yaw)[] tops, bool perBase)
        {
            var g = new GameObject(role).transform; g.SetParent(parent, false);
            var row = new List<GameObject>();
            float cursor = 0;
            for (int i = 0; i < baseRow.Length; i++)
            {
                var go = Spawn(baseRow[i].source, g, "Base" + i, baseRow[i].yaw);
                var b = Measure(go.transform, ctx.Root);
                go.transform.localPosition += new Vector3(cursor - b.min.x, -b.min.y, foot.z - b.center.z);
                cursor = Measure(go.transform, ctx.Root).max.x + .04f;
                row.Add(go);
            }
            float shiftX = foot.x - Measure(g, ctx.Root).center.x;
            foreach (var go in row) go.transform.localPosition += new Vector3(shiftX, 0, 0);
            var placed = new List<GameObject>(row);
            var chainSupport = Measure(g, ctx.Root);
            for (int i = 0; i < tops.Length; i++)
            {
                if (perBase && i >= row.Count) throw new InvalidOperationException(role + ": верхних больше, чем опор.");
                var support = perBase ? Measure(row[i].transform, ctx.Root) : chainSupport;
                var go = Spawn(tops[i].source, g, "Top" + i, tops[i].yaw);
                Ground(ctx, go, new Vector3(support.center.x, support.max.y, support.center.z));
                var tb = Measure(go.transform, ctx.Root);
                if (tb.min.x < support.min.x - .06f || tb.max.x > support.max.x + .06f || tb.min.z < support.min.z - .06f || tb.max.z > support.max.z + .06f)
                    throw new InvalidOperationException(role + ": верхний элемент свисает с опоры " + tb.size);
                chainSupport = tb; placed.Add(go);
            }
            var sb = Measure(g, ctx.Root);
            if (alignBackToFacade)
            {
                g.localPosition += new Vector3(0, 0, foot.z - sb.max.z);
                sb = Measure(g, ctx.Root);
            }
            if (sb.size.x > envelope.x || sb.size.z > envelope.y || sb.size.y > maxHeight)
                throw new InvalidOperationException(role + " не помещается в резерв: " + sb.size);
            foreach (var go in placed)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
                Record(ctx, go, source != null ? AssetDatabase.GetAssetPath(source) : go.name, parent.name);
            }
            Register(ctx, role, sb);
            return sb;
        }

        // ---------- Размещение ----------

        private static (GameObject, float) P(Ctx ctx, string stem, float yaw) => (ctx.Sources[stem], yaw);

        private static GameObject Spawn(GameObject source, Transform parent, string role, float yaw, Quaternion? baseRotation = null)
        {
            if (source == null) throw new InvalidOperationException("Нет источника для " + role);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            if (go == null) throw new InvalidOperationException("Не удалось создать экземпляр " + source.name);
            go.name = role + "__" + source.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0) * (baseRotation ?? Quaternion.identity);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            return go;
        }

        private static GameObject Prop(Ctx ctx, Transform parent, string stem, string role, Vector3 foot, float yaw, bool ground)
        {
            var go = Spawn(ctx.Sources[stem], parent, role, yaw);
            Ground(ctx, go, foot);
            if (ground) Register(ctx, role, Measure(go.transform, ctx.Root));
            Record(ctx, go, AssetDatabase.GetAssetPath(ctx.Sources[stem]), parent.name);
            return go;
        }

        // Ставит низ bounds на foot.y, центр XZ — в foot.
        private static void Ground(Ctx ctx, GameObject go, Vector3 foot)
        {
            var b = Measure(go.transform, ctx.Root);
            go.transform.localPosition += foot - new Vector3(b.center.x, b.min.y, b.center.z);
        }

        // Настенный элемент: центр X/Y и задняя (северная) грань bounds — на плоскости фасада.
        private static GameObject WallProp(Ctx ctx, Transform parent, string stem, string role, float yaw, float centerX, float centerY, float backZ)
        {
            var go = Spawn(ctx.Sources[stem], parent, role, yaw);
            var b = Measure(go.transform, ctx.Root);
            go.transform.localPosition += new Vector3(centerX - b.center.x, centerY - b.center.y, backZ - b.max.z);
            Record(ctx, go, AssetDatabase.GetAssetPath(ctx.Sources[stem]), parent.name);
            return go;
        }

        private static GameObject Block(Ctx ctx, Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent, false); go.transform.localPosition = center; go.transform.localRotation = rotation;
            go.transform.localScale = size; go.GetComponent<MeshRenderer>().sharedMaterial = material;
            Sanitize(go); Record(ctx, go, "primitive_cube_" + material.name, parent.name);
            return go;
        }

        private static void AddSocket(Ctx ctx, Transform parent, string name, Vector3 position)
        {
            var socket = new GameObject(name).transform; socket.SetParent(parent, false); socket.localPosition = position;
            LastReport.sockets.Add(new Socket { path = GroupName + "/" + parent.name + "/" + name, localPosition = position });
        }

        private static Transform Group(Ctx ctx, string name)
        {
            var t = new GameObject(name).transform; t.SetParent(ctx.Root, false);
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            return t;
        }

        private static Material LoadMaterial(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
            if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("Неверный материал: " + name);
            return material;
        }

        // ---------- Проверки ----------

        private static void ValidateShell(Ctx ctx, Transform shell, Transform entry)
        {
            var sb = Measure(shell, ctx.Root); var eb = Measure(entry, ctx.Root);
            LastReport.shell = sb; LastReport.entrance = eb;
            bool ok = Near(sb.min.x, WestFace) && Near(sb.max.x, EastFace) && Near(sb.min.z, Facade) && Near(sb.max.z, NorthFace)
                && Near(sb.min.y, 0) && Near(eb.min.x, GateMinX) && Near(eb.max.x, GateMaxX) && Near(eb.min.z, Facade)
                && Math.Abs(eb.size.y - 2.50023f) < .02f && Near(eb.min.y, 0);
            if (!ok) throw new InvalidOperationException("Изменилась согласованная геометрия склада (ожидался южный вход x[-4,-1], z=-6): shell=" + sb + " entry=" + eb);
            ctx.RoofTop = sb.max.y;
        }

        private static void Validate(Ctx ctx)
        {
            var building = MinMax(new Vector3(WestFace, 0, Facade), new Vector3(EastFace, ctx.RoofTop, NorthFace));
            foreach (var r in ctx.Root.GetComponentsInChildren<Renderer>(true))
            {
                var b = Measure(r.transform, ctx.Root, false);
                if (!Inside(b, Reservation)) throw new InvalidOperationException("Вышло из резерва входа: " + HierarchyPath(r.transform, ctx.Root));
                if (Intersects(b, Corridor) || Intersects(b, GateMouth))
                    throw new InvalidOperationException("Перекрыт проход 1.5 м/створ проёма: " + HierarchyPath(r.transform, ctx.Root));
                if (Intersects(Shrink(b, .003f), building))
                    throw new InvalidOperationException("Заходит в корпус: " + HierarchyPath(r.transform, ctx.Root));
            }
            for (int i = 0; i < ctx.Ground.Count; i++)
            {
                if (Math.Abs(ctx.Ground[i].Value.min.y) > .01f) throw new InvalidOperationException("Не стоит на грунте: " + ctx.Ground[i].Key);
                for (int j = i + 1; j < ctx.Ground.Count; j++)
                    if (HorizontalOverlap(ctx.Ground[i].Value, ctx.Ground[j].Value, .04f))
                        throw new InvalidOperationException("Пересечение групп: " + ctx.Ground[i].Key + " / " + ctx.Ground[j].Key);
            }
            var root = ctx.Root;
            LastReport.actualBounds = Measure(root, root);
            LastReport.renderers = root.GetComponentsInChildren<Renderer>(true).Length;
            LastReport.prefabInstances = root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject));
            LastReport.colliders = root.GetComponentsInChildren<Collider>(true).Length;
            LastReport.lights = root.GetComponentsInChildren<Light>(true).Length;
            LastReport.scripts = root.GetComponentsInChildren<MonoBehaviour>(true).Length;
            LastReport.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;
            LastReport.animators = root.GetComponentsInChildren<Animator>(true).Length;
            LastReport.particleSystems = root.GetComponentsInChildren<ParticleSystem>(true).Length;
            if (LastReport.colliders + LastReport.lights + LastReport.scripts + LastReport.audioSources + LastReport.animators + LastReport.particleSystems != 0)
                throw new InvalidOperationException("Вход мастерской содержит запрещённые компоненты.");
            if (LastReport.prefabInstances > InstanceBudget) throw new InvalidOperationException("Превышен бюджет экземпляров: " + LastReport.prefabInstances);
        }

        private static void Register(Ctx ctx, string role, Bounds b) => ctx.Ground.Add(new KeyValuePair<string, Bounds>(role, b));

        private static void Record(Ctx ctx, GameObject go, string source, string group)
            => LastReport.items.Add(new Item { role = go.name, source = source, group = group, bounds = Measure(go.transform, ctx.Root),
                lodLevels = go.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length) });

        // ---------- Геометрия и очистка (как в LobbyConstructionZoneBuilder) ----------

        private static Bounds Measure(Transform subtree, Transform space, bool children = true)
        {
            var result = new Bounds(); var has = false;
            var filters = children ? subtree.GetComponentsInChildren<MeshFilter>(true) : subtree.GetComponents<MeshFilter>();
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) throw new InvalidOperationException("Пустой mesh: " + filter.name);
                var b = filter.sharedMesh.bounds;
                var matrix = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
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

        private static void Sanitize(GameObject go)
        {
            if (go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidOperationException("Статичный вход не принимает skinned-источник: " + go.name);
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
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    GameObjectUtility.GetStaticEditorFlags(t.gameObject) | StaticEditorFlags.OccludeeStatic);
        }

        private static Bounds MinMax(Vector3 min, Vector3 max) { var b = new Bounds(); b.SetMinMax(min, max); return b; }
        private static Bounds Shrink(Bounds b, float d) { b.Expand(-2 * d); return b; }
        private static bool Near(float a, float b) => Math.Abs(a - b) < .035f;
        private static bool Inside(Bounds a, Bounds outer) => a.min.x >= outer.min.x && a.max.x <= outer.max.x && a.min.y >= outer.min.y
            && a.max.y <= outer.max.y && a.min.z >= outer.min.z && a.max.z <= outer.max.z;
        private static bool Intersects(Bounds a, Bounds b) => a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y
            && a.max.y > b.min.y && a.min.z < b.max.z && a.max.z > b.min.z;
        private static bool HorizontalOverlap(Bounds a, Bounds b, float gap) => a.min.x < b.max.x + gap && a.max.x > b.min.x - gap
            && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;
        private static string HierarchyPath(Transform t, Transform root) => t == root ? root.name : HierarchyPath(t.parent, root) + "/" + t.name;
    }
}
