using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Перенос станций и визуальных границ на сохранённые игровые сцены, без изменения Environment.</summary>
    public static class ArsenalMapMigration
    {
        private const string StationPath = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        private const string HousingPath = "Assets/Prefabs/Maps/ZoneBoundaryDisplayHousing.prefab";

        public static void RunMenu() => VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.OpenMaintenance(VrBattlegrounds.Editor.Arsenal.ArsenalEditorWindow.Tab.Stations);

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Редактор в Play Mode.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StationPath);
            if (prefab == null) throw new InvalidOperationException("Нет нового префаба станции.");
            var housing = BuildHousing(prefab);
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/Maps" })
                .Select(AssetDatabase.GUIDToAssetPath).Concat(new[] { "Assets/Scenes/Lobby.unity" }).ToArray();
            var previous = SceneManager.GetActiveScene();
            var report = new StringBuilder();
            foreach (var path in paths)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool loaded = scene.IsValid() && scene.isLoaded;
                if (loaded && scene.isDirty) throw new InvalidOperationException("Сначала сохранить изменения: " + path);
                if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    string backup = System.IO.Path.GetFullPath("tmp/arsenal-migration-" + scene.name + ".unity");
                    if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new InvalidOperationException("Не создана копия " + path);
                    var stationPrefab = scene.name == "Lobby"
                        ? AssetDatabase.LoadAssetAtPath<GameObject>(ArsenalPresetAssetBuilder.DemoPath)
                        : prefab;
                    if (stationPrefab == null) throw new InvalidOperationException("Нет префаба станции для " + scene.name);
                    report.AppendLine(Migrate(scene, stationPrefab, housing));
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            return report.ToString();
        }

        private static string Migrate(Scene scene, GameObject prefab, GameObject housing)
        {
            var roots = scene.GetRootGameObjects();
            var zones = roots.SelectMany(r => r.GetComponentsInChildren<TeamSpawnZone>(true)).ToArray();
            var oldWalls = roots.SelectMany(r => r.GetComponentsInChildren<ArsenalWallController>(true))
                .Where(w => w.GetComponent<ArsenalEquipmentPoses>() == null).ToArray();
            if (oldWalls.Length == 0) return UpdateLayout(scene);
            if (zones.Length == 0) throw new InvalidOperationException("Нет зоны: " + scene.name);
            // Карта под MapBootstrap не хранит координатор в сцене: его спавнит сервер при запуске.
            if (roots.Any(r => r.GetComponentInChildren<MapBootstrap>(true) != null))
                throw new InvalidOperationException("Карта " + scene.name + " под MapBootstrap: старые стены заменять вручную, координатор в сцену не кладётся.");
            var coordinatorGO = new GameObject("ArsenalEquipmentCoordinator");
            SceneManager.MoveGameObjectToScene(coordinatorGO, scene);
            coordinatorGO.transform.SetParent(MapGameplayHierarchy.GameplayRoot(scene), false);
            coordinatorGO.AddComponent<NetworkIdentity>();
            var coordinator = coordinatorGO.AddComponent<ArsenalBoundaryWall>();
            var stations = new List<ArsenalDeploymentAnimator>();
            var replacements = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            Bounds folded = prefab.GetComponent<ArsenalEquipmentPoses>().FoldedBoundsLocal;
            foreach (var old in oldWalls)
            {
                var zone = zones.OrderBy(z => Vector3.SqrMagnitude(z.transform.position - old.transform.position)).First();
                var box = zone.GetComponent<BoxCollider>();
                Vector3 center = zone.transform.TransformPoint(box.center);
                Vector3 local = zone.transform.InverseTransformPoint(old.transform.position) - box.center;
                Vector3 axis = zone.HomeTeam != null ? Vector3.forward : Mathf.Abs(local.x / box.size.x) > Mathf.Abs(local.z / box.size.z)
                    ? Vector3.right * Mathf.Sign(local.x) : Vector3.forward * Mathf.Sign(local.z);
                Vector3 outward = zone.transform.TransformDirection(axis).normalized;
                Vector3 extents = box.size * .5f;
                float support = Mathf.Abs(Vector3.Dot(outward, zone.transform.TransformVector(Vector3.right * extents.x)))
                    + Mathf.Abs(Vector3.Dot(outward, zone.transform.TransformVector(Vector3.forward * extents.z)));
                Vector3 plane = center + outward * support;
                var station = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                station.name = old.name;
                station.transform.SetParent(MapGameplayHierarchy.ArsenalParent(scene, zone), true);
                station.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
                Vector3 position = old.transform.position;
                // Корень переднего края касается границы; значение соответствует образцу пользователя в Lobby.
                position += outward * (Vector3.Dot(plane - position, outward) + .008f);
                station.transform.position = position;
                var anchor = station.GetComponent<ArsenalStationAnchor>();
                var standing = anchor.StandingPoint.position;
                standing += outward * (Vector3.Dot(plane - standing, outward) - .6f);
                standing.y = old.transform.position.y;
                anchor.StandingPoint.position = standing;
                anchor.Configure(station.GetComponent<ArsenalWallController>(), zone, anchor.StandingPoint, station.transform.Find("ArenaFacing"));
                stations.Add(station.GetComponent<ArsenalDeploymentAnimator>());
                replacements[old.gameObject] = station;
                replacements[old.transform] = station.transform;
                foreach (var component in old.GetComponents<Component>())
                {
                    var replacement = station.GetComponent(component.GetType());
                    if (replacement != null) replacements[component] = replacement;
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(station.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(anchor.StandingPoint);
            }
            // Внешние ссылки на корни сохраняем; неизвестную ссылку на удаляемую деталь нельзя молча обнулить.
            foreach (var component in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)))
            {
                if (component == null || oldWalls.Any(w => component.transform.IsChildOf(w.transform))) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    if (value == null) continue;
                    UnityEngine.Object replacement;
                    if (replacements.TryGetValue(value, out replacement)) { property.objectReferenceValue = replacement; changed = true; }
                    else
                    {
                        var referenced = value as Component;
                        var go = value as GameObject;
                        Transform target = referenced != null ? referenced.transform : go != null ? go.transform : null;
                        if (target != null && oldWalls.Any(w => target.IsChildOf(w.transform)))
                            throw new InvalidOperationException("Неизвестная внешняя ссылка: " + component.name + "." + property.propertyPath);
                    }
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var old in oldWalls) UnityEngine.Object.DestroyImmediate(old.gameObject);
            coordinator.Configure(stations.ToArray(), 1.35f);
            foreach (var station in stations) PrefabUtility.RecordPrefabInstancePropertyModifications(station);
            foreach (var zone in zones)
            {
                var screens = zone.GetComponent<LaserGridScreens>();
                if (screens != null) { screens.ConfigureHousing(housing); PrefabUtility.RecordPrefabInstancePropertyModifications(screens); }
            }
            UpdateLayout(scene);
            return scene.name + ": заменено " + oldWalls.Length + " станций, " + zones.Length + " визуальных границ.";
        }

        public static void RebuildCurrent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            VrBattlegrounds.Core.GameLog.Arsenal.Info(UpdateLayout(SceneManager.GetActiveScene()));
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        public static string UpdateLayout(Scene scene)
        {
            var anchors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalStationAnchor>(true)).ToArray();
            foreach (var anchor in anchors)
            {
                if (anchor.GetComponentInChildren<SpawnZoneBoundaryOpening>(true) == null)
                {
                    var go = new GameObject("BoundaryOpening");
                    go.transform.SetParent(anchor.transform, false);
                    var bounds = anchor.RaisedBoundsWorld;
                    var local = new Bounds(anchor.transform.InverseTransformPoint(bounds.center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        local.Encapsulate(anchor.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.center = local.center; box.size = local.size;
                    go.AddComponent<SpawnZoneBoundaryOpening>();
                }
            }
            // Сегменты проекции — часть зоны, а не служебного координатора: у карты реестра координатора
            // в сцене нет (его спавнит MapBootstrap). Прежние сегменты находятся по ссылкам визуала зоны;
            // старые сегменты под сценовым координатором стендов — по имени, как раньше.
            foreach (var legacy in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalBoundaryWall>(true)))
                foreach (var transform in legacy.GetComponentsInChildren<Transform>(true)
                    .Where(t => t != legacy.transform && t.name.StartsWith("ZoneBoundary_", StringComparison.Ordinal)).ToArray())
                    UnityEngine.Object.DestroyImmediate(transform.gameObject);
            foreach (var zone in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TeamSpawnZone>(true)))
            {
                RemoveBoundarySegments(zone);
                BuildBoundary(zone);
            }
            return ArsenalWallOpenings.Apply(scene);
        }

        /// <summary>
        /// Контейнер сегментов проекции границы зоны: в группе базы рядом с TeamSpawnZone, identity,
        /// вне масштаба зоны и вне служебных объектов. Создаётся только для зоны, у которой есть сегменты.
        /// </summary>
        public static Transform BoundaryContainer(TeamSpawnZone zone)
        {
            Transform group = zone.transform.parent;
            if (group == null) throw new InvalidOperationException("Зона без группы базы: " + zone.name);
            if ((group.lossyScale - Vector3.one).sqrMagnitude > 1e-6f)
                throw new InvalidOperationException("Группа базы " + group.name + " масштабирована: сегменты границы исказятся.");
            string name = zone.name + ".Boundary";
            Transform container = group.Find(name);
            if (container == null)
            {
                container = new GameObject(name).transform;
                container.SetParent(group, false);
            }
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
            return container;
        }

        /// <summary>Удалить сегменты проекции зоны по ссылкам её визуала и опустевший контейнер.</summary>
        public static void RemoveBoundarySegments(TeamSpawnZone zone)
        {
            var visual = zone.GetComponent<SpawnZoneBoundaryVisual>();
            if (visual != null)
                foreach (var renderer in visual.Renderers.ToArray())
                    if (renderer != null) UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            Transform container = zone.transform.parent != null ? zone.transform.parent.Find(zone.name + ".Boundary") : null;
            if (container != null && container.childCount == 0) UnityEngine.Object.DestroyImmediate(container.gameObject);
        }

        public static void BuildBoundary(TeamSpawnZone zone)
        {
            Transform parent = null;
            var box = zone.GetComponent<BoxCollider>();
            Vector3 center = box.center;
            Vector3 e = box.size * .5f;
            var corners = new[] { new Vector3(-e.x, 0, -e.z), new Vector3(e.x, 0, -e.z),
                new Vector3(e.x, 0, e.z), new Vector3(-e.x, 0, e.z) };
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/ArsenalBoundary/WeldedWireSection.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/ArsenalBoundary/ZoneBoundaryProjection.mat");
            if (mesh == null || material == null) throw new InvalidOperationException("Нет ассетов проекции границы.");
            var renderers = new List<Renderer>();
            var volumes = zone.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SpawnZoneBoundaryOpening>(true)).ToArray();
            // Командная зона: только передняя грань (локальная -Z), обращённая к центру арены. Лобби без проекции.
            for (int side = 0; side < (zone.HomeTeam != null ? 1 : 0); side++)
            {
                Vector3 start = zone.transform.TransformPoint(center + corners[side]);
                Vector3 end = zone.transform.TransformPoint(center + corners[(side + 1) % 4]);
                start.y = end.y = zone.transform.position.y;
                Vector3 direction = (end - start).normalized;
                float length = Vector3.Distance(start, end);
                var openings = new List<Vector2>();
                foreach (var volume in volumes)
                {
                    Vector3 normal = Vector3.Cross(direction, Vector3.up);
                    var bounds = volume.WorldBounds;
                    Vector3 b = bounds.extents;
                    float thickness = Mathf.Abs(normal.x) * b.x + Mathf.Abs(normal.z) * b.z;
                    if (Mathf.Abs(Vector3.Dot(normal, bounds.center - start)) > thickness) continue;
                    float half = Mathf.Abs(direction.x) * b.x + Mathf.Abs(direction.z) * b.z;
                    float middle = Vector3.Dot(bounds.center - start, direction);
                    openings.Add(new Vector2(Mathf.Clamp(middle - half - .03f, 0, length), Mathf.Clamp(middle + half + .03f, 0, length)));
                }
                var spans = new List<Vector2>();
                float cursor = 0;
                foreach (var opening in openings.OrderBy(o => o.x))
                {
                    if (opening.x > cursor) spans.Add(new Vector2(cursor, opening.x));
                    cursor = Mathf.Max(cursor, opening.y);
                }
                if (cursor < length) spans.Add(new Vector2(cursor, length));
                int index = 0;
                foreach (var span in spans)
                {
                    int count = Mathf.CeilToInt((span.y - span.x) / 2f);
                    float width = (span.y - span.x) / count;
                    for (int i = 0; i < count; i++)
                    {
                    var go = new GameObject("ZoneBoundary_" + zone.transform.parent.name + "_" + side + "_" + i);
                    if (parent == null) parent = BoundaryContainer(zone);
                    go.transform.SetParent(parent, false);
                    go.name += "_" + index++;
                    go.transform.position = start + direction * (span.x + (i + .5f) * width);
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.right, direction);
                    go.transform.localScale = new Vector3(width / 2f, 1f, 1f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderers.Add(renderer);
                    }
                }
            }
            var visual = zone.GetComponent<SpawnZoneBoundaryVisual>() ?? zone.gameObject.AddComponent<SpawnZoneBoundaryVisual>();
            visual.Configure(zone, renderers.ToArray(), true);
            var legacy = zone.GetComponent<MeshRenderer>();
            if (legacy != null) { legacy.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(legacy); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
        }

        private static GameObject BuildHousing(GameObject station)
        {
            var source = station.GetComponentsInChildren<Transform>(true).First(t => t.name == "WalletDisplayHousing");
            var root = UnityEngine.Object.Instantiate(source.gameObject);
            try
            {
                root.name = "ZoneBoundaryDisplayHousing";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                foreach (var text in root.GetComponentsInChildren<TextMeshPro>(true)) UnityEngine.Object.DestroyImmediate(text.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, HousingPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(HousingPath);
        }
    }
}
