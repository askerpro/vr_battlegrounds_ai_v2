using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Gameplay.SightSplit
{
    /// <summary>Вынесенная деталь: компактный меш с началом в точке R/F и связь с исходным мешем.</summary>
    public sealed class SightPartMesh
    {
        public SightSplitPartSpec Spec;
        /// <summary>Меш детали: вершины исходного меша детали со сдвигом на −точка (единицы меша узла).</summary>
        public Mesh Mesh;
        /// <summary>Новый индекс вершины → индекс вершины исходного меша.</summary>
        public int[] SourceVertices;
        /// <summary>Подмеш детали → подмеш исходного меша (для материалов).</summary>
        public int[] SourceSubmeshes;
        public List<MeshIslands.Island> Islands;
        public int Triangles;
    }

    /// <summary>
    /// Результат разделения меша одного узла. Корпус сохраняет все вершины исходного меша (те же индексы и байты
    /// атрибутов, включая ставшие неиспользуемыми вершины детали) и теряет только треугольники деталей; деталь
    /// ужата до своих вершин. Объединение треугольников корпуса и деталей точно равно исходному по каждому подмешу.
    /// </summary>
    public sealed class SightMeshSplit
    {
        public Mesh Source;
        public Mesh Body;
        public MeshIslands Islands;
        /// <summary>У исходного меша были скин-атрибуты, в выходные меши они не перенесены.</summary>
        public bool DroppedSkin;
        public readonly List<SightPartMesh> Parts = new List<SightPartMesh>();
        /// <summary>Острова неподвижных половин (только контроль селекторов, геометрия остаётся в корпусе).</summary>
        public readonly Dictionary<SightSplitPartSpec, List<MeshIslands.Island>> ReferenceIslands =
            new Dictionary<SightSplitPartSpec, List<MeshIslands.Island>>();

        public void DestroyMeshes()
        {
            if (Body != null) UnityEngine.Object.DestroyImmediate(Body);
            foreach (SightPartMesh part in Parts)
                if (part.Mesh != null) UnityEngine.Object.DestroyImmediate(part.Mesh);
        }
    }

    /// <summary>
    /// Снимок меша: позиции, индексы по подмешам и сырые байты каждого вершинного атрибута в исходном формате.
    /// Копирование побайтное (техника Tr15OpticBuilder.UpdateMesh), поэтому нормали, тангенты, цвета и все UV
    /// сохраняют формат и размерность. Скин-атрибуты (BlendWeight/BlendIndices) у мешей FBX пака Hands есть, но узлы
    /// рисуются MeshRenderer без костей — в выходные меши они не переносятся (bindposes не нужны).
    /// Чтение — <see cref="Mesh.AcquireReadOnlyMeshData(Mesh)"/>, поэтому Read/Write импорта не требуется в редакторе.
    /// </summary>
    public sealed class SightMeshSnapshot
    {
        public string Name;
        public int VertexCount;
        /// <summary>Атрибуты выходных мешей: исходные без скин-атрибутов, потоки перенумерованы подряд.</summary>
        public VertexAttributeDescriptor[] Attributes;
        /// <summary>Атрибут → байты всех вершин подряд (размер элемента — <see cref="AttributeSize"/>).</summary>
        public Dictionary<VertexAttribute, byte[]> AttributeData;
        public Dictionary<VertexAttribute, int> AttributeSize;
        public Vector3[] Positions;
        public List<int[]> SubmeshIndices;
        public Bounds Bounds;
        public IndexFormat IndexFormat;
        public bool DroppedSkin;

        public int TriangleCount => SubmeshIndices.Sum(s => s.Length / 3);

        public static SightMeshSnapshot Read(Mesh mesh, string context)
        {
            if (mesh == null) throw new InvalidOperationException($"{context}: нет меша");
            if (mesh.blendShapeCount != 0) throw new InvalidOperationException($"{context}: меш {mesh.name} с blend shapes — не поддерживается");
            var snapshot = new SightMeshSnapshot
            {
                Name = mesh.name, Bounds = mesh.bounds, IndexFormat = mesh.indexFormat,
                AttributeData = new Dictionary<VertexAttribute, byte[]>(), AttributeSize = new Dictionary<VertexAttribute, int>()
            };
            VertexAttributeDescriptor[] source = mesh.GetVertexAttributes();
            using (Mesh.MeshDataArray array = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData data = array[0];
                snapshot.VertexCount = data.vertexCount;
                if (data.vertexCount == 0 || data.vertexCount != mesh.vertexCount)
                    throw new InvalidOperationException($"{context}: вершины меша {mesh.name} недоступны на CPU ({data.vertexCount}/{mesh.vertexCount})");
                if (!data.HasVertexAttribute(VertexAttribute.Position)
                    || data.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32
                    || data.GetVertexAttributeDimension(VertexAttribute.Position) != 3)
                    throw new InvalidOperationException($"{context}: позиции меша {mesh.name} не Float32×3");

                var streams = new Dictionary<int, byte[]>();
                var streamMap = new Dictionary<int, int>();
                var kept = new List<VertexAttributeDescriptor>();
                foreach (VertexAttributeDescriptor attribute in source)
                {
                    if (attribute.attribute == VertexAttribute.BlendWeight || attribute.attribute == VertexAttribute.BlendIndices)
                    {
                        snapshot.DroppedSkin = true;
                        continue;
                    }
                    int stream = data.GetVertexAttributeStream(attribute.attribute);
                    int stride = data.GetVertexBufferStride(stream);
                    if (!streams.TryGetValue(stream, out byte[] bytes))
                    {
                        NativeArray<byte> raw = data.GetVertexData<byte>(stream);
                        if (raw.Length != stride * data.vertexCount)
                            throw new InvalidOperationException($"{context}: поток {stream} меша {mesh.name}: {raw.Length} байт, ожидается {stride * data.vertexCount}");
                        streams[stream] = bytes = raw.ToArray();
                    }
                    int offset = data.GetVertexAttributeOffset(attribute.attribute);
                    int size = FormatSize(attribute.format) * attribute.dimension;
                    var values = new byte[size * data.vertexCount];
                    for (int v = 0; v < data.vertexCount; v++) Buffer.BlockCopy(bytes, v * stride + offset, values, v * size, size);
                    snapshot.AttributeData[attribute.attribute] = values;
                    snapshot.AttributeSize[attribute.attribute] = size;
                    if (!streamMap.ContainsKey(stream)) streamMap[stream] = streamMap.Count;
                    kept.Add(new VertexAttributeDescriptor(attribute.attribute, attribute.format, attribute.dimension, streamMap[stream]));
                }
                snapshot.Attributes = kept.ToArray();

                using (var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    snapshot.Positions = positions.ToArray();
                }

                snapshot.SubmeshIndices = new List<int[]>(data.subMeshCount);
                for (int s = 0; s < data.subMeshCount; s++)
                {
                    SubMeshDescriptor sub = data.GetSubMesh(s);
                    if (sub.topology != MeshTopology.Triangles)
                        throw new InvalidOperationException($"{context}: подмеш {s} меша {mesh.name} — {sub.topology}, нужны треугольники");
                    using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, s, true);
                        snapshot.SubmeshIndices.Add(indices.ToArray());
                    }
                }
            }
            return snapshot;
        }

        /// <summary>Байты атрибута одной вершины (сверка в тестах инструмента).</summary>
        public byte[] AttributeBytes(VertexAttribute attribute, int vertex)
        {
            int size = AttributeSize[attribute];
            var result = new byte[size];
            Buffer.BlockCopy(AttributeData[attribute], vertex * size, result, 0, size);
            return result;
        }

        /// <summary>Меш со всеми вершинами (те же индексы и байты атрибутов) и заданными индексами по подмешам.</summary>
        public Mesh BuildWithAllVertices(IReadOnlyList<int[]> submeshIndices, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat };
            WriteVertices(mesh, Enumerable.Range(0, VertexCount).ToArray(), null);
            SetSubmeshes(mesh, submeshIndices);
            mesh.bounds = Bounds;
            return mesh;
        }

        /// <summary>Компактный меш из выбранных вершин; позиции сдвигаются на −<paramref name="origin"/>.</summary>
        public Mesh BuildCompact(int[] sourceVertices, IReadOnlyList<int[]> submeshIndices, Vector3 origin, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = sourceVertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            WriteVertices(mesh, sourceVertices, origin);
            SetSubmeshes(mesh, submeshIndices);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void WriteVertices(Mesh mesh, int[] sourceVertices, Vector3? origin)
        {
            int count = sourceVertices.Length;
            mesh.SetVertexBufferParams(count, Attributes);
            int streamCount = Attributes.Max(a => a.stream) + 1;
            for (int s = 0; s < streamCount; s++)
            {
                int stride = mesh.GetVertexBufferStride(s);
                var bytes = new byte[count * stride];
                foreach (VertexAttributeDescriptor attribute in Attributes.Where(a => a.stream == s))
                {
                    int offset = mesh.GetVertexAttributeOffset(attribute.attribute);
                    int size = AttributeSize[attribute.attribute];
                    byte[] values = AttributeData[attribute.attribute];
                    bool shift = attribute.attribute == VertexAttribute.Position && origin.HasValue;
                    for (int v = 0; v < count; v++)
                    {
                        int at = v * stride + offset;
                        if (!shift)
                        {
                            Buffer.BlockCopy(values, sourceVertices[v] * size, bytes, at, size);
                            continue;
                        }
                        Vector3 p = Positions[sourceVertices[v]] - origin.Value;
                        WriteFloat(bytes, at, p.x);
                        WriteFloat(bytes, at + 4, p.y);
                        WriteFloat(bytes, at + 8, p.z);
                    }
                }
                mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, s, MeshUpdateFlags.DontRecalculateBounds);
            }
        }

        private static int FormatSize(VertexAttributeFormat format)
        {
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                case VertexAttributeFormat.UInt32:
                case VertexAttributeFormat.SInt32: return 4;
                case VertexAttributeFormat.Float16:
                case VertexAttributeFormat.UNorm16:
                case VertexAttributeFormat.SNorm16:
                case VertexAttributeFormat.UInt16:
                case VertexAttributeFormat.SInt16: return 2;
                default: return 1;
            }
        }

        private static void SetSubmeshes(Mesh mesh, IReadOnlyList<int[]> submeshIndices)
        {
            mesh.subMeshCount = submeshIndices.Count;
            for (int s = 0; s < submeshIndices.Count; s++)
                mesh.SetIndices(submeshIndices[s], MeshTopology.Triangles, s, false);
        }

        private static void WriteFloat(byte[] bytes, int at, float value)
        {
            byte[] raw = BitConverter.GetBytes(value);
            Buffer.BlockCopy(raw, 0, bytes, at, 4);
        }
    }
}
