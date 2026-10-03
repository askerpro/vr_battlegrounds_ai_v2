using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.LevelDesign;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Точечное изменение диаметра цилиндра без изменения GUID, высоты, корневого масштаба и других форм.</summary>
    public static class CylinderSourceReplacement
    {
        public const string SourcePrefabPath = "Assets/Prefabs/LevelDesign/LD_Alphabet/LD_Can_Mid.prefab";
        public const string SourcePrefabGuid = "4c324de87c7f53f42a95440d1a72b5a2";
        public const string SourceMeshGuid = "c4e9adb58c21ff847b3afe46f61dd32b";
        public const float Diameter = 1.5f;

        private static GameObject Source()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null || AssetDatabase.AssetPathToGUID(SourcePrefabPath) != SourcePrefabGuid || source.transform.localScale != Vector3.one)
                throw new InvalidOperationException("Исходный цилиндр отсутствует либо изменены его GUID/корневой масштаб.");
            var filter = source.GetComponent<MeshFilter>(); var collider = source.GetComponent<MeshCollider>();
            if (filter == null || filter.sharedMesh == null || collider == null || collider.sharedMesh != filter.sharedMesh ||
                source.GetComponentsInChildren<Collider>(true).Length != 1 ||
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(filter.sharedMesh)) != SourceMeshGuid)
                throw new InvalidOperationException("Ожидался один исходный цилиндрический mesh с общим MeshCollider и сохранённым GUID.");
            return source;
        }

        public static Mesh BuildMesh(Mesh source)
        {
            var bounds = source.bounds;
            if (bounds.size.x <= 0 || bounds.size.z <= 0 || !source.isReadable)
                throw new InvalidOperationException("Исходный меш цилиндра недоступен для точечной правки.");
            var result = Object.Instantiate(source);
            var vertices = result.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].x = bounds.center.x + (vertices[i].x - bounds.center.x) * Diameter / bounds.size.x;
                vertices[i].z = bounds.center.z + (vertices[i].z - bounds.center.z) * Diameter / bounds.size.z;
            }
            result.name = "Цилиндр диаметром 1,5 м";
            result.vertices = vertices; result.RecalculateBounds(); result.RecalculateNormals(); result.RecalculateTangents();
            return result;
        }

        /// <summary>Вызывается владельцем замка Unity. Резервирует и сохраняет только цилиндр, его паспорт и строку общего JSON.</summary>
        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Замена диаметра выполняется вне Play Mode после компиляции.");
            var source = Source(); var asset = source.GetComponent<MeshFilter>().sharedMesh;
            var data = JsonUtility.FromJson<BlockoutGridSettings.Data>(JsonUtility.ToJson(BlockoutGridSettings.Current));
            var dimensions = data.blocks.Single(b => b.name == "LD_Can_Mid");
            dimensions.width = Diameter; dimensions.depth = Diameter; BlockoutGridSettings.Validate(data);
            var definitions = BlockoutCanonicalRegistry.DefinitionsIncludingLegacy().Where(d => d.geometryPrefab == source ||
                d.materialVariants.Any(v => v?.sourcePrefab == source)).ToArray();
            string backup = "Temp/LevelDesign/Cylinder/Before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(backup);
            foreach (string path in definitions.Select(AssetDatabase.GetAssetPath).Concat(new[] { SourcePrefabPath, AssetDatabase.GetAssetPath(asset), BlockoutGridSettings.Path }).Distinct())
            {
                if (File.Exists(path)) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
                if (File.Exists(path + ".meta")) File.Copy(path + ".meta", Path.Combine(backup, Path.GetFileName(path) + ".meta"), false);
            }
            var mesh = BuildMesh(asset);
            try
            {
                var contents = PrefabUtility.LoadPrefabContents(SourcePrefabPath);
                try
                {
                    EditorUtility.CopySerialized(mesh, asset); EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
                    contents.GetComponent<MeshFilter>().sharedMesh = asset;
                    var collider = contents.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.sharedMesh = asset;
                    PrefabUtility.SaveAsPrefabAsset(contents, SourcePrefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("Не удалось сохранить цилиндр; резервная копия: " + backup);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                foreach (var definition in definitions)
                {
                    definition.minDimensions.x = definition.maxDimensions.x = Diameter;
                    definition.minDimensions.z = definition.maxDimensions.z = Diameter;
                    EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
                }
                BlockoutGridSettings.Save(data);
                BlockoutGridSettingsPanel.Reload();
                BlockoutRegistryFactory.InvalidateSource(source);
                return backup;
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        /// <summary>Preview проверяет геометрию кандидата; sourceOnly проверяет реально применённый источник.</summary>
        public static Dictionary<string, object> Probe(bool sourceOnly = false)
        {
            var source = Source(); var original = source.GetComponent<MeshFilter>().sharedMesh;
            var scene = EditorSceneManager.NewPreviewScene(); Mesh candidate = null;
            try
            {
                var clone = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                if (!sourceOnly)
                {
                    candidate = BuildMesh(original); clone.GetComponent<MeshFilter>().sharedMesh = candidate;
                    var collider = clone.GetComponent<MeshCollider>(); collider.sharedMesh = null; collider.sharedMesh = candidate;
                }
                Physics.SyncTransforms();
                var actual = clone.GetComponent<MeshFilter>().sharedMesh;
                Bounds initial = actual.bounds;
                bool diameter = Mathf.Abs(initial.size.x - Diameter) < .0001f && Mathf.Abs(initial.size.z - Diameter) < .0001f;
                bool sourceY = Mathf.Abs(initial.min.y - original.bounds.min.y) < .0001f && Mathf.Abs(initial.size.y - original.bounds.size.y) < .0001f;
                var physics = clone.GetComponent<MeshCollider>(); float bottom = physics.bounds.min.y;
                var heightGeometry = clone.GetComponent<BlockoutHeightGeometry>();
                if (heightGeometry == null) heightGeometry = clone.AddComponent<BlockoutHeightGeometry>();
                if (!heightGeometry.Initialized) heightGeometry.Initialize(initial.size.y, 1.6f);
                bool heights = true, rays = true, meshAgreement = true;
                foreach (float height in new[] { 1.2f, 1.6f, 2.5f })
                {
                    heightGeometry.ApplyHeight(height); Physics.SyncTransforms();
                    var bounds = physics.bounds;
                    heights &= Mathf.Abs(bounds.size.x - Diameter) < .001f && Mathf.Abs(bounds.size.z - Diameter) < .001f &&
                        Mathf.Abs(bounds.size.y - height) < .001f && Mathf.Abs(bounds.min.y - bottom) < .001f && clone.transform.localScale == Vector3.one;
                    meshAgreement &= physics.sharedMesh.bounds == clone.GetComponent<MeshFilter>().sharedMesh.bounds;
                    foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    {
                        rays &= physics.Raycast(new Ray(bounds.center - direction * 2, direction), out _, 4);
                        Vector3 tangent = Vector3.Cross(Vector3.up, direction);
                        rays &= !physics.Raycast(new Ray(bounds.center + tangent * .8f - direction * 2, direction), out _, 4);
                    }
                }
                return new Dictionary<string, object>
                {
                    { "sourceOnly", sourceOnly }, { "actualDiameter15", diameter }, { "meshBounds", initial }, { "sourceHeightAndBottomPreserved", sourceY },
                    { "heightsLowMidTall", heights }, { "centerBlocksOutsidePasses", rays }, { "meshColliderBoundsAgree", meshAgreement },
                    { "rootScaleOne", clone.transform.localScale == Vector3.one },
                    { "materialPreserved", source.GetComponent<Renderer>().sharedMaterials.SequenceEqual(clone.GetComponent<Renderer>().sharedMaterials) },
                    { "settings", BlockoutGridSettings.Dimensions("LD_Can_Mid") },
                    { "sourceGUIDsPreserved", AssetDatabase.AssetPathToGUID(SourcePrefabPath) == SourcePrefabGuid && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)) == SourceMeshGuid }
                };
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); if (candidate != null) Object.DestroyImmediate(candidate); }
        }
    }
}
