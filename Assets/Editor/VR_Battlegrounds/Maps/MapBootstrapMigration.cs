using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Перевод карт реестра на MapBootstrap. Один проход: служебные префабы и каталог, привязки станций,
    /// MapRoot в каждой сцене, удаление сценовых MapReferee и координатора, запечка отпечатков, preflight.
    ///
    /// <para>
    /// <see cref="DryRun"/> работает в preview-сценах и ничего не сохраняет. <see cref="Apply"/> сохраняет
    /// только затронутые сцены и assets (без глобального SaveAssets). Повторный запуск идемпотентен:
    /// существующие ключи станций и MapRoot сохраняются, GUID не пересоздаются.
    /// </para>
    /// </summary>
    public static class MapBootstrapMigration
    {
        public const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        public const string CoordinatorPath = "Assets/Prefabs/Managers/ArsenalEquipmentCoordinator.prefab";
        private const string RefereePath = "Assets/Prefabs/Managers/MapReferee.prefab";
        private const string ManagersPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string RegistryPath = "Assets/Data/Maps/MapRegistry.asset";
        private const string ModesPath = "Assets/Data/GameModes/GameModeRegistry.asset";
        private const string ReportDirectory = "Docs/tasks/report/map-runtime-bootstrap/details/";
        private static readonly string[] StationPrefabPaths =
        {
            "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab",
            "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab",
        };

        private const string Menu = "Tools/VR Battlegrounds/Maps/Map Bootstrap/";

        [MenuItem(Menu + "Migration Dry Run")]
        private static void DryRunMenu() => Core.GameLog.Debug.Info("[MapBootstrapMigration] " + Newtonsoft.Json.JsonConvert.SerializeObject(DryRun()));

        [MenuItem(Menu + "Apply Migration")]
        private static void ApplyMenu() => Core.GameLog.Debug.Info("[MapBootstrapMigration] " + Newtonsoft.Json.JsonConvert.SerializeObject(Apply()));

        [MenuItem(Menu + "Rebake Catalog")]
        private static void RebakeMenu() => Core.GameLog.Debug.Info("[MapBootstrapMigration] " + Newtonsoft.Json.JsonConvert.SerializeObject(RebakeCatalog()));

        /// <summary>Пробный проход в preview-сценах: что изменится и какие проверки MapRoot не пройдут.</summary>
        public static object DryRun()
        {
            EnsureIdle();
            var maps = new List<object>();
            var failures = new List<string>();
            foreach (MapData map in RegistryMaps())
            {
                string path = ScenePath(map.sceneName);
                if (path == null) { failures.Add("Scene.Missing:" + map.sceneName); continue; }
                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var actions = new List<string>();
                    var errors = MigrateScene(scene, map, actions, dryRun: true);
                    failures.AddRange(errors.Select(e => map.sceneName + "/" + e));
                    maps.Add(new { scene = map.sceneName, kind = IntendedKind(map).ToString(), actions, errors });
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            return Report("migration-dry-run.json", failures, new { passed = failures.Count == 0, failures, maps });
        }

        /// <summary>Миграция: префабы, каталог, сцены реестра, запечка и preflight.</summary>
        public static object Apply()
        {
            EnsureIdle();
            var failures = new List<string>();
            var maps = new List<object>();

            GameObject coordinator = EnsureCoordinatorPrefab();
            foreach (string path in StationPrefabPaths) EnsureStationPrefabBinding(path);
            MapRuntimeCatalog catalog = EnsureCatalogAsset(coordinator);
            InstallInManagers(coordinator, catalog);
            // SaveAsPrefabAsset пишет _assetId: 0; без канона нормализатор допишет его отложенно
            // (delayCall) — уже после аренды, прямо в возврат worker. Записываем канон сразу.
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(
                StationPrefabPaths.Concat(new[] { CoordinatorPath, ManagersPath }), log: false);

            foreach (MapData map in RegistryMaps())
            {
                SetKind(map, IntendedKind(map));
                string path = ScenePath(map.sceneName);
                if (path == null) { failures.Add("Scene.Missing:" + map.sceneName); continue; }
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var actions = new List<string>();
                var errors = MigrateScene(scene, map, actions, dryRun: false);
                failures.AddRange(errors.Select(e => map.sceneName + "/" + e));
                if (!EditorSceneManager.SaveScene(scene)) failures.Add("Scene.SaveFailed:" + map.sceneName);
                maps.Add(new { scene = map.sceneName, actions, errors });
            }

            object rebake = RebakeCatalog();
            return Report("migration-apply.json", failures, new { passed = failures.Count == 0, failures, maps, rebake });
        }

        /// <summary>
        /// Пересчитать отпечатки содержимого карт в каталоге и прогнать preflight. Нужен после любой
        /// правки сцены карты, MapData или реестра режимов; сборка вызывает его сама.
        /// </summary>
        public static object RebakeCatalog() => RebakeCatalog(out _);

        public static object RebakeCatalog(out bool passed)
        {
            passed = false;
            EnsureIdle();
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            if (catalog == null) return new { passed = false, failures = new[] { "Catalog.Missing:" + CatalogPath } };
            var modes = AssetDatabase.LoadAssetAtPath<GameModeRegistry>(ModesPath);
            var so = new SerializedObject(catalog);
            SerializedProperty content = so.FindProperty("_content");
            MapData[] maps = RegistryMaps().ToArray();
            content.arraySize = maps.Length;
            for (int i = 0; i < maps.Length; i++)
            {
                string path = ScenePath(maps[i].sceneName) ?? string.Empty;
                SerializedProperty entry = content.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Map").objectReferenceValue = maps[i];
                entry.FindPropertyRelative("ScenePath").stringValue = path;
                entry.FindPropertyRelative("ContentFingerprint").stringValue =
                    path.Length > 0 ? MapRunPreflight.ContentFingerprint(path, maps[i], modes) : string.Empty;
                entry.FindPropertyRelative("ArsenalFingerprint").stringValue =
                    maps[i].arsenalPreset != null ? MapRunPreflight.ArsenalFingerprint(maps[i].arsenalPreset) : string.Empty;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(catalog);
            return MapRunPreflight.Validate(catalog, RegisteredPrefabs(), out passed);
        }

        // ── Сцена ────────────────────────────────────────────────────────────

        private static List<string> MigrateScene(Scene scene, MapData map, List<string> actions, bool dryRun)
        {
            var errors = new List<string>();

            var bindings = new List<ArsenalStationCompositionBinding>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            ArsenalWallController[] walls = InScene<ArsenalWallController>(scene)
                .OrderBy(w => w.TryGetComponent(out NetworkIdentity ni) ? ni.sceneId : 0UL).ToArray();
            foreach (ArsenalWallController wall in walls)
            {
                ArsenalStationCompositionBinding binding = wall.GetComponent<ArsenalStationCompositionBinding>();
                if (binding == null)
                {
                    binding = wall.gameObject.AddComponent<ArsenalStationCompositionBinding>();
                    actions.Add("Station.Binding.Added:" + wall.name);
                }
                string key = ConfigureBinding(binding, wall, map.sceneName, keys);
                actions.Add("Station.Key:" + key);
                bindings.Add(binding);
            }

            float duration = 1f;
            foreach (ArsenalBoundaryWall coordinator in InScene<ArsenalBoundaryWall>(scene))
            {
                duration = new SerializedObject(coordinator).FindProperty("_duration").floatValue;
                string name = coordinator.name;
                RehomeBoundarySegments(scene, coordinator.transform, actions);
                if (TryRemove(coordinator.gameObject, errors))
                    actions.Add("Coordinator.Removed:" + name + $" (duration {duration:0.##})");
            }
            foreach (MapReferee referee in InScene<MapReferee>(scene))
            {
                string name = referee.name;
                if (TryRemove(referee.gameObject, errors)) actions.Add("Referee.Removed:" + name);
            }

            MapRoot root = InScene<MapRoot>(scene).FirstOrDefault();
            if (root == null)
            {
                var go = new GameObject("MapRoot");
                SceneManager.MoveGameObjectToScene(go, scene);
                root = go.AddComponent<MapRoot>();
                actions.Add("MapRoot.Created");
            }
            if (!root.TryGetComponent(out MapBootstrap bootstrap))
            {
                bootstrap = root.gameObject.AddComponent<MapBootstrap>();
                actions.Add("MapBootstrap.Added");
            }
            root.transform.SetParent(null, false);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            GameObject environment = SingleRoot(scene, "Environment", errors);
            GameObject gameplay = SingleRoot(scene, "Gameplay", errors);
            NormalizeGroupRoot(environment, actions, errors);
            NormalizeGroupRoot(gameplay, actions, errors);

            var rootSo = new SerializedObject(root);
            rootSo.FindProperty("_map").objectReferenceValue = map;
            rootSo.FindProperty("_environment").objectReferenceValue = environment;
            rootSo.FindProperty("_gameplay").objectReferenceValue = gameplay;
            PhysicalArenaLayout[] layouts = InScene<PhysicalArenaLayout>(scene);
            if (layouts.Length > 1) errors.Add("PhysicalArenaLayout.Ambiguous");
            rootSo.FindProperty("_layout").objectReferenceValue = layouts.FirstOrDefault();
            SetArray(rootSo.FindProperty("_zones"), InScene<TeamSpawnZone>(scene));
            SetArray(rootSo.FindProperty("_stations"), bindings.ToArray());
            rootSo.ApplyModifiedPropertiesWithoutUndo();

            var bootstrapSo = new SerializedObject(bootstrap);
            bootstrapSo.FindProperty("_deploymentDuration").floatValue = Mathf.Max(0.1f, duration);
            bootstrapSo.ApplyModifiedPropertiesWithoutUndo();

            // Карта, мигрированная до переноса сегментов, потеряла проекцию границы вместе с координатором.
            // Сегменты генерируемые: строим заново тем же генератором, а не восстанавливаем копии.
            TeamSpawnZone[] broken = BrokenBoundaryZones(scene);
            if (broken.Length > 0)
            {
                string zones = string.Join(", ", broken.Select(z => z.name));
                if (dryRun) actions.Add("ZoneBoundary.WillRegenerate:" + zones);
                else
                {
                    ArsenalMapMigration.UpdateLayout(scene);
                    actions.Add("ZoneBoundary.Regenerated:" + zones);
                    if (BrokenBoundaryZones(scene).Length > 0) errors.Add("ZoneBoundary.RegenerateFailed:" + zones);
                }
            }

            MapRootValidation validation = root.ValidateBindings();
            errors.AddRange(validation.Errors);
            if (!EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.MarkSceneDirty(scene);
            return errors;
        }

        private static string ConfigureBinding(ArsenalStationCompositionBinding binding, ArsenalWallController wall,
            string sceneName, HashSet<string> keys)
        {
            var so = new SerializedObject(binding);
            SerializedProperty keyProperty = so.FindProperty("_stationKey");
            string key = keyProperty.stringValue;
            if (string.IsNullOrWhiteSpace(key) || keys.Contains(key))
            {
                // Ключ назначается один раз и дальше хранится сериализованным: переименование объекта
                // или зоны его не меняет. Зона базы делает ключ читаемым: "TestMap1.MilitaryZone.ArsenalWall-1".
                var anchor = wall.GetComponent<ArsenalStationAnchor>();
                string zone = anchor != null && anchor.Zone != null ? Slug(anchor.Zone.name) + "." : string.Empty;
                string stem = sceneName + "." + zone + Slug(wall.name);
                key = stem;
                for (int i = 2; keys.Contains(key); i++) key = stem + "-" + i;
                keyProperty.stringValue = key;
            }
            keys.Add(key);
            so.FindProperty("_mode").enumValueIndex = (int)ArsenalCompositionMode.Authored;
            AssignOwnerRefs(so, wall);
            so.ApplyModifiedPropertiesWithoutUndo();
            return key;
        }

        private static string Slug(string name)
        {
            string raw = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            while (raw.Contains("--")) raw = raw.Replace("--", "-");
            return raw.Trim('-');
        }

        private static void AssignOwnerRefs(SerializedObject so, Component station)
        {
            so.FindProperty("_controller").objectReferenceValue = station.GetComponent<ArsenalWallController>();
            so.FindProperty("_authoredBinding").objectReferenceValue = station.GetComponent<ArsenalStationPresetBinding>();
            so.FindProperty("_stationAnchor").objectReferenceValue = station.GetComponent<ArsenalStationAnchor>();
            so.FindProperty("_equipmentPoses").objectReferenceValue = station.GetComponent<ArsenalEquipmentPoses>();
            so.FindProperty("_stationIdentity").objectReferenceValue = station.GetComponent<NetworkIdentity>();
        }

        /// <summary>
        /// Сегменты проекции границы зоны жили под сценовым координатором. Они принадлежат зоне
        /// (на них ссылается её SpawnZoneBoundaryVisual), поэтому переезжают в контейнер группы базы
        /// с сохранением мировых поз, а не удаляются вместе со служебным объектом.
        /// </summary>
        private static void RehomeBoundarySegments(Scene scene, Transform service, List<string> actions)
        {
            foreach (SpawnZoneBoundaryVisual visual in InScene<SpawnZoneBoundaryVisual>(scene))
            {
                TeamSpawnZone zone = visual.Zone != null ? visual.Zone : visual.GetComponent<TeamSpawnZone>();
                if (zone == null) continue;
                int moved = 0;
                foreach (Renderer renderer in visual.Renderers)
                {
                    if (renderer == null || !renderer.transform.IsChildOf(service)) continue;
                    renderer.transform.SetParent(ArsenalMapMigration.BoundaryContainer(zone), true);
                    moved++;
                }
                if (moved > 0) actions.Add($"ZoneBoundary.Moved:{zone.name} ({moved})");
            }
        }

        /// <summary>Зоны команды, у которых проекция границы потеряна: нет визуала, сегментов или ссылки пусты.</summary>
        private static TeamSpawnZone[] BrokenBoundaryZones(Scene scene) =>
            InScene<TeamSpawnZone>(scene).Where(zone =>
            {
                if (zone.HomeTeam == null) return false;
                var visual = zone.GetComponent<SpawnZoneBoundaryVisual>();
                return visual == null || visual.Renderers.Count == 0 || visual.Renderers.Any(r => r == null);
            }).ToArray();

        private static bool TryRemove(GameObject go, List<string> errors)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                errors.Add("LegacyService.InsideForeignPrefab:" + go.name);
                return false;
            }
            // Служебный объект удаляется только со своими детьми. Чужой ребёнок (не из его префаба)
            // принадлежит кому-то ещё: молча удалить его — потерять чужую сущность.
            foreach (Transform child in go.transform)
            {
                bool own = PrefabUtility.IsPartOfPrefabInstance(child.gameObject) &&
                           !PrefabUtility.IsAddedGameObjectOverride(child.gameObject);
                if (own) continue;
                errors.Add($"LegacyService.ForeignChild:{go.name}/{child.name}");
                return false;
            }
            UnityEngine.Object.DestroyImmediate(go);
            return true;
        }

        /// <summary>
        /// Группирующий корень — identity (правило иерархии сцены). Поворот или сдвиг корня переносится
        /// в его прямых детей с сохранением мировых поз: геометрия и игровые объекты не сдвигаются.
        /// Масштаб корня не переносится — это решение автора карты, миграция отказывает.
        /// </summary>
        private static void NormalizeGroupRoot(GameObject group, List<string> actions, List<string> errors)
        {
            if (group == null) return;
            Transform root = group.transform;
            if ((root.localScale - Vector3.one).sqrMagnitude > .000001f)
            {
                errors.Add(group.name + ".Scale.NotIdentity");
                return;
            }
            if (root.localPosition.sqrMagnitude < .000001f && Quaternion.Angle(root.localRotation, Quaternion.identity) < .001f)
                return;

            var children = new List<Transform>();
            foreach (Transform child in root) children.Add(child);
            var poses = children.Select(c => (c.position, c.rotation)).ToArray();
            string before = $"pos {root.localPosition}, rot {root.localEulerAngles}";
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            for (int i = 0; i < children.Count; i++) children[i].SetPositionAndRotation(poses[i].position, poses[i].rotation);
            actions.Add($"{group.name}.Normalized: {before} → identity, детей {children.Count}, мировые позы сохранены");
        }

        private static GameObject SingleRoot(Scene scene, string name, List<string> errors)
        {
            GameObject[] roots = scene.GetRootGameObjects().Where(g => g.name == name).ToArray();
            if (roots.Length != 1) errors.Add(name + ".RootCount:" + roots.Length);
            return roots.FirstOrDefault();
        }

        // ── Assets ───────────────────────────────────────────────────────────

        private static GameObject EnsureCoordinatorPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CoordinatorPath);
            if (existing != null) return existing;
            var go = new GameObject("ArsenalEquipmentCoordinator");
            try
            {
                go.AddComponent<NetworkIdentity>();
                go.AddComponent<ArsenalBoundaryWall>();
                return PrefabUtility.SaveAsPrefabAsset(go, CoordinatorPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void EnsureStationPrefabBinding(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!contents.TryGetComponent(out ArsenalWallController wall))
                    throw new InvalidOperationException("Station prefab без ArsenalWallController: " + path);
                var binding = contents.GetComponent<ArsenalStationCompositionBinding>() ??
                              contents.AddComponent<ArsenalStationCompositionBinding>();
                var so = new SerializedObject(binding);
                so.FindProperty("_mode").enumValueIndex = (int)ArsenalCompositionMode.Authored;
                AssignOwnerRefs(so, wall);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static MapRuntimeCatalog EnsureCatalogAsset(GameObject coordinator)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MapRuntimeCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var so = new SerializedObject(catalog);
            so.FindProperty("_maps").objectReferenceValue = AssetDatabase.LoadAssetAtPath<MapRegistry>(RegistryPath);
            so.FindProperty("_modes").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameModeRegistry>(ModesPath);
            so.FindProperty("_refereePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(RefereePath).GetComponent<MapReferee>();
            so.FindProperty("_coordinatorPrefab").objectReferenceValue = coordinator.GetComponent<ArsenalBoundaryWall>();
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(catalog);
            return catalog;
        }

        private static void InstallInManagers(GameObject coordinator, MapRuntimeCatalog catalog)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(ManagersPath);
            try
            {
                var manager = contents.GetComponentInChildren<GameNetworkManager>(true);
                if (manager == null) throw new InvalidOperationException("В " + ManagersPath + " нет GameNetworkManager.");
                if (!manager.spawnPrefabs.Contains(coordinator)) manager.spawnPrefabs.Add(coordinator);
                var so = new SerializedObject(manager);
                so.FindProperty("_mapRuntimeCatalog").objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, ManagersPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void SetKind(MapData map, MapRunKind kind)
        {
            if (map.kind == kind) return;
            var so = new SerializedObject(map);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(map);
        }

        private static MapRunKind IntendedKind(MapData map)
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(RegistryPath);
            if (registry != null && registry.lobby == map) return MapRunKind.Lobby;
            return map.debugOnly ? MapRunKind.Debug : MapRunKind.Combat;
        }

        // ── Общее ────────────────────────────────────────────────────────────

        internal static IReadOnlyList<GameObject> RegisteredPrefabs()
        {
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPath);
            var manager = managers != null ? managers.GetComponentInChildren<GameNetworkManager>(true) : null;
            return manager != null ? manager.spawnPrefabs : null;
        }

        private static IEnumerable<MapData> RegistryMaps()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(RegistryPath);
            return registry != null ? registry.maps.Where(m => m != null) : Enumerable.Empty<MapData>();
        }

        private static string ScenePath(string sceneName)
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (Path.GetFileNameWithoutExtension(scene.path) == sceneName) return scene.path;
            return null;
        }

        private static T[] InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();

        private static void SetArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Миграция карт требует idle Editor.");
        }

        /// <summary>Полный отчёт — в файл; наружу только статус, счётчик и до 10 примеров.</summary>
        private static object Report(string file, List<string> failures, object report)
        {
            Directory.CreateDirectory(ReportDirectory);
            string path = ReportDirectory + file;
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            return new { passed = failures.Count == 0, failureCount = failures.Count, failures = failures.Take(10).ToArray(), reportPath = path };
        }
    }
}
