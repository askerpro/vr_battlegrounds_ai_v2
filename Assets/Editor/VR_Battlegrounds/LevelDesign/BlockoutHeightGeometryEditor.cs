using System;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Проверка совместимости штатных форм и запись явных правок высоты под Undo фабрики.</summary>
    [InitializeOnLoad]
    public static class BlockoutHeightGeometryEditor
    {
        static BlockoutHeightGeometryEditor()
        {
            Undo.undoRedoPerformed += RestoreGeometry;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseBeforeReload;
        }
        private static void ReleaseBeforeReload()
        {
            foreach(var geometry in Resources.FindObjectsOfTypeAll<BlockoutHeightGeometry>())
                if(geometry!=null&&geometry.gameObject.scene.IsValid()&&!EditorUtility.IsPersistent(geometry))geometry.ReleaseEditorMeshes();
        }

        public static bool CanResize(GameObject source, out string reason)
        {
            reason = null;
            if (source == null) { reason = "Исходная форма отсутствует."; return false; }
            if ((source.transform.localScale - Vector3.one).sqrMagnitude > .000001f)
            { reason = "Изменение высоты требует единичного масштаба корня."; return false; }
            if ((source.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
            { reason = "Масштаб родителя изменяет физическую высоту формы; требуется единичный мировой масштаб."; return false; }
            if (Vector3.Dot(source.transform.up, Vector3.up) < .99999f)
            { reason = "Изменение высоты требует вертикальной оси Y корня."; return false; }
            if (source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
            { reason = "Деформация SkinnedMeshRenderer не поддерживается."; return false; }
            bool hasMesh = false;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                hasMesh = true;
                if (!Readable(filter.sharedMesh, out reason)) return false;
            }
            if (!hasMesh) { reason = "Форма не содержит геометрии MeshFilter."; return false; }
            foreach (var collider in source.GetComponentsInChildren<Collider>(true))
            {
                if (collider is MeshCollider mesh)
                {
                    if (mesh.sharedMesh == null || !Readable(mesh.sharedMesh, out reason))
                    { reason = reason ?? "MeshCollider не содержит меша."; return false; }
                }
                else if (collider is BoxCollider)
                {
                    var matrix = source.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vector3 direction = matrix.GetColumn(axis);
                        if (direction.sqrMagnitude < .0000001f)
                        { reason = "Коллайдер имеет вырожденный масштаб."; return false; }
                        direction.Normalize();
                        float vertical = Mathf.Abs(direction.y);
                        if (vertical > .0001f && vertical < .9999f)
                        { reason = "Наклонённый BoxCollider нельзя растянуть по Y без изменения физической формы."; return false; }
                    }
                }
                else
                {
                    reason = collider.GetType().Name + ": пропорциональная деформация только по Y не представима этим коллайдером.";
                    return false;
                }
            }
            return true;
        }

        private static bool Readable(Mesh mesh, out string reason)
        {
            reason = mesh.isReadable && mesh.vertexCount > 0 ? null : "Меш «" + mesh.name + "» не содержит доступных вершин.";
            return reason == null;
        }

        public static BlockoutHeightGeometry Initialize(GameObject instance, float baseHeight, float targetHeight, bool recordUndo = true)
        {
            if (!CanResize(instance, out string reason)) throw new ArgumentException(reason);
            var geometry = instance.GetComponent<BlockoutHeightGeometry>();
            if (geometry == null) geometry = recordUndo ? Undo.AddComponent<BlockoutHeightGeometry>(instance) : instance.AddComponent<BlockoutHeightGeometry>();
            if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(instance, "Изменить высоту блока");
            if (geometry.Initialized) geometry.ApplyHeight(targetHeight); else geometry.Initialize(baseHeight, targetHeight);
            Record(instance);
            return geometry;
        }

        public static void Apply(GameObject instance, float targetHeight)
        {
            if (!CanResize(instance, out string reason)) throw new ArgumentException(reason);
            var geometry = instance.GetComponent<BlockoutHeightGeometry>();
            if (geometry == null || !geometry.Initialized) throw new InvalidOperationException("Исходная высота блока не сохранена.");
            Undo.RegisterFullObjectHierarchyUndo(instance, "Изменить высоту блока");
            geometry.ApplyHeight(targetHeight);
            Record(instance);
        }

        private static void Record(GameObject instance)
        {
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (component != null)
                {
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
        }

        private static void RestoreGeometry()
        {
            foreach (var geometry in Resources.FindObjectsOfTypeAll<BlockoutHeightGeometry>())
                if (geometry != null && geometry.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(geometry)) geometry.Rebuild();
        }
    }
}
