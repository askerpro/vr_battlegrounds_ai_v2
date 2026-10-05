using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Программа стенда <see cref="AvatarPuppetStand"/>.</summary>
    public enum PuppetProgramId
    {
        /// <summary>Медленный ход головой 0,3 / 0,6 / 1,0 м/с в 8 направлениях (вперёд, вбок, назад, диагонали) — ритм шагов.</summary>
        SlowWalk,
        /// <summary>Быстрые движения: бег 3,5 м/с, резкая остановка, отскок назад, рывок вбок, выпады, ход 1 м/с.</summary>
        FastMoves,
        /// <summary>Весь набор: поворот, присед, наклоны, взгляд вниз, рывок, ходьба, броски головой.</summary>
        Full,
        /// <summary>Короткая: ход вбок вправо и влево 0,6 и 1,0 м/с (таз, зазор стоп) — для быстрых A/B.</summary>
        SideWalk,
        /// <summary>
        /// Поворот одной головы ±20/±45/±70° (кисти и тело на месте) и поворот тела ±45/±90° (голова и кисти вместе с ним):
        /// стоит ли корпус аватара, пока голова повёрнута меньше порога UltimateXR (HeadFreeRangeTorsion 30°).
        /// Затем то же с шумом трекинга шлема (<c>N_*</c>): дрожь ±3 мм/кадр и рывки 1–3 см — стоя и при поворотах головы.
        /// </summary>
        HeadTurn,
        /// <summary>
        /// Присед: полуприсед с наклоном корпуса (L1, L2 — колено не должно включаться), затем голова прямо вниз на 0,9 м за
        /// 4 с, пауза, вверх; затем глубже — на 1,0 м (колено должно включиться). Снимки каждые 0,3 с; замеры
        /// приседа (Legs_Crouch по высоте головы, колени и подошвы над полом, зазоры стоп и коленей) — <c>crouch.txt</c>,
        /// <c>crouch.csv</c>.
        /// </summary>
        Crouch,
        /// <summary>
        /// Колено и качание корпуса: прямо вниз на колено (голова −0,6 м), затем таз неподвижен, корпус качается — вперёд-назад
        /// ±30° (0,5 Гц), вбок ±25° (0,8 Гц), назад до −15°, смешанно (1 Гц) — голова ходит на ±0,2–0,35 м. Ноги должны стоять:
        /// шагов 0, Legs_Crouch держится, колени не скрещиваются (<c>crouch.txt</c>, <c>metrics.txt</c>).
        /// </summary>
        KneelSway,
        /// <summary>
        /// Очень низко: голова прямо вниз до 0,45 м над полом (−1,2 м за 6 с), пауза, вверх; затем вниз до 0,5 м с наклоном 30°.
        /// Таз аватара не должен уходить ниже минимума и под пол — излишек забирает наклон корпуса вперёд.
        /// </summary>
        VeryLow,
    }

    /// <summary>
    /// Шаг программы: <see cref="Duration"/> секунд, каждый кадр <see cref="Apply"/>(состояние, t от начала шага, dt).
    /// <see cref="Steady"/> — ровный участок ходьбы: его ритм шагов идёт ещё и в gait.txt.
    /// </summary>
    public sealed class PuppetStep
    {
        public string Name;
        public float Duration;
        public Action<PuppetMotion, float, float> Apply;
        public int ShotEveryFrames;       // 0 — без снимков
        public float[] ShotsAt;           // доли длительности (если не по кадрам)
        public bool Steady;
        public PuppetClipEntry Clip;      // сценарий из клипа: в начале шага начать играть этот клип
    }

    /// <summary>
    /// Библиотека программ: перенос <c>Full</c> и <c>FastMoves</c> из <c>LegsComparisonRig</c> (удалён) плюс медленная
    /// ходьба. Ходьба — головой (физическая ходьба по арене), корень аватара стоит.
    /// </summary>
    public static class PuppetPrograms
    {
        public static List<PuppetStep> Build(PuppetProgramId id, float speedMultiplier)
        {
            var steps = new List<PuppetStep>();
            float k = Mathf.Max(0.05f, speedMultiplier);
            steps.Add(Hold("00_idle", 2.5f, 1f));
            switch (id)
            {
                case PuppetProgramId.SlowWalk: SlowWalk(steps, k); break;
                case PuppetProgramId.FastMoves: FastMoves(steps, k); break;
                case PuppetProgramId.Full: Full(steps, k); break;
                case PuppetProgramId.SideWalk: SideWalk(steps, k); break;
                case PuppetProgramId.HeadTurn: HeadTurn(steps); break;
                case PuppetProgramId.Crouch: Crouch(steps); break;
                case PuppetProgramId.KneelSway: KneelSway(steps); break;
                case PuppetProgramId.VeryLow: VeryLow(steps); break;
            }
            return steps;
        }

        /// <summary>Разгон и первые шаги (1 с) не меряются как ритм; ровный ход 5 с — сегмент <c>*_steady</c>.</summary>
        private static void SlowWalk(List<PuppetStep> steps, float k)
        {
            float d = Mathf.Sqrt(0.5f);
            (string name, Vector3 dir)[] dirs =
            {
                ("fwd", Vector3.forward), ("right", Vector3.right), ("left", Vector3.left), ("back", Vector3.back),
                ("fwdright", new Vector3(d, 0f, d)), ("fwdleft", new Vector3(-d, 0f, d)),
                ("backright", new Vector3(d, 0f, -d)), ("backleft", new Vector3(-d, 0f, -d)),
            };
            foreach ((string name, Vector3 dir) in dirs)
            foreach (float v0 in new[] { 0.3f, 0.6f, 1.0f })
            {
                float v = v0 * k;
                string seg = "S_" + name + "_" + Mathf.RoundToInt(v0 * 100f).ToString("000", CultureInfo.InvariantCulture);
                steps.Add(Drive(seg, 1.0f, t => dir * v * Mathf.Clamp01(t / 0.3f), 0));
                PuppetStep steady = Drive(seg + "_steady", 5.0f, t => dir * v, 60);
                steady.Steady = true;
                steps.Add(steady);
                steps.Add(Drive(seg + "_settle", 1.2f, t => Vector3.zero, 0));
            }
        }

        private static void SideWalk(List<PuppetStep> steps, float k)
        {
            foreach ((string name, Vector3 dir) in new[] { ("right", Vector3.right), ("left", Vector3.left) })
            foreach (float v0 in new[] { 0.6f, 1.0f })
            {
                float v = v0 * k;
                string seg = "W_" + name + "_" + Mathf.RoundToInt(v0 * 100f).ToString("000", CultureInfo.InvariantCulture);
                steps.Add(Drive(seg, 1.0f, t => dir * v * Mathf.Clamp01(t / 0.3f), 0));
                PuppetStep steady = Drive(seg + "_steady", 4.0f, t => dir * v, 60);
                steady.Steady = true;
                steps.Add(steady);
                steps.Add(Drive(seg + "_settle", 1.0f, t => Vector3.zero, 0));
            }
        }

        /// <summary>
        /// Голова отдельно от тела: поворот одной головы на угол и обратно (кисти держит тело — не двигаются), затем поворот
        /// тела (голова и кисти вместе). Сегменты <c>H_head_±NN_hold</c> и <c>H_body_±NN_hold</c> — замер рысканья груди и
        /// таза аватара от тела и от головы.
        /// </summary>
        private static void HeadTurn(List<PuppetStep> steps)
        {
            foreach (float a in new[] { 20f, -20f, 45f, -45f, 70f, -70f })
            {
                string seg = "H_head_" + (a > 0 ? "+" : "-") + Mathf.RoundToInt(Mathf.Abs(a)).ToString("00", CultureInfo.InvariantCulture);
                float angle = a;
                steps.Add(Ease(seg, 0.6f, (m, e) => m.HeadYaw = angle * e));
                steps.Add(Hold(seg + "_hold", 1.5f, 1f));
                steps.Add(Ease(seg + "_back", 0.6f, (m, e) => m.HeadYaw = angle * (1f - e)));
                steps.Add(Hold(seg + "_settle", 0.8f));
            }

            Realign(steps, "R1");
            foreach (float a in new[] { 45f, -45f, 90f, -90f })
            {
                string seg = "H_body_" + (a > 0 ? "+" : "-") + Mathf.RoundToInt(Mathf.Abs(a)).ToString("00", CultureInfo.InvariantCulture);
                float angle = a;
                steps.Add(Ease(seg, 0.8f, (m, e) => m.Yaw = angle * e));
                steps.Add(Hold(seg + "_hold", 1.5f, 1f));
                steps.Add(Ease(seg + "_back", 0.8f, (m, e) => m.Yaw = angle * (1f - e)));
                steps.Add(Hold(seg + "_settle", 1.0f));
            }

            // Как в шлеме: тот же поворот головы вокруг шеи, но поза шлема дрожит и иногда прыгает. Корпус и ноги должны
            // стоять (до свободного угла), а дрожь и рывки — не считаться ходьбой.
            Realign(steps, "R2");
            steps.Add(Noisy(Hold("N_still", 4f, 0.5f)));
            foreach (float a in new[] { 25f, -25f, 45f, -45f, 70f, -70f })
            {
                string seg = "N_head_" + (a > 0 ? "+" : "-") + Mathf.RoundToInt(Mathf.Abs(a)).ToString("00", CultureInfo.InvariantCulture);
                float angle = a;
                steps.Add(Noisy(Ease(seg, 0.5f, (m, e) => m.HeadYaw = angle * e)));
                steps.Add(Noisy(Hold(seg + "_hold", 2.0f, 1f)));
                steps.Add(Noisy(Ease(seg + "_back", 0.5f, (m, e) => m.HeadYaw = angle * (1f - e))));
                steps.Add(Noisy(Hold(seg + "_settle", 1.0f)));
            }

            // Как в шлеме, вариант «руки за плечами»: контроллеры уходят за взглядом на 40 % поворота головы — скрутка
            // торса UltimateXR за руками (Spine/Chest Torsion) свободного угла головы не ждёт.
            Realign(steps, "R3");
            foreach (float a in new[] { 25f, -25f, 45f, -45f })
            {
                string seg = "A_head_" + (a > 0 ? "+" : "-") + Mathf.RoundToInt(Mathf.Abs(a)).ToString("00", CultureInfo.InvariantCulture);
                float angle = a;
                steps.Add(Noisy(Ease(seg, 0.5f, (m, e) => m.HeadYaw = angle * e), 0.4f));
                steps.Add(Noisy(Hold(seg + "_hold", 2.0f, 1f), 0.4f));
                steps.Add(Noisy(Ease(seg + "_back", 0.5f, (m, e) => m.HeadYaw = angle * (1f - e)), 0.4f));
                steps.Add(Noisy(Hold(seg + "_settle", 1.0f), 0.4f));
            }
        }

        /// <summary>
        /// Корпус аватара — снова по взгляду: после поворотов головы UltimateXR оставляет корпус там, где он был (в пределах
        /// свободного угла), и следующий замер начинался бы с остатка. Шаг вперёд и назад выпрямляет корпус.
        /// </summary>
        private static void Realign(List<PuppetStep> steps, string name)
        {
            steps.Add(Drive(name + "_realign_fwd", 1.2f, t => Vector3.forward * 0.6f * Mathf.Clamp01(t / 0.2f), 0));
            steps.Add(Drive(name + "_realign_back", 1.2f, t => Vector3.back * 0.6f * Mathf.Clamp01(t / 0.2f), 0));
            steps.Add(Hold(name + "_realign_settle", 1.2f));
        }

        /// <summary>Шаг с шумом трекинга шлема (<see cref="PuppetMotion.Noise"/> = 1) и кистями за головой на долю <paramref name="hands"/>.</summary>
        private static PuppetStep Noisy(PuppetStep step, float hands = 0f)
        {
            Action<PuppetMotion, float, float> apply = step.Apply;
            step.Apply = (m, t, dt) =>
            {
                m.Noise = 1f;
                m.HandsFollowHead = hands;
                apply(m, t, dt);
            };
            return step;
        }

        /// <summary>Голова вниз равномерно (не SmoothStep: порог приседа меряется по высоте головы), пауза, вверх.</summary>
        private static void Crouch(List<PuppetStep> steps)
        {
            const int every = 18;   // 0,3 с при 60 кадрах/с
            // Полуприсед с наклоном корпуса: голова низко и впереди, таз высоко — колено включаться НЕ должно.
            foreach ((string name, float crouch, float lean) in new[] { ("L1_semi_lean40", 0.35f, 40f), ("L2_semi_lean25", 0.25f, 25f) })
            {
                float c = crouch, a = lean;
                steps.Add(new PuppetStep { Name = name + "_down", Duration = 3f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = Mathf.Clamp01(t / 3f); m.Crouch = c * e; m.LeanForward = a * e; } });
                steps.Add(new PuppetStep { Name = name + "_hold", Duration = 2f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = c; m.LeanForward = a; } });
                steps.Add(new PuppetStep { Name = name + "_up", Duration = 2.5f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = 1f - Mathf.Clamp01(t / 2.5f); m.Crouch = c * e; m.LeanForward = a * e; } });
                steps.Add(new PuppetStep { Name = name + "_settle", Duration = 1.5f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0f; m.LeanForward = 0f; } });
            }

            // Колено с прямой спиной: голова прямо вниз — колено включаться должно.
            foreach ((string name, float depth, float down) in new[] { ("K1_090", 0.9f, 4f), ("K2_100", 1.0f, 4.4f) })
            {
                float d = depth, dur = down;
                steps.Add(new PuppetStep { Name = name + "_down", Duration = dur, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = d * Mathf.Clamp01(t / dur); } });
                steps.Add(new PuppetStep { Name = name + "_hold", Duration = 2f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = d; } });
                steps.Add(new PuppetStep { Name = name + "_up", Duration = 3f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = d * (1f - Mathf.Clamp01(t / 3f)); } });
                steps.Add(new PuppetStep { Name = name + "_settle", Duration = 1.5f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0f; } });
            }
        }

        private static void VeryLow(List<PuppetStep> steps)
        {
            const int every = 15;   // 0,25 с: при 0,2 м/с опускания — снимок каждые 5 см
            foreach ((string name, float depth, float lean) in new[] { ("V1_straight_045", 1.2f, 0f), ("V2_lean30_050", 1.0f, 30f) })
            {
                float c = depth, a = lean;
                steps.Add(new PuppetStep { Name = name + "_down", Duration = 6f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = Mathf.Clamp01(t / 6f); m.Crouch = c * e; m.LeanForward = a * e; } });
                steps.Add(new PuppetStep { Name = name + "_hold", Duration = 2f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = c; m.LeanForward = a; } });
                steps.Add(new PuppetStep { Name = name + "_up", Duration = 4f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = 1f - Mathf.Clamp01(t / 4f); m.Crouch = c * e; m.LeanForward = a * e; } });
                steps.Add(new PuppetStep { Name = name + "_settle", Duration = 1.5f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0f; m.LeanForward = 0f; } });
            }

            // Низко, взгляд вниз 60° и поворот головы ±60°: проекция взгляда на пол мала — наклон корпуса не должен крутиться.
            steps.Add(new PuppetStep { Name = "V3_low_look_down", Duration = 3f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = Mathf.Clamp01(t / 3f); m.Crouch = 0.9f * e; m.LookDown = 60f * e; } });
            steps.Add(new PuppetStep { Name = "V3_low_head_yaw", Duration = 6f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0.9f; m.LookDown = 60f; m.HeadYaw = 60f * Mathf.Sin(2f * Mathf.PI * t / 3f); } });
            steps.Add(new PuppetStep { Name = "V3_settle", Duration = 3f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = 1f - Mathf.Clamp01(t / 3f); m.Crouch = 0.9f * e; m.LookDown = 60f * e; m.HeadYaw = 0f; } });
            // Наклон вбок + взгляд вниз (крен + тангаж): прежняя мера тангажа Atan2(z, y) при крене давала ложный «наклон назад» и скрутку.
            steps.Add(new PuppetStep { Name = "V4_side_down", Duration = 3f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = Mathf.Clamp01(t / 3f); m.Crouch = 0.6f * e; m.LeanSide = 30f * e; m.LookDown = 45f * e; } });
            steps.Add(new PuppetStep { Name = "V4_side_down_yaw", Duration = 6f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0.6f; m.LeanSide = 30f * Mathf.Cos(2f * Mathf.PI * t / 6f); m.LookDown = 45f; m.HeadYaw = 50f * Mathf.Sin(2f * Mathf.PI * t / 3f); } });
            steps.Add(new PuppetStep { Name = "V4_settle", Duration = 3f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); float e = 1f - Mathf.Clamp01(t / 3f); m.Crouch = 0.6f * e; m.LeanSide = 30f * e; m.LookDown = 45f * e; m.HeadYaw = 0f; } });
        }

        private static void KneelSway(List<PuppetStep> steps)
        {
            const int every = 18;
            const float kneel = 0.6f;
            steps.Add(new PuppetStep { Name = "KS0_down", Duration = 3f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel * Mathf.Clamp01(t / 3f); } });
            steps.Add(new PuppetStep { Name = "KS0_hold", Duration = 1.5f, ShotsAt = new[] { 1f }, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; } });
            const float tau = 2f * Mathf.PI;
            steps.Add(new PuppetStep { Name = "KS1_fwd_back_30", Duration = 6f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; m.LeanForward = 30f * Mathf.Sin(tau * 0.5f * t); } });
            steps.Add(new PuppetStep { Name = "KS2_side_25", Duration = 5f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; m.LeanForward = 0f; m.LeanSide = 25f * Mathf.Sin(tau * 0.8f * t); } });
            steps.Add(new PuppetStep { Name = "KS3_back_15", Duration = 4f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; m.LeanSide = 0f; m.LeanForward = -15f * Mathf.Abs(Mathf.Sin(tau * 0.5f * t)); } });
            steps.Add(new PuppetStep { Name = "KS4_mix_1hz", Duration = 6f, ShotEveryFrames = every, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; m.LeanForward = 20f * Mathf.Sin(tau * 1f * t); m.LeanSide = 15f * Mathf.Sin(tau * 0.7f * t); } });
            steps.Add(new PuppetStep { Name = "KS5_still", Duration = 2f, ShotsAt = new[] { 1f }, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel; m.LeanForward = m.LeanSide = 0f; } });
            steps.Add(new PuppetStep { Name = "KS6_up", Duration = 2.5f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = kneel * (1f - Mathf.Clamp01(t / 2.5f)); } });
            steps.Add(new PuppetStep { Name = "KS6_settle", Duration = 1.5f, Apply = (m, t, dt) => { m.Walk(Vector3.zero, dt); m.Crouch = 0f; } });
        }

        private static void FastMoves(List<PuppetStep> steps, float k)
        {
            float run = 3.5f * k;
            steps.Add(Drive("F1_run", 1.6f, t => Vector3.forward * run * Mathf.Clamp01(t / 0.3f), 3));
            steps.Add(Drive("F2_stop", 0.12f, t => Vector3.forward * run * (1f - t / 0.12f), 3));
            steps.Add(Drive("F2_stop_settle", 0.8f, t => Vector3.zero, 6));
            steps.Add(Drive("F3_back", 1.0f, t => Vector3.back * 2f * k * Mathf.Clamp01(t / 0.2f), 3));
            steps.Add(Drive("F3_back_stop", 0.1f, t => Vector3.back * 2f * k * (1f - t / 0.1f), 3));
            steps.Add(Drive("F3_back_settle", 0.8f, t => Vector3.zero, 6));
            steps.Add(Drive("F4_side", 0.8f, t => Vector3.right * 2.5f * k * Mathf.Clamp01(t / 0.15f), 3));
            steps.Add(Drive("F4_side_stop", 0.1f, t => Vector3.right * 2.5f * k * (1f - t / 0.1f), 3));
            steps.Add(Drive("F4_side_settle", 0.8f, t => Vector3.zero, 6));
            for (int i = 0; i < 2; i++)
            {
                steps.Add(Drive($"F5_lunge{i}", 0.2f, t => Vector3.forward * 5f * k * Mathf.Sin(t / 0.2f * Mathf.PI) * Mathf.PI / 4f, 3));
                steps.Add(Drive($"F5_lunge{i}_back", 0.2f, t => Vector3.back * 5f * k * Mathf.Sin(t / 0.2f * Mathf.PI) * Mathf.PI / 4f, 3));
                steps.Add(Drive($"F5_lunge{i}_settle", 0.5f, t => Vector3.zero, 0));
            }
            steps.Add(Drive("F6_walk_right", 1.6f, t => Vector3.right * k * Mathf.Clamp01(t / 0.3f), 6));
            steps.Add(Drive("F6_walk_right_settle", 0.6f, t => Vector3.zero, 0));
            steps.Add(Drive("F7_walk_back", 1.6f, t => Vector3.back * k * Mathf.Clamp01(t / 0.3f), 6));
            steps.Add(Drive("F7_walk_back_settle", 0.6f, t => Vector3.zero, 0));
            steps.Add(Drive("F8_walk_left", 1.6f, t => Vector3.left * k * Mathf.Clamp01(t / 0.3f), 6));
            steps.Add(Drive("F8_walk_left_settle", 0.6f, t => Vector3.zero, 0));
        }

        private static void Full(List<PuppetStep> steps, float k)
        {
            steps.Add(Ease("01_turn", 1.0f, (m, e) => m.Yaw = 90f * e, 0.5f, 1f));
            steps.Add(Hold("01_turn_right_settled", 1.0f, 1f));
            steps.Add(Ease("02_turn_back", 1.5f, (m, e) => m.Yaw = 90f - 180f * e, 0.5f, 1f));
            steps.Add(Hold("02_turn_left_settled", 1.0f, 1f));
            steps.Add(Ease("03_turn_center", 1.0f, (m, e) => m.Yaw = -90f + 90f * e));
            steps.Add(Hold("03_turn_center_settled", 1.5f, 1f));
            steps.Add(Ease("04_crouch", 1.0f, (m, e) => m.Crouch = 0.45f * e, 0.5f, 1f));
            steps.Add(Hold("04_crouch_settled", 1.0f, 1f));
            steps.Add(Ease("05_stand_up", 0.8f, (m, e) => m.Crouch = 0.45f * (1f - e)));
            steps.Add(Hold("05_stood_up", 1.0f, 1f));
            steps.Add(Ease("06_lean_side", 0.8f, (m, e) => m.LeanSide = 20f * e, 1f));
            steps.Add(Ease("07_lean_side_back", 0.6f, (m, e) => m.LeanSide = 20f * (1f - e)));
            steps.Add(Ease("08_lean_forward", 0.8f, (m, e) => m.LeanForward = 30f * e, 1f));
            steps.Add(Ease("09_lean_forward_back", 0.6f, (m, e) => m.LeanForward = 30f * (1f - e)));
            steps.Add(Hold("09_settled", 0.8f));
            steps.Add(Ease("09c_look_down", 0.8f, (m, e) => m.LookDown = 65f * e, 0.5f, 1f));
            steps.Add(Hold("09c_look_down_settled", 1.5f, 1f));
            steps.Add(Ease("09d_look_up", 0.6f, (m, e) => m.LookDown = 65f * (1f - e)));
            steps.Add(Hold("09d_look_up_settled", 1.0f));
            steps.Add(Drive("10_dash", 0.6f, t => Vector3.forward * 2f * k, 9));
            steps.Add(Drive("10_dash_after", 1.2f, t => Vector3.zero, 18));
            steps.Add(Drive("11_walk", 2f / 0.6f, t => Vector3.forward * 0.6f * k, 30));
            steps.Add(Drive("11_walk_stop", 1.2f, t => Vector3.zero, 0));
            steps.Add(Drive("12_walk_normal", 2f, t => Vector3.forward * 1.2f * k, 30));
            steps.Add(Drive("12_walk_normal_stop", 1.0f, t => Vector3.zero, 0));
            steps.Add(Drive("13_strafe_right", 1.25f, t => Vector3.right * 0.8f * k, 30));
            steps.Add(Drive("13_strafe_stop", 1.0f, t => Vector3.zero, 0));
            steps.Add(Drive("14_step_back", 1f, t => Vector3.back * 0.6f * k, 30));
            steps.Add(Drive("14_step_back_stop", 1.0f, t => Vector3.zero, 0));
            steps.Add(Drive("15_jerk_side", 0.15f, t => Vector3.right * 0.4f / 0.15f, 9));
            steps.Add(Drive("15_jerk_side_back", 0.15f, t => Vector3.left * 0.4f / 0.15f, 9));
            steps.Add(Drive("15_jerk_side_settled", 0.6f, t => Vector3.zero, 18));
            steps.Add(Drive("16_jerk_forward", 0.15f, t => Vector3.forward * 0.4f / 0.15f, 9));
            steps.Add(Drive("16_jerk_forward_back", 0.15f, t => Vector3.back * 0.4f / 0.15f, 9));
            steps.Add(Drive("16_jerk_forward_settled", 0.6f, t => Vector3.zero, 18));
        }

        /// <summary>Ход головой со скоростью <paramref name="velocityAt"/>(t), м/с в осях стенда.</summary>
        public static PuppetStep Drive(string name, float duration, Func<float, Vector3> velocityAt, int shotEveryFrames) =>
            new PuppetStep
            {
                Name = name,
                Duration = duration,
                ShotEveryFrames = shotEveryFrames,
                Apply = (m, t, dt) => m.Walk(velocityAt(t), dt),
            };

        /// <summary>Плавный (SmoothStep) переход параметра за <paramref name="duration"/>; снимки — в долях длительности.</summary>
        public static PuppetStep Ease(string name, float duration, Action<PuppetMotion, float> apply, params float[] shotsAt) =>
            new PuppetStep
            {
                Name = name,
                Duration = duration,
                ShotsAt = shotsAt,
                Apply = (m, t, dt) => apply(m, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration))),
            };

        public static PuppetStep Hold(string name, float duration, params float[] shotsAt) =>
            new PuppetStep { Name = name, Duration = duration, ShotsAt = shotsAt, Apply = (m, t, dt) => m.Walk(Vector3.zero, dt) };
    }
}
