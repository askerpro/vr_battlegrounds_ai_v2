using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;
using static System.FormattableString;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Итог одной фазы стресс-теста.
    /// </summary>
    [Serializable]
    public sealed class PerfPhaseReport
    {
        public string name;
        public int    frames;
        public float  seconds;
        public float  budgetMs;
        public float  overBudgetShare;
        public int    spikes;
        public List<PerfMetricReport> metrics = new List<PerfMetricReport>();

        public PerfSummary Get(string key)
        {
            foreach (PerfMetricReport m in metrics)
            {
                if (m.key == key) return m.summary;
            }
            return default;
        }
    }

    [Serializable]
    public sealed class PerfMetricReport
    {
        public string      key;
        public PerfSummary summary;
    }

    /// <summary>
    /// Пишет лог производительности <b>по изменениям</b>: метрики усредняются окнами
    /// по <see cref="WindowSeconds"/>, и строка уходит в лог, только когда окно значимо
    /// отличается от последней записанной строки (<see cref="PerfChangeFilter"/>).
    /// Отдельно, сразу и с разбивкой по подсистемам, пишутся всплески — одиночные
    /// кадры, которые окно размазало бы. Перцентили фазы считаются по всем кадрам
    /// в памяти, в файл сами кадры не идут.
    ///
    /// <para>
    /// <b>Выравнивание по кадрам.</b> <see cref="Tick"/> зовётся в начале кадра N и
    /// описывает кадр N−1: <c>unscaledDeltaTime</c> — его длительность,
    /// <c>ProfilerRecorder.LastValue</c> — его счётчики, время UltimateXR и события
    /// накоплены за него же. Только GPU из <c>FrameTimingManager</c> отстаёт ещё на
    /// несколько кадров — у всплеска его значение не про тот кадр.
    /// </para>
    ///
    /// <para>
    /// Разбивка по подсистемам (маркеры PlayerLoop) доступна только в Development-сборке:
    /// в release Unity маркеры не отдаёт, и в заголовке лога это видно списком
    /// недоступных метрик.
    /// </para>
    /// </summary>
    public sealed class PerfFrameRecorder : IDisposable
    {
        public const float WindowSeconds = 0.5f;

        private const float SpikeBudgetFactor = 1.5f;
        private const float SpikeLevelFactor  = 2f;

        // ── Метрики ─────────────────────────────────────────────────────────

        private enum Source { DeltaTime, CpuFrame, CpuMain, CpuRender, Gpu, Uxr, Profiler }

        private sealed class Metric
        {
            public string          Key;
            public Source          Source;
            public string          StatName;
            public double          Scale;
            public ChangeThreshold Threshold;
            public bool            IsSubsystemTime;   // идёт в разбивку всплеска
            public ProfilerRecorder Recorder;

            public float       FrameValue;
            public double      WindowSum;
            public List<float> PhaseSamples = new List<float>(4096);
        }

        private static readonly ChangeThreshold TimeThreshold  = new ChangeThreshold(1.0f, 0.15f);
        private static readonly ChangeThreshold SmallTime      = new ChangeThreshold(0.5f, 0.25f);
        private static readonly ChangeThreshold CountThreshold = new ChangeThreshold(20f, 0.10f);
        private static readonly ChangeThreshold MemThreshold   = new ChangeThreshold(8f, 0.05f);
        private static readonly ChangeThreshold AllocThreshold = new ChangeThreshold(4f, 0.50f);

        private const double NsToMs  = 1e-6;
        private const double BytesKb = 1.0 / 1024.0;
        private const double BytesMb = 1.0 / (1024.0 * 1024.0);

        /// <summary>
        /// Встроенные маркеры и счётчики Unity. Имя, которого нет в этой сборке
        /// (маркеры в release, переименование в новой Unity), просто выпадает —
        /// список недоступных пишется в заголовок лога.
        /// </summary>
        private static List<Metric> CreateMetrics()
        {
            return new List<Metric>
            {
                // Кадр целиком
                new Metric { Key = "dt",         Source = Source.DeltaTime, Threshold = TimeThreshold },
                new Metric { Key = "cpu",        Source = Source.CpuFrame,  Threshold = TimeThreshold },
                new Metric { Key = "cpu_main",   Source = Source.CpuMain,   Threshold = TimeThreshold },
                new Metric { Key = "cpu_render", Source = Source.CpuRender, Threshold = TimeThreshold },
                new Metric { Key = "gpu",        Source = Source.Gpu,       Threshold = TimeThreshold },

                // Подсистемы (только Development-сборка, кроме uxr — он меряется секундомером)
                new Metric { Key = "uxr",       Source = Source.Uxr, Threshold = SmallTime, IsSubsystemTime = true },
                Marker("physics",     "FixedUpdate.PhysicsFixedUpdate"),
                Marker("scripts",     "Update.ScriptRunBehaviourUpdate"),
                Marker("scripts_late","PreLateUpdate.ScriptRunBehaviourLateUpdate"),
                Marker("anim",        "PreLateUpdate.DirectorUpdateAnimationBegin"),
                Marker("anim_end",    "PreLateUpdate.DirectorUpdateAnimationEnd"),
                Marker("skinning",    "PostLateUpdate.UpdateAllSkinnedMeshes"),
                Marker("particles",   "PreLateUpdate.ParticleSystemBeginUpdateAll"),
                Marker("render",      "PostLateUpdate.FinishFrameRendering"),
                Marker("gc_collect",  "GC.Collect"),
                Marker("present_wait","Gfx.WaitForPresentOnGfxThread"),

                // Рендер и память
                Counter("batches",   "Batches Count",       1.0,     CountThreshold),
                Counter("draws",     "Draw Calls Count",    1.0,     CountThreshold),
                Counter("setpass",   "SetPass Calls Count", 1.0,     CountThreshold),
                Counter("tris_k",    "Triangles Count",     0.001,   new ChangeThreshold(20f, 0.10f)),
                Counter("gc_alloc_kb","GC Allocated In Frame", BytesKb, AllocThreshold),
                Counter("gc_mb",     "GC Used Memory",      BytesMb, MemThreshold),
                Counter("mem_mb",    "System Used Memory",  BytesMb, MemThreshold),
            };
        }

        private static Metric Marker(string key, string stat) => new Metric
        {
            Key = key, Source = Source.Profiler, StatName = stat, Scale = NsToMs,
            Threshold = SmallTime, IsSubsystemTime = true,
        };

        private static Metric Counter(string key, string stat, double scale, ChangeThreshold threshold) => new Metric
        {
            Key = key, Source = Source.Profiler, StatName = stat, Scale = scale, Threshold = threshold,
        };

        // ── Состояние ───────────────────────────────────────────────────────

        private readonly List<Metric> _metrics;
        private readonly List<string> _unavailable = new List<string>();
        private readonly PerfChangeFilter _filter;
        private readonly float[] _windowValues;
        private readonly bool[]  _changed;
        private readonly int[]   _frameEvents  = new int[PerfEvents.KindCount];
        private readonly int[]   _windowEvents = new int[PerfEvents.KindCount];
        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly Stopwatch _uxrWatch = new Stopwatch();
        private readonly StreamWriter _writer;
        private readonly float _budgetMs;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private double _uxrAccumMs;
        private string _phase = "-";
        private bool   _phaseMeasured;
        private float  _phaseStart;
        private int    _phaseFrames;
        private int    _phaseSpikes;
        private float  _windowStart;
        private int    _windowFrames;
        private float  _levelMs;
        private int    _consecutiveSpikes;
        private const int MaxConsecutiveSpikes = 3;
        private bool   _disposed;

        public string Directory { get; }
        public string LogPath   { get; }
        public float  BudgetMs  => _budgetMs;

        /// <summary>Текущий уровень кадра (среднее последнего окна), мс. Для оверлея.</summary>
        public float LevelMs => _levelMs;

        public PerfFrameRecorder(string directory, float budgetMs, string header)
        {
            Directory = directory;
            _budgetMs = budgetMs;
            System.IO.Directory.CreateDirectory(directory);

            _metrics = CreateMetrics();
            StartProfilerRecorders();

            var thresholds = new ChangeThreshold[_metrics.Count];
            for (int i = 0; i < _metrics.Count; i++) thresholds[i] = _metrics[i].Threshold;
            _filter       = new PerfChangeFilter(thresholds);
            _windowValues = new float[_metrics.Count];
            _changed      = new bool[_metrics.Count];

            LogPath = Path.Combine(directory, "perf.log");
            _writer = new StreamWriter(LogPath, false, new UTF8Encoding(false));

            WriteRaw(header);
            WriteRaw(Invariant($"Бюджет кадра: {budgetMs:F2} мс. Окно: {WindowSeconds:F1} с. Всплеск: кадр > бюджет×{SpikeBudgetFactor} и > уровень×{SpikeLevelFactor}; в разгоне, пока уровень не известен, всплески не пишутся."));
            WriteRaw(_unavailable.Count == 0
                ? "Все метрики доступны."
                : "Недоступны в этой сборке: " + string.Join(", ", _unavailable) +
                  (UnityEngine.Debug.isDebugBuild ? "" : " (release — маркеры подсистем только в Development)"));
            WriteRaw("Формат строки: #кадр время [фаза] метрика значение (дельта от прошлой строки). " +
                     "Показаны кадр целиком и изменившиеся метрики; нет строки — ничего значимо не менялось.");
            WriteRaw("");

            FrameTimingManager.CaptureFrameTimings();
            UxrManager.StageUpdating += OnUxrStageUpdating;
            UxrManager.StageUpdated  += OnUxrStageUpdated;
            PerfEvents.Reset();
        }

        private void StartProfilerRecorders()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            var byName = new Dictionary<string, ProfilerRecorderDescription>();
            foreach (ProfilerRecorderHandle handle in handles)
            {
                ProfilerRecorderDescription desc = ProfilerRecorderHandle.GetDescription(handle);
                if (!byName.ContainsKey(desc.Name)) byName.Add(desc.Name, desc);
            }

            for (int i = _metrics.Count - 1; i >= 0; i--)
            {
                Metric m = _metrics[i];
                if (m.Source != Source.Profiler) continue;

                if (byName.TryGetValue(m.StatName, out ProfilerRecorderDescription desc))
                {
                    m.Recorder = ProfilerRecorder.StartNew(desc.Category, desc.Name, 1);
                    if (m.Recorder.Valid) continue;
                    m.Recorder.Dispose();
                }

                _unavailable.Add(m.Key);
                _metrics.RemoveAt(i);
            }
        }

        // ── Фазы ────────────────────────────────────────────────────────────

        /// <summary>
        /// Начинает фазу. Неизмеряемая фаза (<paramref name="measured"/> = false) — это
        /// успокоение после спавна: её строки и всплески пишутся в лог, но в сводку
        /// не идут, иначе рывок самого спавна исказил бы p99 фазы.
        /// </summary>
        public void BeginPhase(string name, bool measured)
        {
            FlushWindow(force: false);

            _phase         = name;
            _phaseMeasured = measured;
            _phaseStart    = Time.realtimeSinceStartup;
            _phaseFrames   = 0;
            _phaseSpikes   = 0;
            foreach (Metric m in _metrics) m.PhaseSamples.Clear();

            _filter.Reset();
            WriteLine($"=== фаза «{name}»{(measured ? "" : " (успокоение, в сводку не идёт)")}");
        }

        /// <summary>Закрывает фазу и возвращает её сводку (null для неизмеряемой).</summary>
        public PerfPhaseReport EndPhase()
        {
            FlushWindow(force: true);

            if (!_phaseMeasured) return null;

            var report = new PerfPhaseReport
            {
                name     = _phase,
                frames   = _phaseFrames,
                seconds  = Time.realtimeSinceStartup - _phaseStart,
                budgetMs = _budgetMs,
                spikes   = _phaseSpikes,
            };

            foreach (Metric m in _metrics)
            {
                report.metrics.Add(new PerfMetricReport { key = m.Key, summary = PerfStats.Summarize(m.PhaseSamples) });
                if (m.Source == Source.DeltaTime)
                    report.overBudgetShare = PerfStats.OverBudgetShare(m.PhaseSamples, _budgetMs);
            }

            WriteLine(Invariant($"=== итог фазы «{_phase}»: {report.frames} кадров за {report.seconds:F0} с, вне бюджета {report.overBudgetShare:P1}, всплесков {report.spikes}"));
            foreach (PerfMetricReport m in report.metrics)
            {
                WriteRaw($"    {m.key,-12} {m.summary}");
            }

            return report;
        }

        // ── Кадр ────────────────────────────────────────────────────────────

        /// <summary>Звать в начале каждого кадра — описывает предыдущий кадр.</summary>
        public void Tick()
        {
            if (_disposed) return;

            // Первый кадр после старта: предыдущего кадра под наблюдением не было.
            if (Time.frameCount <= 1) return;

            FrameTimingManager.CaptureFrameTimings();
            bool hasTiming = FrameTimingManager.GetLatestTimings(1, _timings) > 0;

            PerfEvents.DrainCounters(_frameEvents);
            float uxrMs = (float)_uxrAccumMs;
            _uxrAccumMs = 0;

            foreach (Metric m in _metrics)
            {
                m.FrameValue = Read(m, hasTiming, uxrMs);
            }

            int frame = Time.frameCount - 1;
            float frameMs = _metrics[0].FrameValue;

            if (PerfStats.IsSpike(frameMs, _levelMs, _budgetMs, SpikeBudgetFactor, SpikeLevelFactor))
            {
                // Всплески подряд — это не всплески, а новый уровень, который окно ещё
                // не успело записать (спавн нагрузки). Пишем первые несколько, дальше —
                // одну строку, пока окно не обновит уровень: иначе на 72 Гц до 36 строк.
                _consecutiveSpikes++;
                if (_consecutiveSpikes <= MaxConsecutiveSpikes)
                {
                    WriteSpike(frame, frameMs);
                    if (_phaseMeasured) _phaseSpikes++;
                }
                else if (_consecutiveSpikes == MaxConsecutiveSpikes + 1)
                {
                    WriteLine(FormattableString.Invariant(
                        $"#{frame} {Time.realtimeSinceStartup:F2}s [{_phase}] … кадры дольше уровня идут подряд — это смена уровня, её запишет следующая строка окна"));
                }
            }
            else
            {
                _consecutiveSpikes = 0;
            }

            if (_phaseMeasured)
            {
                _phaseFrames++;
                foreach (Metric m in _metrics) m.PhaseSamples.Add(m.FrameValue);
            }

            if (_windowFrames == 0) _windowStart = Time.realtimeSinceStartup;
            _windowFrames++;
            foreach (Metric m in _metrics) m.WindowSum += m.FrameValue;
            for (int i = 0; i < _frameEvents.Length; i++) _windowEvents[i] += _frameEvents[i];

            while (PerfEvents.TryDequeueNote(out string note))
            {
                WriteLine(Invariant($"#{frame} {Time.realtimeSinceStartup:F2}s [{_phase}] • {note}"));
            }

            if (Time.realtimeSinceStartup - _windowStart >= WindowSeconds)
            {
                FlushWindow(force: false);
            }
        }

        private float Read(Metric m, bool hasTiming, float uxrMs)
        {
            switch (m.Source)
            {
                case Source.DeltaTime: return Time.unscaledDeltaTime * 1000f;
                case Source.CpuFrame:  return hasTiming ? (float)_timings[0].cpuFrameTime : 0f;
                case Source.CpuMain:   return hasTiming ? (float)_timings[0].cpuMainThreadFrameTime : 0f;
                case Source.CpuRender: return hasTiming ? (float)_timings[0].cpuRenderThreadFrameTime : 0f;
                case Source.Gpu:       return hasTiming ? (float)_timings[0].gpuFrameTime : 0f;
                case Source.Uxr:       return uxrMs;
                default:               return (float)(m.Recorder.LastValue * m.Scale);
            }
        }

        /// <summary>
        /// Закрывает окно: считает средние и пишет строку, если что-то значимо изменилось
        /// или в окне были дискретные события (спавн, уничтожение, взрыв).
        /// </summary>
        private void FlushWindow(bool force)
        {
            if (_windowFrames == 0) return;

            for (int i = 0; i < _metrics.Count; i++)
            {
                _windowValues[i] = (float)(_metrics[i].WindowSum / _windowFrames);
                _metrics[i].WindowSum = 0;
            }

            _levelMs = _windowValues[0];
            _consecutiveSpikes = 0;

            bool discrete = _windowEvents[(int)PerfEventKind.Spawn] > 0
                         || _windowEvents[(int)PerfEventKind.Despawn] > 0
                         || _windowEvents[(int)PerfEventKind.Explosion] > 0;

            bool changed = _filter.Evaluate(_windowValues, _changed);
            if (changed || discrete || (force && !_filter.HasWritten))
            {
                WriteWindow();
                _filter.Commit(_windowValues);
            }

            _windowFrames = 0;
            Array.Clear(_windowEvents, 0, _windowEvents.Length);
        }

        private void WriteWindow()
        {
            bool first = !_filter.HasWritten;
            float[] last = _filter.LastWritten;

            _sb.Clear();
            _sb.Append('#').Append(Time.frameCount - 1).Append(' ')
               .Append(Time.realtimeSinceStartup.ToString("F2", CultureInfo.InvariantCulture)).Append("s [")
               .Append(_phase).Append(']');

            for (int i = 0; i < _metrics.Count; i++)
            {
                // Кадр целиком (первые пять) — всегда, остальное — только изменившееся.
                bool core = _metrics[i].Source != Source.Profiler && _metrics[i].Source != Source.Uxr;
                if (!first && !core && !_changed[i]) continue;

                _sb.Append(' ').Append(_metrics[i].Key).Append(' ').Append(Format(_windowValues[i]));
                if (!first && _changed[i])
                {
                    float delta = _windowValues[i] - last[i];
                    _sb.Append(" (").Append(delta >= 0 ? "+" : "").Append(Format(delta)).Append(')');
                }
            }

            AppendEvents(_windowEvents);
            WriteLine(_sb.ToString());
        }

        private void WriteSpike(int frame, float frameMs)
        {
            _sb.Clear();
            _sb.Append("!ВСПЛЕСК #").Append(frame).Append(' ')
               .Append(Time.realtimeSinceStartup.ToString("F2", CultureInfo.InvariantCulture)).Append("s [")
               .Append(_phase).Append("] dt ").Append(Format(frameMs))
               .Append(" (уровень ").Append(Format(_levelMs)).Append(')');

            // Кадр целиком — как есть.
            foreach (Metric m in _metrics)
            {
                if (m.Source == Source.CpuMain || m.Source == Source.CpuRender || m.Source == Source.Gpu)
                    _sb.Append(' ').Append(m.Key).Append(' ').Append(Format(m.FrameValue));
            }

            // Подсистемы — самые дорогие вперёд: первая и есть ответ «что лагнуло».
            var top = new List<Metric>();
            foreach (Metric m in _metrics)
            {
                if (m.IsSubsystemTime && m.FrameValue >= 0.5f) top.Add(m);
            }
            top.Sort((a, b) => b.FrameValue.CompareTo(a.FrameValue));

            if (top.Count > 0)
            {
                _sb.Append(" | ");
                for (int i = 0; i < top.Count && i < 6; i++)
                {
                    if (i > 0) _sb.Append(", ");
                    _sb.Append(top[i].Key).Append(' ').Append(Format(top[i].FrameValue));
                }
            }

            foreach (Metric m in _metrics)
            {
                if (m.Key == "gc_alloc_kb" && m.FrameValue >= 1f)
                    _sb.Append(" | gc_alloc_kb ").Append(Format(m.FrameValue));
            }

            AppendEvents(_frameEvents);
            WriteLine(_sb.ToString(), warning: true);
        }

        private void AppendEvents(int[] events)
        {
            bool any = false;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i] == 0) continue;
                _sb.Append(any ? ", " : " | события: ").Append(((PerfEventKind)i).ToString()).Append('×').Append(events[i]);
                any = true;
            }
        }

        private static string Format(float value)
        {
            float abs = Math.Abs(value);
            string format = abs >= 100f ? "F0" : "F1";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        // ── Время UltimateXR ────────────────────────────────────────────────

        private void OnUxrStageUpdating(UxrUpdateStage stage) => _uxrWatch.Restart();

        private void OnUxrStageUpdated(UxrUpdateStage stage)
        {
            _uxrWatch.Stop();
            _uxrAccumMs += _uxrWatch.Elapsed.TotalMilliseconds;
        }

        // ── Вывод ───────────────────────────────────────────────────────────

        /// <summary>Строка в файл и в канал Perf (logcat на шлеме).</summary>
        public void WriteLine(string line, bool warning = false)
        {
            WriteRaw(line);
            if (warning) GameLog.Perf.Warning("[Perf] " + line);
            else         GameLog.Perf.Info("[Perf] " + line);
        }

        private void WriteRaw(string line)
        {
            if (_disposed) return;
            _writer.WriteLine(line);
            // Строк мало (пишутся только изменения), а на шлеме прогон может оборваться
            // вылетом — сбрасываем сразу, чтобы лог дожил до причины.
            _writer.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;

            UxrManager.StageUpdating -= OnUxrStageUpdating;
            UxrManager.StageUpdated  -= OnUxrStageUpdated;

            foreach (Metric m in _metrics)
            {
                if (m.Source == Source.Profiler) m.Recorder.Dispose();
            }

            _writer.Flush();
            _writer.Dispose();
            _disposed = true;
        }
    }
}
