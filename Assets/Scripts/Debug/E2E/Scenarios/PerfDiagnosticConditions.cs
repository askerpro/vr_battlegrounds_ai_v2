#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.Maps.Detail;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>Снимок настроек до разгона; читает единственного владельца SceneDetail.</summary>
    internal sealed class PerfDiagnosticConditions
    {
        internal const string FingerprintMethod = "unity-urp-scene-detail-settings-v1";
        internal readonly JObject Quality;
        internal readonly string Fingerprint;
        internal readonly string UnavailableReason;
        private readonly SceneDetailApplier _applier;
        private readonly uint _revision;
        private readonly RenderPipelineAsset _pipeline;
        private readonly UniversalRenderPipelineAsset _urp;
        private readonly int _level, _mipLimit, _msaa, _cascades, _mainShadow, _additionalShadow;
        private readonly float _lodBias, _renderScale, _shadowDistance;
        private readonly bool _hdr;
        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
        private readonly TextureMipmapLimitSettings[] _textureSettings;
        private readonly bool[] _textureExists;
        private readonly string[] _textureNames;

        internal PerfDiagnosticConditions()
        {
            _applier = SceneDetailApplier.Current;
            _revision = _applier != null ? _applier.DiagnosticsRevision : 0;
            _level = QualitySettings.GetQualityLevel();
            _lodBias = QualitySettings.lodBias;
            _mipLimit = QualitySettings.globalTextureMipmapLimit;
            _pipeline = GraphicsSettings.currentRenderPipeline;
            _urp = _pipeline as UniversalRenderPipelineAsset;
            _renderScale = _urp != null ? _urp.renderScale : 1f;
            _shadowDistance = _urp != null ? _urp.shadowDistance : QualitySettings.shadowDistance;
            _msaa = _urp != null ? _urp.msaaSampleCount : QualitySettings.antiAliasing;
            _hdr = _urp != null && _urp.supportsHDR;
            _cascades = _urp != null ? _urp.shadowCascadeCount : QualitySettings.shadowCascades;
            _mainShadow = _urp != null ? _urp.mainLightShadowmapResolution : 0;
            _additionalShadow = _urp != null ? _urp.additionalLightsShadowmapResolution : 0;
            Quality = new JObject
            {
                ["schemaVersion"] = 1, ["method"] = FingerprintMethod,
                ["coverage"] = "Перечисленные настройки и effective SceneDetail; не hash всех ресурсов/шейдеров, не actual draw.",
                ["qualityLevel"] = _level, ["lodBias"] = _lodBias, ["globalTextureMipmapLimit"] = _mipLimit,
                ["pipelineName"] = _pipeline != null ? _pipeline.name : null,
                ["renderScale"] = _urp != null ? (JToken)_renderScale : JValue.CreateNull(),
                ["shadowDistance"] = _shadowDistance, ["msaa"] = _msaa, ["hdr"] = _hdr,
                ["shadowCascadeCount"] = _cascades, ["mainLightShadowmapResolution"] = _urp != null ? (JToken)_mainShadow : JValue.CreateNull(),
                ["additionalLightsShadowmapResolution"] = _urp != null ? (JToken)_additionalShadow : JValue.CreateNull()
            };
            _textureSettings = new TextureMipmapLimitSettings[SceneDetailApplier.BandEdges.Length];
            _textureExists = new bool[_textureSettings.Length];
            _textureNames = new string[_textureSettings.Length];
            var textures = new JArray();
            for (int i = 0; i < _textureSettings.Length; i++)
            {
                string group = SceneDetailApplier.BandGroupPrefix + i;
                _textureNames[i] = group;
                _textureExists[i] = TextureMipmapLimitGroups.HasGroup(group);
                if (_textureExists[i]) _textureSettings[i] = QualitySettings.GetTextureMipmapLimitSettings(group);
                textures.Add(new JObject { ["name"] = group, ["exists"] = _textureExists[i],
                    ["bias"] = _textureExists[i] ? (JToken)_textureSettings[i].limitBias : JValue.CreateNull(),
                    ["mode"] = _textureExists[i] ? _textureSettings[i].limitBiasMode.ToString() : null });
            }
            Quality["textureGroups"] = textures;
            if (_applier != null)
            {
                if (!_applier.TryCaptureDiagnostics(out SceneDetailDiagnosticsSnapshot snapshot, out string reason) || !snapshot.complete)
                    UnavailableReason = reason ?? "SceneDetail snapshot incomplete";
                else Quality["sceneDetail"] = Detail(snapshot);
            }
            else Quality["sceneDetail"] = JValue.CreateNull();
            if (UnavailableReason == null)
            {
                using (var sha = SHA256.Create())
                    Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Quality.ToString(Formatting.None))))
                        .Replace("-", "").ToLowerInvariant();
            }
            SubsystemManager.GetSubsystems(_displays);
        }

        private static JObject Rule(SceneDetailProfile.PlaneRule rule) => new JObject
        { ["lodSkip"] = rule.lodSkip, ["textureBias"] = rule.textureBias, ["cullSize"] = rule.cullSize };

        private static JObject Detail(SceneDetailDiagnosticsSnapshot snapshot)
        {
            var groups = new JArray();
            foreach (SceneDetailDiagnosticsSnapshot.Group group in snapshot.groups)
            {
                var levels = new JArray();
                foreach (SceneDetailDiagnosticsSnapshot.Level level in group.effective)
                    levels.Add(new JObject { ["screenRelativeTransitionHeight"] = level.screenRelativeTransitionHeight,
                        ["fadeTransitionWidth"] = level.fadeTransitionWidth, ["rendererCount"] = level.renderers.Length });
                groups.Add(new JObject { ["distance"] = group.arenaEdgeDistance, ["effective"] = levels,
                    ["effectiveToOriginal"] = new JArray(group.effectiveToOriginal) });
            }
            var enabled = new JArray();
            foreach (SceneDetailDiagnosticsSnapshot.RendererState renderer in snapshot.renderers) enabled.Add(renderer.currentEnabled);
            return new JObject { ["midStart"] = snapshot.midStart, ["farStart"] = snapshot.farStart,
                ["mid"] = Rule(snapshot.mid), ["far"] = Rule(snapshot.far), ["groups"] = groups, ["rendererEnabled"] = enabled };
        }

        internal bool SettingsUnchanged()
        {
            if (SceneDetailApplier.Current != _applier || (_applier != null && _applier.DiagnosticsRevision != _revision) ||
                QualitySettings.GetQualityLevel() != _level || QualitySettings.lodBias != _lodBias ||
                QualitySettings.globalTextureMipmapLimit != _mipLimit || GraphicsSettings.currentRenderPipeline != _pipeline) return false;
            if (_urp != null && (_urp.renderScale != _renderScale || _urp.shadowDistance != _shadowDistance ||
                _urp.msaaSampleCount != _msaa || _urp.supportsHDR != _hdr || _urp.shadowCascadeCount != _cascades ||
                _urp.mainLightShadowmapResolution != _mainShadow || _urp.additionalLightsShadowmapResolution != _additionalShadow)) return false;
            if (_urp == null && (QualitySettings.shadowDistance != _shadowDistance || QualitySettings.antiAliasing != _msaa ||
                QualitySettings.shadowCascades != _cascades)) return false;
            return true;
        }

        internal bool TextureSettingsUnchanged()
        {
            for (int i = 0; i < _textureSettings.Length; i++)
            {
                string group = _textureNames[i];
                bool exists = TextureMipmapLimitGroups.HasGroup(group);
                if (exists != _textureExists[i]) return false;
                if (!exists) continue;
                TextureMipmapLimitSettings current = QualitySettings.GetTextureMipmapLimitSettings(group);
                if (current.limitBias != _textureSettings[i].limitBias || current.limitBiasMode != _textureSettings[i].limitBiasMode) return false;
            }
            return true;
        }

        internal XRDisplaySubsystem Display
        {
            get { foreach (XRDisplaySubsystem display in _displays) if (display.running) return display; return null; }
        }

        internal Dictionary<string, DiagnosticConditionValue> Capture(string scene, string role, string phase,
            string view, string workload, in PerfRawFrame frame, bool hasFrame)
        {
            var values = new Dictionary<string, DiagnosticConditionValue>(StringComparer.Ordinal);
            void Known(string key, object value, string source) => values.Add(key, new DiagnosticConditionValue(value, source, ""));
            void Unknown(string key, string reason) => values.Add(key, new DiagnosticConditionValue(null, "runtime-observation", reason));
            Known("applicabilityPolicy", "vrb-gpu-platform-v1", "PerfDiagnosticConditions");
            Known("runtimePlatform", Application.platform.ToString(), "Application.platform");
            Known("xrEnabled", XRSettings.enabled, "XRSettings.enabled");
            Known("deviceModel", SystemInfo.deviceModel, "SystemInfo.deviceModel");
            if (string.IsNullOrEmpty(SystemInfo.deviceUniqueIdentifier) || SystemInfo.deviceUniqueIdentifier == SystemInfo.unsupportedIdentifier)
                Unknown("deviceId", "SystemInfo.deviceUniqueIdentifier unavailable");
            else Known("deviceId", SystemInfo.deviceUniqueIdentifier, "SystemInfo.deviceUniqueIdentifier");
            Known("graphicsApi", SystemInfo.graphicsDeviceType.ToString(), "SystemInfo.graphicsDeviceType");
            Known("developmentBuild", Debug.isDebugBuild, "Debug.isDebugBuild");
            Known("sceneId", scene, "MapRunAuthority + MapBootstrap.IsLocallyReady");
            Known("role", role, "E2EDiagnosticSession.Role");
            Known("phaseId", phase, "StressTestClientSession phase observation");
            Known("viewId", view, "E2EViews settled target + fresh camera observation");
            Known("workloadId", workload, "RunSpec identity; actual workload recorded separately");
            Known("qualityFingerprintMethod", FingerprintMethod, "PerfDiagnosticConditions");
            if (Fingerprint == null) Unknown("qualityHash", UnavailableReason); else Known("qualityHash", Fingerprint, FingerprintMethod);
            XRDisplaySubsystem display = Display;
            double refresh = 0;
            if (display != null && display.TryGetDisplayRefreshRate(out float rate)) refresh = rate;
            else if (!XRSettings.enabled) refresh = Screen.currentResolution.refreshRateRatio.value;
            if (refresh > 0) Known("refreshHz", refresh, display != null ? "XRDisplaySubsystem" : "Screen.currentResolution.refreshRateRatio");
            else Unknown("refreshHz", "Display refresh rate unavailable; no fallback72");
            int width = XRSettings.enabled ? XRSettings.eyeTextureWidth : Screen.width;
            int height = XRSettings.enabled ? XRSettings.eyeTextureHeight : Screen.height;
            if (width > 0 && height > 0) Known("eyeResolution", new Dictionary<string, object>
            { ["mode"] = XRSettings.enabled ? "stereo" : "mono", ["width"] = (long)width, ["height"] = (long)height },
                XRSettings.enabled ? "XRSettings.eyeTextureWidth/Height" : "Screen.width/height");
            else Unknown("eyeResolution", "Render target dimensions unavailable");
            bool monoWindows = Application.platform == RuntimePlatform.WindowsPlayer && !Application.isBatchMode && !XRSettings.enabled;
            string notApplicable = "not-applicable: rendered Windows client with XR disabled";
            if (monoWindows)
            { Unknown("ffr", notApplicable); Unknown("cpuLevel", notApplicable); Unknown("gpuLevel", notApplicable); }
            else
            {
                if (display != null) Known("ffr", (double)display.foveatedRenderingLevel, "XRDisplaySubsystem.foveatedRenderingLevel");
                else Unknown("ffr", "No running XRDisplaySubsystem");
                if (hasFrame && frame.HasOculus && DiagnosticJson.Finite(frame.Oculus.CpuLevel) && DiagnosticJson.Finite(frame.Oculus.GpuLevel) &&
                    frame.Oculus.CpuLevel >= 0 && frame.Oculus.GpuLevel >= 0)
                { Known("cpuLevel", (double)frame.Oculus.CpuLevel, "OculusPerfStats observation"); Known("gpuLevel", (double)frame.Oculus.GpuLevel, "OculusPerfStats observation"); }
                else { Unknown("cpuLevel", "Oculus levels unavailable"); Unknown("gpuLevel", "Oculus levels unavailable"); }
            }
            return values;
        }
    }
}
#endif
