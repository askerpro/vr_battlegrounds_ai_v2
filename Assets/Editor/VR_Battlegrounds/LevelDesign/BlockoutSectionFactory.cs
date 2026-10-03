using System;
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
    /// <summary>Полный рецепт секций применяется одной Undo-операцией; исходные ассеты не изменяются.</summary>
    [InitializeOnLoad]
    public static class BlockoutSectionFactory
    {
        static BlockoutSectionFactory()
        {
            Undo.undoRedoPerformed += Restore;
            Selection.selectionChanged += NormalizeSelection;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
        }
        private static void Restore()
        {
            foreach (var geometry in Resources.FindObjectsOfTypeAll<BlockoutSectionGeometry>())
                if (geometry != null && geometry.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(geometry)) geometry.Rebuild();
        }
        private static void NormalizeSelection()
        {
            var selected = Selection.activeGameObject; var root = Root(selected);
            if (selected != root) Selection.activeGameObject = root;
        }
        private static void Release()
        {
            foreach (var geometry in Resources.FindObjectsOfTypeAll<BlockoutSectionGeometry>())
                if (geometry != null && geometry.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(geometry)) geometry.ReleaseMeshes();
        }

        public static GameObject Root(GameObject go)
        {
            var section = go != null ? go.GetComponentInParent<BlockoutSectionPart>() : null;
            return section != null && section.Instance != null ? section.Instance.gameObject : go;
        }
        public static BlockoutSectionSettings[] Recipe(GameObject go, float height, CoverClass material, BlockoutOpeningSettings openings)
        {
            var instance = go != null ? go.GetComponent<BlockoutBlockInstance>() : null;
            return instance != null && instance.HasSections ? BlockoutSectionSettings.Copy(instance.sections)
                : BlockoutSectionSettings.FromLegacy(height, material, openings);
        }
        public static bool Validate(BlockoutBlockDefinition definition, Vector3 dimensions, BlockoutSectionSettings[] sections, out string reason)
        {
            reason = null;
            if (sections == null || sections.Length != 3 || sections.Any(s => s == null)) { reason = "Рецепт должен содержать три секции."; return false; }
            if (!BlockoutRegistryFactory.Validate(definition, dimensions, sections[0].material, default, out reason)) return false;
            for (int i = 0; i < 3; i++)
            {
                var section = sections[i]; var opening = section.openings;
                if (!definition.Allows(section.material)) { reason = BlockoutSectionSettings.Label(i) + ": материал не разрешён для формы."; return false; }
                if (!opening.enabled) continue;
                if (!definition.supportsOpenings || !definition.supportsCellWall)
                { reason = BlockoutSectionSettings.Label(i) + ": " + definition.openingDisabledReason; return false; }
                if (!Finite(opening.spacing) || !Finite(opening.width) || !Finite(opening.sillHeight) || !Finite(opening.lintelHeight)
                    || opening.spacing <= .01f || opening.width <= .001f || opening.width >= opening.spacing || opening.sillHeight < 0 || opening.lintelHeight < 0)
                { reason = BlockoutSectionSettings.Label(i) + ": ширина/шаг должны быть положительными, ширина меньше шага, основание/перемычка неотрицательны."; return false; }
                float bottom = BlockoutSectionSettings.Bottom(i), top = Mathf.Min(dimensions.y, BlockoutSectionSettings.Top(i));
                if (top <= bottom + .000001f) continue;
                if (opening.sillHeight + opening.lintelHeight >= top - bottom - .000001f)
                { reason = BlockoutSectionSettings.Label(i) + ": щель не помещается по высоте присутствующей секции."; return false; }
                float length = dimensions.x;
                bool fits = false;
                for (float center = opening.spacing / 2; center + opening.width / 2 < length - .001f; center += opening.spacing)
                    if (center - opening.width / 2 > .001f) { fits = true; break; }
                if (!fits) { reason = BlockoutSectionSettings.Label(i) + ": ни одна настоящая щель не помещается между концевыми стойками."; return false; }
            }
            return true;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>Вызывается фабрикой после создания прежней формы; конвертация общей щели не меняет её пустоту.</summary>
        public static void Ensure(BlockoutBlockInstance instance, bool recordUndo = true)
        {
            var root = instance.gameObject;
            if (!instance.HasSections) instance.sections = BlockoutSectionSettings.FromLegacy(instance.dimensions.y, instance.material, instance.openings);
            var geometry = root.GetComponent<BlockoutSectionGeometry>();
            if (geometry == null) geometry = recordUndo ? Undo.AddComponent<BlockoutSectionGeometry>(root) : root.AddComponent<BlockoutSectionGeometry>();
            bool reanchored = false;
            if (instance.definition.supportsCellWall && root.GetComponent<BlockoutCellWall>() == null)
            {
                // Старый прямоугольный prefab получает тот же параметрический профиль, что новая форма.
                // Его исходный центр переносится в локальный нижний угол без изменения мировой формы.
                var bounds = BlockoutRegistryFactory.GeometryBounds(root);
                if (!geometry.AnchorInitialized)
                {
                    root.transform.position += root.transform.TransformVector(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                    geometry.AdoptCenteredAnchor();
                }
                reanchored = true;
                var wall = recordUndo ? Undo.AddComponent<BlockoutCellWall>(root) : root.AddComponent<BlockoutCellWall>();
                wall.geometryMode = BlockoutGeometryMode.ThinStraight; wall.yaw = 0; wall.cellSize = BlockoutGrid.Cell;
                wall.length = instance.dimensions.x; wall.wallHeight = instance.dimensions.y; wall.thickness = instance.dimensions.z;
                wall.openings = instance.openings; wall.cells = new System.Collections.Generic.List<Vector2Int>();
            }
            if (!geometry.Initialized)
            {
                float oldBottom = reanchored ? 0 : BlockoutRegistryFactory.GeometryBounds(root).min.y;
                if (Mathf.Abs(oldBottom) > .000001f) root.transform.position += root.transform.up * oldBottom;
                if (root.TryGetComponent<BlockoutSteppedGeometry>(out var step)) step.localBottomCenter.y = 0;
                geometry.CaptureSource();
            }
            if (geometry.parts == null || geometry.parts.Length != 3) geometry.parts = new BlockoutSectionPart[3];
            for (int i = 0; i < 3; i++)
            {
                if (geometry.parts[i] != null) continue;
                var go = new GameObject(BlockoutSectionSettings.ChildName(i));
                go.layer = root.layer; go.isStatic = root.isStatic;
                go.transform.SetParent(root.transform, false);
                if (recordUndo) Undo.RegisterCreatedObjectUndo(go, "Создать секцию блока");
                var part = recordUndo ? Undo.AddComponent<BlockoutSectionPart>(go) : go.AddComponent<BlockoutSectionPart>();
                part.owner = geometry; part.sectionIndex = i; geometry.parts[i] = part;
            }
            var rootCover = root.GetComponent<CoverSurface>();
            if (rootCover != null) { if (recordUndo) Undo.DestroyObjectImmediate(rootCover); else Object.DestroyImmediate(rootCover); }
            root.name = instance.definition.title + "_Sections";
            geometry.Rebuild();
            foreach (var part in geometry.parts) BlockoutRegistryFactory.ApplyDisplayMaterial(part.gameObject, instance.sections[part.sectionIndex].material);
            Record(root);
        }

        /// <summary>Подготовка временной копии либо уже зарегистрированной Undo-операции без записи общего ассета.</summary>
        public static void PrepareGeometry(GameObject root, Vector3 dimensions, BlockoutSectionSettings[] sections, bool recordUndo = false)
        {
            var instance = root.GetComponent<BlockoutBlockInstance>();
            if (instance == null) throw new ArgumentException("У корня отсутствует BlockoutBlockInstance.");
            instance.dimensions = dimensions; instance.sections = BlockoutSectionSettings.Copy(sections);
            if (root.TryGetComponent<BlockoutCellWall>(out var wall))
            {
                if (Mathf.Abs(wall.length - dimensions.x) > .00001f || Mathf.Abs(wall.thickness - dimensions.z) > .00001f) wall.geometryMode = BlockoutGeometryMode.ThinStraight;
                wall.length = dimensions.x; wall.thickness = dimensions.z; wall.wallHeight = dimensions.y;
            }
            if (root.TryGetComponent<BlockoutSteppedGeometry>(out var stepped)) stepped.totalHeight = dimensions.y;
            Ensure(instance, recordUndo);
        }

        public static bool Apply(GameObject root, Vector3 dimensions, BlockoutSectionSettings[] sections, out string reason,
            bool force = false, float? yaw = null, GameObject publishedSource = null, bool normalizeBottomAnchor = false)
        {
            root = Root(root);
            if (root == null || BlockoutBlockRoles.IsProtection(root) || !BlockoutRegistryFactory.TryDefinition(root, out var definition))
            { reason = "Выберите игровой корень блока; защита изменяется через арену."; return false; }
            if (!Validate(definition, dimensions, sections, out reason)) return false;
            if (yaw.HasValue && !Finite(yaw.Value)) { reason = "Угол должен быть конечным."; return false; }
            if ((root.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f || Vector3.Dot(root.transform.up, Vector3.up) < .99999f)
            { reason = "Секции требуют единичного мирового масштаба и вертикальной оси Y."; return false; }
            var scene = EditorSceneManager.NewPreviewScene(); GameObject probe = null;
            try
            {
                probe = Object.Instantiate(root); probe.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(probe, scene);
                var instance = probe.GetComponent<BlockoutBlockInstance>() ?? probe.AddComponent<BlockoutBlockInstance>(); instance.definition = definition;
                if (!instance.HasSections) { instance.dimensions = dimensions; instance.material = sections[0].material; }
                PrepareGeometry(probe, dimensions, sections);
                if (publishedSource != null) RefreshSource(probe, publishedSource);
                if (yaw.HasValue) probe.transform.rotation = Quaternion.Euler(0, yaw.Value - (probe.GetComponent<BlockoutCellWall>()?.yaw ?? 0), 0);
                if (!force)
                {
                    if (!BlockoutGrid.TryFloor(root.scene, out var floor)) { reason = "Не настроен пол активной сцены."; return false; }
                    Physics.SyncTransforms();
                    bool prism = probe.GetComponent<BlockoutCellWall>() != null || probe.GetComponent<BlockoutSteppedGeometry>() != null;
                    bool conflict = prism ? BlockoutWallEditing.Conflict(root.scene, floor,
                        BlockoutCellWall.TransformParts(probe.GetComponent<BlockoutSectionGeometry>().LocalSolidParts(), probe.transform.position, probe.transform.rotation), root)
                        : BlockoutWallEditing.PrefabConflictAtYaw(probe, root.scene, floor, BlockoutPainter.WorldBounds(probe), probe.transform.eulerAngles.y, root);
                    if (conflict) { reason = "Новая секционная геометрия пересекает препятствие или выходит за доступный пол."; return false; }
                }
            }
            catch (Exception error) { reason = error.Message; return false; }
            finally { if (probe != null) Object.DestroyImmediate(probe); EditorSceneManager.ClosePreviewScene(scene); }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.RegisterFullObjectHierarchyUndo(root, "Изменить секции блока");
            var target = root.GetComponent<BlockoutBlockInstance>() ?? Undo.AddComponent<BlockoutBlockInstance>(root); target.definition = definition;
            if (!target.HasSections) { target.dimensions = dimensions; target.material = sections[0].material; }
            PrepareGeometry(root, dimensions, sections, true);
            if (publishedSource != null) RefreshSource(root, publishedSource);
            if (yaw.HasValue) root.transform.rotation = Quaternion.Euler(0, yaw.Value - (root.GetComponent<BlockoutCellWall>()?.yaw ?? 0), 0);
            if (normalizeBottomAnchor && BlockoutGrid.TryFloor(root.scene, out var actualFloor))
            {
                var position = root.transform.position; position.y = Mathf.Max(BlockoutPainter.WorldBounds(root).min.y, actualFloor.max.y); root.transform.position = position;
            }
            Record(root); Physics.SyncTransforms(); Undo.CollapseUndoOperations(group); reason = null; return true;
        }

        public static void RefreshSource(GameObject root, GameObject source, Mesh candidate = null, Vector2? basePlan = null, Vector2? topPlan = null)
        {
            var geometry = root.GetComponent<BlockoutSectionGeometry>();
            if (root.TryGetComponent<BlockoutSteppedGeometry>(out var stepped))
            {
                var sourceStep = source.GetComponent<BlockoutSteppedGeometry>();
                stepped.baseSize = basePlan ?? sourceStep.baseSize; stepped.topSize = topPlan ?? sourceStep.topSize;
                stepped.lowerHeightLimit = sourceStep.lowerHeightLimit; stepped.localBottomCenter = sourceStep.localBottomCenter; stepped.localBottomCenter.y = 0;
            }
            else if (root.GetComponent<BlockoutCellWall>() == null)
            {
                if (candidate != null) { root.GetComponent<MeshFilter>().sharedMesh = candidate; geometry.CaptureSource(); }
                else geometry.CaptureSource(source);
            }
            geometry.Rebuild(); Record(root);
        }
        public static void Record(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue; EditorUtility.SetDirty(component);
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            EditorUtility.SetDirty(root);
            if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }
    }
}
