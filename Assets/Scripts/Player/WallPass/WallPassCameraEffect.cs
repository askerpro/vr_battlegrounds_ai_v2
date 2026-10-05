using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using VrBattlegrounds.Core;
using static UnityEngine.Rendering.RenderGraphModule.Util.RenderGraphUtils;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Собственный проход назначенной камеры. Не меняет sharedMaterials, Volume или
    /// renderPostProcessing, которым уже владеет GhostViewEffect.
    /// </summary>
    public sealed class WallPassCameraEffect : IDisposable
    {
        private readonly VisualPass _pass = new VisualPass();
        private readonly WallPassPeripheralHalo _halo = new WallPassPeripheralHalo();
        private Material _world;
        private Material _body;
        private Camera _camera;
        private bool _failed;

        public WallPassCameraEffect() => RenderPipelineManager.beginCameraRendering += BeginCamera;

        public void SetVisual(Camera camera, float severity, WallPassVisualSettings settings,
                              Renderer[] ownRenderers, WallPassParticleFlow flow, bool stylize = true)
        {
            _camera = camera;
            if (camera == null || settings == null || severity <= 0f) { Clear(); return; }
            if (!_failed && _world == null)
            {
                Material world = Resources.Load<Material>("WallPassWorld");
                Material body = Resources.Load<Material>("WallPassBody");
                if (world == null || body == null || world.shader == null || body.shader == null ||
                    !world.shader.isSupported || !body.shader.isSupported)
                {
                    _failed = true;
                    GameLog.Player.Error("[WallPassCameraEffect] Resources-шейдер окружения/тела недоступен; поток и предупреждение сохраняются.");
                }
                else
                {
                    _world = new Material(world) { hideFlags = HideFlags.HideAndDontSave };
                    _body = new Material(body) { hideFlags = HideFlags.HideAndDontSave };
                }
            }
            _pass.World = _world;
            _pass.Body = _body;
            _pass.WorldStrength = stylize ? Mathf.Clamp01(severity * WallPassVisualSettings.Safe01(settings.WorldStrength)) : 0f;
            _pass.BodyStrength = stylize ? Mathf.Clamp01(severity * WallPassVisualSettings.Safe01(settings.BodyStrength)) : 0f;
            _pass.RimStrength = Mathf.Clamp01(severity * WallPassVisualSettings.Safe01(settings.RimStrength));
            _halo.UpdateVisual(camera, severity, settings, stylize);
            _pass.Halo = _halo;
            _pass.Renderers = ownRenderers ?? Array.Empty<Renderer>();
            _pass.Flow = flow;
            _pass.requiresIntermediateTexture = _world != null && _pass.WorldStrength > 0f;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera) EnqueueForCamera(camera);
        }

        private void EnqueueForCamera(Camera camera)
        {
            if (_camera != camera || camera == null || UniversalRenderPipeline.asset == null) return;
            ScriptableRenderer renderer = camera.TryGetComponent(out UniversalAdditionalCameraData data)
                ? data.scriptableRenderer : UniversalRenderPipeline.asset.scriptableRenderer;
            renderer?.EnqueuePass(_pass);
        }

        public void Clear()
        {
            _camera = null;
            _pass.Flow = null;
            _pass.Halo = null;
            _pass.Renderers = Array.Empty<Renderer>();
            _pass.WorldStrength = _pass.BodyStrength = _pass.RimStrength = 0f;
        }

        public void Dispose()
        {
            Clear();
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            _halo.Dispose();
            Destroy(_world);
            Destroy(_body);
            _world = _body = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        private sealed class VisualPass : ScriptableRenderPass
        {
            public Material World, Body;
            public float WorldStrength, BodyStrength, RimStrength;
            public Renderer[] Renderers = Array.Empty<Renderer>();
            public WallPassParticleFlow Flow;
            public WallPassPeripheralHalo Halo;

            private sealed class PassData
            {
                public TextureHandle Source;
                public Material World, Body, FlowMaterial, HaloMaterial;
                public float WorldStrength, BodyStrength, RimStrength;
                public Renderer[] Renderers;
                public Mesh FlowMesh;
                public Matrix4x4 FlowMatrix;
                public Mesh HaloMesh;
                public Matrix4x4 HaloMatrix;
                public Camera Camera;
            }

            public VisualPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                profilingSampler = new ProfilingSampler("T40 Local Feedback");
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                TextureHandle source = TextureHandle.nullHandle;
                bool world = World != null && WorldStrength > 0f;
                if (world && !resources.isActiveTargetBackBuffer)
                {
                    var description = graph.GetTextureDesc(resources.activeColorTexture);
                    description.name = "T40 Color Copy";
                    description.clearBuffer = false;
                    source = graph.CreateTexture(description);
                    graph.AddBlitPass(resources.activeColorTexture, source, Vector2.one, Vector2.zero, passName: "T40 Copy Color");
                }
                using (var builder = graph.AddRasterRenderPass<PassData>("T40 Local Feedback", out var data, profilingSampler))
                {
                    data.Source = source;
                    data.World = World;
                    data.Body = Body;
                    data.WorldStrength = WorldStrength;
                    data.BodyStrength = BodyStrength;
                    data.RimStrength = RimStrength;
                    data.HaloMesh = Halo?.Mesh;
                    data.HaloMaterial = Halo?.Material;
                    data.HaloMatrix = Halo != null ? Halo.Matrix : Matrix4x4.identity;
                    data.Renderers = Renderers;
                    data.FlowMaterial = Flow?.Material;
                    data.FlowMesh = Flow?.Mesh;
                    data.FlowMatrix = Flow != null ? Flow.Matrix : Matrix4x4.identity;
                    data.Camera = camera.camera;
                    if (source.IsValid()) builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    if (resources.activeDepthTexture.IsValid())
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                    {
                        if (pass.Source.IsValid())
                        {
                            pass.World.SetFloat("_Strength", pass.WorldStrength);
                            Blitter.BlitTexture(context.cmd, pass.Source, new Vector4(1, 1, 0, 0), pass.World, 0);
                        }
                        if (pass.Body != null && pass.BodyStrength > 0f)
                        {
                            pass.Body.SetFloat("_Strength", pass.BodyStrength);
                            foreach (Renderer renderer in pass.Renderers)
                            {
                                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                                    !renderer.gameObject.activeInHierarchy ||
                                    (pass.Camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                                    renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh :
                                    renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                                if (mesh == null) continue;
                                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                                    context.cmd.DrawRenderer(renderer, pass.Body, submesh, 0);
                            }
                        }
                        if (pass.HaloMesh != null && pass.HaloMaterial != null && pass.RimStrength > 0f)
                        {
                            context.cmd.DrawMesh(pass.HaloMesh, pass.HaloMatrix, pass.HaloMaterial, 0, 0);
                        }
                        if (pass.FlowMesh != null && pass.FlowMaterial != null && pass.FlowMesh.vertexCount > 0)
                            context.cmd.DrawMesh(pass.FlowMesh, pass.FlowMatrix, pass.FlowMaterial, 0, 0);
                    });
                }
            }
        }
    }
}
