using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Общая фабрика для кисти и будущего выращивателя. Миграция меняет только реестр.</summary>
    public static class BlockoutRegistryFactory
    {
        public const string RegistryPath = "Assets/Settings/LevelDesign/BlockoutBlocks/Registry.asset";
        public const float DefaultEditableHeight = 1.6f;
        public const float MaxEditableHeight = 2.5f;
        private const string SourceFolder = "Assets/Prefabs/LevelDesign/LD_Alphabet";
        public static BlockoutBlockRegistry Current => AssetDatabase.LoadAssetAtPath<BlockoutBlockRegistry>(RegistryPath);
        /// <summary>Узкая синхронизация уже заменённого ступенчатого источника; список, GUID и размеры X/Z реестра сохраняются.</summary>
        public static int SynchronizeSteppedDefinitions()
        {
            int changed=0;var registry=Current;if(registry==null)return 0;
            foreach(var definition in registry.Definitions)
            {
                if(definition==null||definition.geometryPrefab==null||definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>()==null)continue;
                definition.heightEditable=true;definition.editableDimensions=false;definition.supportsCellWall=false;
                definition.supportsOpenings=false;definition.openingDisabledReason="Сквозные отверстия ступенчатой формы пока не поддерживаются.";
                definition.minDimensions.y=1.2f;definition.vaultShapeSuitable=false;
                BlockoutDefinitionText.SetSteppedText(definition);
                EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);changed++;
            }
            return changed;
        }
        public static Func<GameObject, IEnumerable<Bounds>, bool> GeometryConflict;
        public static Func<GameObject, IEnumerable<BlockoutSolidPart>, bool> PreciseGeometryConflict;
        private static bool Conflicts(GameObject go,IEnumerable<BlockoutSolidPart> parts)
        {
            var snapshot=parts.ToArray();
            return GeometryConflict!=null?GeometryConflict(go,snapshot.Select(p=>p.BroadphaseBounds)):PreciseGeometryConflict?.Invoke(go,snapshot)==true;
        }
        private static readonly Regex ClassSuffix = new Regex("_(Hard|Soft|Visual)(?=$|[_\\s(.])");
        private sealed class SourceBoundsEntry {public Hash128 dependencyHash;public Bounds bounds;}
        private static readonly Dictionary<GameObject,SourceBoundsEntry> BoundsCache = new Dictionary<GameObject,SourceBoundsEntry>();
        static BlockoutRegistryFactory() => EditorApplication.projectChanged += BoundsCache.Clear;
        public static void InvalidateSource(GameObject source=null)
        {
            if(source==null)BoundsCache.Clear();else BoundsCache.Remove(source);
            BlockoutPlacementPreview.InvalidateSource(source);
            BlockoutThumbnails.InvalidateSource();
        }
        private static void MigrateMenu() => EnsureRegistry();

        /// <summary>Чтение опубликованного алфавита без скрытого пересоздания общих ассетов.</summary>
        public static BlockoutBlockRegistry EnsureRegistry()
        {
            var registry=Current;
            if(registry==null||registry.Definitions.Count!=BlockoutCanonicalRegistry.ActiveFormCount||registry.Definitions.Any(d=>d==null||d.geometryPrefab==null))
                throw new InvalidOperationException("Опубликуйте активный алфавит в общих настройках блокаута.");
            return registry;
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private static CoverClass ClassOf(GameObject prefab) => CoverClassRules.TryParse(prefab.name,out var material)?material:CoverClass.Hard;
        public static Vector3 DefaultDimensions(BlockoutBlockDefinition definition)
        {
            Vector2 plan=BlockoutGridSettings.Dimensions(definition.dimensionsSourceKey);
            return new Vector3(plan.x,definition.heightEditable?DefaultEditableHeight:GeometryBounds(definition.geometryPrefab).size.y,plan.y);
        }
        public static bool TryDefinition(GameObject go,out BlockoutBlockDefinition definition)
        {
            go=BlockoutSectionFactory.Root(go);
            definition=go!=null?go.GetComponent<BlockoutBlockInstance>()?.definition:null;
            if(definition!=null) return true;
            if(go==null||Current==null) return false;
            var source=PrefabUtility.GetCorrespondingObjectFromSource(go);
            definition=Current.Definitions.FirstOrDefault(d=>d.geometryPrefab==source||d.materialVariants.Any(v=>v.sourcePrefab==source)
                ||ClassSuffix.Replace(go.name.Replace("(Clone)","").Trim(),"")==d.title);
            return definition!=null || BlockoutCanonicalRegistry.TryLegacyDefinition(go,out definition);
        }
        public static bool Validate(BlockoutBlockDefinition definition,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,out string reason)
        {
            reason=null;
            if(definition==null||definition.geometryPrefab==null) reason="Определение формы отсутствует.";
            else if(!FinitePositive(dimensions.x)||!FinitePositive(dimensions.y)||!FinitePositive(dimensions.z)) reason="Размеры должны быть положительными конечными числами.";
            else if(definition.heightEditable&&(dimensions.y<1.2f-.0001f||dimensions.y>MaxEditableHeight+.0001f))reason="Игровая высота должна быть от 1.2 до 2.5 м.";
            else if(!definition.Allows(material)) reason="Класс материала не разрешён для этой формы.";
            else if(dimensions.x<definition.minDimensions.x-.0001f||dimensions.y<definition.minDimensions.y-.0001f||dimensions.z<definition.minDimensions.z-.0001f
                ||dimensions.x>definition.maxDimensions.x+.0001f||dimensions.y>definition.maxDimensions.y+.0001f||dimensions.z>definition.maxDimensions.z+.0001f) reason="Размеры вне допустимых границ формы.";
            else if(!definition.heightEditable&&Mathf.Abs(dimensions.y-GeometryBounds(definition.geometryPrefab).size.y)>.0001f) reason="Высота этой формы фиксирована.";
            else if(!definition.editableDimensions&&(Mathf.Abs(dimensions.x-DefaultDimensions(definition).x)>.0001f||Mathf.Abs(dimensions.z-DefaultDimensions(definition).z)>.0001f)) reason="Произвольные размеры основания этой формы не поддерживаются.";
            else if(definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>()!=null&&dimensions.y<1.2f-.0001f)reason="Минимальная высота ступенчатой формы — 1.2 м.";
            else if(definition.heightEditable&&!definition.supportsCellWall&&definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>()==null&&!BlockoutHeightGeometryEditor.CanResize(definition.geometryPrefab,out reason)) { }
            else if(openings.enabled&&!definition.supportsOpenings) reason=definition.openingDisabledReason;
            else if(openings.enabled&&(!FinitePositive(openings.spacing)||openings.spacing<=.01f||!FinitePositive(openings.width)||openings.width<=.001f||openings.width>=openings.spacing
                ||!FinitePositive(openings.sillHeight)||!FinitePositive(openings.lintelHeight)||openings.sillHeight+openings.lintelHeight>=dimensions.y)) reason="Щели требуют положительных ширины, шага, основания и перемычки; ширина меньше шага.";
            return reason==null;
        }
        private static bool FinitePositive(float value) => value>0&&!float.IsNaN(value)&&!float.IsInfinity(value);
        public static bool ValidatePlacement(BlockoutBlockDefinition definition,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,float yaw,out string reason,float? cellStep=null)
        {
            if(!Validate(definition,dimensions,material,openings,out reason))return false;
            if(float.IsNaN(yaw)||float.IsInfinity(yaw))
            {reason="Угол должен быть конечным.";return false;}
            float step=cellStep??BlockoutGrid.Cell;
            if(!FinitePositive(step)){reason="Шаг клеток должен быть положительным конечным числом.";return false;}
            reason=openings.enabled&&definition.supportsCellWall&&!BlockoutCellWall.HasEffectiveOpenings(null,step,dimensions.y,openings,BlockoutGeometryMode.ThinStraight,dimensions.x,dimensions.z,yaw)?"Ни одна щель не помещается между концевыми стойками.":null;
            return reason==null;
        }
        public static GameObject Create(BlockoutBlockDefinition definition,Scene scene,Vector3 origin,float yaw,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,
            Func<GameObject,IEnumerable<Bounds>,bool> placementConflict = null,float? cellStep = null)
            => CreateCore(definition,scene,origin,yaw,dimensions,material,openings,placementConflict,cellStep,true);

        /// <summary>Штатная геометрия рецепта в отдельной PhysicsScene, без пользовательского Undo.</summary>
        public static GameObject CreatePreview(BlockoutBlockDefinition definition,Scene scene,MapGrowthBlockRecipe recipe,float cellStep=.3f)
        {
            if(recipe==null||definition==null||recipe.ShapeId!=definition.shapeId||!scene.IsValid()||!scene.isLoaded
                ||scene.GetPhysicsScene()==Physics.defaultPhysicsScene)
                throw new ArgumentException("Preview требует рецепт этой формы и отдельную загруженную PhysicsScene.");
            if(!BlockoutSectionFactory.Validate(definition,recipe.Dimensions,recipe.CopySections(),out string reason))throw new ArgumentException(reason);
            // InstantiatePrefab создаёт корень в scene; SetParent секций переносит их туда до добавления геометрии.
            // Active scene не меняется, рабочая сцена не получает Undo/dirty-записей.
            return CreateCore(definition,scene,recipe.BottomCenter,recipe.Yaw,recipe.Dimensions,recipe.CopySections()[0].material,
                BlockoutOpeningSettings.Default,null,cellStep,false,recipe);
        }

        /// <summary>Тот же рецепт/нижний центр/секции в рабочей карте, с пользовательским Undo.</summary>
        public static GameObject CreateRecipe(BlockoutBlockDefinition definition,Scene scene,MapGrowthBlockRecipe recipe,float cellStep=.3f)
        {
            if(recipe==null||definition==null||recipe.ShapeId!=definition.shapeId||!scene.IsValid()||!scene.isLoaded)
                throw new ArgumentException("Нужны рецепт этой формы и загруженная сцена.");
            if(!BlockoutSectionFactory.Validate(definition,recipe.Dimensions,recipe.CopySections(),out string reason))throw new ArgumentException(reason);
            return CreateCore(definition,scene,recipe.BottomCenter,recipe.Yaw,recipe.Dimensions,recipe.CopySections()[0].material,
                BlockoutOpeningSettings.Default,null,cellStep,true,recipe);
        }

        private static GameObject CreateCore(BlockoutBlockDefinition definition,Scene scene,Vector3 origin,float yaw,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,
            Func<GameObject,IEnumerable<Bounds>,bool> placementConflict,float? cellStep,bool recordUndo,MapGrowthBlockRecipe preview=null)
        {
            if(Current==null||!Current.Definitions.Contains(definition))throw new ArgumentException("Новые блоки создаются только из активного канонического реестра. Старые определения доступны для редактирования существующих объектов.");
            if(!ValidatePlacement(definition,dimensions,material,openings,yaw,out string reason,cellStep)) throw new ArgumentException(reason);
            float step=cellStep??BlockoutGrid.Cell;
            // Клетки прямой формы не являются физической геометрией или источником её толщины.
            var wallCells=new List<Vector2Int>();
            if(recordUndo)Undo.IncrementCurrentGroup();int group=recordUndo?Undo.GetCurrentGroup():-1;GameObject go=null;
            try
            {
                go=(GameObject)PrefabUtility.InstantiatePrefab(definition.geometryPrefab,scene);
                // Вся новая иерархия готовится без промежуточных Undo.AddComponent/создания секций.
                // Единственная запись создания ниже сохраняет окончательные якорь, рецепт и ссылки дочерних объектов.
                // Экземпляр хранит связь с definition, чтобы свойства не зависели от старого варианта префаба.
                if(definition.supportsCellWall) PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                go.transform.localScale=Vector3.one;
                if(definition.supportsCellWall)
                {
                    foreach(var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    go.transform.SetPositionAndRotation(origin,Quaternion.Euler(0,yaw,0));
                    var wall=go.AddComponent<BlockoutCellWall>();wall.geometryMode=BlockoutGeometryMode.ThinStraight;wall.cellSize=step;wall.yaw=0;
                    wall.length=dimensions.x;wall.wallHeight=dimensions.y;wall.thickness=dimensions.z;wall.openings=openings;
                    wall.cells=wallCells;wall.Rebuild();
                }
                else
                {
                    var stepped=go.GetComponent<BlockoutSteppedGeometry>();
                    if(stepped!=null)stepped.ApplyHeight(dimensions.y);
                    else if(definition.heightEditable)
                        BlockoutHeightGeometryEditor.Initialize(go,GeometryBounds(definition.geometryPrefab).size.y,dimensions.y,false);
                    go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(0,yaw,0));
                    go.transform.position+=origin-WorldBounds(go).min;
                }
                var instance=go.AddComponent<BlockoutBlockInstance>();instance.definition=definition;
                WriteProperties(instance,dimensions,material,openings,false);
                if(preview!=null)
                {
                    if(go.TryGetComponent<BlockoutSteppedGeometry>(out var steppedRecipe))
                    {steppedRecipe.baseSize=new Vector2(dimensions.x,dimensions.z);steppedRecipe.topSize=preview.TopSize;}
                    BlockoutSectionFactory.PrepareGeometry(go,dimensions,preview.CopySections(),false);
                    go.transform.rotation=Quaternion.Euler(0,yaw,0);
                    var local=GeometryBounds(go);
                    go.transform.position=origin-go.transform.TransformVector(new Vector3(local.center.x,local.min.y,local.center.z));
                    Physics.SyncTransforms();
                    if(recordUndo){BlockoutSectionFactory.Record(go);Undo.RegisterCreatedObjectUndo(go,"Поставить блок по реестру");Undo.CollapseUndoOperations(group);}return go;
                }
                Physics.SyncTransforms();
                var volumes=go.TryGetComponent<BlockoutCellWall>(out var cellwall)?cellwall.SolidVolumes():new[]{WorldBounds(go)};
                var stepGeometry=go.GetComponent<BlockoutSteppedGeometry>();
                var parts=cellwall!=null?cellwall.SolidParts():stepGeometry!=null?stepGeometry.WorldParts():volumes.Select(b=>new BlockoutSolidPart {center=b.center,size=b.size,rotation=Quaternion.identity});
                bool conflict=placementConflict!=null?placementConflict(go,volumes):Conflicts(go,parts);
                if(conflict) throw new InvalidOperationException(BlockoutPublishedSceneSync.InspectWarning(go)
                    ??"Проверка размещения отклонила новую геометрию.");
                if(recordUndo){BlockoutSectionFactory.Record(go);Undo.RegisterCreatedObjectUndo(go,"Поставить блок по реестру");Undo.CollapseUndoOperations(group);}return go;
            }
            catch {if(recordUndo)Undo.RevertAllDownToGroup(group);if(go!=null) Object.DestroyImmediate(go);throw;}
        }
        public static bool ApplyProperties(GameObject selected,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,out string reason,float? yaw=null,bool allowContainedPublishedResize=false,bool forcePublishedResize=false)
        {
            selected=BlockoutSectionFactory.Root(selected);
            var sectional=selected!=null?selected.GetComponent<BlockoutBlockInstance>():null;
            if(sectional!=null&&sectional.HasSections)
                return BlockoutSectionFactory.Apply(selected,dimensions,BlockoutSectionSettings.Copy(sectional.sections),out reason,forcePublishedResize,yaw);
            if(BlockoutBlockRoles.IsProtection(selected))
            {reason="Защитная оболочка меняется только через подтверждение физической арены.";return false;}
            if(!TryDefinition(selected,out var definition)) {reason="Объект не принадлежит реестру блоков.";return false;}
            if(!Validate(definition,dimensions,material,openings,out reason)) return false;
            if(yaw.HasValue&&(float.IsNaN(yaw.Value)||float.IsInfinity(yaw.Value))) {reason="Угол должен быть конечным.";return false;}
            var wall=selected.GetComponent<BlockoutCellWall>();
            if(definition.supportsCellWall&&wall==null && (openings.enabled||Mathf.Abs(dimensions.x-DefaultDimensions(definition).x)>.0001f||Mathf.Abs(dimensions.z-DefaultDimensions(definition).z)>.0001f))
            {reason="Для изменения формы или щелей сначала переведите старую стену в клеточную геометрию.";return false;}
            List<Vector2Int> cells=null;
            BlockoutGeometryMode mode=wall!=null?wall.geometryMode:BlockoutGeometryMode.LegacyCellUnion;
            if(wall!=null)
            {
                float proposedYaw=yaw??wall.RotationYaw;
                bool changed=Mathf.Abs(wall.length-dimensions.x)>.0001f||Mathf.Abs(wall.thickness-dimensions.z)>.0001f;
                cells=new List<Vector2Int>(wall.cells);
                if(changed)mode=BlockoutGeometryMode.ThinStraight;
                if(openings.enabled&&!BlockoutCellWall.HasEffectiveOpenings(cells,wall.cellSize,dimensions.y,openings,mode,dimensions.x,dimensions.z,proposedYaw)) {reason="Ни одна щель не помещается между концевыми стойками.";return false;}
                var localParts=BlockoutCellWall.SolidParts(cells,Vector3.zero,wall.cellSize,dimensions.y,openings,mode,dimensions.x,dimensions.z,wall.yaw);
                var proposedParts=BlockoutCellWall.TransformParts(localParts,wall.transform.position,Quaternion.Euler(0,proposedYaw-wall.yaw,0)).ToArray();
                var oldMaterial=selected.GetComponent<CoverSurface>()?.Class??selected.GetComponent<BlockoutBlockInstance>()?.material??definition.defaultMaterial;
                bool containedResize=allowContainedPublishedResize&&!yaw.HasValue&&Mathf.Abs(wall.wallHeight-dimensions.y)<.00001f
                    &&oldMaterial==material&&JsonUtility.ToJson(wall.openings)==JsonUtility.ToJson(openings)
                    &&IsGeometrySubset(proposedParts,wall.SolidParts());
                if(!forcePublishedResize&&!containedResize&&Conflicts(selected,proposedParts))
                {reason="Новая геометрия пересекает препятствие или выходит за доступный пол.";return false;}
            }
            else if(definition.heightEditable)
            {
                if(selected.GetComponent<BlockoutSteppedGeometry>()==null&&!BlockoutHeightGeometryEditor.CanResize(selected,out reason))return false;
                if(!forcePublishedResize&&HeightConflict(selected,dimensions.y,out reason))return false;
            }
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.RegisterFullObjectHierarchyUndo(selected,"Изменить свойства блока");
            var instance=selected.GetComponent<BlockoutBlockInstance>()??Undo.AddComponent<BlockoutBlockInstance>(selected);instance.definition=definition;
            if(wall!=null)
            {
                bool geometryChanged=wall.geometryMode!=mode||wall.length!=dimensions.x||wall.thickness!=dimensions.z||wall.wallHeight!=dimensions.y
                    ||!JsonUtility.ToJson(wall.openings).Equals(JsonUtility.ToJson(openings));
                wall.geometryMode=mode;wall.length=dimensions.x;wall.thickness=dimensions.z;wall.wallHeight=dimensions.y;wall.openings=openings;wall.cells=cells;
                if(yaw.HasValue)wall.transform.rotation=Quaternion.Euler(0,yaw.Value-wall.yaw,0);
                if(geometryChanged)wall.Rebuild();
            }
            else if(definition.heightEditable)
            {
                var stepped=selected.GetComponent<BlockoutSteppedGeometry>();
                if(stepped!=null)stepped.ApplyHeight(dimensions.y);
                else
                {
                    var heightGeometry=selected.GetComponent<BlockoutHeightGeometry>();
                    float baseHeight=heightGeometry!=null&&heightGeometry.Initialized?heightGeometry.BaseHeight:GeometryBounds(selected).size.y;
                    BlockoutHeightGeometryEditor.Initialize(selected,baseHeight,dimensions.y);
                }
            }
            WriteProperties(instance,dimensions,material,openings);EditorUtility.SetDirty(selected);Undo.CollapseUndoOperations(group);return true;
        }
        /// <summary>Явное применение опубликованного источника к прежнему объекту; identity, поза и параметры экземпляра сохраняются.</summary>
        public static bool ApplyPublishedDimensions(GameObject selected,BlockoutBlockDefinition definition,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,out string reason,bool force=false,bool normalizeBottomAnchor=false)
        {
            selected=BlockoutSectionFactory.Root(selected);
            var sectional=selected!=null?selected.GetComponent<BlockoutBlockInstance>():null;
            if(sectional!=null&&sectional.HasSections)
                return BlockoutSectionFactory.Apply(selected,dimensions,BlockoutSectionSettings.Copy(sectional.sections),out reason,force,
                    publishedSource:definition.geometryPrefab,normalizeBottomAnchor:normalizeBottomAnchor);
            if(selected==null||BlockoutBlockRoles.IsProtection(selected)){reason="Защиты физической арены обновляются через вкладку «Арена».";return false;}
            if(!TryDefinition(selected,out var current)||current!=definition){reason="Объект не связан с указанным определением.";return false;}
            if((selected.transform.lossyScale-Vector3.one).sqrMagnitude>.000001f){reason="Явное обновление размеров требует единичного мирового масштаба; масштаб родителя не исправляется автоматически.";return false;}
            if(!BlockoutGrid.TryFloor(selected.scene,out var floor)){reason="Арена и пол не настроены.";return false;}
            float publishedRootY=normalizeBottomAnchor?Mathf.Max(WorldBounds(selected).min.y,floor.max.y):selected.transform.position.y;
            if(selected.GetComponent<BlockoutCellWall>()!=null)
            {
                bool applied=ApplyProperties(selected,dimensions,material,openings,out reason,allowContainedPublishedResize:true,forcePublishedResize:force);
                if(applied&&normalizeBottomAnchor)
                {
                    Undo.RecordObject(selected.transform,"Нормализовать нижний якорь блока");
                    var position=selected.transform.position;position.y=publishedRootY;selected.transform.position=position;
                    EditorUtility.SetDirty(selected.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(selected.transform);
                }
                return applied;
            }
            var published=DefaultDimensions(definition);
            if(definition.supportsCellWall&&(openings.enabled||Mathf.Abs(dimensions.x-published.x)>.0001f||Mathf.Abs(dimensions.z-published.z)>.0001f))
            {reason="Нестандартные размеры или щели старого префаба требуют явного перевода в параметрическую геометрию; скрытая смена pivot не выполняется.";return false;}
            if(!Validate(definition,dimensions,material,openings,out reason))return false;
            selected.GetComponent<BlockoutHeightGeometry>()?.FreezeSourceGeometry();
            var previewScene=EditorSceneManager.NewPreviewScene();GameObject probe=null;
            try
            {
                probe=Object.Instantiate(selected);probe.hideFlags=HideFlags.HideAndDontSave;SceneManager.MoveGameObjectToScene(probe,previewScene);
                AdoptPublishedSource(probe,definition,dimensions.y,false,normalizeBottomAnchor);
                if(normalizeBottomAnchor){var position=probe.transform.position;position.y=publishedRootY;probe.transform.position=position;}
                Physics.SyncTransforms();
                bool hasStep=probe.TryGetComponent<BlockoutSteppedGeometry>(out var step);
                var oldMaterial=selected.GetComponent<CoverSurface>()?.Class??selected.GetComponent<BlockoutBlockInstance>()?.material??definition.defaultMaterial;
                var oldOpenings=selected.GetComponent<BlockoutBlockInstance>()?.openings??BlockoutOpeningSettings.Default;
                bool containedStep=hasStep&&selected.TryGetComponent<BlockoutSteppedGeometry>(out var oldStep)
                    &&Mathf.Abs(oldStep.TotalHeight-dimensions.y)<.00001f&&oldMaterial==material
                    &&JsonUtility.ToJson(oldOpenings)==JsonUtility.ToJson(openings)&&IsGeometrySubset(step.WorldParts(),oldStep.WorldParts());
                bool conflict=!force&&!containedStep&&(hasStep
                    ?BlockoutWallEditing.Conflict(selected.scene,floor,step.WorldParts(),selected)
                    :BlockoutWallEditing.PrefabConflictAtYaw(probe,selected.scene,floor,WorldBounds(probe),selected.transform.eulerAngles.y,selected));
                if(conflict){reason="Обновлённая геометрия пересекает препятствие или выходит за доступный пол.";return false;}
            }
            catch(ArgumentException error){reason=error.Message;return false;}
            catch(InvalidOperationException error){reason=error.Message;return false;}
            finally{if(probe!=null)Object.DestroyImmediate(probe);EditorSceneManager.ClosePreviewScene(previewScene);}
            Undo.RegisterFullObjectHierarchyUndo(selected,"Применить размеры опубликованной формы");
            AdoptPublishedSource(selected,definition,dimensions.y,true,normalizeBottomAnchor);
            if(normalizeBottomAnchor){var position=selected.transform.position;position.y=publishedRootY;selected.transform.position=position;}
            var instance=selected.GetComponent<BlockoutBlockInstance>()??Undo.AddComponent<BlockoutBlockInstance>(selected);instance.definition=definition;
            WriteProperties(instance,dimensions,material,openings);EditorUtility.SetDirty(selected);reason=null;return true;
        }
        private static void AdoptPublishedSource(GameObject target,BlockoutBlockDefinition definition,float height,bool recordUndo,bool normalizeBottomAnchor=false)
        {
            var source=definition.geometryPrefab;
            float preservedRootBottom=normalizeBottomAnchor?0f:GeometryBounds(target).min.y;
            if(source.TryGetComponent<BlockoutSteppedGeometry>(out var sourceStep))
            {
                var step=target.GetComponent<BlockoutSteppedGeometry>();
                if(step==null)throw new InvalidOperationException("У экземпляра отсутствует профиль ступенчатой формы.");
                step.baseSize=sourceStep.baseSize;step.topSize=sourceStep.topSize;step.lowerHeightLimit=sourceStep.lowerHeightLimit;step.localBottomCenter=sourceStep.localBottomCenter;
                step.localBottomCenter.y=preservedRootBottom;
                step.ApplyHeight(height);return;
            }
            var geometry=target.GetComponent<BlockoutHeightGeometry>();
            if(geometry!=null)geometry.ReleaseEditorMeshes();
            foreach(var sourceFilter in source.GetComponentsInChildren<MeshFilter>(true))
                CorrespondingComponent(target,source,sourceFilter).sharedMesh=sourceFilter.sharedMesh;
            foreach(var sourceCollider in source.GetComponentsInChildren<MeshCollider>(true))
            {
                var collider=CorrespondingComponent(target,source,sourceCollider);
                collider.sharedMesh=sourceCollider.sharedMesh;collider.convex=sourceCollider.convex;
            }
            foreach(var sourceBox in source.GetComponentsInChildren<BoxCollider>(true))
            {
                var box=CorrespondingComponent(target,source,sourceBox);box.center=sourceBox.center;box.size=sourceBox.size;
            }
            if(!BlockoutHeightGeometryEditor.CanResize(target,out var reason))throw new ArgumentException(reason);
            if(geometry==null)geometry=recordUndo?Undo.AddComponent<BlockoutHeightGeometry>(target):target.AddComponent<BlockoutHeightGeometry>();
            geometry.CapturePublishedSource(GeometryBounds(source).size.y,height,preservedRootBottom);
        }
        private static T CorrespondingComponent<T>(GameObject target,GameObject source,T component) where T:Component
        {
            var indices=new Stack<int>();var node=component.transform;
            while(node!=source.transform){indices.Push(node.GetSiblingIndex());node=node.parent;if(node==null)throw new InvalidOperationException("Компонент находится вне источника.");}
            var corresponding=target.transform;
            foreach(int index in indices)
            {if(index>=corresponding.childCount)throw new InvalidOperationException("Иерархия экземпляра отличается от опубликованной формы.");corresponding=corresponding.GetChild(index);}
            int componentIndex=Array.IndexOf(component.GetComponents<T>(),component);var components=corresponding.GetComponents<T>();
            if(componentIndex<0||componentIndex>=components.Length)throw new InvalidOperationException("Состав компонентов экземпляра отличается от опубликованной формы.");
            return components[componentIndex];
        }
        /// <summary>Достаточное точное доказательство отсутствия нового объёма: каждая новая призма целиком внутри одной прежней.</summary>
        public static bool IsGeometrySubset(IEnumerable<BlockoutSolidPart> proposed,IEnumerable<BlockoutSolidPart> existing)
        {
            var oldParts=existing.ToArray();var newParts=proposed.ToArray();
            if(oldParts.Length==0||newParts.Length==0)return false;
            foreach(var part in newParts)
            {
                bool contained=false;
                foreach(var old in oldParts)
                {
                    var inverse=Quaternion.Inverse(old.rotation);bool inside=true;
                    for(int x=-1;x<=1&&inside;x+=2)for(int y=-1;y<=1&&inside;y+=2)for(int z=-1;z<=1&&inside;z+=2)
                    {
                        var corner=part.center+part.rotation*Vector3.Scale(part.size*.5f,new Vector3(x,y,z));
                        var local=inverse*(corner-old.center);
                        if(Mathf.Abs(local.x)>old.size.x*.5f+.00001f||Mathf.Abs(local.y)>old.size.y*.5f+.00001f||Mathf.Abs(local.z)>old.size.z*.5f+.00001f)inside=false;
                    }
                    if(inside){contained=true;break;}
                }
                if(!contained)return false;
            }
            return true;
        }
        /// <summary>Точная проверка новой высоты на временной копии до изменения экземпляра и записи Undo.</summary>
        private static bool HeightConflict(GameObject selected,float height,out string reason)
        {
            reason=null;
            var previewScene=EditorSceneManager.NewPreviewScene();
            var probe=Object.Instantiate(selected);probe.hideFlags=HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(probe,previewScene);
            try
            {
                var stepped=probe.GetComponent<BlockoutSteppedGeometry>();
                if(stepped!=null)stepped.ApplyHeight(height);
                else
                {
                    var geometry=probe.GetComponent<BlockoutHeightGeometry>()??probe.AddComponent<BlockoutHeightGeometry>();
                    if(geometry.Initialized)geometry.ApplyHeight(height);else geometry.Initialize(GeometryBounds(selected).size.y,height);
                }
                Physics.SyncTransforms();
                Bounds bounds=WorldBounds(probe);
                bool conflict=GeometryConflict!=null?GeometryConflict(selected,new[]{bounds}):PreciseGeometryConflict!=null
                    &&(!BlockoutGrid.TryFloor(selected.scene,out var floor)
                        ||BlockoutWallEditing.PrefabConflictAtYaw(probe,selected.scene,floor,bounds,selected.transform.eulerAngles.y,selected));
                if(conflict)reason="Новая высота пересекает препятствие или выходит за доступный пол.";
                return conflict;
            }
            finally {Object.DestroyImmediate(probe);EditorSceneManager.ClosePreviewScene(previewScene);}
        }
        public static bool ValidateCells(GameObject go,IEnumerable<Vector2Int> cells,out string reason)
        {
            go=BlockoutSectionFactory.Root(go);
            if(BlockoutBlockRoles.IsProtection(go))
            {reason="Защитную оболочку нельзя уменьшать обычной клеточной кистью.";return false;}
            var wall=go!=null?go.GetComponent<BlockoutCellWall>():null;
            if(wall==null||!TryDefinition(go,out var definition)||!definition.supportsCellWall) {reason="Объект не является клеточной формой реестра.";return false;}
            if(wall.geometryMode!=BlockoutGeometryMode.LegacyCellUnion) {reason="Прямая форма редактируется размерами и щелями; маска затронутых клеток не превращается в её геометрию.";return false;}
            var proposed=cells.Distinct().ToList();
            if(!BlockoutCellWall.Connected(proposed)) {reason="Клетки должны образовывать одну непустую связанную форму.";return false;}
            if(!ValidateOpenings(proposed,wall.cellSize,wall.wallHeight,wall.openings,out reason)) return false;
            if(Conflicts(go,BlockoutCellWall.TransformParts(BlockoutCellWall.SolidParts(proposed,Vector3.zero,wall.cellSize,wall.wallHeight,wall.openings,BlockoutGeometryMode.LegacyCellUnion,wall.length,wall.thickness,wall.yaw),go.transform.position,go.transform.rotation)))
            {reason="Изменённая форма пересекает препятствие или выходит за доступный пол.";return false;}
            return true;
        }
        public static bool ApplyCells(GameObject go,IEnumerable<Vector2Int> cells,out string reason)
        {
            go=BlockoutSectionFactory.Root(go);
            var proposed=cells.Distinct().ToList();
            if(!ValidateCells(go,proposed,out reason))return false;
            var wall=go.GetComponent<BlockoutCellWall>();
            TryDefinition(go,out var definition);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.RegisterFullObjectHierarchyUndo(go,"Изменить клетки блока");
            wall.geometryMode=BlockoutGeometryMode.LegacyCellUnion;wall.cells=proposed;wall.Rebuild();
            var instance=go.GetComponent<BlockoutBlockInstance>()??Undo.AddComponent<BlockoutBlockInstance>(go);instance.definition=definition;
            var material=go.GetComponent<CoverSurface>()?.Class??definition.defaultMaterial;
            WriteProperties(instance,new Vector3(wall.length,wall.wallHeight,wall.thickness),material,wall.openings);
            EditorUtility.SetDirty(wall);EditorUtility.SetDirty(go);Undo.CollapseUndoOperations(group);return true;
        }
        /// <summary>Явное редактирование позы применяется под Undo; геометрические конфликты становятся диагностикой.</summary>
        public static bool ApplyTransform(GameObject go,Vector3 position,float yaw,out string reason)
        {
            go=BlockoutSectionFactory.Root(go);
            reason=null;
            if(go==null||BlockoutBlockRoles.IsProtection(go)||!TryDefinition(go,out var definition))
            {reason="Выберите игровой блок реестра; защита меняется через арену.";return false;}
            if(!BlockoutGrid.TryOrigin(go.scene,out var origin,out _)||!BlockoutGrid.TryFloor(go.scene,out var floor))
            {reason="Источник сетки и пола отсутствует.";return false;}
            if(float.IsNaN(position.x)||float.IsNaN(position.y)||float.IsNaN(position.z)||float.IsInfinity(position.x)||float.IsInfinity(position.y)||float.IsInfinity(position.z)||float.IsNaN(yaw)||float.IsInfinity(yaw))
            {reason="Положение и угол должны быть конечными.";return false;}
            // Намерение перемещения фиксируется до возможной миграции прежнего якоря.
            var requestedDelta=position-go.transform.position;bool moved=requestedDelta.x*requestedDelta.x+requestedDelta.z*requestedDelta.z>.0000000001f;
            Undo.RegisterFullObjectHierarchyUndo(go,"Переместить / повернуть блок");
            if(definition.heightEditable&&definition.gameplayGeometry&&!BlockoutSectionGeometry.Owns(go))
            {
                var instance=go.GetComponent<BlockoutBlockInstance>();
                if(instance==null)
                {
                    instance=Undo.AddComponent<BlockoutBlockInstance>(go);instance.definition=definition;
                    var oldWall=go.GetComponent<BlockoutCellWall>();
                    instance.dimensions=oldWall!=null?new Vector3(oldWall.length,oldWall.wallHeight,oldWall.thickness):GeometryBounds(go).size;
                    instance.material=go.GetComponent<CoverSurface>()?.Class??definition.defaultMaterial;
                    instance.openings=oldWall!=null?oldWall.openings:BlockoutOpeningSettings.Default;
                }
                BlockoutSectionFactory.Ensure(instance);
            }
            go.GetComponent<BlockoutSectionGeometry>()?.Rebuild();
            if(BlockoutSectionGeometry.Owns(go))position=go.transform.position+requestedDelta;
            var wall=go.GetComponent<BlockoutCellWall>();float step=wall!=null?wall.cellSize:BlockoutGrid.Cell;
            if(moved)
            {
                position.x=origin.x+Mathf.Round((position.x-origin.x)/step)*step;
                position.z=origin.y+Mathf.Round((position.z-origin.y)/step)*step;
            }
            Quaternion rotation=wall!=null?Quaternion.Euler(0,yaw-wall.yaw,0)
                :Quaternion.Euler(0,Mathf.DeltaAngle(go.transform.eulerAngles.y,yaw),0)*go.transform.rotation;
            if(wall==null&&!BlockoutSectionGeometry.Owns(go))
            {
                var currentDimensions=go.GetComponent<BlockoutBlockInstance>()?.dimensions??DefaultDimensions(definition);
                currentDimensions.y=WorldBounds(go).size.y;
                var proposed=PreviewVolumes(definition,position,yaw,currentDimensions,BlockoutOpeningSettings.Default).First();
                // Для штатного префаба position задаёт опорный угол его мирового envelope.
                Vector3 localMinimum=rotation*GeometryBounds(go).center-proposed.extents;
                position=proposed.min-localMinimum;
            }
            go.transform.SetPositionAndRotation(position,rotation);
            EditorUtility.SetDirty(go);EditorUtility.SetDirty(go.transform);
            if(PrefabUtility.IsPartOfPrefabInstance(go.transform))PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            Physics.SyncTransforms();reason=BlockoutEditDiagnostics.Refresh(go,true);return true;
        }
        /// <summary>Перевод существующего объекта сохраняет его identity и сторонние компоненты под полной Undo-операцией.</summary>
        public static bool AdoptCellWall(GameObject go,BlockoutBlockDefinition definition,Bounds envelope,float yaw,CoverClass material,BlockoutOpeningSettings openings,out string reason)
        {
            go=BlockoutSectionFactory.Root(go);
            if(go==null) {reason="Объект отсутствует.";return false;}
            if(go.GetComponent<BlockoutBlockInstance>()?.HasSections==true)
            {reason="Секционный блок уже использует единый владелец геометрии. Его профиль и щели изменяются в свойствах секций; повторный клеточный перевод недоступен.";return false;}
            if(BlockoutBlockRoles.IsProtection(go)) {reason="Защитная оболочка меняется через физическую арену.";return false;}
            if(!Validate(definition,envelope.size,material,openings,out reason)) return false;
            if(!definition.supportsCellWall) {reason="Определение не поддерживает клеточную геометрию.";return false;}
            float step=BlockoutGrid.Cell;
            var cells=new List<Vector2Int>();
            if(!ValidatePlacement(definition,envelope.size,material,openings,yaw,out reason,step))return false;
            if(Conflicts(go,BlockoutCellWall.SolidParts(cells,envelope.min,step,envelope.size.y,openings,BlockoutGeometryMode.ThinStraight,envelope.size.x,envelope.size.z,yaw)))
            {reason="Клеточная геометрия пересекает препятствие или выходит за доступный пол.";return false;}
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.RegisterFullObjectHierarchyUndo(go,"Перевести блок в клеточную геометрию");
            foreach(var collider in go.GetComponentsInChildren<Collider>(true))
                if(!(go.GetComponent<BlockoutCellWall>()!=null&&collider.gameObject==go&&collider is MeshCollider)) Undo.DestroyObjectImmediate(collider);
            foreach(var renderer in go.GetComponentsInChildren<Renderer>(true)) if(renderer.gameObject!=go) renderer.enabled=false;
            go.transform.SetPositionAndRotation(envelope.min,Quaternion.Euler(0,yaw,0));go.transform.localScale=Vector3.one;
            var wall=go.GetComponent<BlockoutCellWall>()??Undo.AddComponent<BlockoutCellWall>(go);
            wall.geometryMode=BlockoutGeometryMode.ThinStraight;wall.cellSize=step;wall.length=envelope.size.x;wall.thickness=envelope.size.z;wall.wallHeight=envelope.size.y;wall.yaw=0;wall.openings=openings;wall.cells=cells;
            // RequireComponent не добавляет заново ранее удалённый collider у существующей стены.
            if(go.GetComponent<MeshCollider>()==null) Undo.AddComponent<MeshCollider>(go);
            wall.Rebuild();
            var instance=go.GetComponent<BlockoutBlockInstance>()??Undo.AddComponent<BlockoutBlockInstance>(go);instance.definition=definition;
            WriteProperties(instance,envelope.size,material,openings);EditorUtility.SetDirty(go);Undo.CollapseUndoOperations(group);return true;
        }
        private static bool ValidateOpenings(IEnumerable<Vector2Int> cells,float step,float height,BlockoutOpeningSettings openings,out string reason)
        {
            reason=openings.enabled&&!BlockoutCellWall.HasEffectiveOpenings(cells,step,height,openings)?"Ни одна щель не помещается между концевыми стойками при текущих размерах и шаге.":null;
            return reason==null;
        }
        public static IEnumerable<Bounds> PreviewVolumes(BlockoutBlockDefinition definition,Vector3 origin,float yaw,Vector3 dimensions,BlockoutOpeningSettings openings,float? cellStep=null)
        {
            float step=cellStep??BlockoutGrid.Cell;
            if(!FinitePositive(step)) throw new ArgumentException("Шаг клеток должен быть положительным конечным числом.");
            if(definition.supportsCellWall)
                return PreviewParts(definition,origin,yaw,dimensions,openings,step).Select(p=>p.BroadphaseBounds);
            Vector3 size=GeometryBounds(definition.geometryPrefab).size;
            if(definition.heightEditable)size.y=dimensions.y;
            Quaternion rotation=Quaternion.Euler(0,yaw,0);
            Vector3 u=rotation*new Vector3(size.x,0,0),v=rotation*new Vector3(0,0,size.z);
            Vector3 envelope=new Vector3(Mathf.Abs(u.x)+Mathf.Abs(v.x),size.y,Mathf.Abs(u.z)+Mathf.Abs(v.z));
            return new[]{new Bounds(origin+envelope/2,envelope)};
        }
        public static IEnumerable<BlockoutSolidPart> PreviewParts(BlockoutBlockDefinition definition,Vector3 origin,float yaw,Vector3 dimensions,BlockoutOpeningSettings openings,float? cellStep=null)
        {
            if(definition.supportsCellWall)return BlockoutCellWall.SolidParts(null,origin,cellStep??BlockoutGrid.Cell,dimensions.y,openings,BlockoutGeometryMode.ThinStraight,dimensions.x,dimensions.z,yaw);
            var stepped=definition.geometryPrefab.GetComponent<BlockoutSteppedGeometry>();
            if(stepped!=null)return SteppedParts(stepped,origin,yaw,dimensions.y);
            return PreviewVolumes(definition,origin,yaw,dimensions,openings,cellStep).Select(b=>new BlockoutSolidPart {center=b.center,size=b.size,rotation=Quaternion.identity});
        }
        /// <summary>Опорный угол envelope преобразуется в pivot профиля; занятость плеч не расширяется до AABB.</summary>
        public static IEnumerable<BlockoutSolidPart> SteppedParts(BlockoutSteppedGeometry stepped,Vector3 envelopeMin,float yaw,float height)
        {
            var local=stepped.LocalPartsAtHeight(height).ToArray();var bounds=local[0];
            foreach(var part in local.Skip(1))bounds.Encapsulate(part);
            Quaternion rotation=Quaternion.Euler(0,yaw,0);
            Vector3 u=rotation*new Vector3(bounds.size.x,0,0),v=rotation*new Vector3(0,0,bounds.size.z);
            Vector3 size=new Vector3(Mathf.Abs(u.x)+Mathf.Abs(v.x),bounds.size.y,Mathf.Abs(u.z)+Mathf.Abs(v.z));
            Vector3 pivot=envelopeMin-rotation*bounds.center+size/2;
            return stepped.WorldPartsAtHeight(height,pivot,rotation);
        }
        private static void WriteProperties(BlockoutBlockInstance instance,Vector3 dimensions,CoverClass material,BlockoutOpeningSettings openings,bool recordUndo=true)
        {
            instance.dimensions=dimensions;instance.material=material;instance.openings=openings;
            if(instance.definition.heightEditable && instance.definition.gameplayGeometry)
            {BlockoutSectionFactory.Ensure(instance,recordUndo);return;}
            var root=instance.gameObject;
            root.name=instance.definition.title+"_"+material;
            var source=instance.definition.materialVariants.FirstOrDefault(v=>v.material==material)?.sourcePrefab??instance.definition.geometryPrefab;
            var sourceRenderers=source.GetComponentsInChildren<Renderer>(true);var targetRenderers=root.GetComponentsInChildren<Renderer>(true);
            for(int i=0;i<targetRenderers.Length&&sourceRenderers.Length>0;i++) targetRenderers[i].sharedMaterials=sourceRenderers[Mathf.Min(i,sourceRenderers.Length-1)].sharedMaterials;
            ApplyDisplayMaterial(root,material);
            foreach(var surface in root.GetComponentsInChildren<CoverSurface>(true))
            {
                surface.gameObject.name=ClassSuffix.Replace(surface.gameObject.name,"_"+material);
                if(surface.gameObject!=root&&!CoverClassRules.TryParse(surface.gameObject.name,out _)) surface.gameObject.name+="_"+material;
                CoverClassRules.TryExpectedClass(surface.gameObject,out var expected);surface.Class=expected;
            }
            var rootSurface=root.GetComponent<CoverSurface>()??(recordUndo?Undo.AddComponent<CoverSurface>(root):root.AddComponent<CoverSurface>());
            CoverClassRules.TryExpectedClass(root,out var rootClass);rootSurface.Class=rootClass;
            var sourceSurface=source.GetComponent<CoverSurface>();if(sourceSurface!=null) rootSurface.PenetrationModifier=sourceSurface.PenetrationModifier;
            foreach(var component in root.GetComponentsInChildren<Component>(true))
                if(component!=null)
                {
                    EditorUtility.SetDirty(component);
                    if(PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            if(PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }
        /// <summary>Цвет класса не зависит от признака перешагивания и исходного художественного материала.</summary>
        public static void ApplyDisplayMaterial(GameObject root,CoverClass material)
        {
            if(material==CoverClass.Visual)return;
            string path="Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/"+(material==CoverClass.Hard?"GridBlue_01_Mat":"GreyBlue_Mat")+".mat";
            var canonical=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(canonical==null)throw new InvalidOperationException("Отсутствует эталонный материал класса: "+path);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials=Enumerable.Repeat(canonical,Mathf.Max(1,renderer.sharedMaterials.Length)).ToArray();
        }
        public static Bounds GeometryBounds(GameObject root)
        {
            bool persistent=EditorUtility.IsPersistent(root);
            var dependencyHash=persistent?AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(root)):default;
            if(persistent&&BoundsCache.TryGetValue(root,out var cached)&&cached.dependencyHash==dependencyHash) return cached.bounds;
            bool first=true;var bounds=new Bounds();
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh!=null&&(!BlockoutSectionGeometry.Owns(root)||filter.GetComponentInParent<BlockoutSectionPart>()!=null))
                    foreach(var point in filter.sharedMesh.vertices)
                    {var p=root.transform.InverseTransformPoint(filter.transform.TransformPoint(point));if(first){bounds=new Bounds(p,Vector3.zero);first=false;}else bounds.Encapsulate(p);}
            if(persistent)BoundsCache[root]=new SourceBoundsEntry {bounds=bounds,dependencyHash=dependencyHash};return bounds;
        }
        private static Bounds WorldBounds(GameObject root)
        {
            return BlockoutPainter.WorldBounds(root);
        }
        private static string GeometryKey(GameObject prefab)
        {
            Vector2 plan=BlockoutGridSettings.Dimensions(prefab.name);
            if(prefab.name.StartsWith("LD_Wall_",StringComparison.Ordinal)||IsThinPrism(prefab,plan)) return (ClassOf(prefab)==CoverClass.Visual?"visual-prism:":"wall:")+Mathf.RoundToInt(plan.x*10000)+":"+Mathf.RoundToInt(plan.y*10000)+":"+Mathf.RoundToInt(GeometryBounds(prefab).size.y*10000);
            using(var bytes=new MemoryStream()) using(var writer=new BinaryWriter(bytes))
            {
                writer.Write(plan.x);writer.Write(plan.y);
                foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(filter.sharedMesh==null) continue;
                    writer.Write(filter.sharedMesh.vertexCount);
                    foreach(var vertex in filter.sharedMesh.vertices)
                    {var point=prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));writer.Write(point.x);writer.Write(point.y);writer.Write(point.z);}
                    foreach(int index in filter.sharedMesh.triangles) writer.Write(index);
                }
                foreach(var collider in prefab.GetComponentsInChildren<Collider>(true))
                {
                    writer.Write(collider.GetType().Name);writer.Write(collider.enabled);writer.Write(collider.isTrigger);
                    Matrix4x4 matrix=prefab.transform.worldToLocalMatrix*collider.transform.localToWorldMatrix;
                    for(int row=0;row<4;row++) for(int column=0;column<4;column++) writer.Write(matrix[row,column]);
                    if(collider is BoxCollider box) {writer.Write(box.center.x);writer.Write(box.center.y);writer.Write(box.center.z);writer.Write(box.size.x);writer.Write(box.size.y);writer.Write(box.size.z);}
                    if(collider is SphereCollider sphere) {writer.Write(sphere.center.x);writer.Write(sphere.center.y);writer.Write(sphere.center.z);writer.Write(sphere.radius);}
                    if(collider is CapsuleCollider capsule) {writer.Write(capsule.center.x);writer.Write(capsule.center.y);writer.Write(capsule.center.z);writer.Write(capsule.radius);writer.Write(capsule.height);writer.Write(capsule.direction);}
                    if(collider is MeshCollider mesh)
                    {
                        writer.Write(mesh.convex);writer.Write(mesh.sharedMesh!=null?mesh.sharedMesh.vertexCount:0);
                        if(mesh.sharedMesh!=null)
                        {
                            foreach(var vertex in mesh.sharedMesh.vertices) {writer.Write(vertex.x);writer.Write(vertex.y);writer.Write(vertex.z);}
                            foreach(int index in mesh.sharedMesh.triangles) writer.Write(index);
                        }
                    }
                }
                writer.Flush();using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes.ToArray()));
            }
        }
        private static bool IsThinPrism(GameObject prefab,Vector2 plan)
        {
            if(plan.y>.3001f)return false;
            var colliders=prefab.GetComponentsInChildren<Collider>(true);var meshes=prefab.GetComponentsInChildren<MeshFilter>(true);
            if(colliders.Length!=1||!(colliders[0] is BoxCollider box)||box.isTrigger||!box.enabled||meshes.Length!=1||meshes[0].sharedMesh==null)return false;
            var mesh=meshes[0].sharedMesh;var bounds=GeometryBounds(prefab);
            if(mesh.triangles.Length!=36||Mathf.Abs(bounds.size.x-plan.x)>.001f||Mathf.Abs(bounds.size.z-plan.y)>.001f)return false;
            if(Quaternion.Angle(Quaternion.Inverse(prefab.transform.rotation)*box.transform.rotation,Quaternion.identity)>.001f)return false;
            var boxCenter=prefab.transform.InverseTransformPoint(box.transform.TransformPoint(box.center));
            if((boxCenter-bounds.center).sqrMagnitude>.000001f||(box.size-bounds.size).sqrMagnitude>.000001f)return false;
            var points=mesh.vertices.Select(p=>prefab.transform.InverseTransformPoint(meshes[0].transform.TransformPoint(p))).ToArray();
            foreach(var p in points)
                if(Mathf.Min(Mathf.Abs(p.x-bounds.min.x),Mathf.Abs(p.x-bounds.max.x))>.0001f
                    ||Mathf.Min(Mathf.Abs(p.y-bounds.min.y),Mathf.Abs(p.y-bounds.max.y))>.0001f
                    ||Mathf.Min(Mathf.Abs(p.z-bounds.min.z),Mathf.Abs(p.z-bounds.max.z))>.0001f)return false;
            float volume=0;var triangles=mesh.triangles;
            for(int i=0;i<triangles.Length;i+=3)volume+=Vector3.Dot(points[triangles[i]]-bounds.center,Vector3.Cross(points[triangles[i+1]]-bounds.center,points[triangles[i+2]]-bounds.center))/6;
            return Mathf.Abs(Mathf.Abs(volume)-bounds.size.x*bounds.size.y*bounds.size.z)<.0001f;
        }
    }
}
