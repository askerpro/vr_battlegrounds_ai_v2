using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Внутреннее неинтерактивное оснащение; вызывается единственным авторингом Lobby.</summary>
    public static class LobbyInteriorDecorationBuilder
    {
        public static void Build(Transform parent, Scene scene, Bounds floor)
        {
            throw new InvalidOperationException("Нужен новый Build с actual tent, полным station sweep и mirror view; прежние внешние ниши запрещены.");
        }

        [Serializable] public sealed class BuildReport
        {
            public Bounds arena, tent;
            public int placed, excluded;
            public List<string> missingSourceGuids = new List<string>();
            public List<LobbySidewallDecorationBuilder.Placement> placements = new List<LobbySidewallDecorationBuilder.Placement>();
        }
        public static BuildReport LastReport { get; private set; }

        public static void Build(Transform parent, Scene scene, Bounds arena, Bounds tent, Bounds[] stationSweeps, Bounds mirrorViewArea)
        {
            if (parent == null || !scene.IsValid() || !scene.isLoaded || parent.gameObject.scene != scene)
                throw new ArgumentException("Нужны родитель и загруженная сцена Lobby.");
            if (parent.Find("InteriorDecoration") != null)
                throw new InvalidOperationException("Группа InteriorDecoration уже есть; её заменой управляет главный авторинг.");
            LastReport = new BuildReport { arena = arena, tent = tent };
            foreach (string guid in new[] { MugGuid, ChairGuid, CaseGuid })
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)) == null)
                    LastReport.missingSourceGuids.Add(guid);
            if (LastReport.missingSourceGuids.Count > 0)
            {
                SaveInteriorReport();
                throw new InvalidOperationException("Нет обязательных импортированных источников interior: " + string.Join(", ", LastReport.missingSourceGuids));
            }
            if (stationSweeps == null || stationSweeps.Length != 2 || stationSweeps.Any(b => b.size.x <= 0 || b.size.z <= 0))
                throw new ArgumentException("Нужны два актуальных полных station sweep bounds.");
            if (Mathf.Abs(tent.size.x - arena.size.x) > .01f || Mathf.Abs(tent.size.z - (arena.size.z - 4)) > .01f)
                throw new ArgumentException("Ширина шатра равна арене; каждый торец отступает внутрь на 2 м.");
            var exclusions = new List<Bounds>(stationSweeps) { mirrorViewArea };
            // Физические колонны и окно — запретные объёмы; gameplay объекты только читаются.
            foreach (var collider in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)))
                if (collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy
                    && collider.GetComponentsInParent<Transform>(true).Any(t => t.name.StartsWith("столб", StringComparison.OrdinalIgnoreCase)
                        || t.name.IndexOf("Column", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Pillar", StringComparison.OrdinalIgnoreCase) >= 0))
                    exclusions.Add(collider.bounds);
            exclusions.Add(new Bounds(new Vector3(tent.max.x - .45f, arena.max.y + 1.80f, tent.center.z + LobbyMilitaryCampSources.SideWindowOffset(1)), new Vector3(1.05f, 1.20f, 1.60f)));
            exclusions.Add(new Bounds(new Vector3(tent.min.x + .45f, arena.max.y + 1.80f, tent.center.z + LobbyMilitaryCampSources.SideWindowOffset(-1)), new Vector3(1.05f, 1.20f, 1.60f)));
            var pockets = new List<Pocket>();
            foreach (string name in new[] { "Table_North", "Table_South", "Table_East", "Table_West" })
                pockets.AddRange(MeasurePockets(scene, arena, name));
            var palette = new Palette();
            var root = New("InteriorDecoration", parent);
            try
            {
                for (int i = 0; i < pockets.Count; i++)
                {
                    var pocket = pockets[i];
                    var group = New(pocket.Name, root.transform);
                    group.transform.SetPositionAndRotation(pocket.Bottom, pocket.Rotation);
                    // Семейство не зависит от пропусков неподдерживаемых surfaces и общего list index.
                    bool westKept = pocket.Name == "Table_West_EndB";
                    int variation = pocket.Name.StartsWith("Table_North", StringComparison.Ordinal) ? 0
                        : pocket.Name.StartsWith("Table_South", StringComparison.Ordinal) ? 1 : 2;
                    if (westKept || pocket.Name.EndsWith("EndA", StringComparison.Ordinal)) CommunicationKit(group.transform, palette, variation);
                    else MaintenanceKit(group.transform, palette, variation);
                    AcceptOrRemove(group, tent, exclusions);
                }
                var fixedTables = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.name.StartsWith("Table_", StringComparison.Ordinal) && t.gameObject.activeInHierarchy)
                    .SelectMany(t => t.GetComponentsInChildren<BoxCollider>(true)).Where(c => c.enabled && !c.isTrigger).Select(c => c.bounds).ToArray();
                var furnitureExclusions = new List<Bounds>(exclusions); furnitureExclusions.AddRange(fixedTables);
                // Явные ниши соответствуют вырезам старых столов, согласованным по actual sweep.
                // Эти напольные ниши заняты крупными шкафами нового рабочего оснащения.
                // Стулья и личный реквизит размещаются только по фактическим свободным bounds.
                LobbySidewallDecorationBuilder.Build(root.transform, arena, tent, furnitureExclusions.ToArray());
                LobbyInteriorWorkstationsBuilder.Build(root.transform, arena, tent, exclusions.ToArray());
            }
            finally
            {
                SaveInteriorReport();
            }
            GameLog.Debug.Info($"[LobbyDecoration] Внутренние наборы: {LastReport.placed}, исключено {LastReport.excluded}; actual bounds в interior/latest-build.json.");
        }

        internal static GameObject CreateDeskPersonalDetails(Transform parent, Bounds surface, bool west)
        {
            var group = New("Кофе и личные вещи", parent);
            group.transform.SetPositionAndRotation(new Vector3(surface.center.x, surface.max.y + .006f, surface.center.z - .27f), Quaternion.identity);
            var palette = new Palette();
            CoffeeCup(group.transform, palette, Vector3.zero, west ? 12 : -18);
            if (west)
                MeshProp("Небольшая оставленная сумка", group.transform, new Vector3(.18f, .06f, -.09f), new Vector3(.20f, .12f, .16f), palette.SoftBag, palette.Canvas);
            else
            {
                Box("Личный блокнот", group.transform, new Vector3(.18f, .012f, -.08f), new Vector3(.15f, .024f, .17f), palette.Black);
                Box("Карандаш", group.transform, new Vector3(.18f, .029f, -.08f), new Vector3(.008f, .008f, .12f), palette.Orange);
            }
            return group;
        }

        private static void SaveInteriorReport()
        {
            string path = Path.Combine(Application.dataPath, "../tasks/lobby-decoration/reports/implementation/interior/latest-build.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(LastReport, true));
        }

        private static bool AcceptOrRemove(GameObject item, Bounds tent, List<Bounds> exclusions)
        {
            var b = LobbySidewallDecorationBuilder.VisibleBounds(item);
            bool accepted = LobbySidewallDecorationBuilder.Inside(tent, b, .04f) && !LobbySidewallDecorationBuilder.Overlaps(b, exclusions, .055f);
            LastReport.placements.Add(new LobbySidewallDecorationBuilder.Placement { name = item.name, bounds = b, position = item.transform.position,
                lodLevels = item.GetComponentsInChildren<LODGroup>(true).Sum(l => l.GetLODs().Length), decision = accepted ? "placed" : "excluded: actual bounds выходят за шатёр или пересекают запретный объём" });
            if (accepted) { LastReport.placed++; exclusions.Add(b); }
            else { LastReport.excluded++; UnityEngine.Object.DestroyImmediate(item); }
            return accepted;
        }

        private const float HalfLength = .70f;
        private const float HalfDepth = .17f;
        private const string MugGuid = "abc00000000004222768201726487176";
        private const string ChairGuid = "abc00000000015024090355226227209";
        private const string CaseGuid = "abc00000000012017236203265661070";
        private const string MaterialFolder = "Assets/Materials/Lobby/Interior";
        private const string MeshFolder = "Assets/Art/Lobby/Interior";

        private readonly struct Pocket
        {
            public readonly string Name;
            public readonly Vector3 Bottom;
            public readonly Quaternion Rotation;
            public Pocket(string name, Vector3 bottom, Quaternion rotation)
            { Name = name; Bottom = bottom; Rotation = rotation; }
        }

        private static IEnumerable<Pocket> MeasurePockets(Scene scene, Bounds floor, string tableName)
        {
            var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == tableName && t.gameObject.activeInHierarchy).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Нужен ровно один активный " + tableName);
            var solids = matches[0].GetComponentsInChildren<BoxCollider>(true)
                .Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
            if (solids.Length == 0) throw new InvalidOperationException("Нет опорных коллайдеров " + tableName);
            float top = solids.Max(c => c.bounds.max.y);
            if (top < floor.max.y + .5f) throw new InvalidOperationException("Слишком низкая столешница " + tableName);
            var surfaces = solids.Where(c => Mathf.Abs(c.bounds.max.y - top) < .003f).Select(c => c.bounds).ToArray();
            bool side = tableName == "Table_East" || tableName == "Table_West";
            var aggregate = surfaces[0];
            foreach (var b in surfaces.Skip(1)) aggregate.Encapsulate(b);
            for (int end = 0; end < 2; end++)
            {
                // Отступ 1,15 м от внешнего края исключает наложение соседних столов в углу.
                float along = side ? (end == 0 ? aggregate.min.z + 1.15f : aggregate.max.z - 1.15f)
                    : (end == 0 ? aggregate.min.x + 1.15f : aggregate.max.x - 1.15f);
                var center = side ? new Vector3(aggregate.center.x, top, along) : new Vector3(along, top, aggregate.center.z);
                var extent = side ? new Vector3(HalfDepth, 0, HalfLength) : new Vector3(HalfLength, 0, HalfDepth);
                bool supported = surfaces.Any(b => center.x - extent.x >= b.min.x + .025f && center.x + extent.x <= b.max.x - .025f
                    && center.z - extent.z >= b.min.z + .025f && center.z + extent.z <= b.max.z - .025f);
                if (!supported)
                {
                    LastReport.placements.Add(new LobbySidewallDecorationBuilder.Placement { name = tableName, decision = "insufficient_surface: краевой набор не поддерживается текущими активными столешницами" });
                    continue;
                }
                yield return new Pocket(tableName + (end == 0 ? "_EndA" : "_EndB"), center + Vector3.up * .005f,
                    Quaternion.Euler(0, side ? 90 : 0, 0));
            }
        }

        private sealed class Palette
        {
            public readonly Material Olive, Steel, Black, Canvas, Label, Orange, Coffee;
            public readonly Mesh SoftBag, CanvasRoll;
            public Palette()
            {
                EnsureFolder(MaterialFolder);
                EnsureFolder(MeshFolder);
                Olive = MaterialAsset("OliveCase", new Color(.19f, .23f, .13f), .07f, .24f);
                Steel = MaterialAsset("WornSteel", new Color(.28f, .30f, .28f), .65f, .26f);
                Black = MaterialAsset("Rubber", new Color(.045f, .052f, .047f), 0, .12f);
                Canvas = MaterialAsset("KhakiCanvas", new Color(.39f, .34f, .22f), 0, .08f);
                Label = MaterialAsset("StencilledCream", new Color(.66f, .65f, .49f), 0, .10f);
                Orange = MaterialAsset("ToolGrip", new Color(.42f, .16f, .065f), 0, .20f);
                Coffee = MaterialAsset("Coffee", new Color(.055f, .027f, .012f), 0, .65f);
                SoftBag = MeshAsset("SmallSupplyBag", false);
                CanvasRoll = MeshAsset("RolledCanvas", true);
            }
        }

        private static void CommunicationKit(Transform parent, Palette p, int variation)
        {
            var caseRoot = New("Закрытый полевой кейс", parent).transform;
            caseRoot.localPosition = new Vector3(-.32f, 0, 0);
            caseRoot.localRotation = Quaternion.Euler(0, variation % 2 == 0 ? 2 : -3, 0);
            SourceProp("Военный кейс MilitaryCamp", caseRoot, CaseGuid, new Vector3(.55f, .23f, .29f));

            var radio = New("Полевая радиостанция", parent).transform;
            radio.localPosition = new Vector3(.34f, 0, -.025f);
            radio.localRotation = Quaternion.Euler(0, 5 - variation * 4, 0);
            Box("Корпус", radio, new Vector3(0, .095f, 0), new Vector3(.22f, .19f, .17f), p.Olive);
            Box("Панель", radio, new Vector3(0, .10f, -.09f), new Vector3(.19f, .15f, .012f), p.Black);
            for (int i = 0; i < 4; i++) Box("Решётка динамика", radio, new Vector3(-.035f, .065f + i * .022f, -.10f), new Vector3(.095f, .007f, .008f), p.Steel);
            Box("Шкала", radio, new Vector3(.06f, .14f, -.10f), new Vector3(.045f, .032f, .009f), p.Label);
            Box("Антенна", radio, new Vector3(.075f, .29f, .03f), new Vector3(.009f, .20f + variation * .012f, .009f), p.Black);
            Box("Ручка сверху", radio, new Vector3(-.015f, .208f, .02f), new Vector3(.10f, .02f, .018f), p.Black);
            if (variation % 2 == 0)
            {
                Box("Блокнот", parent, new Vector3(.59f, .012f, .03f), new Vector3(.14f, .024f, .19f), p.Canvas);
                Box("Обложка блокнота", parent, new Vector3(.59f, .025f, .03f), new Vector3(.145f, .004f, .195f), p.Black);
                Box("Карандаш", parent, new Vector3(.59f, .031f, .04f), new Vector3(.008f, .008f, .13f), p.Orange);
            }
            else CoffeeCup(parent, p, new Vector3(.59f, 0, .035f), variation * 15);
        }

        private static void MaintenanceKit(Transform parent, Palette p, int variation)
        {
            var roll = New("Свёрнутый брезент", parent).transform;
            roll.localPosition = new Vector3(-.38f, .086f, 0);
            roll.localRotation = Quaternion.Euler(0, variation % 2 == 0 ? 4 : -6, 0);
            MeshProp("Брезентовый рулон", roll, Vector3.zero, new Vector3(.36f, .16f, .16f), p.CanvasRoll, p.Canvas);
            foreach (float x in new[] { -.10f, .10f })
                MeshProp("Стяжка рулона", roll, new Vector3(x, 0, 0), new Vector3(.025f, .166f, .166f), p.CanvasRoll, p.Black);
            var tools = New("Набор инструмента", parent).transform;
            tools.localPosition = new Vector3(.03f, 0, 0);
            Box("Лоток", tools, new Vector3(0, .012f, 0), new Vector3(.25f, .024f, .27f), p.Olive);
            foreach (float z in new[] { -.133f, .133f }) Box("Борт лотка", tools, new Vector3(0, .027f, z), new Vector3(.25f, .03f, .008f), p.Steel);
            Box("Ручка отвёртки", tools, new Vector3(-.065f, .038f, -.035f), new Vector3(.032f, .027f, .08f), p.Orange);
            Box("Жало отвёртки", tools, new Vector3(-.065f, .038f, .045f), new Vector3(.01f, .01f, .08f), p.Steel);
            Box("Ключ", tools, new Vector3(.025f, .033f, 0), new Vector3(.019f, .015f, .17f), p.Steel);
            foreach (float z in new[] { -.07f, .07f })
                foreach (float x in new[] { .006f, .044f }) Box("Губка ключа", tools, new Vector3(x, .033f, z), new Vector3(.018f, .015f, .033f), p.Steel);
            var bags = New("Малые мешки снабжения", parent).transform;
            bags.localPosition = new Vector3(.44f, 0, 0);
            MeshProp("Мешок нижний", bags, new Vector3(0, .055f, 0), new Vector3(.30f, .11f, .25f), p.SoftBag, p.Canvas);
            var upper = MeshProp("Мешок верхний", bags, new Vector3(-.018f, .145f, .012f), new Vector3(.25f, .09f, .22f), p.SoftBag, p.Canvas);
            upper.transform.localRotation = Quaternion.Euler(0, variation % 2 == 0 ? 7 : -7, 0);
            Box("Перевязь", bags, new Vector3(0, .197f, 0), new Vector3(.028f, .009f, .22f), p.Olive);
        }

        private static void CoffeeCup(Transform parent, Palette p, Vector3 bottom, float angle)
        {
            var cup = New("Кружка с недопитым кофе", parent).transform;
            cup.localPosition = bottom;
            cup.localRotation = Quaternion.Euler(0, angle, 0);
            SourceProp("Каталоговая кружка", cup, MugGuid, new Vector3(.135f, .105f, .135f));
            // Ручка исходной модели смещает общий bounds по z; центр чаши смещён назад.
            var coffee = MeshProp("Кофе ниже края", cup, new Vector3(0, .073f, -.019f), new Vector3(.002f, .07f, .07f), p.CanvasRoll, p.Coffee);
            coffee.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Box("Жетон", parent, bottom + new Vector3(-.02f, .004f, -.10f), new Vector3(.023f, .006f, .035f), p.Steel);
        }

        private static GameObject ChairNiche(Transform parent, Palette p, Vector3 bottom, bool west)
        {
            var chair = New(west ? "Западная ниша отдыха" : "Восточная ниша отдыха", parent).transform;
            chair.SetPositionAndRotation(bottom, Quaternion.Euler(0, west ? 180 : 0, 0));
            SourceProp("Полевой стул MilitaryCamp", chair, ChairGuid, new Vector3(.50f, .92f, .50f));
            var pack = New("Оставленный рюкзак", chair).transform;
            pack.localPosition = new Vector3(.32f, .70f, west ? -.015f : .035f);
            MeshProp("Мягкий корпус рюкзака", pack, Vector3.zero, new Vector3(.19f, .42f, .33f), p.SoftBag, p.Olive);
            MeshProp("Наружный карман", pack, new Vector3(.077f, -.07f, 0), new Vector3(.045f, .20f, .25f), p.SoftBag, p.Canvas);
            foreach (float z in new[] { -.09f, .09f })
                Box("Лямка рюкзака", pack, new Vector3(-.09f, -.04f, z), new Vector3(.025f, .30f, .033f), p.Black);
            Box("Петля рюкзака", pack, new Vector3(0, .23f, 0), new Vector3(.02f, .04f, .09f), p.Black);
            if (west) Box("Сложенная куртка на сиденье", chair, new Vector3(-.08f, .505f, -.03f), new Vector3(.26f, .05f, .29f), p.Olive);
            return chair.gameObject;
        }

        private static GameObject ArmorNiche(Transform parent, Palette p, Vector3 bottom)
        {
            var stand = New("Ниша бронежилетов", parent).transform;
            stand.SetPositionAndRotation(bottom, Quaternion.identity);
            foreach (float z in new[] { -.33f, .33f })
            {
                Box("Опора стойки", stand, new Vector3(0, .025f, z), new Vector3(.50f, .05f, .05f), p.Black);
                Box("Вертикальная труба", stand, new Vector3(0, .77f, z), new Vector3(.025f, 1.5f, .025f), p.Steel);
            }
            Box("Перекладина", stand, new Vector3(0, 1.51f, 0), new Vector3(.03f, .03f, .69f), p.Steel);
            for (int i = 0; i < 2; i++)
            {
                var vest = New(i == 0 ? "Бронежилет на вешалке" : "Запасной бронежилет", stand).transform;
                vest.localPosition = new Vector3(i == 0 ? -.06f : .06f, .79f, i == 0 ? -.17f : .17f);
                vest.localRotation = Quaternion.Euler(0, i == 0 ? 5 : -9, 0);
                Box("Панель бронежилета", vest, new Vector3(0, .27f, 0), new Vector3(.11f, .40f, .29f), p.Olive);
                foreach (float z in new[] { -.105f, .105f })
                    Box("Плечевая лямка", vest, new Vector3(0, .535f, z), new Vector3(.075f, .20f, .065f), p.Canvas);
                Box("Пояс бронежилета", vest, new Vector3(-.01f, .11f, 0), new Vector3(.14f, .06f, .33f), p.Black);
                foreach (float z in new[] { -.095f, 0, .095f })
                    MeshProp("Подсумок жилета", vest, new Vector3(-.079f, .21f, z), new Vector3(.065f, .14f, .075f), p.SoftBag, p.Canvas);
                Box("Крепление вешалки", vest, new Vector3(0, .675f, 0), new Vector3(.012f, .10f, .012f), p.Steel);
            }
            return stand.gameObject;
        }

        private static GameObject New(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject RequirePrefab(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Не импортирован выбранный исходный prefab: " + guid);
            return prefab;
        }

        private static void SourceProp(string name, Transform parent, string guid, Vector3 maximumSize)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(RequirePrefab(guid), parent);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            // Коллайдеры исходного prefab не становятся частью физического окружения Lobby.
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            var filters = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length == 0) throw new InvalidOperationException("Нет геометрии выбранного prefab: " + guid);
            var bounds = new Bounds();
            bool first = true;
            foreach (var filter in filters)
            {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = parent.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                throw new InvalidOperationException("Некорректные габариты исходного prefab: " + guid);
            float scale = Mathf.Min(maximumSize.x / bounds.size.x, maximumSize.y / bounds.size.y, maximumSize.z / bounds.size.z);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.OccludeeStatic);
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
        }

        private static void Box(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private static GameObject MeshProp(string name, Transform parent, Vector3 center, Vector3 size, Mesh mesh, Material material)
        {
            var go = New(name, parent);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static Material MaterialAsset(string name, Color color, float metal, float smooth)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Не найден URP/Lit для интерьерного декора.");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh MeshAsset(string name, bool roll)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            // Низкополигональные объёмы без текстур и внешних зависимостей: рулон 48, мешок 80 треугольников.
            int segments = roll ? 12 : 8;
            float[] axes = roll ? new[] { -.5f, .5f } : new[] { -.5f, -.35f, 0f, .35f, .5f };
            var vertices = new List<Vector3>();
            for (int ring = 0; ring < axes.Length; ring++)
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2 / segments;
                    float radius = roll ? .5f : (ring == 0 || ring == axes.Length - 1 ? .23f : .5f);
                    vertices.Add(roll ? new Vector3(axes[ring], Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius)
                        : new Vector3(Mathf.Cos(angle) * radius, axes[ring], Mathf.Sin(angle) * radius));
                }
            var triangles = new List<int>();
            for (int ring = 0; ring < axes.Length - 1; ring++)
                for (int i = 0; i < segments; i++)
                {
                    int a = ring * segments + i, b = ring * segments + (i + 1) % segments;
                    int c = a + segments, d = b + segments;
                    if (roll) triangles.AddRange(new[] { a, b, c, b, d, c });
                    else triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            for (int end = 0; end < 2; end++)
            {
                int offset = end == 0 ? 0 : (axes.Length - 1) * segments;
                int center = vertices.Count;
                vertices.Add(roll ? new Vector3(axes[end == 0 ? 0 : axes.Length - 1], 0, 0)
                    : new Vector3(0, axes[end == 0 ? 0 : axes.Length - 1], 0));
                for (int i = 0; i < segments; i++)
                {
                    int a = offset + i, b = offset + (i + 1) % segments;
                    if ((end == 0) == roll) triangles.AddRange(new[] { center, b, a });
                    else triangles.AddRange(new[] { center, a, b });
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(parent)) throw new InvalidOperationException("Не подготовлена общая папка " + parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

    }
}
