using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Прогон стенда <see cref="AvatarPuppetStand"/>: очередь программ и сценариев из клипа, по шагам. Каждый кадр — поза
    /// манипулятора («голова» и кисти в осях стенда) для стенда; в конце кадра — замеры (<see cref="PuppetMetrics"/>) и
    /// снимки (<see cref="PuppetShots"/>). Итог прогона — папка <c>&lt;выход&gt;/&lt;имя прогона&gt;/</c>.
    /// </summary>
    public sealed class PuppetRunner
    {
        /// <summary>Один прогон: имя (папка) и шаги; <see cref="FromClip"/> — голову и кисти даёт клип, а не программа.</summary>
        public sealed class Run
        {
            public string Name;
            public List<PuppetStep> Steps;
            public bool FromClip;
        }

        private readonly Queue<Run> _queue = new Queue<Run>();
        private readonly PuppetMotion _motion = new PuppetMotion();
        private Run _run;
        private int _step = -1;
        private float _t;
        private int _frameInStep;
        private int _shot;
        private bool _begun;

        public PuppetMetrics Metrics { get; set; }
        public bool IsRunning => _run != null;
        public bool FromClip => _run != null && _run.FromClip;
        public string Current => _run == null ? "—" : $"{_run.Name}: {(_step >= 0 && _step < _run.Steps.Count ? _run.Steps[_step].Name : "")}";
        public string OutputRoot;
        public PuppetMotion Motion => _motion;

        /// <summary>Шаги сценария из клипа: каждый клип — сегмент <c>C&lt;n&gt;_&lt;клип&gt;</c> с ровным ходом в gait.txt.</summary>
        public static List<PuppetStep> ClipSteps(PuppetClipEntry[] clips)
        {
            var steps = new List<PuppetStep>();
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || clips[i].clip == null) continue;
                steps.Add(new PuppetStep
                {
                    Name = $"C{i}_{clips[i].clip.name}_x{clips[i].speed:0.00}".Replace(',', '.'),
                    Duration = clips[i].duration,
                    ShotEveryFrames = 30,
                    Steady = true,
                    Clip = clips[i],
                    Apply = (m, t, dt) => { },
                });
            }
            return steps;
        }

        public void Enqueue(Run run) => _queue.Enqueue(run);

        public void Stop()
        {
            _queue.Clear();
            _run = null;
        }

        /// <summary>
        /// Кадр (Update): начать шаг, продвинуть время. Возвращает false, если прогона нет. <paramref name="clipEntry"/> —
        /// клип, который надо начать играть в этом кадре (сценарий из клипа), иначе null.
        /// </summary>
        public bool Tick(float dt, out PuppetClipEntry clipEntry, out bool firstClip)
        {
            clipEntry = null;
            firstClip = false;
            if (_run == null && !NextRun()) return false;

            if (!_begun)
            {
                PuppetStep step = _run.Steps[_step];
                Metrics.BeginSegment(step.Name, step.Steady);
                _t = 0f;
                _frameInStep = 0;
                _shot = 0;
                _begun = true;
                clipEntry = step.Clip;
                firstClip = _step == 0;
            }

            _t += dt;
            _frameInStep++;
            _run.Steps[_step].Apply(_motion, _t, dt);
            _motion.TickNoise(dt);
            return true;
        }

        /// <summary>Конец кадра: замер, снимок по расписанию шага, переход к следующему шагу.</summary>
        public void EndOfFrame(float dt, Vector3 head, Func<string, bool> capture)
        {
            if (_run == null || !_begun || Metrics == null) return;
            PuppetStep step = _run.Steps[_step];
            Metrics.Measure(dt, _motion.Yaw, head, true, _motion.Yaw + _motion.HeadYaw);

            bool shot = step.ShotEveryFrames > 0 && _frameInStep % step.ShotEveryFrames == 0;
            if (!shot && step.ShotsAt != null && _shot < step.ShotsAt.Length && _t >= step.ShotsAt[_shot] * step.Duration - 1e-4f)
            {
                shot = true;
                _shot++;
            }
            if (shot) capture(Path.Combine(OutputRoot, _run.Name, $"{step.Name}_f{_frameInStep:000}.png"));

            if (_t < step.Duration - 1e-4f) return;
            Metrics.EndSegment();
            _begun = false;
            if (++_step < _run.Steps.Count) return;

            Metrics.Write(Path.Combine(OutputRoot, _run.Name));
            GameLog.Debug.Info($"[PuppetRunner] Прогон '{_run.Name}' записан: {Path.Combine(OutputRoot, _run.Name)}");
            _run = null;
        }

        /// <summary>Следующий прогон из очереди: отчёт замеров — с чистого листа (аватары и их покой — те же).</summary>
        private bool NextRun()
        {
            while (_queue.Count > 0)
            {
                Run run = _queue.Dequeue();
                if (run.Steps == null || run.Steps.Count == 0) continue;
                _run = run;
                _step = 0;
                _begun = false;
                _motion.Reset();
                Metrics?.ResetReport();
                return Metrics != null;
            }
            return false;
        }
    }
}
