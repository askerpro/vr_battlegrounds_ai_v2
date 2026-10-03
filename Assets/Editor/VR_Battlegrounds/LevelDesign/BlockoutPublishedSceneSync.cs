using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>План и синхронизация только активной сцены через общую фабрику; сохранение сцены остаётся за человеком.</summary>
    public static class BlockoutPublishedSceneSync
    {
        public sealed class Template
        {
            public BlockoutBlockDefinition definition;
            public GameObject source;
            public Mesh candidate;
            public Vector2 previousPlan, plan, topPlan;
            public bool sourceChanges;
        }
        public sealed class Entry
        {
            public GameObject target;
            public Template template;
            public Vector3 dimensions;
            public CoverClass material;
            public BlockoutOpeningSettings openings;
            public BlockoutSectionSettings[] sections;
        }
        public sealed class Report
        {
            public int matched, updated, unchanged;
            public readonly List<string> skipped = new List<string>(), rejected = new List<string>();
            public readonly List<string> sourceBlockers = new List<string>();
            public readonly List<(GameObject target, string reason)> warnings = new List<(GameObject, string)>();
            public readonly List<Entry> entries = new List<Entry>();
            public readonly List<Entry> inspected = new List<Entry>();
            internal int undoGroup = -1;
            public string Summary => $"Активная сцена: найдено {matched}; обновлено {updated}; без изменений {unchanged}; исключено {skipped.Count}; отказов {rejected.Count}. " +
                $"Предупреждений: {warnings.Count}. " + (rejected.Count > 0 ? string.Join("; ", rejected) : "") + (skipped.Count > 0 ? " Исключены: " + string.Join("; ", skipped) : "");
        }
        public static Report LastReport { get; private set; }

        public static Report Prepare(IEnumerable<Template> values)
        {
            var templates = values.ToArray(); var report = new Report(); LastReport = report;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return report;
            var primary = templates.GroupBy(t => t.definition.shapeId).ToDictionary(g => g.Key,
                g => g.FirstOrDefault(t => t.source == t.definition.geometryPrefab) ?? g.First());
            var exactSources = templates.ToDictionary(t => AssetDatabase.GetAssetPath(t.source), t => primary[t.definition.shapeId]);
            var affectedMeshes = new HashSet<Mesh>(templates.Where(t => t.sourceChanges).Select(t => t.source.GetComponent<MeshFilter>().sharedMesh));
            foreach (var go in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject))
            {
                var instance = go.GetComponent<BlockoutBlockInstance>(); Template template = null;
                if (instance?.definition != null) primary.TryGetValue(instance.definition.shapeId, out template);
                if (template == null && PrefabUtility.GetNearestPrefabInstanceRoot(go) == go)
                    exactSources.TryGetValue(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go), out template);
                bool gameRole = instance != null && template != null && !BlockoutBlockRoles.IsProtection(go);
                bool protectedObject = BlockoutBlockRoles.IsProtection(go) || !gameRole &&
                    (go.GetComponentInParent<PhysicalArenaDefinition>() != null || go.GetComponentInParent<PhysicalObstacleMarker>() != null);
                if (protectedObject && DependsOnMeshes(go, affectedMeshes))
                    report.sourceBlockers.Add(go.name + ": изменение общего источника затронет подтверждённую физическую защиту; обновите её через арену.");
                if (template == null) continue;
                report.matched++;
                if (protectedObject) { report.skipped.Add(go.name + " (физическая арена/маркер/защита)"); continue; }
                if (go.transform.localScale != Vector3.one || (go.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                { report.rejected.Add(go.name + ": масштаб корня или родителя отличается от 1."); continue; }
                var wall = go.GetComponent<BlockoutCellWall>();
                Vector3 actual = wall != null ? new Vector3(wall.length, wall.wallHeight, wall.thickness) : BlockoutRegistryFactory.GeometryBounds(go).size;
                var dimensions = new Vector3(template.definition.editableDimensions ? actual.x * template.plan.x / template.previousPlan.x : template.plan.x,
                    actual.y, template.plan.y);
                bool metadataChanges = instance != null && (instance.definition != template.definition || (instance.dimensions - dimensions).sqrMagnitude > .000001f);
                if(go.TryGetComponent<BlockoutSteppedGeometry>(out var profile)) metadataChanges |=
                    (profile.baseSize-template.plan).sqrMagnitude>.000001f || (profile.topSize-template.topPlan).sqrMagnitude>.000001f;
                var entry = new Entry { target = go, template = template, dimensions = dimensions,
                    material = go.GetComponent<CoverSurface>()?.Class ?? instance?.material ?? template.definition.defaultMaterial,
                    openings = wall != null ? wall.openings : instance != null ? instance.openings : BlockoutOpeningSettings.Default,
                    sections = instance != null && instance.HasSections ? BlockoutSectionSettings.Copy(instance.sections) : null };
                bool anchorNeeds = Mathf.Abs(BlockoutRegistryFactory.GeometryBounds(go).min.y)>.0001f ||
                    BlockoutGrid.TryFloor(go.scene,out var floorBounds) && BlockoutPainter.WorldBounds(go).min.y<floorBounds.max.y-.0001f;
                report.inspected.Add(entry);
                if ((actual - dimensions).sqrMagnitude < .000001f && !metadataChanges && !anchorNeeds) { report.unchanged++; continue; }
                if (!Preflight(entry, out string reason, out string warning)) report.rejected.Add(go.name + ": " + reason);
                else { report.entries.Add(entry); if (warning != null) report.warnings.Add((go, warning)); }
            }
            return report;
        }

        private static bool DependsOnMeshes(GameObject go, HashSet<Mesh> meshes)
        {
            if (meshes.Count == 0) return false;
            if (go.GetComponents<MeshFilter>().Any(f => meshes.Contains(f.sharedMesh)) || go.GetComponents<MeshCollider>().Any(c => meshes.Contains(c.sharedMesh))) return true;
            var geometry = go.GetComponent<BlockoutHeightGeometry>();
            if (geometry == null) return false;
            var records = new SerializedObject(geometry).FindProperty("meshes");
            for (int i = 0; records != null && i < records.arraySize; i++)
            {
                var record = records.GetArrayElementAtIndex(i);
                var vertices = record.FindPropertyRelative("vertices");
                if ((vertices == null || vertices.arraySize == 0) && meshes.Contains(record.FindPropertyRelative("source").objectReferenceValue as Mesh)) return true;
            }
            return false;
        }

        private static bool Preflight(Entry entry, out string reason, out string warning)
        {
            reason = null; warning = null; var definition = Object.Instantiate(entry.template.definition);
            var previewScene = EditorSceneManager.NewPreviewScene(); GameObject clone = null;
            try
            {
                BlockoutCanonicalRegistry.SetPlanCaps(definition, entry.template.plan);
                // Новые фиксированные границы уже проверяются min/max; Current JSON ещё не опубликован.
                definition.editableDimensions = true;
                if (entry.sections!=null)
                {if(!BlockoutSectionFactory.Validate(definition,entry.dimensions,entry.sections,out reason))return false;}
                else if (!BlockoutRegistryFactory.Validate(definition, entry.dimensions, entry.material, entry.openings, out reason)) return false;
                bool hasFloor = BlockoutGrid.TryFloor(entry.target.scene, out var floor);
                if (!hasFloor) warning = "Не настроен пол активной сцены.";
                clone = Object.Instantiate(entry.target); clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetPositionAndRotation(entry.target.transform.position, entry.target.transform.rotation);
                if(hasFloor)
                {
                    var position=clone.transform.position;position.y=Mathf.Max(BlockoutPainter.WorldBounds(entry.target).min.y,floor.max.y);clone.transform.position=position;
                }
                SceneManager.MoveGameObjectToScene(clone, previewScene);
                if(entry.sections!=null)
                {
                    BlockoutSectionFactory.PrepareGeometry(clone,entry.dimensions,entry.sections);
                    BlockoutSectionFactory.RefreshSource(clone,entry.template.source,entry.template.candidate,entry.template.plan,entry.template.topPlan);
                    var manager=clone.GetComponent<BlockoutSectionGeometry>();
                    var precise=clone.GetComponent<BlockoutCellWall>()!=null||clone.GetComponent<BlockoutSteppedGeometry>()!=null
                        ?BlockoutCellWall.TransformParts(manager.LocalSolidParts(),clone.transform.position,clone.transform.rotation):null;
                    if(hasFloor)warning=GeometryWarning(entry,clone,floor,precise);
                    return true;
                }
                var wall = clone.GetComponent<BlockoutCellWall>();
                if (wall == null && entry.template.definition.supportsCellWall && (entry.openings.enabled ||
                    Mathf.Abs(entry.dimensions.x - entry.template.plan.x) > .0001f || Mathf.Abs(entry.dimensions.z - entry.template.plan.y) > .0001f))
                { reason = "Нестандартный старый префаб сначала переводится в параметрическую геометрию явно."; return false; }
                if (wall != null)
                {
                    if (entry.openings.enabled && !BlockoutCellWall.HasEffectiveOpenings(wall.cells, wall.cellSize, entry.dimensions.y, entry.openings,
                        BlockoutGeometryMode.ThinStraight, entry.dimensions.x, entry.dimensions.z, wall.RotationYaw))
                    { reason = "После изменения размера реальные щели не помещаются."; return false; }
                    if (wall.length != entry.dimensions.x || wall.thickness != entry.dimensions.z) wall.geometryMode = BlockoutGeometryMode.ThinStraight;
                    wall.length = entry.dimensions.x; wall.thickness = entry.dimensions.z; wall.wallHeight = entry.dimensions.y; wall.openings = entry.openings; wall.Rebuild();
                    if (hasFloor) warning = GeometryWarning(entry, clone, floor, wall.SolidParts());
                }
                else
                {
                    PrepareBody(clone, entry);
                    Physics.SyncTransforms();
                    var step = clone.GetComponent<BlockoutSteppedGeometry>();
                    if (hasFloor) warning = GeometryWarning(entry, clone, floor, step != null ? step.WorldParts() : null);
                }
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
            finally { if (clone != null) Object.DestroyImmediate(clone); Object.DestroyImmediate(definition); EditorSceneManager.ClosePreviewScene(previewScene); }
        }

        private static string GeometryWarning(Entry entry, GameObject clone, Bounds floor, IEnumerable<BlockoutSolidPart> parts)
            => GeometryWarning(entry,clone,floor,parts,out _);
        private static string GeometryWarning(Entry entry, GameObject clone, Bounds floor, IEnumerable<BlockoutSolidPart> parts,out BlockoutWallEditing.ConflictInfo conflict)
        {
            conflict=null;
            if(!entry.target.activeInHierarchy)return null;
            Physics.SyncTransforms();
            Bounds bounds = BlockoutPainter.WorldBounds(clone); var reasons = new List<string>();
            var outside=BlockoutWallEditing.FloorConflict(bounds,floor);
            if(outside!=null)reasons.Add(outside.message);
            if(bounds.min.y<floor.max.y-.01f) reasons.Add($"Низ объекта {bounds.min.y:0.###} м ниже поверхности пола {floor.max.y:0.###} м.");
            // Расширенный пол отделяет проверку столкновений от уже описанной проверки границ.
            var collisionFloor = new Bounds(floor.center,new Vector3(100000, floor.size.y,100000));
            bool collision = parts != null ? BlockoutWallEditing.Conflict(entry.target.scene,collisionFloor,parts,out conflict,entry.target) :
                BlockoutWallEditing.PrefabConflictAtYaw(clone,entry.target.scene,collisionFloor,bounds,entry.target.transform.eulerAngles.y,out conflict,entry.target);
            if(collision)
            {
                reasons.Add(conflict.message);
            }
            return reasons.Count>0 ? string.Join(" ",reasons) : null;
        }

        private static void PrepareBody(GameObject clone, Entry entry)
        {
            var sourceStep = entry.template.source.GetComponent<BlockoutSteppedGeometry>();
            if (sourceStep != null)
            {
                var step = clone.GetComponent<BlockoutSteppedGeometry>();
                if (step == null) throw new InvalidOperationException("У ступенчатого экземпляра отсутствует его профиль.");
                step.baseSize = entry.template.plan; step.topSize = entry.template.topPlan; step.lowerHeightLimit = sourceStep.lowerHeightLimit;
                step.localBottomCenter = sourceStep.localBottomCenter;
                step.localBottomCenter.y = 0; step.ApplyHeight(entry.dimensions.y); return;
            }
            if (clone.GetComponentsInChildren<MeshFilter>(true).Length != 1 || clone.GetComponent<MeshFilter>() == null || clone.GetComponentsInChildren<Collider>(true).Length != 1)
                throw new InvalidOperationException("Иерархия экземпляра отличается от простого источника.");
            var oldHeight = clone.GetComponent<BlockoutHeightGeometry>();
            if (oldHeight != null) Object.DestroyImmediate(oldHeight);
            var mesh = entry.template.candidate ?? entry.template.source.GetComponent<MeshFilter>().sharedMesh;
            clone.GetComponent<MeshFilter>().sharedMesh = mesh;
            var box = clone.GetComponent<BoxCollider>(); if (box != null) { box.center = mesh.bounds.center; box.size = mesh.bounds.size; }
            var collider = clone.GetComponent<MeshCollider>(); if (collider != null) { collider.sharedMesh = null; collider.sharedMesh = mesh; }
            clone.AddComponent<BlockoutHeightGeometry>().CapturePublishedSource(mesh.bounds.size.y, entry.dimensions.y, 0);
        }

        /// <summary>Единое Undo относится к сцене. Общие ассеты восстанавливаются отдельно из резервной копии.</summary>
        public static Report Apply(Report report)
        {
            LastReport = report;
            if (report.sourceBlockers.Count > 0) throw new InvalidOperationException(string.Join("; ", report.sourceBlockers));
            BeginSceneUndo(report); int group = report.undoGroup;
            try
            {
                foreach (var entry in report.entries)
                {
                    var go = entry.target; Undo.RegisterFullObjectHierarchyUndo(go, "Применить размеры к активной сцене");
                    var instance = go.GetComponent<BlockoutBlockInstance>() ?? Undo.AddComponent<BlockoutBlockInstance>(go);
                    instance.definition = entry.template.definition;
                    var bindings = go.GetComponentsInChildren<Renderer>(true).Select(r => (renderer: r, materials: r.sharedMaterials)).ToArray();
                    var surfaces = go.GetComponentsInChildren<CoverSurface>(true).Select(s => (surface: s, modifier: s.PenetrationModifier)).ToArray();
                    if (!BlockoutRegistryFactory.ApplyPublishedDimensions(go, entry.template.definition, entry.dimensions, entry.material, entry.openings, out string reason, force:true, normalizeBottomAnchor:true))
                        throw new InvalidOperationException(go.name + ": " + reason);
                    foreach (var binding in bindings)
                    {
                        binding.renderer.sharedMaterials = binding.materials; EditorUtility.SetDirty(binding.renderer);
                        if (PrefabUtility.IsPartOfPrefabInstance(binding.renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(binding.renderer);
                    }
                    foreach (var surface in surfaces)
                    {
                        if(surface.surface==null)continue;
                        surface.surface.PenetrationModifier = surface.modifier; EditorUtility.SetDirty(surface.surface);
                        if (PrefabUtility.IsPartOfPrefabInstance(surface.surface)) PrefabUtility.RecordPrefabInstancePropertyModifications(surface.surface);
                    }
                    report.updated++;
                }
                if(report.updated>0)
                {
                    string sceneGuid=AssetDatabase.AssetPathToGUID(SceneManager.GetActiveScene().path);
                    foreach(string guid in AssetDatabase.FindAssets("t:BlockoutMarkup"))
                    {
                        var markup=AssetDatabase.LoadAssetAtPath<BlockoutMarkup>(AssetDatabase.GUIDToAssetPath(guid));
                        if(markup==null||markup.sceneGuid!=sceneGuid||string.IsNullOrEmpty(sceneGuid))continue;
                        Undo.RecordObject(markup,"Переоценить разметку после изменения размеров");markup.needsReevaluation=true;EditorUtility.SetDirty(markup);
                    }
                }
                Undo.CollapseUndoOperations(group); Physics.SyncTransforms(); RefreshWarnings(report); return report;
            }
            catch (Exception error)
            {
                Rollback(report); report.rejected.Add(error.Message);
                throw new InvalidOperationException("Изменения сцены отменены; общие ассеты уже опубликованы и имеют резервную копию. " + report.Summary, error);
            }
        }
        public static void Rollback(Report report)
        {
            if(report.undoGroup<0)return;
            Undo.RevertAllDownToGroup(report.undoGroup);report.undoGroup=-1;report.updated=0;
        }

        /// <summary>Сценовый снимок обязан предшествовать записи prefab: inherited Transform меняется при импорте источника.</summary>
        public static void BeginSceneUndo(Report report)
        {
            if(report.undoGroup>=0)return;
            CaptureBaselines(report);
            Undo.IncrementCurrentGroup();report.undoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Применить размеры к активной сцене");
            foreach(var entry in report.entries)
            {
                Undo.RegisterFullObjectHierarchyUndo(entry.target,"Применить размеры к активной сцене");
                // Первый Undo.AddComponent метаданных меняет structural hierarchy-запись.
                // Отдельный снимок существующих объектов сохраняет прежнее тело и позу raw-префаба.
                var existingObjects = new Object[] { entry.target }.Concat(entry.target.GetComponentsInChildren<Component>(true).Cast<Object>()).ToArray();
                Undo.RegisterCompleteObjectUndo(existingObjects,"Применить размеры к активной сцене");
                // Уже существующие override позы фиксируются в снимке до изменения источника.
                if(PrefabUtility.IsPartOfPrefabInstance(entry.target.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry.target.transform);
            }
            Undo.FlushUndoRecordObjects();
        }

        /// <summary>Свежая диагностика выбранного тела без публикации, Undo и изменения последнего отчёта.</summary>
        public static string InspectWarning(GameObject target)
            => InspectWarning(target,out _);
        public static string InspectWarning(GameObject target,out BlockoutWallEditing.ConflictInfo conflict)
        {
            conflict=null;
            if(target==null)return null;
            if(!BlockoutGrid.TryFloor(target.scene,out var floor))return "Не настроен пол активной сцены.";
            var scene=EditorSceneManager.NewPreviewScene();GameObject clone=null;
            try
            {
                clone=Object.Instantiate(target);clone.hideFlags=HideFlags.HideAndDontSave;
                clone.transform.SetPositionAndRotation(target.transform.position,target.transform.rotation);
                SceneManager.MoveGameObjectToScene(clone,scene);
                IEnumerable<BlockoutSolidPart> parts=clone.TryGetComponent<BlockoutCellWall>(out var wall)?wall.SolidParts():
                    clone.TryGetComponent<BlockoutSteppedGeometry>(out var step)?step.WorldParts():null;
                return GeometryWarning(new Entry {target=target},clone,floor,parts,out conflict);
            }
            catch(Exception error){return "Диагностика: "+error.Message;}
            finally{if(clone!=null)Object.DestroyImmediate(clone);EditorSceneManager.ClosePreviewScene(scene);}
        }

        /// <summary>Однократная диагностика фактического результата, включая неизменённые объекты.</summary>
        public static void RefreshWarnings(Report report)
        {
            report.warnings.Clear();var previewScene=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var entry in report.inspected)
                {
                    if(entry.target==null)continue;
                    if(!BlockoutGrid.TryFloor(entry.target.scene,out var floor)){report.warnings.Add((entry.target,"Не настроен пол активной сцены."));continue;}
                    GameObject clone=null;
                    try
                    {
                        clone=Object.Instantiate(entry.target);clone.hideFlags=HideFlags.HideAndDontSave;
                        clone.transform.SetPositionAndRotation(entry.target.transform.position,entry.target.transform.rotation);
                        SceneManager.MoveGameObjectToScene(clone,previewScene);
                        IEnumerable<BlockoutSolidPart> parts=clone.TryGetComponent<BlockoutCellWall>(out var wall)?wall.SolidParts():
                            clone.TryGetComponent<BlockoutSteppedGeometry>(out var step)?step.WorldParts():null;
                        string warning=GeometryWarning(entry,clone,floor,parts);
                        if(warning!=null)report.warnings.Add((entry.target,warning));
                    }
                    catch(Exception error){report.warnings.Add((entry.target,"Диагностика: "+error.Message));}
                    finally{if(clone!=null)Object.DestroyImmediate(clone);}
                }
            }
            finally{EditorSceneManager.ClosePreviewScene(previewScene);}
        }

        /// <summary>Неизменяемая база старого тела записывается до публикации общего mesh asset и до scene Undo.</summary>
        public static void CaptureBaselines(Report report)
        {
            foreach (var entry in report.entries)
            {
                var go = entry.target;
                if (go.GetComponent<BlockoutCellWall>() != null || go.GetComponent<BlockoutSteppedGeometry>() != null) continue;
                var geometry = go.GetComponent<BlockoutHeightGeometry>();
                if (geometry == null)
                {
                    geometry = go.AddComponent<BlockoutHeightGeometry>();
                    float height = BlockoutRegistryFactory.GeometryBounds(go).size.y;
                    geometry.Initialize(height, height);
                }
                geometry.FreezeSourceGeometry(); EditorUtility.SetDirty(geometry);
                if (PrefabUtility.IsPartOfPrefabInstance(geometry)) PrefabUtility.RecordPrefabInstancePropertyModifications(geometry);
            }
        }

        public static Dictionary<string, object> Inventory()
        {
            Scene scene = SceneManager.GetActiveScene();
            var instances = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BlockoutBlockInstance>(true)).Where(i => !BlockoutBlockRoles.IsProtection(i.gameObject));
            return instances.ToDictionary(i => i.GetInstanceID().ToString(), i => (object)new Dictionary<string, object>
            {
                { "name", i.name }, { "shapeId", i.definition != null ? i.definition.shapeId : "" }, { "dimensions", i.dimensions },
                { "actualLocalSize", BlockoutRegistryFactory.GeometryBounds(i.gameObject).size }, { "position", i.transform.position },
                { "rotation", i.transform.rotation }, { "parent", i.transform.parent != null ? i.transform.parent.GetInstanceID() : 0 }, { "material", i.material.ToString() }
            });
        }
    }
}
