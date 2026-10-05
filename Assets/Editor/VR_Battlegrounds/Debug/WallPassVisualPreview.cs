using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VrBattlegrounds.Player.WallPass;

namespace VrBattlegrounds.Editor
{
    /// <summary>Изолированная preview-сцена; не открывает и не сохраняет рабочие сцены.</summary>
    public sealed class WallPassVisualPreview : IDisposable
    {
        private readonly PreviewRenderUtility _preview = new PreviewRenderUtility();
        private readonly WallPassCameraEffect _effect = new WallPassCameraEffect();
        private readonly WallPassParticleFlow _flow = new WallPassParticleFlow();
        private readonly Material _material;
        private readonly Renderer[] _hands;
        public RenderTexture Texture { get; }

        public WallPassVisualPreview()
        {
            Texture = new RenderTexture(640, 480, 24) { name = "T40 Preview", hideFlags = HideFlags.HideAndDontSave };
            Texture.Create();
            Camera camera = _preview.camera;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.5f, -3f), Quaternion.identity);
            camera.fieldOfView = 85f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetColor("_BaseColor", new Color(0.65f, 0.52f, 0.3f));
            AddShape(PrimitiveType.Cube, new Vector3(0, -0.1f, 2), new Vector3(6, 0.2f, 12));
            AddShape(PrimitiveType.Cube, new Vector3(-2, 1.5f, 2), new Vector3(0.2f, 3, 10));
            AddShape(PrimitiveType.Cube, new Vector3(2, 1.5f, 2), new Vector3(0.2f, 3, 10));
            AddShape(PrimitiveType.Cube, new Vector3(0, 1.5f, 5), new Vector3(4, 3, 0.2f));
            _hands = new[]
            {
                AddShape(PrimitiveType.Capsule, new Vector3(-0.35f, 1.15f, -1.8f), new Vector3(0.13f, 0.25f, 0.13f)),
                AddShape(PrimitiveType.Capsule, new Vector3(0.35f, 1.15f, -1.8f), new Vector3(0.13f, 0.25f, 0.13f))
            };
        }

        private Renderer AddShape(PrimitiveType shape, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.hideFlags = HideFlags.HideAndDontSave;
            _preview.AddSingleGO(go);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            go.transform.localScale = scale;
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = _material;
            return renderer;
        }

        public void Render(WallPassVisualSettings settings, float risk, float yaw, float time, float eyeOffset = 0f)
        {
            if (UniversalRenderPipeline.asset == null) return;
            Camera camera = _preview.camera;
            Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
            float strength = settings.Evaluate(risk);
            _flow.UpdateVisual(camera, direction, strength, settings, time);
            _effect.SetVisual(camera, strength, settings, _hands, _flow);
            // Геометрия фиксируется выше в центре головы; сдвигается только точка наблюдения.
            // Это редакторская проверка IPD, без вмешательства в реальный XR tracking.
            Vector3 center = camera.transform.position;
            try
            {
                camera.transform.position = center + camera.transform.right * eyeOffset;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = Texture });
            }
            finally { camera.transform.position = center; }
        }

        public void Dispose()
        {
            _effect.Dispose();
            _flow.Dispose();
            _preview.Cleanup();
            UnityEngine.Object.DestroyImmediate(_material);
            Texture.Release();
            UnityEngine.Object.DestroyImmediate(Texture);
        }
    }
}
