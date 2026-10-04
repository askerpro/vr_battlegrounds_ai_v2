using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Editor.LevelDesign;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Разделяет чертёж реальной площадки и собственную игровую геометрию карт.</summary>
    public static class PhysicalArenaLayoutMigration
    {
        public const string LayoutPath = "Assets/Prefabs/Arenas/Nalchick/Tolstogo_street/PhysicalArenaLayout.prefab";
        public const string MaterialPath = "Assets/Art/PhysicalArenaLayout/Diagnostic.mat";
        private static readonly string[] Paths = {
            "Assets/Scenes/Lobby.unity", "Assets/Scenes/Maps/TestMap1.unity", "Assets/Scenes/Maps/TestMap2.unity",
            "Assets/Scenes/Maps/TestMap3.unity", "Assets/Scenes/Maps/ReferenceMap04.unity", "Assets/Scenes/Maps/ServiceYard.unity"
        };

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Validate Physical Arena Layout")]
        private static void ValidateMenu() => GameLog.Debug.Info(ValidateAll());

        public static string ValidateAll()
        {
            var reports = new List<string>();
            foreach (var path in Paths)
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try { Validate(scene); reports.Add(scene.name + ": PASS"); }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            return string.Join("\n", reports);
        }

        private static T[] All<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();

        private static void Validate(Scene scene)
        {
            var layouts = All<PhysicalArenaLayout>(scene);
            if (layouts.Length != 1 || All<PhysicalArenaDefinition>(scene).Length != 1)
                throw new InvalidOperationException(scene.path + ": нужен один паспорт на одном чертеже.");
            var layout = layouts[0];
            string reason;
            var definition = layout.GetComponent<PhysicalArenaDefinition>();
            if (layout.transform.parent != null || !layout.gameObject.activeInHierarchy
                || layout.transform.position.sqrMagnitude > .000001f || !definition.Valid(out reason))
                throw new InvalidOperationException(scene.path + ": неверный корень/паспорт разметки.");
            if (layout.diagnosticGeometry == null || layout.diagnosticGeometry.activeSelf
                || layout.GetComponentsInChildren<Collider>(true).Length != 0
                || definition.floor != null || definition.floorShape == null)
                throw new InvalidOperationException(scene.path + ": разметка должна быть скрыта и не иметь Collider.");
            var anchors = All<PhysicalSpaceAnchor>(scene);
            if (anchors.Length != 2 || anchors.Any(a => !a.gameObject.activeInHierarchy || !a.transform.IsChildOf(layout.transform))
                || anchors.Select(a => a.id).Distinct().Count() != 2)
                throw new InvalidOperationException(scene.path + ": неверная ветка активных меток.");
            PhysicalSpaceAnchorFrame frame;
            if (!PhysicalSpaceAnchorFrame.TryBuild(anchors.OrderBy(a=>a.id).First().transform.position,
                anchors.OrderBy(a=>a.id).Last().transform.position, out frame, out reason))
                throw new InvalidOperationException(scene.path + ": не строится система координат меток: " + reason);
            var markers = layout.GetComponentsInChildren<PhysicalObstacleMarker>(true);
            if (markers.Length != 4 || markers.Select(m=>m.markerId).Distinct().Count()!=4)
                throw new InvalidOperationException(scene.path + ": нужны четыре устойчивых маркера столбов.");
            foreach (var marker in markers)
            {
                Bounds bounds;
                if (!marker.TryBounds(definition, out bounds, out reason))
                    throw new InvalidOperationException(scene.path + ": неверный резерв: " + reason);
            }
            Bounds floor;
            if (!BlockoutSupportSurfaces.TryMapFloor(scene,out floor) || !BlockoutGrid.TryFloor(scene,out floor))
                throw new InvalidOperationException(scene.path + ": нет независимого игрового пола.");
            if (All<Transform>(scene).Any(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == MapGameplayHierarchy.EnvironmentPath))
                throw new InvalidOperationException(scene.path + ": сохранился общий игровой Environment.");
            foreach (var renderer in layout.GetComponentsInChildren<Renderer>(true))
                if ((GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)
                    & (StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic)) != 0)
                    throw new InvalidOperationException(scene.path + ": чертёж отмечен для окклюзии.");
        }

        // Снимок действующей геометрии и исходных меток; ссылки и sceneId проверяются до сохранения.
        private sealed class Snapshot
        {
            private readonly Transform[] transforms;
            private readonly Matrix4x4[] matrices;
            private readonly Collider[] colliders;
            private readonly bool[] enabled;
            private readonly Bounds[] bounds;
            private readonly NetworkIdentity[] identities;
            private readonly ulong[] ids;
            private readonly PhysicalSpaceAnchor[] anchors;
            private readonly int[] anchorIds;
            public Snapshot(Scene scene)
            {
                Physics.SyncTransforms();
                transforms=All<Transform>(scene).Where(t=>!LegacyAnchorContainer(t)).ToArray();
                matrices=transforms.Select(t=>t.localToWorldMatrix).ToArray();
                colliders=All<Collider>(scene).Where(c=>c.GetComponentInParent<PhysicalSpaceAnchor>(true)==null).ToArray();
                enabled=colliders.Select(c=>c.enabled).ToArray();bounds=colliders.Select(c=>c.bounds).ToArray();
                identities=All<NetworkIdentity>(scene);ids=identities.Select(n=>n.sceneId).ToArray();
                anchors=All<PhysicalSpaceAnchor>(scene);anchorIds=anchors.Select(a=>a.id).ToArray();
            }
            public void Verify()
            {
                Physics.SyncTransforms();
                for(int i=0;i<transforms.Length;i++)
                {
                    if(transforms[i]==null)throw new InvalidOperationException("Потерян исходный объект.");
                    var now=transforms[i].localToWorldMatrix;
                    for(int k=0;k<16;k++)if(Mathf.Abs(now[k]-matrices[i][k])>.0001f)
                        throw new InvalidOperationException("Сдвинут объект: "+transforms[i].name);
                }
                for(int i=0;i<colliders.Length;i++)
                    if(colliders[i]==null||colliders[i].enabled!=enabled[i]
                        ||(colliders[i].bounds.center-bounds[i].center).sqrMagnitude>.000001f
                        ||(colliders[i].bounds.size-bounds[i].size).sqrMagnitude>.000001f)
                        throw new InvalidOperationException("Изменена игровая физика: "+i);
                for(int i=0;i<identities.Length;i++)if(identities[i]==null||identities[i].sceneId!=ids[i])
                    throw new InvalidOperationException("Изменён сетевой sceneId.");
                for(int i=0;i<anchors.Length;i++)if(anchors[i]==null||anchors[i].id!=anchorIds[i]||!anchors[i].gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Изменена исходная метка.");
            }
        }

        /// <summary>Вызывать под замком Unity. Повторный запуск проверяет готовые сцены; чужие ассеты не сохраняет.</summary>
        public static string MigrateAll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Требуется обычная сцена вне Play Mode.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Есть несохранённая сцена.");
            string backup="Temp/PhysicalArenaLayout/"+DateTime.Now.ToString("yyyyMMdd-HHmmss");
            foreach(var path in Paths)
            {
                var destination=backup+"/"+path;Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(path,destination,false);
            }
            var setup=EditorSceneManager.GetSceneManagerSetup();
            bool complete=false;
            try
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPath);
                if(prefab==null)prefab=CreateLayout();
                var contents=PrefabUtility.LoadPrefabContents(LayoutPath);
                try { if(CleanupGeometry(contents,true))PrefabUtility.SaveAsPrefabAsset(contents,LayoutPath); }
                finally {PrefabUtility.UnloadPrefabContents(contents);}
                prefab=AssetDatabase.LoadAssetAtPath<GameObject>(LayoutPath);
                foreach(var path in Paths)
                {
                    var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                    if(All<PhysicalArenaLayout>(scene).Length!=0)
                    {
                        var existingSnapshot=new Snapshot(scene);
                        bool changed=false;
                        foreach(var root in scene.GetRootGameObjects())
                            if(root.GetComponent<PhysicalArenaLayout>()==null)changed|=CleanupGeometry(root,false);
                        existingSnapshot.Verify();Validate(scene);
                        if(changed&&!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Не сохранена сцена: "+path);
                        continue;
                    }
                    var snapshot=new Snapshot(scene);
                    var oldDefinition=All<PhysicalArenaDefinition>(scene).SingleOrDefault();
                    var origin=oldDefinition!=null?oldDefinition.WorldOrigin:prefab.GetComponent<PhysicalArenaDefinition>().WorldOrigin;
                    var anchors=All<PhysicalSpaceAnchor>(scene);
                    if(anchors.Length!=2)throw new InvalidOperationException("Ожидалось две исходные метки: "+path);
                    // В части карт общий префаб вложен под собственный контейнер Environment.
                    foreach(var transform in All<Transform>(scene))
                        if(PrefabUtility.IsAnyPrefabInstanceRoot(transform.gameObject)
                            && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject)==MapGameplayHierarchy.EnvironmentPath)
                            PrefabUtility.UnpackPrefabInstance(transform.gameObject,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                    var layout=instance.GetComponent<PhysicalArenaLayout>();
                    var definition=instance.GetComponent<PhysicalArenaDefinition>();
                    var generated=instance.transform.Find("CalibrationAnchors");
                    Object.DestroyImmediate(generated.gameObject);
                    var group=new GameObject("CalibrationAnchors");
                    SceneManager.MoveGameObjectToScene(group,scene);group.transform.SetParent(instance.transform,false);
                    foreach(var anchor in anchors)anchor.transform.SetParent(group.transform,true);
                    foreach(var collider in group.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                    // Исходные визуальные метки больше не являются частью игровой статической геометрии.
                    foreach(var transform in instance.GetComponentsInChildren<Transform>(true))
                        GameObjectUtility.SetStaticEditorFlags(transform.gameObject,0);
                    definition.gridOrigin=origin;
                    if(oldDefinition!=null)
                    {
                        definition.arenaId=oldDefinition.arenaId;definition.gridStep=oldDefinition.gridStep;
                        definition.safetyMargin=oldDefinition.safetyMargin;
                        RemapDefinition(scene,oldDefinition,definition);
                        Object.DestroyImmediate(oldDefinition);
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(definition);
                    foreach(var root in scene.GetRootGameObjects())
                        if(root.GetComponent<PhysicalArenaLayout>()==null)CleanupGeometry(root,false);
                    PhysicalArenaPanel.Invalidate(scene);
                    snapshot.Verify();Validate(scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Не сохранена сцена: "+path);
                    snapshot.Verify();
                }
                complete=true;
            }
            finally { if(complete)EditorSceneManager.RestoreSceneManagerSetup(setup); }
            return "PASS: шесть сцен мигрированы; игровые объекты, геометрия и метки сохранены. Копии: "+backup+"\n"+ValidateAll();
        }

        private static void RemapDefinition(Scene scene,PhysicalArenaDefinition before,PhysicalArenaDefinition after)
        {
            foreach(var component in All<Component>(scene))
            {
                if(component==null||component==before)continue;
                var serialized=new SerializedObject(component);
                var property=serialized.GetIterator();bool changed=false;
                while(property.Next(true))
                    if(property.propertyType==SerializedPropertyType.ObjectReference&&property.objectReferenceValue==before)
                    {property.objectReferenceValue=after;changed=true;}
                if(changed)serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // Имена используются только при однократном удалении известного наследия, не как игровой контракт.
        private static bool LegacyAnchorContainer(Transform transform) =>
            (transform.name=="SapceAnchors"||transform.name=="SpaceAnchors")
            && transform.GetComponents<Component>().Length==1;

        private static bool CleanupGeometry(GameObject root,bool diagnostic)
        {
            bool changed=false;
            foreach(var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if(LegacyAnchorContainer(transform)&&transform.childCount==0)
                {Object.DestroyImmediate(transform.gameObject);changed=true;continue;}
                if(transform.name=="nalchik_tolst"&&transform.parent!=null&&transform.parent.name=="Geometry")
                {transform.name=diagnostic?"ArenaGeometry":"MapShell";changed=true;}
            }
            return changed;
        }

        private static GameObject CreateLayout()
        {
            string id;Vector2 origin;float step,margin;
            var reference=EditorSceneManager.OpenPreviewScene(Paths[4]);
            try
            {
                var old=All<PhysicalArenaDefinition>(reference).Single();
                id=old.arenaId;origin=old.WorldOrigin;step=old.gridStep;margin=old.safetyMargin;
            }
            finally {EditorSceneManager.ClosePreviewScene(reference);}
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)throw new InvalidOperationException("Нет штатного диагностического Unlit shader.");
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null)
            {
                material=new Material(shader){name="PhysicalArenaDiagnostic"};
                material.SetColor("_BaseColor",new Color(1f,.03f,.8f,1f));AssetDatabase.CreateAsset(material,MaterialPath);
            }
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(MapGameplayHierarchy.EnvironmentPath);
            var root=new GameObject("PhysicalArenaLayout");
            try
            {
                var layout=root.AddComponent<PhysicalArenaLayout>();
                var definition=root.GetComponent<PhysicalArenaDefinition>();
                definition.arenaId=id;definition.gridOrigin=origin;definition.gridStep=step;definition.safetyMargin=margin;
                var geometry=Object.Instantiate(source.transform.Find("Geometry").gameObject,root.transform);
                geometry.name="Geometry";
                var anchors=geometry.GetComponentsInChildren<PhysicalSpaceAnchor>(true);
                if(anchors.Length!=2)throw new InvalidOperationException("Неверные метки исходной площадки.");
                var calibration=new GameObject("CalibrationAnchors");calibration.transform.SetParent(root.transform,false);
                foreach(var anchor in anchors)anchor.transform.SetParent(calibration.transform,true);
                var floor=geometry.GetComponentsInChildren<BoxCollider>(true).Single(c=>c.gameObject.layer==LayerMask.NameToLayer("Ground"));
                var pillars=geometry.GetComponentsInChildren<BoxCollider>(true).Where(c=>c!=floor&&c.transform.parent==floor.transform.parent).ToArray();
                if(pillars.Length!=4)throw new InvalidOperationException("Неверные столбы исходной площадки.");
                foreach(var collider in geometry.GetComponentsInChildren<BoxCollider>(true))
                {
                    var shape=collider.gameObject.AddComponent<PhysicalArenaShape>();shape.center=collider.center;shape.size=collider.size;
                    if(collider==floor)definition.floorShape=shape;
                    if(pillars.Contains(collider))
                    {
                        var marker=collider.gameObject.AddComponent<PhysicalObstacleMarker>();
                        marker.markerId=Guid.NewGuid().ToString("N");marker.sourceShapes=new[]{shape};
                    }
                }
                foreach(var collider in root.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
                CleanupGeometry(root,true);
                foreach(var renderer in geometry.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObjectUtility.SetStaticEditorFlags(transform.gameObject,0);
                    transform.gameObject.tag=GameTagRules.ExpectedTag(transform.gameObject);
                }
                layout.diagnosticGeometry=geometry;geometry.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root,LayoutPath);
            }
            finally {Object.DestroyImmediate(root);}
        }
    }
}
