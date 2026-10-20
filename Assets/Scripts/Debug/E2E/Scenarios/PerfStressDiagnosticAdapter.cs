#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>Read-only мост workload → существующий provider; не переносит камеру и не управляет фазами.</summary>
    internal sealed class PerfStressDiagnosticAdapter : IStressTestDiagnosticObserver, IDisposable
    {
        private readonly E2EContext _context;
        private readonly E2EDiagnosticSession _provider;
        private readonly PerfDiagnosticRecorder _recorder;
        private readonly IDisposable _subscription;
        private readonly string _directory;
        private PerfDiagnosticConditions _conditions;
        private PerfSceneDetailRunControl _control;
        private PerfRenderDiagnosticControl _renderDiag;
        private object[] _identityTables, _identityReferences;
        private PerfPhaseObservation _phase;
        private PerfRawFrame _frame;
        private bool _hasFrame, _phaseOpen, _disposed, _attemptedReady, _stopped;
        private int _readyCount;
        private long _generation;
        private string _readyId, _failure;
        private MapRunKey _mapKey;
        private Vector3 _readyPosition;
        private Quaternion _readyRotation;
        private float _readyFfr, _readyCpuLevel, _readyGpuLevel;
        private bool _hasReadyLevels;

        // Это допуски стабильности вида, не бюджет GPU. Фактические отклонения сохраняются.
        private const float PositionTolerance = .05f;
        private const float RotationTolerance = 1f;

        internal PerfStressDiagnosticAdapter(E2EContext context)
        {
            _context = context; _provider = context.Diagnostics;
            _directory = Path.Combine(_provider.ArtifactDirectory, "perf-runtime");
            _recorder = new PerfDiagnosticRecorder(_provider.RunId, context.DiagnosticInput.Variant, mode: context.DiagnosticInput.Mode);
            _subscription = StressTestClientSession.ObserveRuns(this);
        }

        internal void Prepare()
        {
            if (_context.Args.TryGetValue(PerfSceneDetailRunControl.ArgKey, out string variant))
                _control = new PerfSceneDetailRunControl(variant);
            if (_context.Args.TryGetValue(PerfRenderDiagnosticControl.ArgKey, out string renderVariant))
                _renderDiag = new PerfRenderDiagnosticControl(renderVariant);
            _conditions = new PerfDiagnosticConditions();
            _identityTables = PerfSceneIdentityTable.CaptureProvenance();
            _identityReferences = PerfSceneIdentityTable.CaptureReferences();
        }

        public void OnRawFrame(in PerfRawFrame value)
        {
            _recorder.OnRawFrame(in value);
            _frame = value; _hasFrame = true;
            if (_readyId != null && _hasReadyLevels && (!value.HasOculus ||
                value.Oculus.CpuLevel != _readyCpuLevel || value.Oculus.GpuLevel != _readyGpuLevel))
                Invalidate("hardware-levels-changed");
        }

        public void OnPhase(in PerfPhaseObservation value)
        {
            _recorder.OnPhase(in value);
            if (value.IsBegin)
            {
                _generation++;
                _phase = value; _phaseOpen = true; _attemptedReady = false;
            }
            else Invalidate("phase-ended");
            Report(_provider.ReportPhase(new PhaseObservation(value.PhaseId, _generation,
                value.IsBegin ? DiagnosticPhaseTransition.Begin : DiagnosticPhaseTransition.End,
                _provider.Role, "StressTestClientSession client-observation", value.IsMeasured,
                value.IsBegin ? (double?)value.SecondsLeft : 0d, _provider.CaptureStamp())));
            if (!value.IsBegin) _phaseOpen = false;
        }

        public void OnStopped(string reason)
        {
            _stopped = true;
            Invalidate(reason);
            _recorder.OnStopped(reason);
        }

        internal static bool MapReady(E2EContext context, out MapRunKey key, out string reason)
        {
            key = default;
            MapRunAuthority authority = MapRunAuthority.Instance;
            MapRunSnapshot run = authority != null ? authority.Current : default;
            string expected = context.DiagnosticInput.RequestedSceneId;
            Scene scene = SceneManager.GetSceneByPath(expected);
            MapBootstrap bootstrap = scene.IsValid() && scene.isLoaded ? MapBootstrap.ForScene(scene) : null;
            if (run.Config == null || !run.IsReady || (run.Config.MapScene != context.Map && run.Config.MapScene != expected) ||
                bootstrap == null || !bootstrap.IsLocallyReady(run.Key))
            { reason = "canonical map/MapRun/local bootstrap not ready: " + expected; return false; }
            key = run.Key; reason = null; return true;
        }

        internal void Step(E2EViewPoint target, bool settled)
        {
            if (_stopped || !_phaseOpen || !_phase.IsMeasured || _conditions == null) return;
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            Camera camera = avatar != null ? avatar.CameraComponent : null;
            if (_readyId != null)
            {
                if (!Mirror.NetworkClient.isConnected) Invalidate("disconnected");
                else if (!Application.isFocused) Invalidate("focus-lost");
                else if (!MapReady(_context, out MapRunKey current, out _) || current != _mapKey) Invalidate("map-generation-changed");
                else if (!_conditions.SettingsUnchanged() || !_conditions.TextureSettingsUnchanged()) Invalidate("quality-changed");
                else if (camera == null || Vector3.Distance(camera.transform.position, _readyPosition) > PositionTolerance ||
                         Quaternion.Angle(camera.transform.rotation, _readyRotation) > RotationTolerance) Invalidate("view-moved");
                else if (_conditions.Display != null && _conditions.Display.foveatedRenderingLevel != _readyFfr) Invalidate("ffr-changed");
                return;
            }
            if (_attemptedReady || target == null || !settled || camera == null || !_hasFrame) return;
            _attemptedReady = true;
            if (!MapReady(_context, out _mapKey, out string reason)) { RecordRefusal(reason); return; }
            if (!_conditions.SettingsUnchanged() || !_conditions.TextureSettingsUnchanged()) { RecordRefusal("quality changed after prewarm snapshot"); return; }
            Transform pose = camera.transform;
            Vector3 floor = avatar.CameraFloorPosition;
            float dx = floor.x - target.Floor.x, dz = floor.z - target.Floor.z;
            float deviationXZ = Mathf.Sqrt(dx * dx + dz * dz);
            float deviationYaw = Mathf.Abs(Mathf.DeltaAngle(target.Yaw, pose.eulerAngles.y));
            bool viewOk = deviationXZ <= PositionTolerance && deviationYaw <= RotationTolerance &&
                (!target.HasFloorY || Mathf.Abs(floor.y - target.Floor.y) <= PositionTolerance);
            DiagnosticReportResult view = _provider.ReportView(new ViewObservation(target.Name, _phase.PhaseId, _generation,
                viewOk, pose.position, pose.rotation, deviationXZ, deviationYaw,
                viewOk ? "" : "fresh measured-phase camera outside view stability tolerance",
                "UxrAvatar.LocalAvatar.CameraComponent read-only; E2EViews settled", _provider.CaptureStamp()));
            if (!Report(view) || !viewOk) return;
            Dictionary<string, DiagnosticConditionValue> values = _conditions.Capture(_context.DiagnosticInput.RequestedSceneId,
                _provider.Role, _phase.PhaseId, target.Name, _context.DiagnosticInput.WorkloadId, in _frame, _hasFrame);
            values.Add("mapRunKey", new DiagnosticConditionValue(_mapKey.ToString(), "MapRunAuthority.Current.Key", ""));
            values.Add("puppetCount", new DiagnosticConditionValue((long)_phase.PuppetCount, "StressTestClientSession", ""));
            values.Add("clutterCount", new DiagnosticConditionValue((long)_phase.ClutterCount, "StressTestClientSession", ""));
            // Вариант рендера не входит в quality fingerprint: сравнение A/B видит его как явное различие условий.
            // У известного значения reason пустой (контракт E2EDiagnosticSession); сводка подмены — в runtime.json.
            values.Add("renderDiagnostic", new DiagnosticConditionValue(_renderDiag?.Variant ?? "none",
                "PerfRenderDiagnosticControl", ""));
            DiagnosticReportResult conditions = _provider.ReportConditions(new ConditionsObservation(_context.DiagnosticInput.RequestedSceneId,
                _phase.PhaseId, _generation, _conditions.Fingerprint ?? "", values, _provider.CaptureStamp()));
            if (!Report(conditions)) return;
            if (_context.DiagnosticInput.DiscoveryOnly) return;
            StressTestClientSession session = StressTestClientSession.Current;
            double remaining = session != null ? session.PhaseSecondsLeft : 0;
            if (remaining <= 0 || _conditions.Fingerprint == null) { RecordRefusal("unknown quality fingerprint or no remaining phase window"); return; }
            DiagnosticObservationStamp stamp = _provider.CaptureStamp();
            double observed = (double)stamp.MonotonicTicks / stamp.TickFrequency;
            string readyId = "perf-" + _generation.ToString(CultureInfo.InvariantCulture);
            DiagnosticReportResult ready = _provider.ReportWorkloadReady(new WorkloadReadyObservation(readyId,
                _phase.PhaseId, _generation, _conditions.Fingerprint, conditions.EventId, view.EventId, remaining,
                new DiagnosticTimeBounds(stamp.ClockId, observed, observed, null, false,
                    "local observation only; GPU source interval/native mapping unknown"), stamp));
            if (!Report(ready)) return;
            _readyCount++;
            _readyId = readyId; _readyPosition = pose.position; _readyRotation = pose.rotation;
            _readyFfr = _conditions.Display != null ? _conditions.Display.foveatedRenderingLevel : 0;
            _hasReadyLevels = _frame.HasOculus;
            _readyCpuLevel = _frame.Oculus.CpuLevel; _readyGpuLevel = _frame.Oculus.GpuLevel;
        }

        private bool Report(DiagnosticReportResult receipt)
        {
            if (receipt.Accepted) return true;
            RecordRefusal(receipt.Reason); return false;
        }
        private void RecordRefusal(string reason)
        {
            if (_failure == null) _failure = reason;
            GameLog.Perf.Warning("[PerfDiagnostic] " + reason);
        }
        private void Invalidate(string reason)
        {
            if (_readyId == null) return;
            string id = _readyId; _readyId = null;
            Report(_provider.InvalidateWorkload(id, reason));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription.Dispose();
            if (!_stopped) OnStopped("scenario-disposed");
            _renderDiag?.Dispose();
            _control?.Dispose();
            PerfDiagnosticExportResult exported = _recorder.FlushPackage(_directory);
            if (exported.Succeeded)
            {
                var provenance = new { schemaVersion = 1, runId = _provider.RunId, role = _provider.Role,
                    processInstanceId = _provider.ProcessInstanceId, clockId = _provider.ClockId,
                    requestedMode = _context.DiagnosticInput.Mode,
                    discoveryOnly = _context.DiagnosticInput.DiscoveryOnly,
                    sourceSha = _context.DiagnosticInput.SourceSha, specSha256 = _context.DiagnosticInput.SpecHash,
                    buildManifestSha256 = _context.DiagnosticInput.BuildHash, phaseGenerations = _generation,
                    readyCount = _readyCount,
                    sceneDetailControl = _control?.Variant, sceneDetailRestore = _control?.RestoreStatus,
                    renderDiagnostic = _renderDiag?.Variant ?? "none", renderDiagnosticSummary = _renderDiag?.Summary,
                    renderDiagnosticRestore = _renderDiag?.RestoreStatus,
                    firstRefusal = _failure, positionToleranceMetres = PositionTolerance, rotationToleranceDegrees = RotationTolerance,
                    qualityFingerprintMethod = PerfDiagnosticConditions.FingerprintMethod,
                    qualityFingerprint = _conditions?.Fingerprint, quality = _conditions?.Quality,
                    qualityCanonicalPath = _conditions != null ? "quality-canonical.json" : null,
                    qualityUnavailableReason = _conditions?.UnavailableReason,
                    sceneIdentitiesPath = _identityTables != null && _identityTables.Length > 0 ? "scene-identities.json" : null,
                    sourceIntervalsKnown = false, gpuPhaseMappingVerified = false };
                if (_conditions != null)
                    File.WriteAllText(Path.Combine(_directory, "quality-canonical.json"), _conditions.Quality.ToString(Formatting.None),
                        new System.Text.UTF8Encoding(false));
                if (_identityTables != null && _identityTables.Length > 0)
                    File.WriteAllText(Path.Combine(_directory, "scene-identities.json"), JsonConvert.SerializeObject(
                        new { schemaVersion = 1, tables = _identityTables, references = _identityReferences,
                            limitations = "Serialized Unity refs only; dynamic objects/GPU batching/capture resource IDs require separate joins" },
                        Formatting.Indented).Replace("\r\n", "\n") + "\n", new System.Text.UTF8Encoding(false));
                File.WriteAllText(Path.Combine(_directory, "runtime.json"), JsonConvert.SerializeObject(provenance, Formatting.Indented)
                    .Replace("\r\n", "\n") + "\n", new System.Text.UTF8Encoding(false));
            }
            if (!exported.Succeeded)
                GameLog.Error("[PerfDiagnostic] Runtime export failed: " + exported.Error);
        }
    }
}
#endif
