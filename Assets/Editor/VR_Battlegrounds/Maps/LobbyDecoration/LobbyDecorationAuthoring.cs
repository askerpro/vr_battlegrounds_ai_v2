using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.LobbyAudio;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor;
using ZoneBinding = VrBattlegrounds.LobbyZones.LobbyZoneLayoutBinding;
using ZoneEditor = VrBattlegrounds.Editor.Maps.LobbyDecoration.LobbyZoneLayoutEditor;
using ZoneHierarchy = VrBattlegrounds.Editor.Maps.LobbyDecoration.LobbyZoneHierarchyBuilder;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Воспроизводимый прототип; изменяет только собственный декор и Ambience в Lobby.</summary>
    public static class LobbyDecorationAuthoring
    {
        private const string Menu = "Tools/VR Battlegrounds/Lobby Decoration/";
        private const string Art = "Assets/Art/Lobby/";
        private const string Report = "tasks/lobby-decoration/reports/implementation/";
        private const string Prefab = "Assets/Prefabs/Environment/LobbyDecoration/FieldCamp.prefab";
        private const string Profile = "Assets/Data/Audio/LobbyAmbienceProfile.asset";

        [MenuItem(Menu + "3. Проверить Android compilation")]
        public static void CompileAndroid()
        {
            Check(false);
            var result = AndroidCompileGate.Run();
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + "android-compile.json", JsonUtility.ToJson(result, true));
            GameLog.Debug.Info(result.Summary);
        }

        [MenuItem(Menu + "4. Перезапечь каталог")]
        public static void RebakeCatalog()
        {
            Check(true);
            object result = MapBootstrapMigration.RebakeCatalog(out bool passed);
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + "catalog-rebake.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            if (!passed) throw new InvalidOperationException("Каталог не прошёл проверку; подробности в catalog-rebake.json.");
        }

        [MenuItem(Menu + "5. Штатная окклюзия только Lobby")]
        public static void BakeLobby()
        {
            Check(true);
            // Штатный классификатор и Umbra без вызова Run(), который открывает все карты.
            var bake = typeof(OcclusionBakeTool).GetMethod("BakeScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("Изменился API OcclusionBakeTool: пакет нужно обновить.");
            object result = bake.Invoke(null, new object[] { "Assets/Scenes/Lobby.unity" });
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + "occlusion-lobby.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            if (!OcclusionBakeTool.SceneReferencesOcclusionData("Assets/Scenes/Lobby.unity")) throw new InvalidOperationException("Нет ссылки на данные окклюзии Lobby.");
        }

        private static Scene Check(bool write)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(root, "F:/UnityProjects/Vr_Battlegrounds_ai", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Этот пакет разрешён только в основном checkout lobby-decoration.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Нужен свободный Editor вне Play/компиляции/prefab stage.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Lobby.unity" || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Откройте только Assets/Scenes/Lobby.unity.");
            if (write && scene.isDirty) throw new InvalidOperationException("Сначала сохраните свои изменения сцены; пакет не перезаписывает dirty Lobby.");
            return scene;
        }

        [MenuItem(Menu + "1. Снять исходные измерения")]
        public static void Measure()
        {
            var scene = Check(false);
            var roots = scene.GetRootGameObjects();
            var definition = roots.SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            if (!definition.TryFloorBounds(out var floor)) throw new InvalidOperationException("Нет bounds физического пола.");
            var lines = new List<string> { DateTime.UtcNow.ToString("O"), "Main checkout; Editor; Play выключен.", "Пол: " + floor, "Dirty: " + scene.isDirty };
            foreach (var shape in definition.GetComponentsInChildren<PhysicalArenaShape>(true))
                if (shape.TryBounds(out var bounds)) lines.Add("Физическая форма " + shape.name + ": " + bounds);
            foreach (var r in roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                if (PathOf(r.transform).Contains("Arsenal") || PathOf(r.transform).Contains("Table") || PathOf(r.transform).Contains("Target"))
                    lines.Add(PathOf(r.transform) + ": " + r.bounds + "; enabled=" + r.enabled);
            foreach (var a in roots.SelectMany(r => r.GetComponentsInChildren<AudioSource>(true)))
                lines.Add("Audio " + PathOf(a.transform) + "; resource=" + a.resource + "; loop=" + a.loop + "; volume=" + a.volume);
            lines.Add("Раскрытые bounds станций и физические raycast требуют отдельной проверки Play; этот замер их не симулирует.");
            Directory.CreateDirectory(Report);
            File.WriteAllLines(Report + "live-layout.txt", lines);
            GameLog.Debug.Info("[LobbyDecoration] Замеры сохранены: " + Report + "live-layout.txt");
        }

        [MenuItem(Menu + "2. Собрать прототип")]
        public static void Build()
        {
            var scene = Check(true);
            PreflightSources();
            var environment = scene.GetRootGameObjects().Single(r => r.name == "Environment").transform;
            // С 2026-10-11 зоны распакованы прямо под Environment и правятся в сцене; префаб FieldCamp не источник.
            if (environment.Find("ArmoryRangeZone") != null)
                throw new InvalidOperationException("Декор Lobby распакован в зоны Environment. Полная пересборка префаба отключена: правьте зоны в сцене.");
            var definition = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            var floor = definition.FloorBounds;
            var pivot = new Vector3(floor.center.x, floor.max.y, floor.center.z);
            var oldZones = ReadZones(environment.Find("LobbyDecoration"));
            var poses = ZoneHierarchy.CaptureZonePoses(oldZones);
            var bindings = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ZoneBinding>(true)).ToArray();
            if (bindings.Any(b => !oldZones.Values.Contains(b.SourceZone)))
                throw new InvalidOperationException("Не разрешено изменять чужую привязку зоны Lobby.");
            var prefabBefore = File.Exists(Prefab) ? File.ReadAllBytes(Prefab) : null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Оформление Lobby — единая операция");
            using (ZoneEditor.SuppressAutomaticSynchronization())
            {
                try
                {
                    EnsureExistingGeometry(scene);
                    ZoneEditor.ValidateScene(scene);
                    ZoneEditor.NormalizeForRegeneration(bindings, pivot);
                    ZoneHierarchy.NormalizeZonePoses(oldZones, pivot);
                    BuildCore(poses, pivot);
                    Undo.CollapseUndoOperations(group);
                }
                catch
                {
                    // Не оставляем изменённый общий prefab после отказа позднего builder.
                    if (prefabBefore != null && File.Exists(Prefab) && !File.ReadAllBytes(Prefab).SequenceEqual(prefabBefore))
                    {
                        File.WriteAllBytes(Prefab, prefabBefore);
                        AssetDatabase.ImportAsset(Prefab, ImportAssetOptions.ForceUpdate);
                    }
                    Undo.RevertAllDownToGroup(group);
                    throw;
                }
            }
        }

        private static void PreflightSources()
        {
            // Проверяем зависимости до изменения игровых владельцев; Undo откатывает последующие ошибки.
            foreach (var stem in LobbyCheckpointZoneBuilder.RequiredSources.Concat(LobbyConstructionZoneBuilder.RequiredSources)
                .Concat(LobbyRestCanopyZoneBuilder.RequiredSources).Concat(LobbyBarracksDressingBuilder.RequiredSources).Distinct())
                if (LobbyMilitaryCampSources.Prefab(stem) == null) throw new InvalidOperationException("Нет источника: " + stem);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Art/Lobby/Imported" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Не импортирован prefab: " + path);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                        throw new InvalidOperationException("Неполные материалы до сборки: " + path + "/" + r.name);
                foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh == null) throw new InvalidOperationException("Нет исходного mesh: " + path);
            }
        }

        private static void BuildCore(ZoneHierarchy.ZonePose[] savedPoses, Vector3 pivot)
        {
            // Wrapper уже проверил чистую сцену; далее dirty принадлежит этой Undo-операции.
            var scene = Check(false);
            if (!File.Exists(Report + "live-layout.txt")) throw new InvalidOperationException("Сначала выполните замеры и baseline существующих проверок.");
            var environment = scene.GetRootGameObjects().Single(r => r.name == "Environment");
            var ambience = environment.transform.Find("Ambience");
            if (ambience == null || ambience.GetComponent<AudioSource>() == null) throw new InvalidOperationException("Нет исходного Ambience/AudioSource.");
            var definition = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Single();
            if (!definition.TryFloorBounds(out var floor)) throw new InvalidOperationException("Нет физического пола.");
            var tentBounds = LobbyMilitaryCampSources.TentBounds(floor);
            var canopy = Material("CanvasOlive", new Color(.29f, .32f, .19f));
            canopy.SetTexture("_BaseMap", LobbyMilitaryCampSources.Texture("T_Tent_a_BC"));
            canopy.SetTextureScale("_BaseMap", new Vector2(.22f, .15f));
            canopy.SetTextureOffset("_BaseMap", new Vector2(.13f, .30f));
            canopy.SetColor("_BaseColor", new Color(.62f, .65f, .48f));
            EditorUtility.SetDirty(canopy);
            var metal = Material("FrameDark", new Color(.12f, .15f, .13f));
            var earth = Material("DryEarth", new Color(.33f, .28f, .20f));
            var sand = Material("SandCanvas", new Color(.48f, .43f, .29f));
            foreach (string guid in AssetDatabase.FindAssets("t:Texture", new[] { Art }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.maxTextureSize > 1024)
                { importer.maxTextureSize = 1024;importer.SaveAndReimport(); }
            }
            var draft = new GameObject("LobbyDecoration");
            try
            {
                var tent = New("OpenTent", draft.transform);
                LobbyTentRoofBuilder.Build(tent.transform, tentBounds, floor.max.y);
                foreach (float side in new[] { -1f, 1f })
                {
                    var curtain = New(side < 0 ? "WestCurtain" : "EastCurtain", tent.transform);
                    curtain.transform.position = new Vector3(tentBounds.center.x + side * tentBounds.extents.x, floor.max.y, tentBounds.center.z);
                    curtain.AddComponent<MeshFilter>().sharedMesh = Curtain(side < 0 ? "WestCurtain" : "EastCurtain", tentBounds.size.z, LobbyMilitaryCampSources.SideWindowOffset(side));
                    curtain.AddComponent<MeshRenderer>().sharedMaterial = canopy;
                }
                // Внутренние продолжения опор совпадают с физическими колоннами.
                foreach (var shape in definition.GetComponentsInChildren<PhysicalArenaShape>(true))
                    if (shape != definition.floorShape && shape.TryBounds(out var b) && b.size.y > 1 && b.size.x < 1 && b.size.z < 1)
                    {
                        float top = LobbyTentRoofBuilder.HeightAt(tentBounds,floor.max.y,b.center.x,b.center.z) - .10f;
                        float height = Mathf.Max(.01f, top - b.max.y);
                        Box("Продолжение опоры", tent.transform, new Vector3(b.center.x, (top + b.max.y) * .5f, b.center.z), new Vector3(b.size.x * .85f, height, b.size.z * .85f), metal);
                    }
                foreach (float x in new[] { tentBounds.min.x + .08f, tentBounds.max.x - .08f })
                    foreach (float z in new[] { tentBounds.min.z + .08f, tentBounds.max.z - .08f })
                        Box("Внешняя стойка", tent.transform, new Vector3(x, floor.max.y + 2.1f, z), new Vector3(.16f, 4.2f, .16f), metal);
                var stationSweeps = LobbyArenaLayoutOverrides.Apply(scene, floor, tentBounds, out var mirrorView);
                Physics.SyncTransforms();
                LobbyTargetVisualAdapter.Apply(scene);
                LobbyInteriorDecorationBuilder.Build(draft.transform, scene, floor, tentBounds, stationSweeps, mirrorView);
                LobbyExteriorDecorationBuilder.Build(draft.transform, floor);
                LobbyPerimeterDecorationBuilder.Build(draft.transform, floor, tentBounds);
                LobbyBaseBuildingsBuilder.Build(draft.transform, floor, tentBounds);
                LobbyBarracksDressingBuilder.Build(draft.transform, floor, tentBounds);
                LobbyBaseBackdropBuilder.Build(draft.transform, floor, tentBounds);
                LobbyCheckpointZoneBuilder.Build(draft.transform, floor, tentBounds);
                LobbyRestCanopyZoneBuilder.Build(draft.transform, floor, tentBounds);
                LobbyRangeDressingBuilder.Build(draft.transform, floor, tentBounds);
                LobbyConstructionZoneBuilder.Build(draft.transform, floor, tentBounds);
                BuildDistantGround(draft.transform);
                var sunlight = New("CampDaylight", draft.transform).AddComponent<Light>();
                sunlight.type = LightType.Directional;sunlight.intensity = 1.2f;sunlight.color = new Color(1, .96f, .88f);sunlight.shadows = LightShadows.None;
                sunlight.transform.rotation = Quaternion.Euler(48, -32, 0);
                BuildLighting(draft.transform, floor, metal);
                var authoredLights = draft.GetComponentsInChildren<Light>(true);
                if (authoredLights.Length != 13 || authoredLights.Any(l => l.name == "Point Light"))
                    throw new InvalidOperationException("Ожидались 13 источников единого владельца без vendor Light.");
                foreach (var renderer in draft.GetComponentsInChildren<Renderer>())
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    // LOD сохраняет независимое отсечение; не дублируем все деревья в static batches.
                    var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.OccludeeStatic;
                    if (renderer.GetComponentInParent<LODGroup>() == null) flags |= StaticEditorFlags.BatchingStatic;
                    else flags &= ~StaticEditorFlags.BatchingStatic;
                    GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
                }
                GroupFunctionalZones(draft.transform, floor, pivot);
                Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
                PrefabUtility.SaveAsPrefabAsset(draft, Prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(draft); }
            var old = environment.transform.Find("LobbyDecoration");
            if (old != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject) != Prefab)
                throw new InvalidOperationException("LobbyDecoration занято чужой группой.");
            foreach (Transform child in environment.transform.Cast<Transform>().ToArray())
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) == Prefab) Undo.DestroyObjectImmediate(child.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
            instance.name = "LobbyDecoration";
            instance.transform.SetParent(environment.transform, false);
            Undo.RegisterCreatedObjectUndo(instance, "Оформление Lobby");
            // Скрываем только визуальную крышу; collider и физическая разметка остаются.
            foreach (var renderer in environment.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(instance.transform) && renderer.gameObject.name == "Ceiling")
                { Undo.RecordObject(renderer, "Скрыть прежнюю крышу Lobby");renderer.enabled = false; }
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                if (renderer.gameObject.name == "OuterGround")
                { Undo.RecordObject(renderer, "Грунт стрельбища Lobby");renderer.sharedMaterial = earth; }
            ApplyGroundVisuals(scene, definition, canopy);
            ConnectAudio(ambience.gameObject, instance.transform, floor);
            var zones = ReadZones(instance.transform);
            var gameplay = scene.GetRootGameObjects().Single(r => r.name == "Gameplay").transform;
            var shootingRange = gameplay.Find("ShootingRange");
            var spawnZones = gameplay.Find("SpawnZones");
            var existingGeometry = environment.transform.Find("ArmoryRangeExistingGeometry");
            if (shootingRange == null || spawnZones == null || existingGeometry == null)
                throw new InvalidOperationException("Потеряны согласованные группы игрового и статичного стрельбища.");
            ZoneEditor.BindAndCapture(shootingRange, zones["ArmoryRangeZone"], environment.transform, gameplay,
                shootingRange, spawnZones, existingGeometry);
            ZoneHierarchy.RestoreZonePoses(zones, savedPoses);
            ZoneEditor.ValidateScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Art, "Assets/Materials/Lobby" }))
                AssetDatabase.SaveAssetIfDirty(new GUID(guid));
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<LobbyAmbienceProfile>(Profile));
            Measure();
            GameLog.Debug.Info("[LobbyDecoration] Прототип собран. Нужны проверки станций, обзор мишеней и прослушивание.");
        }

        private static Dictionary<string,Transform> ReadZones(Transform decoration)
        {
            var container = decoration == null ? null : decoration.Find("FunctionalZones");
            return container == null ? new Dictionary<string,Transform>(StringComparer.Ordinal)
                : container.Cast<Transform>().ToDictionary(t => t.name, t => t, StringComparer.Ordinal);
        }

        private static void EnsureExistingGeometry(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var environment = roots.Single(r => r.name == "Environment").transform;
            var geometry = environment.Find("ArmoryRangeExistingGeometry");
            if (geometry == null)
            {
                geometry = New("ArmoryRangeExistingGeometry", environment).transform;
                Undo.RegisterCreatedObjectUndo(geometry.gameObject,"Сценовая статичная геометрия стрельбища");
            }
            // Существующая группа могла законно следовать за вручную размещённой зоной.
            // Её позицию/поворот нормализует только тот же binding вместе с Source.
            if (geometry.localScale != Vector3.one)
                throw new InvalidOperationException("Сценовая группа геометрии должна иметь масштаб1.");
            var mirror = geometry.Find("MirrorDisplayClean") ?? roots.SingleOrDefault(r => r.name == "MirrorDisplayClean")?.transform;
            var range = roots.SingleOrDefault(r => r.name == "LobbyRange");
            var tables = geometry.Find("Tables") ?? (range == null ? null : range.transform.Find("Tables"));
            if (mirror == null || tables == null) throw new InvalidOperationException("Нет исходных MirrorDisplayClean/Tables.");
            foreach (var branch in new[] {mirror,tables})
            {
                if (branch.GetComponentsInChildren<PhysicalArenaDefinition>(true).Length != 0
                    || branch.GetComponentsInChildren<PhysicalArenaShape>(true).Length != 0
                    || branch.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                    throw new InvalidOperationException("Статичная группа содержит запрещённую физическую сущность.");
                var before = branch.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.localToWorldMatrix);
                if (branch.parent != geometry) Undo.SetTransformParent(branch,geometry,"Разделить статичную и игровую геометрию Lobby");
                foreach (var item in before)
                    for (int i=0;i<16;i++) if (Mathf.Abs(item.Key.localToWorldMatrix[i]-item.Value[i])>.0001f)
                        throw new InvalidOperationException("Перенос изменил мировую геометрию: "+item.Key.name);
            }
        }

        private static void GroupFunctionalZones(Transform draft, Bounds floor, Vector3 pivot)
        {
            Func<string,Transform> need = path => draft.Find(path) ?? throw new InvalidOperationException("Нет части функциональной зоны: "+path);
            var lighting = need("CampLighting").Cast<Transform>().ToArray();
            var armory = new List<Transform>{need("OpenTent"),need("InteriorDecoration"),need("PerimeterDecoration"),
                need("ShootingRangeDressing"),need("BaseBackdrop/SharedFiveLaneRockBackstop"),
                need("BaseBackdrop/LayeredPaidRockMasses/NorthRangeCliffLayers")};
            armory.AddRange(lighting.Where(t=>t.name=="InteriorWarmLight"||t.name=="SM_Lamp_long"));
            var barracks = new List<Transform>{need("BaseBuildings/Казарма A"),need("BaseBuildings/Казарма B"),need("BarracksDressing")};
            var warehouse = new List<Transform>{need("BaseBuildings/Склад и мастерская")};
            var vehicleYard = new List<Transform>{need("ExteriorDecoration/VehicleMaintenance"),
                need("ExteriorDecoration/FuelDepot"),need("ExteriorDecoration/PaidArmoredVehicle")};
            var checkpoint = new List<Transform>{need("CheckpointZone")};
            var canopy = new List<Transform>{need("RestCanopyZone")};
            foreach (var t in lighting.Where(t=>t.name=="ExteriorWarmLight"||t.name=="SM_Door_lamp_a"))
            {
                if (t.position.x<floor.center.x) barracks.Add(t);
                else if (Mathf.Abs(t.position.z-(floor.center.z-21))<.2f) warehouse.Add(t);
                else if (Mathf.Abs(t.position.z-(floor.center.z-29.84f))<.2f) checkpoint.Add(t);
                else throw new InvalidOperationException("Неизвестный владелец наружного света: "+t.position);
            }
            canopy.AddRange(lighting.Where(t=>t.name=="CanopyWarmLight"));
            // Части других зон выносятся из лесного и скального контейнеров, а сами контейнеры целиком уходят в периметр.
            foreach (var t in vehicleYard.Append(armory.Single(t=>t.name=="NorthRangeCliffLayers"))) t.SetParent(draft, true);
            var perimeter = new List<Transform>{need("BaseBackdrop/DecorativeBaseChainMesh"),need("BaseBackdrop/LayeredPaidRockMasses"),
                need("ExteriorDecoration"),need("DistantGroundBackdrop")};
            // Точка вращения — центр участка из плана сверху (SitePlan-2026-10-10), чтобы зону удобно двигать и вращать.
            Func<float,float,Vector3> at = (x,z) => new Vector3(floor.center.x+x, floor.max.y, floor.center.z+z);
            ZoneHierarchy.GroupByRoles(draft,pivot,
                new ZoneHierarchy.ZoneAssignment("ArmoryRangeZone",armory.ToArray()),
                new ZoneHierarchy.ZoneAssignment("CheckpointZone",at(26.5f,-32.5f),checkpoint.ToArray()),
                new ZoneHierarchy.ZoneAssignment("BarracksZone",at(-23.5f,-16f),barracks.ToArray()),
                new ZoneHierarchy.ZoneAssignment("ConstructionZone",at(-25.5f,16f),need("ConstructionZone")),
                new ZoneHierarchy.ZoneAssignment("RestCanopyZone",at(-22f,-30.5f),canopy.ToArray()),
                new ZoneHierarchy.ZoneAssignment("WarehouseZone",at(24f,-21f),warehouse.ToArray()),
                new ZoneHierarchy.ZoneAssignment("VehicleYardZone",at(13.5f,-26f),vehicleYard.ToArray()),
                new ZoneHierarchy.ZoneAssignment("ShippingDepotZone",at(-23.5f,35.5f),need("BaseBackdrop/OrganizedContainerStores")),
                new ZoneHierarchy.ZoneAssignment("PerimeterZone",perimeter.ToArray()));
            // Бывшие тематические контейнеры после раскладки пусты; в иерархии остаются только зоны и глобальные части.
            foreach (var path in new[]{"BaseBackdrop","BaseBuildings","CampLighting"})
            {
                var empty = draft.Find(path);
                if (empty != null && empty.childCount == 0 && empty.GetComponents<Component>().Length == 1)
                    UnityEngine.Object.DestroyImmediate(empty.gameObject);
            }
        }

        private static void ConnectAudio(GameObject ambience, Transform decoration, Bounds floor)
        {
            var profile = AssetDatabase.LoadAssetAtPath<LobbyAmbienceProfile>(Profile);
            if (profile == null) { Directory.CreateDirectory(Path.GetDirectoryName(Profile));profile = ScriptableObject.CreateInstance<LobbyAmbienceProfile>();AssetDatabase.CreateAsset(profile, Profile); }
            profile.forest = Clip("Forest");
            profile.episodes = new[]
            {
                Event(LobbyAmbientKind.Helicopter, LobbyAmbientMode.Path, "Helicopter", 18, .35f, 4),
                Event(LobbyAmbientKind.Truck, LobbyAmbientMode.Path, "Truck", 19, .3f, 3),
                Event(LobbyAmbientKind.Gunfire, LobbyAmbientMode.Point, "Gunfire", 2, .13f, .2f),
                Event(LobbyAmbientKind.Soldiers, LobbyAmbientMode.Path, "SoldiersSteps", 12, .18f, 2)
            };
            // Голос — альтернативный короткий солдатский эпизод, никогда второй источник поверх шагов.
            profile.episodes[3].clips = new[] { Clip("SoldiersSteps"), Clip("SoldiersVoice") };
            EditorUtility.SetDirty(profile);
            var forest = ambience.GetComponent<AudioSource>();Undo.RecordObject(forest, "Заменить городской фон");
            forest.Stop();forest.resource = null;forest.clip = profile.forest;forest.playOnAwake = false;forest.loop = true;forest.volume = profile.forestVolume;forest.spatialBlend = 0;
            var director = ambience.GetComponent<LobbyAmbienceDirector>() ?? Undo.AddComponent<LobbyAmbienceDirector>(ambience);
            var eventObject = ambience.transform.Find("LobbyEpisode");
            if (eventObject == null) eventObject = New("LobbyEpisode", ambience.transform).transform;
            var emitter = eventObject.GetComponent<LobbyAmbientEmitter>() ?? Undo.AddComponent<LobbyAmbientEmitter>(eventObject.gameObject);
            emitter.GetComponent<AudioSource>().playOnAwake = false;
            Undo.RecordObject(director, "Настроить локальный эмбиент");director.profile = profile;director.emitter = emitter;
            var routeRoot = New("AmbientRoutes", decoration);
            director.routes = new LobbyAmbienceDirector.Route[4];
            for (int i = 0; i < 4; i++)
            {
                var start = New(((LobbyAmbientKind)i) + "Start", routeRoot.transform).transform;
                var end = New(((LobbyAmbientKind)i) + "End", routeRoot.transform).transform;
                float y = i == 0 ? 25 : 1.8f;
                start.position = floor.center + new Vector3(-45, y, i == 2 ? 85 : 40 + i * 8);
                end.position = floor.center + new Vector3(45, y, 40 + i * 8);
                director.routes[i] = new LobbyAmbienceDirector.Route { kind = (LobbyAmbientKind)i, start = start, end = end };
            }
        }
        private static LobbyAmbienceProfile.Episode Event(LobbyAmbientKind kind, LobbyAmbientMode mode, string clip, float duration, float volume, float fade)
            => new LobbyAmbienceProfile.Episode { kind = kind, mode = mode, clips = new[] { Clip(clip) }, duration = duration, volume = volume, fade = fade, cooldown = new Vector2(70, 140), maxDistance = 150 };
        private static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Lobby/" + name + ".wav") ?? throw new InvalidOperationException("Не импортирован WAV: " + name);
        private static Material FindMaterial(string name) => AssetDatabase.FindAssets(name + " t:Material", new[] { Art }).Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p) == name).Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault();
        private static Material Material(string name, Color color)
        {
            string path = "Assets/Materials/Lobby/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("URP/Lit недоступен.");
            material = new Material(shader) { name = name };material.SetColor("_BaseColor", color);material.SetFloat("_Smoothness", .08f);
            Directory.CreateDirectory(Path.GetDirectoryName(path));AssetDatabase.CreateAsset(material, path);return material;
        }
        private static void BuildDistantGround(Transform parent)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Art + "Generated/LobbyDistantGround.shader");
            if (!shader) throw new InvalidOperationException("Не импортирован дешёвый shader дальней подложки.");
            string path = "Assets/Materials/Lobby/DistantGround.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetTexture("_BaseMap", LobbyMilitaryCampSources.Texture("T_GroundIntense_BC"));
            material.SetColor("_BaseColor", new Color(.8f,.8f,.75f));
            EditorUtility.SetDirty(material);
            string meshPath = Art + "Generated/DistantGround.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh)
            {
                mesh = new Mesh { name = "DistantGround" };
                mesh.vertices = new[] { new Vector3(-280,0,-280),new Vector3(-280,0,280),new Vector3(280,0,280),new Vector3(280,0,-280) };
                mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            var go = New("DistantGroundBackdrop", parent);
            go.transform.position = new Vector3(0, -.056f, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        /// <summary>
        /// Материалы грунта Lobby: пол шатра и гравий снаружи. Quest: URP Baked Lit — одна выборка текстуры
        /// плюс лайтмапа/проба (весь свет Lobby запечён); прежний трипланарный S_VPWorldAligned стоил ~1,9 мс
        /// кадра (tasks/scene-detail, normal A/B 2026-10-20). Единственный писатель этих двух материалов.
        /// </summary>
        public static void ApplyGroundMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Baked Lit") ?? throw new InvalidOperationException("URP/Baked Lit недоступен.");
            // UV0 пола шатра растянута на весь пол: ≈39,6 м на единицу UV; текстура повторяется каждые 2 м.
            ApplyBakedLit("Assets/Materials/Lobby/WornGround.mat", shader, "T_Ground_BC", 39.59f / 2f, new Color(.85f, .85f, .80f));
            ApplyBakedLit("Assets/Materials/Lobby/OutsideGravel.mat", shader, "T_GroundIntense_BC", 45f, new Color(.70f, .70f, .64f));
        }

        private static void ApplyBakedLit(string path, Shader shader, string texture, float repeat, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", LobbyMilitaryCampSources.Texture(texture));
            material.SetTextureScale("_BaseMap", new Vector2(repeat, repeat));
            material.SetTextureOffset("_BaseMap", Vector2.zero);
            material.SetColor("_BaseColor", color);
            material.renderQueue = -1;
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }

        private static void ApplyGroundVisuals(Scene scene, PhysicalArenaDefinition definition, Material canvas)
        {
            ApplyGroundMaterials();
            var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/WornGround.mat");
            var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/OutsideGravel.mat");
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)))
            {
                string hierarchy = PathOf(renderer.transform);
                if (hierarchy == "Environment/Geometry/MapShell/floor")
                { Undo.RecordObject(renderer, "Матовый грунт только Lobby"); renderer.sharedMaterials = renderer.sharedMaterials.Select(m => ground).ToArray(); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
                else if (hierarchy.StartsWith("Environment/Geometry/MapShell/столб", StringComparison.Ordinal))
                { Undo.RecordObject(renderer, "Брезент только на опорах Lobby"); renderer.sharedMaterials = renderer.sharedMaterials.Select(m => canvas).ToArray(); PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
                else if (hierarchy == "LobbyRange/OuterGround")
                { Undo.RecordObject(renderer, "Текстурный гравий вокруг лагеря"); renderer.sharedMaterial = gravel; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
            }
        }

        private static void CanvasWeave(Material material)
        {
            string path = Art + "Generated/CanvasWeave.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = "CanvasWeave", wrapMode = TextureWrapMode.Repeat };
                var pixels = new Color[128 * 128];
                for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
                {
                    float weave = ((x % 4 == 0 || y % 4 == 0) ? .80f : .96f) + ((x * 17 + y * 31) % 11) * .003f;
                    pixels[y * 128 + x] = new Color(weave, weave, weave, 1);
                }
                texture.SetPixels(pixels);texture.Apply(true, true);
                Directory.CreateDirectory(Path.GetDirectoryName(path));AssetDatabase.CreateAsset(texture, path);
            }
            material.SetTexture("_BaseMap", texture);material.SetTextureScale("_BaseMap", new Vector2(20, 24));EditorUtility.SetDirty(material);
        }
        private static void BuildLighting(Transform parent, Bounds floor, Material metal)
        {
            var group = New("CampLighting", parent);
            // Плафонов шатра здесь больше нет (решение пользователя 2026-10-22): фон шатра — круги под кровлей, длинные
            // плафоны — только над точками акцента; всё это ставит LobbyTentLightFixtures.
            foreach (var site in new[] { new Vector3(-17.75f,2.65f,-16), new Vector3(19.7f,2.65f,-21) })
            {
                var light = New("ExteriorWarmLight", group.transform).AddComponent<Light>();
                var point = site + new Vector3(floor.center.x, floor.max.y, floor.center.z);
                PlaceLightFixture(group.transform, "SM_Door_lamp_a", point, site.x < 0 ? -90 : site.z < -30 ? 0 : 90);
                light.transform.position = point - Vector3.up * .10f;light.type = LightType.Point;light.intensity = 3.5f;light.range = 7;
                light.color = new Color(1,.8f,.55f);light.shadows = LightShadows.None;
            }
            // Статичный светильник входа принадлежит КПП; общий владелец создаёт только свет.
            var socket = parent.Find("CheckpointZone/GuardHousing/LightingSocket__GuardEntry__CommonLightingOwner");
            if (socket == null) throw new InvalidOperationException("Не найден сокет освещения КПП.");
            var guardLight = New("ExteriorWarmLight", group.transform).AddComponent<Light>();
            guardLight.transform.position = socket.position;
            guardLight.type = LightType.Point; guardLight.intensity = 3.5f; guardLight.range = 7;
            guardLight.color = new Color(1,.8f,.55f); guardLight.shadows = LightShadows.None;
            var canopySocket = parent.Find("RestCanopyZone/RidgeFixture__SM_Lamp_long/LightingSocket");
            if (canopySocket == null) throw new InvalidOperationException("Не найден сокет навеса отдыха.");
            var canopyLight = New("CanopyWarmLight", group.transform).AddComponent<Light>();
            canopyLight.transform.position = canopySocket.position;
            canopyLight.type = LightType.Point; canopyLight.intensity = 3.5f; canopyLight.range = 5;
            canopyLight.color = new Color(1,.8f,.55f); canopyLight.shadows = LightShadows.None;
            BuildPlaceholderFloodlights(parent, floor, metal);
        }

        // Временные прожекторные опоры прямо разрешены пользователем до подбора моделей.
        // Визуал и единственный свет остаются в одной заменяемой группе своей функциональной зоны.
        private static void BuildPlaceholderFloodlights(Transform parent, Bounds floor, Material metal)
        {
            var sites = new[] {
                new Vector4(-11,18,0,23), new Vector4(11,18,0,23),
                new Vector4(33,-36,28,-32), new Vector4(-16.5f,-23.5f,-23,-23.5f)
            };
            var roles = new[] { "ShootingRangeDressing", "ShootingRangeDressing", "CheckpointZone", "BarracksDressing" };
            var face = Material("PlaceholderFloodlightFace", new Color(.72f,.70f,.59f));
            var origin = new Vector3(floor.center.x, floor.max.y, floor.center.z);
            for (int i=0; i<sites.Length; i++)
            {
                var owner = parent.Find(roles[i]);
                if (owner == null) throw new InvalidOperationException("Нет зоны прожектора: " + roles[i]);
                var site = sites[i]; var point = origin + new Vector3(site.x,0,site.y);
                var aim = origin + new Vector3(site.z,0,site.w);
                var root = New("Placeholder__Прожектор_" + (i+1), owner);
                root.transform.position = point;
                Box("Металлическая опора 6м", root.transform, point + Vector3.up*3, new Vector3(.14f,6,.14f), metal);
                Box("Перекладина", root.transform, point + Vector3.up*5.85f, new Vector3(1.5f,.12f,.12f), metal);
                var rotation = Quaternion.LookRotation(aim - (point + Vector3.up*5.7f));
                foreach (float side in new[] {-1f,1f})
                {
                    var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    head.name = "Placeholder__Куб ПРОЖЕКТОР";head.transform.SetParent(root.transform,false);
                    head.transform.SetPositionAndRotation(point + new Vector3(side*.5f,5.7f,0),rotation);
                    head.transform.localScale = new Vector3(.55f,.35f,.35f);
                    UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
                    head.GetComponent<Renderer>().sharedMaterial = face;
                }
                var label = New("Маркировка ПРОЖЕКТОР", root.transform);
                label.transform.position = point + Vector3.up*4.95f;
                label.transform.rotation = Quaternion.LookRotation(label.transform.position - (origin + Vector3.up*1.65f));
                var text = label.AddComponent<TextMesh>();text.text = "ПРОЖЕКТОР";
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=48;text.characterSize=.1f;
                text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=new Color(1,.88f,.5f);
                label.GetComponent<Renderer>().sharedMaterial=text.font.material;
                var extent=label.GetComponent<Renderer>().bounds.size;
                label.transform.localScale *= Mathf.Min(1.5f/Mathf.Max(.001f,extent.x),.28f/Mathf.Max(.001f,extent.y));
                var light = New("CommonOwner__PlaceholderSpot",root.transform).AddComponent<Light>();
                light.transform.SetPositionAndRotation(point + Vector3.up*5.7f,rotation);
                light.type=LightType.Spot;light.spotAngle=95;light.range=22;light.intensity=100;
                light.color=new Color(1,.88f,.68f);light.shadows=LightShadows.None;
            }
        }
        private static void PlaceLightFixture(Transform parent, string stem, Vector3 point, float yaw)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LobbyMilitaryCampSources.Prefab(stem), parent);
            // Сохраняем собственный статичный визуал без вложенных зависимых компонентов.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw, 0));
            // URP AdditionalLightData требует Light: удаляем зависимый компонент первым.
            foreach (var component in instance.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer) && !(component is LODGroup))
                    UnityEngine.Object.DestroyImmediate(component);
            foreach (var lod in instance.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            if (instance.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Исходный светильник оставил второго владельца Light.");
        }
        private static void BuildCornerCheckpoint(Transform parent, Bounds arena)
        {
            var root = New("CornerCheckpoint", parent);
            var origin = new Vector3(arena.center.x, arena.max.y, arena.center.z);
            PlaceStaticSource(root.transform,"SM_WatchTower_01","Сторожевая вышка КПП",origin+new Vector3(31,0,-31),18,8);
            PlaceStaticSource(root.transform,"SM_Container_b","Пост охраны",origin+new Vector3(21,0,-32),-90,0);
            var concrete=Material("CheckpointConcrete",new Color(.36f,.36f,.32f));
            foreach(float x in new[]{24f,34f})
                Box("Защита въезда",root.transform,origin+new Vector3(x,.36f,-36.5f),new Vector3(.65f,.72f,2.5f),concrete);
            var steel=Material("CheckpointGateMetal",new Color(.20f,.23f,.20f));
            Box("Открытая створка въезда",root.transform,origin+new Vector3(33,1.15f,-34.5f),new Vector3(.08f,2.3f,7),steel);
            var soil=Material("CheckpointApproach",new Color(.34f,.30f,.24f));
            soil.SetTexture("_BaseMap",LobbyMilitaryCampSources.Texture("T_GroundIntense_BC"));
            soil.SetTextureScale("_BaseMap",new Vector2(4,8));EditorUtility.SetDirty(soil);
            Box("Подъезд КПП",root.transform,origin+new Vector3(29,.013f,-32.5f),new Vector3(7.8f,.012f,11),soil);
        }
        private static void PlaceStaticSource(Transform parent,string stem,string name,Vector3 foot,float yaw,float height)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(LobbyMilitaryCampSources.Prefab(stem),parent);go.name=name;
            go.transform.SetPositionAndRotation(foot,Quaternion.Euler(0,yaw,0));
            foreach(var c in go.GetComponentsInChildren<Component>(true))
                if(!(c is Transform)&&!(c is MeshFilter)&&!(c is MeshRenderer)&&!(c is LODGroup))UnityEngine.Object.DestroyImmediate(c);
            Func<Bounds> measure=()=>
            {
                bool first=true;Bounds b=default;
                foreach(var f in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!f.sharedMesh)continue;var local=f.sharedMesh.bounds;
                    for(int i=0;i<8;i++)
                    {
                        var p=f.transform.TransformPoint(local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                        if(first){b=new Bounds(p,Vector3.zero);first=false;}else b.Encapsulate(p);
                    }
                }
                return b;
            };
            var bounds=measure();if(height>0)go.transform.localScale*=height/bounds.size.y;
            bounds=measure();go.transform.position+=new Vector3(foot.x-bounds.center.x,foot.y-bounds.min.y,foot.z-bounds.center.z);
            foreach(var lod in go.GetComponentsInChildren<LODGroup>(true))lod.RecalculateBounds();
        }
        private static GameObject New(string name, Transform parent) { var go = new GameObject(name);go.transform.SetParent(parent, false);return go; }
        private static void Box(string name, Transform parent, Vector3 world, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);go.name = name;go.transform.SetParent(parent, false);go.transform.position = world;go.transform.localScale = scale;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial = material;
        }
        private static GameObject PlaceModel(string name, Transform parent, Vector3 position)
        {
            string path = AssetDatabase.FindAssets(name, new[] { Art }).Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name && (p.EndsWith(".prefab") || p.EndsWith(".fbx") || p.EndsWith(".obj")));
            if (path == null) throw new InvalidOperationException("Нет выбранной модели: " + name);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = UnityEngine.Object.Instantiate(model, parent);go.name = name;go.transform.position = position;
            foreach (var collider in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            // FBX добавляет Animator даже неподвижным деревьям: он не принадлежит декору.
            foreach (var animator in go.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(animator);
            foreach (var animation in go.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(animation);
            return go;
        }
        private static Mesh Roof(float width, float depth)
        {
            string path = Art + "Generated/OpenCanopy.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { Directory.CreateDirectory(Path.GetDirectoryName(path));mesh = new Mesh { name = "OpenCanopy" };AssetDatabase.CreateAsset(mesh, path); }
            // Адаптация верхних четырёх треугольников CC0 mess-tent; торцы/пол/мебель удалены.
            float x = width * .5f, z = depth * .5f;
            var vertices = new[] { new Vector3(-x,0,-z),new Vector3(-x,0,z),new Vector3(0,1.4f,-z),new Vector3(0,1.4f,z),new Vector3(x,0,-z),new Vector3(x,0,z) };
            mesh.Clear();mesh.vertices = vertices.Concat(vertices).ToArray();
            mesh.triangles = new[] { 0,1,2,1,3,2,2,3,4,3,5,4,8,7,6,8,9,7,10,9,8,10,11,9 };
            var uv = new[] { new Vector2(0,0),new Vector2(0,1),new Vector2(.5f,0),new Vector2(.5f,1),new Vector2(1,0),new Vector2(1,1) };
            mesh.uv = uv.Concat(uv).ToArray();
            mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }
        private static Mesh Curtain(string name, float length, float windowCenter)
        {
            string path = Art + "Generated/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = name };AssetDatabase.CreateAsset(mesh, path); }
            var vertices = new List<Vector3>();var triangles = new List<int>();var uv = new List<Vector2>();
            Action<float, float, float, float> panel = (z0, z1, y0, y1) =>
            {
                int start = vertices.Count;
                var quad = new[] { new Vector3(0,y0,z0), new Vector3(0,y0,z1),new Vector3(0,y1,z1),new Vector3(0,y1,z0) };
                vertices.AddRange(quad);vertices.AddRange(quad);
                var coords = new[] { new Vector2(z0/length+.5f,y0/4.2f),new Vector2(z1/length+.5f,y0/4.2f),new Vector2(z1/length+.5f,y1/4.2f),new Vector2(z0/length+.5f,y1/4.2f) };
                uv.AddRange(coords);uv.AddRange(coords);
                triangles.AddRange(new[] { start,start+1,start+2,start,start+2,start+3,start+6,start+5,start+4,start+7,start+6,start+4 });
            };
            float half = length * .5f;
            panel(-half, windowCenter - .8f, 0, 4.2f);panel(windowCenter + .8f, half, 0, 4.2f);
            panel(windowCenter - .8f, windowCenter + .8f, 0, 1.25f);panel(windowCenter - .8f, windowCenter + .8f, 2.35f, 4.2f);
            mesh.Clear();mesh.SetVertices(vertices);mesh.SetTriangles(triangles, 0);mesh.SetUVs(0, uv);mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
        }
        private static void WorkTable(Transform parent, Vector3 foot, Material metal, Material canvas)
        {
            var table = New("FieldWorkTable", parent);
            Box("Рабочая поверхность", table.transform, foot + Vector3.up * .92f, new Vector3(1.6f,.08f,.75f), metal);
            foreach (float x in new[] { -.65f, .65f }) foreach (float z in new[] { -.25f, .25f })
                Box("Ножка", table.transform, foot + new Vector3(x,.44f,z), new Vector3(.05f,.88f,.05f), metal);
            Case(table.transform, foot + new Vector3(-.35f,.96f,0), canvas, metal);
        }
        private static void Case(Transform parent, Vector3 bottom, Material canvas, Material metal)
        {
            var item = New("MilitaryCase", parent);
            Box("Корпус кейса", item.transform, bottom + Vector3.up * .12f, new Vector3(.58f,.24f,.35f), canvas);
            Box("Крышка", item.transform, bottom + Vector3.up * .245f, new Vector3(.6f,.035f,.37f), metal);
            foreach (float x in new[] { -.2f, .2f }) Box("Ремень", item.transform, bottom + new Vector3(x,.12f,0), new Vector3(.025f,.26f,.37f), metal);
        }
        private static void DressInterior(Scene scene, Transform parent, Material metal, Material canvas)
        {
            var dressing = New("InteriorTableDressing", parent);
            var tables = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.gameObject.activeInHierarchy && (t.name == "Table_North" || t.name == "Table_South")).ToArray();
            if (tables.Length != 2) throw new InvalidOperationException("Нужны оба существующих торцевых стола для безопасного внутреннего декора.");
            foreach (var table in tables)
            {
                var solids = table.GetComponentsInChildren<BoxCollider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy).ToArray();
                float top = solids.Max(c => c.bounds.max.y);
                var surface = solids.Where(c => Mathf.Abs(c.bounds.max.y - top) < .001f).OrderByDescending(c => c.bounds.size.x * c.bounds.size.z).First();
                var b = surface.bounds;
                // Оставляем середину столешницы для игрового оружия; на доступный пол ничего не ставим.
                var bottom = new Vector3(b.min.x + .45f, b.max.y + .01f, b.center.z);
                Case(dressing.transform, bottom, canvas, metal);
                var radio = New("FieldRadio", dressing.transform);
                Box("Радиостанция", radio.transform, bottom + new Vector3(.75f,.10f,0), new Vector3(.24f,.20f,.18f), metal);
                Box("Антенна", radio.transform, bottom + new Vector3(.83f,.34f,0), new Vector3(.015f,.30f,.015f), metal);
                Box("Сложенный брезент", dressing.transform, bottom + new Vector3(1.1f,.055f,0), new Vector3(.35f,.11f,.28f), canvas);
            }
        }
        private static string PathOf(Transform transform) => transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    }
}
