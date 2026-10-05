using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Ограниченный объём нитей вокруг головы. Движение задаётся в мировых координатах;
    /// сетка переводится в координаты камеры только для явного локального DrawMesh.
    /// </summary>
    public sealed class WallPassParticleFlow : IDisposable
    {
        private const int MaximumParticles = 96;
        private const float HalfTravel = 2.4f;
        private readonly List<Vector3> _vertices = new List<Vector3>(MaximumParticles * 10);
        private readonly List<Vector2> _uv = new List<Vector2>(MaximumParticles * 10);
        private readonly List<Color> _colors = new List<Color>(MaximumParticles * 10);
        private readonly List<int> _indices = new List<int>(MaximumParticles * 18);
        private readonly Vector3[] _offsets = new Vector3[MaximumParticles];
        private Vector3 _direction;
        private float _lastTime;
        private float _effectRadius;
        private float _peripheralWidth;
        private bool _initialized;
        private bool _disposed;

        public Mesh Mesh { get; private set; }
        public Material Material { get; private set; }
        public Matrix4x4 Matrix { get; private set; } = Matrix4x4.identity;

        public WallPassParticleFlow()
        {
            Mesh = new Mesh { name = "WallPass particle flow", hideFlags = HideFlags.HideAndDontSave };
            Mesh.MarkDynamic();
            // Ссылка Resources-материала удерживает шейдер и исходную текстуру в Android-билде.
            Material source = Resources.Load<Material>("WallPassFlow");
            if (source != null)
                Material = new Material(source) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
        }

        public void UpdateVisual(Camera camera, Vector3 direction, float severity, WallPassVisualSettings settings, float time)
        {
            if (_disposed) return;
            severity = Mathf.Clamp(WallPassVisualSettings.Safe(severity, 0.0f), 0.0f, 2.0f);
            time = WallPassVisualSettings.Safe(time, _lastTime);
            direction.x = WallPassVisualSettings.Safe(direction.x, 0.0f);
            direction.z = WallPassVisualSettings.Safe(direction.z, 0.0f);
            direction.y = 0.0f;
            if (camera == null || settings == null || Material == null || severity <= 0.0f ||
                WallPassVisualSettings.Safe01(settings.ParticleDensity) <= 0.0f || direction.sqrMagnitude < 0.0001f)
            {
                Clear();
                return;
            }

            direction.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            float effectRadius = Mathf.Clamp(WallPassVisualSettings.Safe(settings.EffectRadius, 1.5f), 1f, 3f);
            float distanceScale = effectRadius / 1.5f;
            float halfTravel = HalfTravel * distanceScale;
            _peripheralWidth = settings.PeripheralWidth;
            if (!_initialized || Vector3.Dot(_direction, direction) < 0.985f || time < _lastTime ||
                !Mathf.Approximately(_effectRadius, effectRadius))
            {
                // Весь объём окружает голову: при повороте открываются уже существующие частицы,
                // а направление не поворачивается вместе со взглядом.
                for (int i = 0; i < MaximumParticles; i++)
                {
                    float angle = Hash(i, 1) * Mathf.PI * 2.0f;
                    // Общий объём не подходит близко к отдельному глазу: минимум 0.8 радиуса.
                    float radius = effectRadius * Mathf.Lerp(0.8f, 1.35f, Mathf.Sqrt(Hash(i, 2)));
                    _offsets[i] = side * (Mathf.Cos(angle) * radius) + Vector3.up * (Mathf.Sin(angle) * radius)
                        + direction * Mathf.Lerp(-halfTravel, halfTravel, Hash(i, 3));
                }
                _initialized = true;
                _direction = direction;
                _lastTime = time;
                _effectRadius = effectRadius;
            }

            float delta = Mathf.Clamp(time - _lastTime, 0.0f, 0.1f);
            _lastTime = time;
            float strength = WallPassVisualSettings.Safe01(severity);
            float speed = Mathf.Clamp(WallPassVisualSettings.Safe(settings.ParticleSpeed, 0.7f), 0.05f, 2.0f) * 1.2f * distanceScale;
            float densityCount = Mathf.Lerp(24, MaximumParticles, WallPassVisualSettings.Safe01(settings.ParticleDensity));
            int count = Mathf.RoundToInt(Mathf.Lerp(24, densityCount, Mathf.Lerp(0.5f, 1.0f, strength)));
            float brightness = Mathf.Clamp(WallPassVisualSettings.Safe(settings.ParticleBrightness, 0.7f), 0.0f, 2.0f);
            // Severity уже включает Intensity. Яркость выше единицы усиливает RGB,
            // сохраняя корректный premultiplied alpha вместо насыщения всего ползунка.
            float opacity = strength * Mathf.Clamp01(brightness);
            float tailLength = Mathf.Clamp(WallPassVisualSettings.Safe(settings.ParticleTailLength, 0.12f), 0.02f, 0.4f)
                * Mathf.Lerp(0.65f, 1.0f, strength) * distanceScale;
            Matrix = Matrix4x4.TRS(camera.transform.position, camera.transform.rotation, Vector3.one);
            Quaternion inverseRotation = Quaternion.Inverse(camera.transform.rotation);
            float minimumDepth = Mathf.Max(0.16f, camera.nearClipPlane + 0.06f);
            _vertices.Clear();
            _uv.Clear();
            _colors.Clear();
            _indices.Clear();

            for (int i = 0; i < MaximumParticles; i++)
            {
                Vector3 offset = _offsets[i] + direction * (speed * delta);
                float along = Vector3.Dot(offset, direction);
                if (along > halfTravel) offset -= direction * (halfTravel * 2.0f);
                _offsets[i] = offset;
                if (i >= count) continue;

                Vector3 head = inverseRotation * offset;
                if (head.z < minimumDepth) continue;
                Vector3 tangent = inverseRotation * direction;
                Vector3 ribbonSide = Vector3.Cross(tangent, head.normalized);
                if (ribbonSide.sqrMagnitude < 0.0001f) ribbonSide = Vector3.right;
                ribbonSide.Normalize();
                float radius = Mathf.Lerp(0.009f, 0.018f, Hash(i, 4)) * distanceScale;
                float fade = Mathf.SmoothStep(0.0f, 1.0f, (halfTravel - Mathf.Abs(Vector3.Dot(offset, direction))) / (0.3f * distanceScale));
                fade *= Mathf.SmoothStep(0.0f, 1.0f, (head.z - minimumDepth) / 0.18f);
                Color color = Color.Lerp(new Color(1.0f, 0.68f, 0.20f), new Color(1.0f, 0.27f, 0.08f), Mathf.Clamp01(severity));
                color *= Mathf.Max(1.0f, brightness);
                color.a = opacity * fade;
                AddRibbon(head, tangent, ribbonSide, tailLength, radius, color);
                AddHead(head, radius * 1.7f, color);
            }

            Mesh.Clear(false);
            Mesh.SetVertices(_vertices);
            Mesh.SetUVs(0, _uv);
            Mesh.SetColors(_colors);
            Mesh.SetTriangles(_indices, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (effectRadius * 2.7f + halfTravel * 2f));
        }

        public void Clear()
        {
            if (!_disposed && Mesh != null) Mesh.Clear(false);
            _initialized = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DestroyOwned(Mesh);
            DestroyOwned(Material);
            Mesh = null;
            Material = null;
        }

        private void AddRibbon(Vector3 head, Vector3 tangent, Vector3 side, float length, float width, Color color)
        {
            int start = _vertices.Count;
            for (int segment = 0; segment < 3; segment++)
            {
                float phase = segment * 0.5f;
                Vector3 center = head - tangent * (length * (1.0f - phase));
                float span = width * Mathf.Lerp(0.15f, 0.65f, phase);
                Color tailColor = color;
                tailColor.a *= phase * phase * 0.65f;
                AddVertex(center - side * span, new Vector2(0, phase), tailColor);
                AddVertex(center + side * span, new Vector2(1, phase), tailColor);
                if (segment > 0) AddQuad(start + (segment - 1) * 2, start + (segment - 1) * 2 + 1,
                    start + segment * 2, start + segment * 2 + 1);
            }
        }

        private void AddHead(Vector3 head, float radius, Color color)
        {
            // Обе оси ортогональны лучу на головку, поэтому спрайт остаётся facing-camera в 3D.
            Vector3 right = Vector3.Cross(Vector3.up, head.normalized).normalized * radius;
            if (right.sqrMagnitude < 0.000001f) right = Vector3.right * radius;
            Vector3 up = Vector3.Cross(head.normalized, right).normalized * radius;
            int start = _vertices.Count;
            AddVertex(head - right - up, new Vector2(0, 0), color);
            AddVertex(head + right - up, new Vector2(1, 0), color);
            AddVertex(head - right + up, new Vector2(0, 1), color);
            AddVertex(head + right + up, new Vector2(1, 1), color);
            AddQuad(start, start + 1, start + 2, start + 3);
        }

        private void AddVertex(Vector3 position, Vector2 uv, Color color)
        {
            // Одна альфа в общей сетке. Оба глаза проецируют её, не режут поток своими рамками.
            color.a *= WallPassHeadSpace.PeripheralAlpha(position, _peripheralWidth);
            _vertices.Add(position);
            _uv.Add(uv);
            _colors.Add(color);
        }

        private void AddQuad(int a, int b, int c, int d)
        {
            _indices.Add(a); _indices.Add(c); _indices.Add(b);
            _indices.Add(b); _indices.Add(c); _indices.Add(d);
        }

        private static float Hash(int index, uint salt)
        {
            uint value = (uint)index * 747796405u + salt * 2891336453u + 277803737u;
            value = ((value >> (int)((value >> 28) + 4)) ^ value) * 277803737u;
            value = (value >> 22) ^ value;
            return (value & 0x00ffffffu) / 16777216.0f;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
