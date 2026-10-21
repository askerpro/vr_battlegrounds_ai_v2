#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Диагностический вариант рендера для normal A/B (только E2E perf-stress с diagnostic RunSpec):
    /// дешёвый шейдер пола вместо S_VPWorldAligned и/или камера без Procedural-скайбокса.
    /// Временная подмена до разгона, полный возврат в Dispose. Это не правка сцены и не путь к релизу:
    /// он отвечает, сколько GPU-времени даст такая правка, прежде чем менять ассеты.
    /// </summary>
    internal sealed class PerfRenderDiagnosticControl : IDisposable
    {
        internal const string ArgKey = "render_diag";
        internal const string FloorShaderName = "Shader Graphs/S_VPWorldAligned";
        internal const string CheapShaderName = "Shader Graphs/S_BasicTextured";
        internal const string FreeShaderName = "Universal Render Pipeline/Unlit";
        // В Development-сборку шейдер кладёт PerfDiagnosticShaderBuildProcessor (Editor); в Prod его нет.
        internal const string PlanarShaderName = "VR Battlegrounds/Diagnostics/PerfPlanarFloor";
        // floor-basic — S_BasicTextured; floor-unlit — URP Unlit по UV меша (нижняя граница, визуально непригоден:
        // UV пола рассчитана на мировую проекцию); floor-planar-unlit/-lit — одна выборка с проекцией сверху по миру
        // (PerfPlanarFloor), без света / с запечённым светом.
        private static readonly string[] Variants = { "none", "floor-basic", "floor-unlit", "floor-planar-unlit",
            "floor-planar-lit", "skybox-color", "floor-basic+skybox-color", "floor-unlit+skybox-color" };

        internal readonly string Variant;
        internal string Summary { get; private set; } = "pending";
        internal string RestoreStatus { get; private set; } = "pending";

        private readonly List<(Renderer renderer, Material[] original)> _renderers = new List<(Renderer, Material[])>();
        private readonly Dictionary<Material, Material> _replacements = new Dictionary<Material, Material>();
        private Camera _camera;
        private CameraClearFlags _clearFlags;
        private Color _background;
        private Material _skybox;
        private string _cheapShader;
        private bool _skyboxChanged, _disposed;

        internal static bool IsKnown(string value) => Array.IndexOf(Variants, value) >= 0;

        internal static string Validate(E2EContext context)
        {
            if (!context.Args.TryGetValue(ArgKey, out string value)) return null;
            if (context.Diagnostics == null) return "render_diag требует diagnostic RunSpec";
            return IsKnown(value) ? null : "render_diag: допустимы " + string.Join(", ", Variants);
        }

        internal PerfRenderDiagnosticControl(string variant)
        {
            if (!IsKnown(variant)) throw new ArgumentException("Unknown render diagnostic", nameof(variant));
            Variant = variant;
            try
            {
                int floorSlots = variant.Contains("floor-basic") ? ReplaceFloor(FloorMode.Basic)
                    : variant.Contains("floor-planar-unlit") ? ReplaceFloor(FloorMode.PlanarUnlit)
                    : variant.Contains("floor-planar-lit") ? ReplaceFloor(FloorMode.PlanarLit)
                    : variant.Contains("floor-unlit") ? ReplaceFloor(FloorMode.UvUnlit) : 0;
                if (variant.Contains("skybox-color")) DisableSkybox();
                // Камера отражения зеркала копирует clearFlags/фон камеры игрока: skybox-color меняет и отражение.
                Summary = string.Format("floorSlots={0}, floorMaterials={1}, cheapShader={2}, skyboxChanged={3}{4}",
                    floorSlots, _replacements.Count, _cheapShader ?? "-", _skyboxChanged,
                    _skyboxChanged ? " (mirror reflection also solid-colour)" : "");
                GameLog.Perf.Info("[PerfDiagnostic] render_diag " + Variant + ": " + Summary);
            }
            catch { Dispose(); throw; }
        }

        private enum FloorMode { Basic, UvUnlit, PlanarUnlit, PlanarLit }

        // Пол: каждый материал на S_VPWorldAligned → копия на дешёвом шейдере с основным слоем исходного.
        // Basic: S_BasicTextured (MainBC/MainNormal, проекция по миру), без вершинного смешивания слоёв и шума краёв.
        // UvUnlit: URP Unlit с MainBC по UV меша. Basic/UvUnlit берут шейдер у загруженного материала сцены.
        // PlanarUnlit/PlanarLit: PerfPlanarFloor (Shader.Find, только Development-сборки) — одна выборка MainBC по миру, без света / с лайтмапой.
        private int ReplaceFloor(FloorMode mode)
        {
            bool planar = mode == FloorMode.PlanarUnlit || mode == FloorMode.PlanarLit;
            bool uvUnlit = mode == FloorMode.UvUnlit;
            Shader cheap;
            if (planar)
            {
                cheap = Shader.Find(PlanarShaderName);
                if (cheap == null || !cheap.isSupported)
                    throw new InvalidOperationException("render_diag: шейдер " + PlanarShaderName + " недоступен (в сборке только Development)");
                _cheapShader = cheap.name + "#" + cheap.GetInstanceID();
            }
            else
            {
                string shaderName = uvUnlit ? FreeShaderName : CheapShaderName;
                // В проекте несколько Shader Graph с именем S_BasicTextured; нужен тот, что умеет проекцию по миру.
                cheap = FindShaderInScene(shaderName, uvUnlit ? null : "_UseWorldAligned");
                _cheapShader = shaderName + "#" + cheap.GetInstanceID();
            }
            int slots = 0;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material[] original = renderer.sharedMaterials;
                Material[] patched = null;
                for (int i = 0; i < original.Length; i++)
                {
                    Material source = original[i];
                    if (source == null || source.shader == null || source.shader.name != FloorShaderName) continue;
                    if (!_replacements.TryGetValue(source, out Material replacement))
                    {
                        replacement = new Material(cheap) { name = source.name + " (render_diag " + Variant + ")" };
                        Copy(source, "_MainBC", replacement, "_BaseMap");
                        float tile = source.HasProperty("_MainTile") ? source.GetFloat("_MainTile") : 1f;
                        if (planar)
                        {
                            replacement.SetFloat("_MainTile", tile);
                            if (source.HasProperty("_MainTint")) replacement.SetColor("_BaseColor", source.GetColor("_MainTint"));
                            if (mode == FloorMode.PlanarLit) replacement.EnableKeyword("_PLANAR_LIT");
                        }
                        else if (uvUnlit)
                        {
                            replacement.SetTextureScale("_BaseMap", new Vector2(tile, tile));
                            if (source.HasProperty("_MainTint") && replacement.HasProperty("_BaseColor"))
                                replacement.SetColor("_BaseColor", source.GetColor("_MainTint"));
                        }
                        else
                        {
                            Copy(source, "_MainNormal", replacement, "_NormalMap");
                            if (replacement.HasProperty("_Tiling")) replacement.SetVector("_Tiling", new Vector4(tile, tile, 0f, 0f));
                            if (source.HasProperty("_MainTint") && replacement.HasProperty("_Tint"))
                                replacement.SetColor("_Tint", source.GetColor("_MainTint"));
                            if (replacement.HasProperty("_UseWorldAligned")) replacement.SetFloat("_UseWorldAligned", 1f);
                            // Дефолты графа S_BasicTextured: _Metallic=1 и ORMH пака — пол получается чёрным металлом.
                            // Диэлектрик без AO: металличность 0, ORM белая (AO=1, roughness=1).
                            if (replacement.HasProperty("_Metallic")) replacement.SetFloat("_Metallic", 0f);
                            if (replacement.HasProperty("_ORMH")) replacement.SetTexture("_ORMH", Texture2D.whiteTexture);
                        }
                        _replacements.Add(source, replacement);
                    }
                    if (patched == null) patched = (Material[])original.Clone();
                    patched[i] = replacement;
                    slots++;
                }
                if (patched == null) continue;
                _renderers.Add((renderer, original));
                renderer.sharedMaterials = patched;
            }
            if (slots == 0) throw new InvalidOperationException("render_diag: нет рендереров с " + FloorShaderName);
            return slots;
        }

        private static void Copy(Material from, string fromName, Material to, string toName)
        {
            if (from.HasProperty(fromName) && to.HasProperty(toName)) to.SetTexture(toName, from.GetTexture(fromName));
        }

        // Шейдер у уже загруженного материала сцены (значит, он есть в сборке); ровно один подходящий, иначе отказ.
        private static Shader FindShaderInScene(string name, string requiredProperty)
        {
            var found = new HashSet<Shader>();
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && material.shader != null && material.shader.name == name &&
                        (requiredProperty == null || material.HasProperty(requiredProperty)))
                        found.Add(material.shader);
            if (found.Count != 1)
                throw new InvalidOperationException(string.Format("render_diag: подходящих шейдеров {0} — {1}", name, found.Count));
            foreach (Shader shader in found) return shader;
            return null;
        }

        // Скайбокс: камера игрока очищается однотонным цветом окружения вместо Procedural-неба.
        private void DisableSkybox()
        {
            _camera = UxrAvatar.LocalAvatarCamera;
            if (_camera == null) throw new InvalidOperationException("render_diag: нет камеры локального аватара");
            _clearFlags = _camera.clearFlags; _background = _camera.backgroundColor; _skybox = RenderSettings.skybox;
            Color ambient = RenderSettings.ambientSkyColor;
            _camera.backgroundColor = new Color(ambient.r, ambient.g, ambient.b, 1f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            RenderSettings.skybox = null;
            _skyboxChanged = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var problems = new List<string>();
            int destroyed = 0;
            foreach ((Renderer renderer, Material[] original) in _renderers)
            {
                if (renderer != null) renderer.sharedMaterials = original;
                else destroyed++;
            }
            if (destroyed > 0) problems.Add("renderers destroyed: " + destroyed);
            foreach (Material replacement in _replacements.Values)
                if (replacement != null) UnityEngine.Object.Destroy(replacement);
            if (_skyboxChanged)
            {
                RenderSettings.skybox = _skybox;
                if (_camera != null) { _camera.clearFlags = _clearFlags; _camera.backgroundColor = _background; }
                else problems.Add("camera destroyed");
            }
            RestoreStatus = problems.Count == 0 ? "restored" : "partial: " + string.Join(", ", problems);
            if (problems.Count > 0) GameLog.Error("[PerfDiagnostic] render_diag restore: " + RestoreStatus);
        }
    }
}
#endif
