using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>Результат явного экспорта; неполный каталог не выдается за успешный пакет.</summary>
    public readonly struct PerfDiagnosticExportResult
    {
        public readonly bool Succeeded;
        public readonly string ManifestPath;
        public readonly string Error;

        internal PerfDiagnosticExportResult(bool succeeded, string manifestPath, string error)
        {
            Succeeded = succeeded;
            ManifestPath = manifestPath;
            Error = error;
        }
    }

    /// <summary>
    /// Ограниченный буфер наблюдений без IO и создания строк на кадре. Это runtime source
    /// bundle, а не нормализованный GPU package v1: исходные GPU интервалы неизвестны.
    /// Создать до старта, подключить ObserveRuns, освободить scope в finally, затем FlushPackage.
    /// Все методы вызываются на одном главном потоке. После остановки буфер не переиспользуется.
    /// </summary>
    public sealed class PerfDiagnosticRecorder : IStressTestDiagnosticObserver
    {
        private readonly struct FrameRecord
        {
            internal readonly PerfRawFrame Value;
            internal readonly long CandidateGeneration;
            internal readonly bool PreviousUnityFrameInsidePhase;
            internal FrameRecord(in PerfRawFrame value, long generation, bool insidePhase)
            {
                Value = value;
                CandidateGeneration = generation;
                PreviousUnityFrameInsidePhase = insidePhase;
            }
        }

        private readonly struct PhaseRecord
        {
            internal readonly PerfPhaseObservation Value;
            internal readonly long Generation;
            internal PhaseRecord(in PerfPhaseObservation value, long generation)
            {
                Value = value;
                Generation = generation;
            }
        }

        [Serializable]
        private sealed class SourceManifest
        {
            public int schemaVersion = 1;
            public string kind = "vrb.perf-runtime-observations";
            public string runId;
            public string variant;
            public string mode = "live";
            public string clock = "process-stopwatch";
            public long tickFrequency;
            public string phasesPath = "raw/phases.csv";
            public string phasesSha256;
            public string framesPath = "raw/frames.csv";
            public string framesSha256;
            public int frameCount;
            public int phaseObservationCount;
            public long droppedFrames;
            public long droppedPhaseObservations;
            public long duplicateOrUnorderedFrames;
            public string stopReason;
            public bool complete;
            public bool phaseMappingVerified = false;
            public bool sourceIntervalsKnown = false;
            public bool normalizedPackage = false;
            public string appGpuSource = "Unity.XR.Oculus.Stats.PerfMetrics.AppGPUTime";
            public string compositorGpuSource = "Unity.XR.Oculus.Stats.PerfMetrics.CompositorGPUTime";
            public string cpuSource = "Unity.XR.Oculus.Stats.PerfMetrics.AppCPUTime";
            public string sourceFrame = "unknown";
            public string sourceLatency = "unknown";
            public string missingMetricReason = "API недоступен, значение не конечное/отрицательное либо время кадра равно нулю; нулевые счётчики допустимы";
            public string[] limitations =
            {
                "Исходный кадр/время OVR и граница задержки недоступны; чтение не подтверждает кадр N−1.",
                "Кандидат поколения фазы описывает локальное наблюдение, а не подтверждённую исходную фазу GPU.",
                "FrameTiming GPU запаздывает; время наблюдения не является исходным интервалом.",
                "Наблюдённые условия, обзор и provenance запуска/сборки должен предоставить адаптер владельца.",
                "Нормализованные samples.csv/window не создаются без доказательств исходных интервалов."
            };
        }

        private readonly FrameRecord[] _frames;
        private readonly PhaseRecord[] _phases;
        private readonly int _threadId;
        private readonly string _runId;
        private readonly string _variant;
        private readonly string _mode;
        private readonly long _frequency;
        private int _frameCount;
        private int _phaseCount;
        private int _lastObservationFrame = -1;
        private int _beginFrame;
        private long _beginTicks;
        private long _generation;
        private bool _phaseOpen;
        private bool _stopped;
        private bool _exportAttempted;

        public int FrameCount => _frameCount;
        public int PhaseObservationCount => _phaseCount;
        public long DroppedFrames { get; private set; }
        public long DroppedPhaseObservations { get; private set; }
        public long DuplicateOrUnorderedFrames { get; private set; }
        public bool IsStopped => _stopped;
        public string StopReason { get; private set; }

        public PerfDiagnosticRecorder(string runId, string variant, int frameCapacity = 65536, int phaseCapacity = 512, string mode = "live")
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Не задан runId.", nameof(runId));
            if (string.IsNullOrWhiteSpace(variant)) throw new ArgumentException("Не задан variant.", nameof(variant));
            if (mode != "live" && mode != "instrumented") throw new ArgumentException("Неизвестный режим наблюдений.", nameof(mode));
            if (frameCapacity < 1 || frameCapacity > 1000000) throw new ArgumentOutOfRangeException(nameof(frameCapacity));
            if (phaseCapacity < 2 || phaseCapacity > 65536) throw new ArgumentOutOfRangeException(nameof(phaseCapacity));
            _frames = new FrameRecord[frameCapacity];
            _phases = new PhaseRecord[phaseCapacity];
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _frequency = Stopwatch.Frequency;
            _runId = runId;
            _variant = variant;
            _mode = mode;
        }

        public void OnPhase(in PerfPhaseObservation value)
        {
            RequireThread();
            if (_stopped) return;
            if (value.TickFrequency != _frequency) throw new ArgumentException("Часы фазы не совпадают.", nameof(value));
            if (value.IsBegin)
            {
                if (_phaseOpen) throw new InvalidOperationException("Новая фаза без закрытия предыдущей.");
                _generation++;
                _phaseOpen = true;
                _beginFrame = value.UnityFrame;
                _beginTicks = value.MonotonicTicks;
            }
            else
            {
                if (!_phaseOpen) throw new InvalidOperationException("Закрытие фазы без начала.");
                _phaseOpen = false;
            }
            if (_phaseCount < _phases.Length) _phases[_phaseCount++] = new PhaseRecord(in value, _generation);
            else DroppedPhaseObservations++;
        }

        public void OnRawFrame(in PerfRawFrame value)
        {
            RequireThread();
            if (_stopped || !_phaseOpen) return;
            if (value.TickFrequency != _frequency) throw new ArgumentException("Часы выборки не совпадают.", nameof(value));
            if (value.ObservationUnityFrame <= _lastObservationFrame)
            {
                DuplicateOrUnorderedFrames++;
                return;
            }
            bool consecutive = _lastObservationFrame == value.ObservationUnityFrame - 1;
            _lastObservationFrame = value.ObservationUnityFrame;
            bool inside = consecutive && value.PreviousUnityFrame > _beginFrame && value.ObservationTicks >= _beginTicks;
            if (_frameCount < _frames.Length)
                _frames[_frameCount++] = new FrameRecord(in value, _generation, inside);
            else DroppedFrames++;
        }

        public void OnStopped(string reason)
        {
            RequireThread();
            if (_stopped) return;
            StopReason = string.IsNullOrEmpty(reason) ? "unspecified-stop" : reason;
            _stopped = true;
        }

        /// <summary>
        /// Только после остановки; новый пустой каталог, без overwrite/retry. Ошибка оставляет
        /// неполное evidence. Manifest записывается последним, raw файлы хешируются побайтно.
        /// </summary>
        public PerfDiagnosticExportResult FlushPackage(string directory)
        {
            RequireThread();
            if (!_stopped) return new PerfDiagnosticExportResult(false, "", "observer-still-active");
            if (_exportAttempted) return new PerfDiagnosticExportResult(false, "", "export-already-attempted");
            if (string.IsNullOrWhiteSpace(directory)) return new PerfDiagnosticExportResult(false, "", "missing-directory");
            _exportAttempted = true;
            try
            {
                string root = Path.GetFullPath(directory);
                if (System.IO.Directory.Exists(root) && System.IO.Directory.GetFileSystemEntries(root).Length != 0)
                    return new PerfDiagnosticExportResult(false, "", "directory-not-empty");
                string raw = Path.Combine(root, "raw");
                System.IO.Directory.CreateDirectory(raw);
                string framePath = Path.Combine(raw, "frames.csv");
                string phasePath = Path.Combine(raw, "phases.csv");
                WriteFrames(framePath);
                WritePhases(phasePath);
                var manifest = new SourceManifest
                {
                    runId = _runId,
                    variant = _variant,
                    mode = _mode,
                    tickFrequency = _frequency,
                    frameCount = _frameCount,
                    phaseObservationCount = _phaseCount,
                    droppedFrames = DroppedFrames,
                    droppedPhaseObservations = DroppedPhaseObservations,
                    duplicateOrUnorderedFrames = DuplicateOrUnorderedFrames,
                    stopReason = StopReason,
                    complete = DroppedFrames == 0 && DroppedPhaseObservations == 0 && DuplicateOrUnorderedFrames == 0
                               && !_phaseOpen && _phaseCount > 0 && _frameCount > 0 && StopReason == "run-completed",
                    framesSha256 = Hash(framePath),
                    phasesSha256 = Hash(phasePath)
                };
                string manifestPath = Path.Combine(root, "source.json");
                using (var writer = NewWriter(manifestPath)) writer.Write(JsonUtility.ToJson(manifest, true) + "\n");
                return new PerfDiagnosticExportResult(true, manifestPath, "");
            }
            catch (Exception e)
            {
                return new PerfDiagnosticExportResult(false, "", e.GetType().Name + ": " + e.Message);
            }
        }

        private void WriteFrames(string path)
        {
            using (var writer = NewWriter(path))
            {
                writer.WriteLine("observationFrame,previousUnityFrame,observationTicks,candidatePhaseGeneration,previousUnityFrameInsidePhase,ovrSourceFrame,sourceStart,sourceEnd,frameTimingReadSucceeded,oculusReadSucceeded,delta_ms,uxr_ms,unity_cpu_ms,unity_gpu_ms,app_cpu_ms,app_gpu_ms,compositor_gpu_ms,gpu_util_percent,cpu_util_percent,cpu_level,gpu_level");
                for (int i = 0; i < _frameCount; i++)
                {
                    FrameRecord record = _frames[i];
                    PerfRawFrame value = record.Value;
                    writer.Write(value.ObservationUnityFrame.ToString(CultureInfo.InvariantCulture));
                    Field(writer, value.PreviousUnityFrame);
                    Field(writer, value.ObservationTicks);
                    Field(writer, record.CandidateGeneration);
                    writer.Write(record.PreviousUnityFrameInsidePhase ? ",true,null,null,null" : ",false,null,null,null");
                    writer.Write(value.HasFrameTiming ? ",true" : ",false");
                    writer.Write(value.HasOculus ? ",true" : ",false");
                    Metric(writer, value.DeltaMs, true, true);
                    Metric(writer, value.UxrMs, true, false);
                    Metric(writer, value.UnityCpuFrameMs, value.HasFrameTiming, true);
                    Metric(writer, value.UnityGpuFrameMs, value.HasFrameTiming, true);
                    Metric(writer, value.Oculus.AppCpuMs, value.HasOculus, true);
                    Metric(writer, value.Oculus.AppGpuMs, value.HasOculus, true);
                    Metric(writer, value.Oculus.CompositorGpuMs, value.HasOculus, true);
                    Metric(writer, value.Oculus.GpuUtilPercent, value.HasOculus, false);
                    Metric(writer, value.Oculus.CpuUtilPercent, value.HasOculus, false);
                    Metric(writer, value.Oculus.CpuLevel, value.HasOculus, false);
                    Metric(writer, value.Oculus.GpuLevel, value.HasOculus, false);
                    writer.WriteLine();
                }
            }
        }

        private void WritePhases(string path)
        {
            using (var writer = NewWriter(path))
            {
                writer.WriteLine("phaseGeneration,transition,phaseId,phaseNumber,measured,unityFrame,monotonicTicks,secondsLeft,puppetCount,clutterCount,skins");
                for (int i = 0; i < _phaseCount; i++)
                {
                    PhaseRecord record = _phases[i];
                    PerfPhaseObservation value = record.Value;
                    writer.Write(record.Generation.ToString(CultureInfo.InvariantCulture));
                    writer.Write(value.IsBegin ? ",begin," : ",end,");
                    Csv(writer, value.PhaseId);
                    Field(writer, value.PhaseNumber);
                    writer.Write(value.IsMeasured ? ",true" : ",false");
                    Field(writer, value.UnityFrame);
                    Field(writer, value.MonotonicTicks);
                    Metric(writer, value.SecondsLeft, true, false);
                    Field(writer, value.PuppetCount);
                    Field(writer, value.ClutterCount);
                    writer.Write(',');
                    Csv(writer, value.Skins);
                    writer.WriteLine();
                }
            }
        }

        private static StreamWriter NewWriter(string path) => new StreamWriter(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { NewLine = "\n" };

        private static void Field(TextWriter writer, long value)
        {
            writer.Write(',');
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Metric(TextWriter writer, double value, bool available, bool positive)
        {
            writer.Write(',');
            writer.Write(available && !double.IsNaN(value) && !double.IsInfinity(value) && (positive ? value > 0 : value >= 0)
                ? value.ToString("R", CultureInfo.InvariantCulture) : "null");
        }

        private static void Csv(TextWriter writer, string value)
        {
            writer.Write('"');
            writer.Write((value ?? "").Replace("\"", "\"\""));
            writer.Write('"');
        }

        private static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private void RequireThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new InvalidOperationException("Диагностика вызывается с другого потока.");
        }
    }
}
