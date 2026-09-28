using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>Режим прогона, который выбирают на экране «Перф-тесты».</summary>
    public enum PerfRunMode
    {
        /// <summary>Все фазы обычного прогона: база, куклы рядами, хлам, «по карте».</summary>
        Standard,
        /// <summary>По фазе на каждый префаб аватара.</summary>
        PerSkin,
        /// <summary>Обычный с фазами по <see cref="StressTestPlan.ShortPhaseSeconds"/> с — проверить, что всё работает.</summary>
        Short,
        /// <summary>Только «куклы: по карте» — реалистичная нагрузка без рядов и хлама.</summary>
        MapOnly,
    }

    /// <summary>Одна фаза плана: имя, длительность, идёт ли в сводку.</summary>
    public readonly struct StressTestPlannedPhase
    {
        public readonly string Name;
        public readonly float Seconds;
        public readonly bool Measured;

        public StressTestPlannedPhase(string name, float seconds, bool measured)
        {
            Name = name;
            Seconds = seconds;
            Measured = measured;
        }
    }

    /// <summary>
    /// План прогона без Unity-объектов: конфиг из выбора на экране «Перф-тесты» и список фаз с
    /// длительностями — для описания режима («N фаз, ~M мин») и прогресса «фаза i из N».
    ///
    /// <para>
    /// <b>Повторяет порядок фаз <c>StressTestServer.Run</c>.</b> Меняешь фазы на сервере — меняй
    /// <see cref="Phases"/>: план только показывает, фазами управляет сервер. Разойдутся — счётчик
    /// «i из N» соврёт, замер не пострадает.
    /// </para>
    /// </summary>
    public static class StressTestPlan
    {
        /// <summary>Фазы короткого прогона, секунды: проверить, что всё работает, а не мерить.</summary>
        public const float ShortPhaseSeconds = 10f;

        public static StressTestConfig BuildConfig(PerfRunMode mode, int puppetSkin, int puppetCount)
        {
            var config = new StressTestConfig
            {
                perSkinPhases = mode == PerfRunMode.PerSkin,
                mapOnly       = mode == PerfRunMode.MapOnly,
                puppetSkin    = mode == PerfRunMode.PerSkin ? StressTestLayout.MixedSkins : puppetSkin,
                puppetCount   = Mathf.Clamp(puppetCount, 1, StressTestConfig.MaxPuppets),
            };

            if (mode == PerfRunMode.Short)
            {
                config.warmupSeconds = 2f;
                config.phaseSeconds  = ShortPhaseSeconds;
                config.settleSeconds = 2f;
            }

            return config;
        }

        /// <summary>Фазы прогона по порядку. <paramref name="skinCount"/> — число скинов (для прогона по скинам).</summary>
        public static List<StressTestPlannedPhase> Phases(StressTestConfig config, int skinCount)
        {
            var phases = new List<StressTestPlannedPhase>
            {
                new StressTestPlannedPhase("разгон", config.warmupSeconds, false),
                new StressTestPlannedPhase("база", config.phaseSeconds, true),
            };

            if (config.perSkinPhases)
            {
                for (int i = 0; i < skinCount; i++) AddPair(phases, "куклы: скин " + (i + 1), config);
                return phases;
            }

            if (config.mapOnly)
            {
                if (config.puppetCount > 0) AddPair(phases, "куклы: по карте", config);
                return phases;
            }

            AddPair(phases, "куклы", config);
            if (config.clutterCount > 0) AddPair(phases, "куклы+хлам", config);
            if (config.mapSpreadPhase && config.puppetCount > 0) AddPair(phases, "куклы: по карте", config);
            return phases;
        }

        public static float TotalSeconds(List<StressTestPlannedPhase> phases)
        {
            float total = 0f;
            foreach (StressTestPlannedPhase phase in phases) total += phase.Seconds;
            return total;
        }

        /// <summary>Одна строка для экрана: что входит в прогон и сколько он идёт.</summary>
        public static string Describe(PerfRunMode mode, StressTestConfig config, int skinCount)
        {
            List<StressTestPlannedPhase> phases = Phases(config, skinCount);
            int measured = 0;
            foreach (StressTestPlannedPhase p in phases) if (p.Measured) measured++;

            string what;
            switch (mode)
            {
                case PerfRunMode.PerSkin: what = $"база, затем по фазе на каждый из {skinCount} скинов"; break;
                case PerfRunMode.Short:   what = $"как обычный, фазы по {ShortPhaseSeconds:F0} с — проверка, не замер"; break;
                case PerfRunMode.MapOnly: what = "база и одна фаза: куклы кольцом по карте, в кадре 1–3"; break;
                default:                  what = "база, куклы рядами, куклы+хлам, куклы по карте"; break;
            }

            return $"{what}. Фаз {phases.Count} (в сводку {measured}), ~{FormatDuration(TotalSeconds(phases))}.";
        }

        public static string FormatDuration(float seconds)
        {
            int s = Mathf.RoundToInt(seconds);
            return s >= 60 ? $"{s / 60} мин {s % 60:D2} с" : $"{s} с";
        }

        private static void AddPair(List<StressTestPlannedPhase> phases, string name, StressTestConfig config)
        {
            phases.Add(new StressTestPlannedPhase(name + "~успокоение", config.settleSeconds, false));
            phases.Add(new StressTestPlannedPhase(name, config.phaseSeconds, true));
        }
    }
}
