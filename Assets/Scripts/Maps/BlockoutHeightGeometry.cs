using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Высота штатной формы без масштаба корня и без изменения исходных мешей.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BlockoutHeightGeometry : MonoBehaviour
    {
        [Serializable]
        private sealed class MeshRecord
        {
            public MeshFilter filter;
            public MeshCollider collider;
            public Mesh source;
            public Matrix4x4 toRoot;
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Vector2[] uv, uv2;
            public Color[] colors;
            public SubmeshRecord[] submeshes;
        }
        [Serializable] private sealed class SubmeshRecord { public int[] indices; }

        [Serializable]
        private sealed class BoxRecord
        {
            public BoxCollider collider;
            public Vector3 center, size;
            public Matrix4x4 toRoot;
        }

        [SerializeField] private float baseHeight, targetHeight, baseRootMinY;
        [SerializeField] private MeshRecord[] meshes = Array.Empty<MeshRecord>();
        [SerializeField] private BoxRecord[] boxes = Array.Empty<BoxRecord>();
        [NonSerialized] private readonly List<Mesh> generated = new List<Mesh>();
        [NonSerialized] private string builtRecipe;
        public float BaseHeight => baseHeight;
        public float TargetHeight => targetHeight;
        public bool Initialized => baseHeight > 0 && meshes.Length > 0;

        /// <summary>Снимок берётся единожды с исходной геометрии экземпляра.</summary>
        public void Initialize(float sourceHeight, float height)
        {
            if (Initialized) throw new InvalidOperationException("Исходная геометрия уже сохранена.");
            RequireHeight(sourceHeight); RequireHeight(height);
            var records = new List<MeshRecord>();
            float bottom = float.PositiveInfinity;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                records.Add(new MeshRecord { filter = filter, source = filter.sharedMesh, toRoot = matrix });
                foreach (var vertex in filter.sharedMesh.vertices)
                    bottom = Mathf.Min(bottom, matrix.MultiplyPoint3x4(vertex).y);
            }
            foreach (var collider in GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh != null)
                    records.Add(new MeshRecord { collider = collider, source = collider.sharedMesh,
                        toRoot = transform.worldToLocalMatrix * collider.transform.localToWorldMatrix });
            if (float.IsInfinity(bottom)) throw new ArgumentException("Форма не содержит читаемого меша.");
            var boxRecords = new List<BoxRecord>();
            foreach (var box in GetComponentsInChildren<BoxCollider>(true))
                boxRecords.Add(new BoxRecord { collider = box, center = box.center, size = box.size,
                    toRoot = transform.worldToLocalMatrix * box.transform.localToWorldMatrix });
            meshes = records.ToArray(); boxes = boxRecords.ToArray();
            baseHeight = sourceHeight; targetHeight = height; baseRootMinY = bottom;
            FreezeSourceGeometry();
            Rebuild();
        }

        /// <summary>Снимок до явной публикации ассета позволяет Undo сцены восстановить прежнюю геометрию при том же GUID источника.</summary>
        public void FreezeSourceGeometry()
        {
            foreach(var record in meshes)
            {
                if(record.source==null||(record.vertices!=null&&record.vertices.Length>0&&record.submeshes!=null&&record.submeshes.Length>0))continue;
                var displayed=record.filter!=null?record.filter.sharedMesh:record.collider!=null?record.collider.sharedMesh:null;
                bool derived=displayed!=null&&displayed.vertexCount>0&&generated.Contains(displayed);
                var snapshot=derived?displayed:record.source;
                record.vertices=snapshot.vertices;record.normals=snapshot.normals;record.tangents=snapshot.tangents;
                if(derived)
                {
                    // Источник с прежним GUID уже мог измениться. Восстанавливаем baseline из реально показанного меша.
                    float ratio=targetHeight/baseHeight;var inverse=record.toRoot.inverse;
                    for(int i=0;i<record.vertices.Length;i++)
                    {var point=record.toRoot.MultiplyPoint3x4(record.vertices[i]);point.y=baseRootMinY+(point.y-baseRootMinY)/ratio;record.vertices[i]=inverse.MultiplyPoint3x4(point);}
                    var deformation=inverse*Matrix4x4.Scale(new Vector3(1,ratio,1))*record.toRoot;
                    for(int i=0;i<record.normals.Length;i++)record.normals[i]=deformation.transpose.MultiplyVector(record.normals[i]).normalized;
                    var inverseDeformation=deformation.inverse;
                    for(int i=0;i<record.tangents.Length;i++)
                    {var tangent=record.tangents[i];var direction=inverseDeformation.MultiplyVector(new Vector3(tangent.x,tangent.y,tangent.z)).normalized;record.tangents[i]=new Vector4(direction.x,direction.y,direction.z,tangent.w);}
                }
                record.uv=snapshot.uv;record.uv2=snapshot.uv2;record.colors=snapshot.colors;
                record.submeshes=new SubmeshRecord[snapshot.subMeshCount];
                for(int i=0;i<record.submeshes.Length;i++)record.submeshes[i]=new SubmeshRecord {indices=snapshot.GetTriangles(i)};
            }
        }
        /// <summary>Только явный Apply заменяет сериализованный источник; вызывающий сначала освобождает производные меши и назначает новую геометрию.</summary>
        public void CapturePublishedSource(float sourceHeight,float height,float? preservedRootBottom=null)
        {
            if(generated.Count!=0)throw new InvalidOperationException("Перед сменой источника необходимо освободить производные меши.");
            meshes=Array.Empty<MeshRecord>();boxes=Array.Empty<BoxRecord>();baseHeight=0;builtRecipe=null;
            Initialize(sourceHeight,height);
            if(preservedRootBottom.HasValue)
            {
                float offset=preservedRootBottom.Value-baseRootMinY;
                foreach(var record in meshes)
                {
                    var inverse=record.toRoot.inverse;
                    for(int i=0;i<record.vertices.Length;i++)
                    {var point=record.toRoot.MultiplyPoint3x4(record.vertices[i]);point.y+=offset;record.vertices[i]=inverse.MultiplyPoint3x4(point);}
                }
                foreach(var box in boxes)
                {var point=box.toRoot.MultiplyPoint3x4(box.center);point.y+=offset;box.center=box.toRoot.inverse.MultiplyPoint3x4(point);}
                baseRootMinY=preservedRootBottom.Value;Rebuild();
            }
        }

        public void ApplyHeight(float height)
        {
            RequireHeight(height);
            if (!Initialized) throw new InvalidOperationException("Исходная геометрия не сохранена.");
            targetHeight = height;
            Rebuild();
        }

        private static void RequireHeight(float value)
        {
            if (value <= 0 || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Высота должна быть положительной и конечной.");
        }

        private Vector3 Deform(Vector3 point, float ratio)
        {
            point.y = baseRootMinY + (point.y - baseRootMinY) * ratio;
            return point;
        }

        /// <summary>Производные меши не сохраняются: сериализованный снимок восстанавливает их после загрузки.</summary>
        public void Rebuild()
        {
            if (BlockoutSectionGeometry.Owns(gameObject)) { GetComponent<BlockoutSectionGeometry>().Rebuild(); return; }
            if (!Initialized || targetHeight <= 0) return;
            string recipe=JsonUtility.ToJson(this);
            bool intact=generated.Count>0;
            foreach(var mesh in generated)if(mesh==null||mesh.vertexCount==0)intact=false;
            foreach(var record in meshes)
            {
                if(record.source==null)continue;
                if(record.filter!=null&&!generated.Contains(record.filter.sharedMesh))intact=false;
                if(record.collider!=null&&!generated.Contains(record.collider.sharedMesh))intact=false;
            }
            if(intact&&builtRecipe==recipe)return;
            ReleaseGenerated();
            float ratio = targetHeight / baseHeight;
            foreach (var record in meshes)
            {
                if (record.source == null || (record.filter == null && record.collider == null)) continue;
                Mesh copy = Instantiate(record.source);
                copy.name = record.source.name + "_BlockoutHeight";
                copy.hideFlags = HideFlags.HideAndDontSave;
                var inverse = record.toRoot.inverse;
                bool hasSnapshot=record.vertices!=null&&record.vertices.Length>0&&record.submeshes!=null&&record.submeshes.Length>0;
                var vertices = hasSnapshot?(Vector3[])record.vertices.Clone():record.source.vertices;
                if(hasSnapshot)
                {
                    copy.Clear();copy.indexFormat=vertices.Length>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
                    copy.vertices=vertices;
                    if(record.uv!=null&&record.uv.Length==vertices.Length)copy.uv=record.uv;
                    if(record.uv2!=null&&record.uv2.Length==vertices.Length)copy.uv2=record.uv2;
                    if(record.colors!=null&&record.colors.Length==vertices.Length)copy.colors=record.colors;
                    copy.subMeshCount=record.submeshes.Length;
                    for(int i=0;i<record.submeshes.Length;i++)copy.SetTriangles(record.submeshes[i].indices,i,false);
                }
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = inverse.MultiplyPoint3x4(Deform(record.toRoot.MultiplyPoint3x4(vertices[i]), ratio));
                copy.vertices = vertices;
                // Обратная транспонированная матрица сохраняет исходные сглаженные нормали.
                var deformation = inverse * Matrix4x4.Scale(new Vector3(1, ratio, 1)) * record.toRoot;
                var normalMatrix = deformation.inverse.transpose;
                var normals = hasSnapshot&&record.normals!=null&&record.normals.Length>0?(Vector3[])record.normals.Clone():record.source.normals;
                for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                if (normals.Length == vertices.Length) copy.normals = normals;
                var tangents = hasSnapshot&&record.tangents!=null&&record.tangents.Length>0?(Vector4[])record.tangents.Clone():record.source.tangents;
                for (int i = 0; i < tangents.Length; i++)
                {
                    Vector3 direction = deformation.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
                }
                copy.tangents = tangents;
                copy.RecalculateBounds(); generated.Add(copy);
                if (record.filter != null) record.filter.sharedMesh = copy;
                if (record.collider != null) record.collider.sharedMesh = copy;
            }
            foreach (var record in boxes)
            {
                if (record.collider == null) continue;
                var inverse = record.toRoot.inverse;
                record.collider.center = inverse.MultiplyPoint3x4(Deform(record.toRoot.MultiplyPoint3x4(record.center), ratio));
                var deformation = inverse * Matrix4x4.Scale(new Vector3(1, ratio, 1)) * record.toRoot;
                record.collider.size = new Vector3(
                    deformation.MultiplyVector(Vector3.right).magnitude * record.size.x,
                    deformation.MultiplyVector(Vector3.up).magnitude * record.size.y,
                    deformation.MultiplyVector(Vector3.forward).magnitude * record.size.z);
            }
            builtRecipe=JsonUtility.ToJson(this);
        }

        private void OnEnable() => Rebuild();

        // Undo временно вызывает OnDisable/OnEnable. Геометрия остаётся живой до уничтожения
        // владельца; редактор отдельно освобождает её перед реальной выгрузкой домена.
        public void ReleaseEditorMeshes() => ReleaseGenerated();

        private void ReleaseGenerated()
        {
            foreach (var record in meshes)
            {
                if (record.filter != null) record.filter.sharedMesh = record.source;
                if (record.collider != null) record.collider.sharedMesh = record.source;
            }
            foreach (var mesh in generated)
                if (mesh != null)
                {
                    if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                }
            generated.Clear();
        }

        private void OnDestroy()
        {
            ReleaseGenerated();
            foreach (var record in boxes)
                if (record.collider != null) { record.collider.center = record.center; record.collider.size = record.size; }
        }
    }
}
