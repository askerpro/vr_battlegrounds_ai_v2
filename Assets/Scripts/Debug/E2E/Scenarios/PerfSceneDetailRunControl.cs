#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps.Detail;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>Только объявленная автором LOD-пара; применяет существующий Live/Apply до разгона.</summary>
    internal sealed class PerfSceneDetailRunControl : IDisposable
    {
        internal const string ArgKey = "scene_detail";
        internal readonly string Variant;
        internal string RestoreStatus { get; private set; } = "pending";
        private readonly SceneDetailApplier _applier;
        private readonly SceneDetailProfile _live;
        private readonly float _midStart, _farStart;
        private readonly SceneDetailProfile.PlaneRule _mid, _far;
        private bool _disposed;

        // Диагностические аргументы (LOD-пара и render_diag) не входят в аргументы стресс-теста.
        internal static IReadOnlyDictionary<string, string> ScenarioArgs(E2EContext context)
        {
            if (!context.Args.ContainsKey(ArgKey) && !context.Args.ContainsKey(PerfRenderDiagnosticControl.ArgKey))
                return context.Args;
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in context.Args)
                if (pair.Key != ArgKey && pair.Key != PerfRenderDiagnosticControl.ArgKey) args.Add(pair.Key, pair.Value);
            return args;
        }

        internal static string Validate(E2EContext context)
        {
            if (!context.Args.TryGetValue(ArgKey, out string value)) return null;
            if (context.Diagnostics == null) return "scene_detail требует diagnostic RunSpec";
            return value == "lod-a" || value == "lod-b" ? null : "scene_detail: допустимы lod-a и lod-b";
        }

        internal PerfSceneDetailRunControl(string variant)
        {
            if (variant != "lod-a" && variant != "lod-b") throw new ArgumentException("Unknown LOD control", nameof(variant));
            Variant = variant;
            _applier = SceneDetailApplier.Current;
            _live = _applier != null ? _applier.Live : null;
            if (_live == null) throw new InvalidOperationException("LOD A/B: нет активного SceneDetailApplier.Live");
            _midStart = _live.midStart; _farStart = _live.farStart; _mid = _live.mid; _far = _live.far;
            try
            {
                _live.midStart = 10f; _live.farStart = variant == "lod-a" ? 25f : 20f;
                _live.mid = new SceneDetailProfile.PlaneRule();
                _live.far = new SceneDetailProfile.PlaneRule { lodSkip = SceneDetailProfile.WorstLod, textureBias = 0, cullSize = 0f };
                _applier.Apply();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_applier == null || SceneDetailApplier.Current != _applier || _applier.Live != _live)
            { RestoreStatus = "skipped: original applier unloaded/replaced; its lifecycle owns restore"; return; }
            try
            {
                _live.midStart = _midStart; _live.farStart = _farStart; _live.mid = _mid; _live.far = _far;
                _applier.Apply();
                RestoreStatus = "restored through original SceneDetailApplier.Apply";
            }
            catch (Exception error)
            {
                RestoreStatus = "failed: " + error.GetType().Name + ": " + error.Message;
                GameLog.Error("[PerfDiagnostic] LOD control restore: " + RestoreStatus);
            }
        }
    }
}
#endif
