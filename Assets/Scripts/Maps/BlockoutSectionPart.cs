using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Физическая секция одного игрового блока; общим владельцем геометрии остаётся его корень.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    [RequireComponent(typeof(CoverSurface))]
    public sealed class BlockoutSectionPart : MonoBehaviour
    {
        [HideInInspector] public BlockoutSectionGeometry owner;
        [HideInInspector] public int sectionIndex;
        public BlockoutBlockInstance Instance => owner != null ? owner.GetComponent<BlockoutBlockInstance>() : null;
        private Mesh cachedMesh;
        private Vector3[] cachedVertices;
        private int[] cachedTriangles;
        /// <summary>Массивы неизменяемого производного меша копируются один раз при его назначении, не на каждую пулю.</summary>
        public void SetMesh(Mesh mesh)
        {
            cachedMesh = mesh; cachedVertices = mesh != null ? mesh.vertices : null; cachedTriangles = mesh != null ? mesh.triangles : null;
        }
        public bool TryMeshData(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
            if (cachedMesh != mesh || cachedVertices == null) SetMesh(mesh);
            vertices = cachedVertices; triangles = cachedTriangles; return vertices != null && triangles != null;
        }
    }
}
