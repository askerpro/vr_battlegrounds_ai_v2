using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Собственный GPU-кэш поля расстояний; никаких SDK lifecycle/asset writes.</summary>
    public sealed class HandPoseGpuField : IDisposable
    {
        public const string ComputePath = "Assets/Editor/VR_Battlegrounds/Avatars/HandPoseReview/HandPoseGpuField.compute";
        [StructLayout(LayoutKind.Sequential)] struct Node { public Vector4 Min, Max, Range; }
        [StructLayout(LayoutKind.Sequential)] struct Triangle { public Vector4 A, B, C; }
        [StructLayout(LayoutKind.Sequential)] public struct Contact { public Vector4 PointDistance, TargetState; }
        public RenderTexture Texture { get; private set; }
        public Vector3 Minimum { get; private set; }
        public Vector3 Size { get; private set; }
        public Vector3 Dimensions => new Vector3(_nx, _ny, _nz);
        public float VoxelMetres { get; private set; }
        public float ErrorMetres => VoxelMetres * Mathf.Sqrt(3) * .5f;
        public bool CanSign { get; private set; }
        public bool Ready => _z >= _nz && !_disposed;
        public bool QueryPending => _pending;
        public float Progress => (float)_z / _nz;
        public string Fingerprint { get; }
        ComputeShader _shader;
        ComputeBuffer _nodes, _triangles, _points, _contacts;
        AsyncGPUReadbackRequest _readback;
        bool _pending, _disposed;
        int _nx, _ny, _nz, _z, _bake, _query;

        public HandPoseGpuField(FitTriangle[] geometry, float voxelMetres, string fingerprint, Bounds? region = null)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports3DRenderTextures)
                throw new NotSupportedException("GPU не поддерживает compute/3D RenderTexture.");
            if (geometry == null || geometry.Length == 0 || geometry.Length > 200000)
                throw new ArgumentException("Для GPU выберите непустую геометрию до 200000 граней.");
            if (float.IsNaN(voxelMetres) || voxelMetres < .00025f || voxelMetres > .005f)
                throw new ArgumentException("Шаг поля: 0,25–5 мм.");
            Fingerprint = fingerprint;
            VoxelMetres = voxelMetres;
            var surface = new FitSurface(geometry);
            var bounds = geometry[0].Bounds;
            foreach (var t in geometry) { bounds.Encapsulate(t.A); bounds.Encapsulate(t.B); bounds.Encapsulate(t.C); }
            bounds.Expand(.012f);
            if (region.HasValue) bounds = region.Value;
            foreach(float value in new[] {bounds.center.x,bounds.center.y,bounds.center.z,bounds.size.x,bounds.size.y,bounds.size.z})
                if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentException("GPU-область должна быть конечной.");
            if(bounds.size.x<=0||bounds.size.y<=0||bounds.size.z<=0)throw new ArgumentException("Размер GPU-области должен быть положительным.");
            Minimum = bounds.min;
            _nx = Mathf.CeilToInt(bounds.size.x / voxelMetres) + 1;
            _ny = Mathf.CeilToInt(bounds.size.y / voxelMetres) + 1;
            _nz = Mathf.CeilToInt(bounds.size.z / voxelMetres) + 1;
            if (_nx < 2 || _ny < 2 || _nz < 2 || _nx > 512 || _ny > 512 || _nz > 512 || (long)_nx * _ny * _nz > 8000000)
                throw new NotSupportedException("Поле превышает 8 млн voxel/512 по оси. Уменьшите область поля или увеличьте шаг.");
            Size = new Vector3((_nx - 1) * voxelMetres, (_ny - 1) * voxelMetres, (_nz - 1) * voxelMetres);
            try {
                var source = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
                if (!source) throw new InvalidOperationException("Не импортирован compute shader поля.");
                _shader = Object.Instantiate(source); _shader.hideFlags = HideFlags.HideAndDontSave;
                _bake = _shader.FindKernel("Bake"); _query = _shader.FindKernel("Query");
                surface.ExportGpuBvh(out var minimum, out var maximum, out var ranges, out var ordered);
                var nodes = new Node[minimum.Length];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = new Node { Min = minimum[i], Max = maximum[i], Range = ranges[i] };
                var triangles = ordered.Select(t => new Triangle { A = t.A, B = t.B, C = t.C }).ToArray();
                _nodes = new ComputeBuffer(nodes.Length, 48); _nodes.SetData(nodes);
                _triangles = new ComputeBuffer(triangles.Length, 48); _triangles.SetData(triangles);
                var topology = HandPoseFitGeometry.Topology(geometry);
                // XOR лучей не применяется к нескольким закрытым частям/их пересечениям.
                CanSign = topology.CanDetermineInside && topology.Components != null && topology.Components.Count == 1;
                Texture = new RenderTexture(_nx, _ny, 0, RenderTextureFormat.RGFloat) {
                    dimension = TextureDimension.Tex3D, volumeDepth = _nz, enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
                    name = "HandPoseFit owned distance field"
                };
                if (!Texture.Create()) throw new NotSupportedException("Не удалось создать RGFloat 3D поле.");
                _shader.SetBuffer(_bake, "_Nodes", _nodes); _shader.SetBuffer(_bake, "_Triangles", _triangles);
                _shader.SetTexture(_bake, "_WriteField", Texture);
                _shader.SetInts("_Dimensions", _nx, _ny, _nz);
                _shader.SetVector("_Minimum", Minimum); _shader.SetVector("_Size", Size);
                _shader.SetFloat("_Voxel", VoxelMetres); _shader.SetFloat("_Error", ErrorMetres);
                _shader.SetInt("_CanSign", CanSign ? 1 : 0);
            } catch { Dispose(); throw; }
        }

        /// <summary>Один ограниченный пакет работы; готовое поле переиспользуется при правке пальцев.</summary>
        public void BuildStep(int slices = 4)
        {
            if (_disposed || Ready) return;
            int count = Mathf.Min(Mathf.Clamp(slices, 1, 16), _nz - _z);
            _shader.SetInt("_OffsetZ", _z); _shader.SetInt("_SliceCount", count);
            _shader.Dispatch(_bake, (_nx + 3) / 4, (_ny + 3) / 4, (count + 3) / 4);
            _z += count;
        }

        public bool Query(Vector3[] points, Action<Contact[]> completed)
        {
            if (!Ready || _pending || points == null || points.Length == 0 || points.Length > 1024) return false;
            if (!SystemInfo.supportsAsyncGPUReadback) return false;
            DispatchQuery(points);
            _pending = true;
            _readback = AsyncGPUReadback.Request(_contacts, request => {
                _pending = false;
                if (_disposed || request.hasError) return;
                completed?.Invoke(request.GetData<Contact>().ToArray());
            });
            // Не продвигать чужой PlayerLoop ради editor readback.
            _readback.forcePlayerLoopUpdate = false;
            return true;
        }

        void DispatchQuery(Vector3[] points)
        {
            _points?.Release(); _contacts?.Release();
            _points = new ComputeBuffer(points.Length, 16);
            _points.SetData(points.Select(p => new Vector4(p.x, p.y, p.z, 0)).ToArray());
            _contacts = new ComputeBuffer(points.Length, 32);
            _shader.SetBuffer(_query, "_Points", _points); _shader.SetBuffer(_query, "_Contacts", _contacts);
            _shader.SetBuffer(_query, "_Nodes", _nodes); _shader.SetBuffer(_query, "_Triangles", _triangles);
            _shader.SetTexture(_query, "_ReadField", Texture); _shader.SetInt("_PointCount", points.Length);
            _shader.Dispatch(_query, (points.Length + 63) / 64, 1, 1);
        }

        /// <summary>Синхронный readback только для ограниченного native fixture под замком.</summary>
        public Contact[] QueryDiagnosticProbe(Vector3[] points)
        {
            if (!Ready || _pending || points == null || points.Length == 0 || points.Length > 1024)
                throw new InvalidOperationException("GPU fixture: поле/точки не готовы.");
            DispatchQuery(points);
            var result = new Contact[points.Length];_contacts.GetData(result);return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_pending) { _readback.forcePlayerLoopUpdate = false; _readback.WaitForCompletion(); _pending = false; }
            _points?.Release(); _contacts?.Release(); _nodes?.Release(); _triangles?.Release();
            _points = _contacts = _nodes = _triangles = null;
            if (Texture) { Texture.Release(); Object.DestroyImmediate(Texture); Texture = null; }
            if (_shader) Object.DestroyImmediate(_shader);
        }
    }
}
