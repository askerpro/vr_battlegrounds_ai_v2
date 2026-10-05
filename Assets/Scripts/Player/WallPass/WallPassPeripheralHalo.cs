using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>Одна сферическая оболочка вокруг центра головы для обеих XR-проекций.</summary>
    public sealed class WallPassPeripheralHalo : IDisposable
    {
        private const int Sectors = 64;
        private const int Rings = 24;
        private float _width = -1f;
        public Mesh Mesh { get; private set; }
        public Material Material { get; private set; }
        public Matrix4x4 Matrix { get; private set; }

        public WallPassPeripheralHalo()
        {
            Mesh = new Mesh { name = "T40 shared head halo", hideFlags = HideFlags.HideAndDontSave };
            Material source = Resources.Load<Material>("WallPassHalo");
            if (source != null && source.shader != null && source.shader.isSupported)
                Material = new Material(source) { hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            else GameLog.Player.Error("[WallPassPeripheralHalo] Resources/WallPassHalo недоступен.");
        }

        public void UpdateVisual(Camera camera, float severity, WallPassVisualSettings settings, bool violating)
        {
            if (camera == null || settings == null || Material == null) return;
            float width = Mathf.Clamp(WallPassVisualSettings.Safe(settings.PeripheralWidth, 0.28f), 0.08f, 0.5f);
            if (!Mathf.Approximately(width, _width)) Rebuild(width);
            float radius = Mathf.Clamp(WallPassVisualSettings.Safe(settings.EffectRadius, 1.5f), 1f, 3f);
            Matrix = Matrix4x4.TRS(camera.transform.position, camera.transform.rotation, Vector3.one * radius);
            Color color = violating ? new Color(1f, 0.08f, 0.03f) : new Color(1f, 0.68f, 0.15f);
            color.a = Mathf.Clamp01(WallPassVisualSettings.Safe(severity, 0f) * WallPassVisualSettings.Safe01(settings.RimStrength));
            Material.SetColor("_Color", color);
        }

        private void Rebuild(float width)
        {
            _width = width;
            var vertices = new Vector3[(Rings + 1) * (Sectors + 1)];
            var colors = new Color[vertices.Length];
            var triangles = new int[Rings * Sectors * 6];
            int index = 0;
            for (int ring = 0; ring <= Rings; ring++)
            {
                float polar = ring * Mathf.PI / Rings;
                for (int sector = 0; sector <= Sectors; sector++)
                {
                    float azimuth = sector * Mathf.PI * 2f / Sectors;
                    Vector3 position = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth),
                        Mathf.Sin(polar) * Mathf.Sin(azimuth), Mathf.Cos(polar));
                    int vertex = ring * (Sectors + 1) + sector;
                    vertices[vertex] = position;
                    colors[vertex] = new Color(1f, 1f, 1f, WallPassHeadSpace.PeripheralAlpha(position, width));
                    if (ring == Rings || sector == Sectors) continue;
                    triangles[index++] = vertex;
                    triangles[index++] = vertex + Sectors + 1;
                    triangles[index++] = vertex + 1;
                    triangles[index++] = vertex + 1;
                    triangles[index++] = vertex + Sectors + 1;
                    triangles[index++] = vertex + Sectors + 2;
                }
            }
            Mesh.Clear();
            Mesh.vertices = vertices;
            Mesh.colors = colors;
            Mesh.triangles = triangles;
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.01f);
        }

        public void Dispose()
        {
            Destroy(Mesh);
            Destroy(Material);
            Mesh = null;
            Material = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
