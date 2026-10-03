using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Единый профиль ступенчатого укрытия: точные объёмы, наружный меш и коллайдер выводятся из него.</summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class BlockoutSteppedGeometry : MonoBehaviour
    {
        [HideInInspector] public float totalHeight = 1.6f, lowerHeightLimit = 1.2f;
        [HideInInspector] public Vector2 baseSize = new Vector2(1.5f, 1.5f), topSize = new Vector2(.9f, .9f);
        [HideInInspector] public Vector3 localBottomCenter;
        private Mesh generated;
        private string builtRecipe;
        public float TotalHeight => totalHeight;

        public void ApplyHeight(float height)
        {
            Validate(height, baseSize, topSize, lowerHeightLimit, localBottomCenter);
            totalHeight = height;
            Rebuild();
        }

        public IEnumerable<Bounds> LocalParts() => Parts(totalHeight, baseSize, topSize, localBottomCenter, lowerHeightLimit);
        public IEnumerable<Bounds> LocalPartsAtHeight(float height) => Parts(height, baseSize, topSize, localBottomCenter, lowerHeightLimit);
        public IEnumerable<BlockoutSolidPart> WorldPartsAtHeight(float height, Vector3 position, Quaternion rotation) =>
            PreviewParts(position, rotation, height, baseSize, topSize, localBottomCenter, lowerHeightLimit);
        public Mesh BuildMeshAtHeight(float height) => BuildMesh(height, baseSize, topSize, localBottomCenter, lowerHeightLimit);
        public void Rebuild(float height) => ApplyHeight(height);

        public IEnumerable<BlockoutSolidPart> WorldParts()
        {
            if (BlockoutSectionGeometry.Owns(gameObject))
            {
                foreach (var part in BlockoutCellWall.TransformParts(GetComponent<BlockoutSectionGeometry>().LocalSolidParts(), transform.position, transform.rotation)) yield return part;
                yield break;
            }
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            foreach (var part in LocalParts())
                yield return new BlockoutSolidPart { center = transform.TransformPoint(part.center), size = Vector3.Scale(part.size, scale), rotation = transform.rotation };
        }

        public static IEnumerable<BlockoutSolidPart> PreviewParts(Vector3 origin, Quaternion rotation, float height,
            Vector2 baseSize, Vector2 topSize, Vector3 localBottomCenter = default, float lowerHeightLimit = 1.2f)
        {
            foreach (var part in Parts(height, baseSize, topSize, localBottomCenter, lowerHeightLimit))
                yield return new BlockoutSolidPart { center = origin + rotation * part.center, size = part.size, rotation = rotation };
        }

        public static IEnumerable<Bounds> Parts(float height, Vector2 baseSize, Vector2 topSize,
            Vector3 bottom = default, float lowerHeightLimit = 1.2f)
        {
            Validate(height, baseSize, topSize, lowerHeightLimit, bottom);
            float lower = Mathf.Min(height, lowerHeightLimit), upper = height - lower;
            yield return new Bounds(bottom + Vector3.up * (lower / 2), new Vector3(baseSize.x, lower, baseSize.y));
            if (upper > .00001f)
                yield return new Bounds(bottom + Vector3.up * (lower + upper / 2), new Vector3(topSize.x, upper, topSize.y));
        }

        private static void Validate(float height, Vector2 basis, Vector2 top, float lower, Vector3 bottom)
        {
            if (!Finite(height) || !Finite(lower) || !Finite(basis.x) || !Finite(basis.y) || !Finite(top.x) || !Finite(top.y) ||
                !Finite(bottom.x) || !Finite(bottom.y) || !Finite(bottom.z) || height < 1.2f - .00001f || lower <= 0 ||
                basis.x <= 0 || basis.y <= 0 || top.x <= 0 || top.y <= 0 || top.x > basis.x || top.y > basis.y)
                throw new ArgumentException("Некорректный профиль ступенчатого укрытия; минимальная высота — 1,2 м.");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Rebuild()
        {
            if (BlockoutSectionGeometry.Owns(gameObject)) { GetComponent<BlockoutSectionGeometry>().Rebuild(); return; }
            string recipe=JsonUtility.ToJson(this);
            if(generated!=null&&builtRecipe==recipe&&GetComponent<MeshFilter>().sharedMesh==generated&&GetComponent<MeshCollider>().sharedMesh==generated)return;
            Mesh next = BuildMesh(totalHeight, baseSize, topSize, localBottomCenter, lowerHeightLimit);
            next.hideFlags = HideFlags.HideAndDontSave;
            var collider = GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            GetComponent<MeshFilter>().sharedMesh = next;
            collider.convex = false;
            collider.sharedMesh = next;
            ReleaseGenerated();
            generated = next;
            builtRecipe=JsonUtility.ToJson(this);
        }

        private void OnEnable()
        {
            // Сам ассет префаба сохраняет исходный mesh GUID; перестраиваются только экземпляры в сценах.
            if (gameObject.scene.IsValid()) Rebuild();
        }
        // Отключение поведения не скрывает геометрию, в том числе при рендере миниатюры.
        private void OnDestroy() => ReleaseGenerated();
        private void ReleaseGenerated()
        {
            if (generated == null) return;
            var filter = GetComponent<MeshFilter>();
            var collider = GetComponent<MeshCollider>();
            if (filter != null && filter.sharedMesh == generated) filter.sharedMesh = null;
            if (collider != null && collider.sharedMesh == generated) collider.sharedMesh = null;
            if (Application.isPlaying) Destroy(generated); else DestroyImmediate(generated);
            generated = null;
        }

        /// <summary>Разбиение общей границы устраняет внутренние грани и Т-стыки на плечах.</summary>
        public static Mesh BuildMesh(float height, Vector2 baseSize, Vector2 topSize,
            Vector3 bottom = default, float lowerHeightLimit = 1.2f)
        {
            var parts = new List<Bounds>(Parts(height, baseSize, topSize, bottom, lowerHeightLimit));
            var lower = parts[0];
            float[] xs = { lower.min.x, bottom.x - topSize.x / 2, bottom.x + topSize.x / 2, lower.max.x };
            float[] zs = { lower.min.z, bottom.z - topSize.y / 2, bottom.z + topSize.y / 2, lower.max.z };
            float[] ys = parts.Count == 1 ? new[] { lower.min.y, lower.max.y } : new[] { lower.min.y, lower.max.y, parts[1].max.y };
            var vertices = new List<Vector3>(); var indices = new List<int>(); var uvs = new List<Vector2>();
            for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) for (int y = 0; y < ys.Length - 1; y++)
            {
                if (!Has(x, y, z) || xs[x + 1] - xs[x] < .00001f || zs[z + 1] - zs[z] < .00001f) continue;
                float a = xs[x], A = xs[x + 1], b = ys[y], B = ys[y + 1], c = zs[z], C = zs[z + 1];
                if (!Has(x, y - 1, z)) Face(new Vector3(a,b,c),new Vector3(A,b,c),new Vector3(A,b,C),new Vector3(a,b,C));
                if (!Has(x, y + 1, z)) Face(new Vector3(a,B,C),new Vector3(A,B,C),new Vector3(A,B,c),new Vector3(a,B,c));
                if (!Has(x, y, z - 1)) Face(new Vector3(a,B,c),new Vector3(A,B,c),new Vector3(A,b,c),new Vector3(a,b,c));
                if (!Has(x, y, z + 1)) Face(new Vector3(a,b,C),new Vector3(A,b,C),new Vector3(A,B,C),new Vector3(a,B,C));
                if (!Has(x - 1, y, z)) Face(new Vector3(a,b,c),new Vector3(a,b,C),new Vector3(a,B,C),new Vector3(a,B,c));
                if (!Has(x + 1, y, z)) Face(new Vector3(A,B,c),new Vector3(A,B,C),new Vector3(A,b,C),new Vector3(A,b,c));
            }
            var mesh = new Mesh { name = "Ступенчатое укрытие" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
            return mesh;
            bool Has(int x, int y, int z) => x >= 0 && x < 3 && z >= 0 && z < 3 && y >= 0 && y < ys.Length - 1 &&
                xs[x + 1] - xs[x] > .00001f && zs[z + 1] - zs[z] > .00001f && (y == 0 || (x == 1 && z == 1));
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
                indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                bool horizontal = Mathf.Abs(a.y - b.y) + Mathf.Abs(a.y - c.y) < .00001f;
                foreach (var p in new[] { a, b, c, d }) uvs.Add(horizontal ? new Vector2(p.x,p.z) / 1.8f : new Vector2(p.x+p.z,p.y) / 1.8f);
            }
        }
    }
}
