using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Контракт и перенос игровых объектов из общего окружения в собственную ветку карты.</summary>
    public static class MapGameplayHierarchy
    {
        public const string EnvironmentPath = "Assets/Prefabs/Arenas/Nalchick/Tolstogo_street/Environment.prefab";
        private static readonly string[] ScenePaths =
        {
            "Assets/Scenes/Lobby.unity", "Assets/Scenes/Maps/TestMap1.unity",
            "Assets/Scenes/Maps/TestMap2.unity", "Assets/Scenes/Maps/TestMap3.unity",
            "Assets/Scenes/Maps/ReferenceMap04.unity", "Assets/Scenes/Maps/ServiceYard.unity"
        };

        public static Transform GameplayRoot(Scene scene, bool undo = false)
        {
            var existing = scene.GetRootGameObjects().Where(g => g.name == "Gameplay").ToArray();
            if (existing.Length > 1) throw new InvalidOperationException("Несколько корней Gameplay: " + scene.path);
            if (existing.Length == 1) return existing[0].transform;
            var root = new GameObject("Gameplay");
            SceneManager.MoveGameObjectToScene(root, scene);
            if (undo) Undo.RegisterCreatedObjectUndo(root, "Создать игровые объекты карты");
            return root.transform;
        }

        /// <summary>При создании зоны выбранное окружение никогда не становится её родителем.</summary>
        public static Transform SpawnParent(Scene scene, GameObject context)
        {
            var gameplay = GameplayRoot(scene, true);
            if (context != null && context.scene == scene && context.transform.IsChildOf(gameplay)
                && context.GetComponentInParent<TeamSpawnZone>(true) == null
                && context.GetComponentInParent<ArsenalWallController>(true) == null)
                return context.transform;
            var group = gameplay.Find("SpawnZones");
            if (group != null) return group;
            var go = new GameObject("SpawnZones");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Создать группу спавнов");
            go.transform.SetParent(gameplay, false);
            return go.transform;
        }

        /// <summary>Станции стоят рядом с зоной, сохраняя единичный масштаб общей группы.</summary>
        public static Transform ArsenalParent(Scene scene, TeamSpawnZone zone)
        {
            var gameplay = GameplayRoot(scene);
            if (zone == null || zone.gameObject.scene != scene || !zone.transform.IsChildOf(gameplay))
                throw new InvalidOperationException("Сначала перенесите зоны в Gameplay: " + scene.path);
            var baseGroup = zone.transform.parent;
            var group = baseGroup.Find("Arsenals");
            if (group != null) return group;
            var go = new GameObject("Arsenals");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(baseGroup, false);
            return go.transform;
        }

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Validate Map Hierarchy")]
        private static void ValidateMenu() => VrBattlegrounds.Core.GameLog.Debug.Info(ValidateAll());

        /// <summary>Проверяет сохранённые сцены в изолированных preview-сценах, не сохраняет рабочую сцену.</summary>
        public static string ValidateAll()
        {
            var errors = new List<string>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            if (asset == null) throw new InvalidOperationException("Нет префаба Environment.");
            ValidateEnvironment(asset, errors, EnvironmentPath);
            foreach (string path in ScenePaths)
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try { ValidateScene(scene, errors); }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            return "PASS: Environment и 6 сцен; игровые объекты принадлежат карте, ссылки станций корректны.";
        }

        private static void ValidateEnvironment(GameObject root, List<string> errors, string label)
        {
            foreach (var c in root.GetComponentsInChildren<Component>(true))
                if (c is TeamSpawnZone || c is ArsenalWallController || c is ArsenalSlotController
                    || c is UxrGrabbableObject || c is ShootingTarget || c is NetworkIdentity
                    || c is Rigidbody || c is ArsenalStationAnchor || c is SpawnZoneBoundaryVisual)
                    errors.Add(label + ": интерактивный объект в Environment: " + c.name);
        }

        private static void ValidateScene(Scene scene, List<string> errors)
        {
            var roots = scene.GetRootGameObjects();
            var gameplay = roots.SingleOrDefault(g => g.name == "Gameplay");
            if (gameplay == null) { errors.Add(scene.path + ": нет Gameplay"); return; }
            foreach (var environment in roots.Where(g => g.name == "Environment"))
                ValidateEnvironment(environment, errors, scene.path);
            var zones = roots.SelectMany(g => g.GetComponentsInChildren<TeamSpawnZone>(true)).ToArray();
            var walls = roots.SelectMany(g => g.GetComponentsInChildren<ArsenalWallController>(true)).ToArray();
            int expectedZones = scene.path == ScenePaths[0] ? 1 : 2;
            int expectedWalls = scene.path == ScenePaths[0] ? 2 : 8;
            if (zones.Length != expectedZones || walls.Length != expectedWalls)
                errors.Add(scene.path + ": неверное количество зон/станций: " + zones.Length + "/" + walls.Length);
            foreach (var zone in zones)
                if (!zone.transform.IsChildOf(gameplay.transform)) errors.Add(scene.path + ": зона вне Gameplay");
            foreach (var wall in walls)
            {
                var anchor = wall.GetComponent<ArsenalStationAnchor>();
                if (!wall.transform.IsChildOf(gameplay.transform)) errors.Add(scene.path + ": станция вне Gameplay");
                if (wall.GetComponentInParent<TeamSpawnZone>(true) != null)
                    errors.Add(scene.path + ": станция вложена в масштабируемую зону");
                if (anchor == null || anchor.Zone == null || !zones.Contains(anchor.Zone))
                    errors.Add(scene.path + ": отсутствует ссылка станции на зону своей карты");
            }
        }

        private sealed class SceneSnapshot
        {
            private readonly Transform[] transforms;
            private readonly Matrix4x4[] matrices;
            private readonly NetworkIdentity[] networks;
            private readonly ulong[] ids;
            private readonly ArsenalStationAnchor[] anchors;
            private readonly TeamSpawnZone[] zones;

            public SceneSnapshot(Scene scene)
            {
                transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
                matrices = transforms.Select(t => t.localToWorldMatrix).ToArray();
                networks = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NetworkIdentity>(true)).ToArray();
                ids = networks.Select(n => n.sceneId).ToArray();
                anchors = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ArsenalStationAnchor>(true)).ToArray();
                zones = anchors.Select(a => a.Zone).ToArray();
            }

            public void Verify()
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i] == null) throw new InvalidOperationException("При переносе потерян исходный объект.");
                    var now = transforms[i].localToWorldMatrix;
                    for (int k = 0; k < 16; k++)
                        if (Mathf.Abs(now[k] - matrices[i][k]) > .0001f)
                            throw new InvalidOperationException("Изменён мировой трансформ: " + transforms[i].name);
                }
                for (int i = 0; i < networks.Length; i++)
                    if (networks[i] == null || networks[i].sceneId != ids[i])
                        throw new InvalidOperationException("Изменён sceneId сетевого объекта.");
                for (int i = 0; i < anchors.Length; i++)
                    if (anchors[i] == null || anchors[i].Zone != zones[i])
                        throw new InvalidOperationException("Изменена ссылка станции на зону.");
            }
        }

        /// <summary>Однократная миграция. Вызывать только под замком Unity; исходники сохраняются в Temp.</summary>
        public static string MigrateAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Редактор в Play Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Есть несохранённая сцена.");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
            if (asset.GetComponentsInChildren<TeamSpawnZone>(true).Length == 0) return ValidateAll();
            string backup = "Temp/MapGameplayHierarchy/" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach (string path in ScenePaths.Concat(new[] { EnvironmentPath }))
            {
                string destination = backup + "/" + path;
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(path, destination, false);
            }
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool completed = false;
            try
            {
                // Сначала отсоединяются ВСЕ экземпляры, пока исходный префаб ещё содержит зоны.
                foreach (string path in ScenePaths)
                {
                    // Одновременно загружена только одна карта: Mirror иначе меняет
                    // совпавшие sceneId ещё до снимка. Предыдущий scene setup возвращается в конце.
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var snapshot = new SceneSnapshot(scene);
                    var arena = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                        .Select(t => t.gameObject).First(g => PrefabUtility.IsAnyPrefabInstanceRoot(g)
                            && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == EnvironmentPath);
                    PrefabUtility.UnpackPrefabInstance(arena, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                    var gameplay = GameplayRoot(scene);
                    var spawnGroup = arena.transform.Find("SpawnZones");
                    if (spawnGroup == null) throw new InvalidOperationException("Нет SpawnZones: " + path);
                    spawnGroup.SetParent(gameplay, true);
                    foreach (Transform baseGroup in spawnGroup)
                    {
                        var walls = baseGroup.GetComponentsInChildren<ArsenalWallController>(true);
                        if (walls.Length == 0) continue;
                        var arsenalGroup = new GameObject("Arsenals");
                        SceneManager.MoveGameObjectToScene(arsenalGroup, scene);
                        arsenalGroup.transform.SetParent(baseGroup, false);
                        foreach (var wall in walls) wall.transform.SetParent(arsenalGroup.transform, true);
                    }
                    var coordinator = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ArsenalEquipmentCoordinator");
                    if (coordinator != null) coordinator.transform.SetParent(gameplay, true);
                    snapshot.Verify();
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Не сохранена сцена: " + path);
                    snapshot.Verify();
                }
                var contents = PrefabUtility.LoadPrefabContents(EnvironmentPath);
                try
                {
                    var spawns = contents.transform.Find("SpawnZones");
                    if (spawns == null) throw new InvalidOperationException("Нет SpawnZones в исходном префабе.");
                    UnityEngine.Object.DestroyImmediate(spawns.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(contents, EnvironmentPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(EnvironmentPath);
                foreach (string path in ScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var snapshot = new SceneSnapshot(scene);
                    // После первого прохода экземпляр отсоединён; ветка SpawnZones уже принадлежит карте.
                    var instance = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                        .First(t => t.Find("Geometry") != null && t.Find("Ambience") != null
                            && (t.name == "Environment" || t.name == "Arena_Core")).gameObject;
                    PrefabUtility.ConvertToPrefabInstance(instance, asset, new ConvertToPrefabInstanceSettings
                    {
                        objectMatchMode = ObjectMatchMode.ByHierarchy,
                        componentsNotMatchedBecomesOverride = true,
                        gameObjectsNotMatchedBecomesOverride = true,
                        recordPropertyOverridesOfMatches = true,
                        changeRootNameToAssetName = false,
                        logInfo = false
                    }, InteractionMode.AutomatedAction);
                    snapshot.Verify();
                    var errors = new List<string>();
                    ValidateScene(scene, errors);
                    if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
                    if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Не сохранена сцена: " + path);
                    snapshot.Verify();
                }
                completed = true;
                return "PASS: 6 сцен перенесены; исходные объекты, мировые трансформы, sceneId и ссылки сохранены. Backup: " + backup;
            }
            finally
            {
                // При отказе dirty-сцена остаётся доступной для диагностики; дисковые исходники в backup.
                if (completed) EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }
    }
}
