using System;
using System.Collections.Generic;
using System.Globalization;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Параметры прогона из строк «ключ=значение» (сценарий e2e <c>perf-stress</c>, <c>-e2eArg</c>) → режим и
    /// <see cref="StressTestConfig"/>. Без Unity-сцены: только разбор и проверка, чтобы ошибку видно было до старта.
    ///
    /// <para>
    /// Конфиг собирает тот же <see cref="StressTestPlan.BuildConfig"/>, что и планшет («Перф-тесты»); явные
    /// параметры ложатся поверх. Умолчания — <see cref="StressTestConfig"/>. Любая ошибка — текст отказа,
    /// без молчаливого отката на умолчание: неизвестный ключ, неверное значение, параметр, который в выбранном
    /// режиме не действует (иначе условия прогона в отчёте соврали бы).
    /// </para>
    /// </summary>
    public static class StressTestArgs
    {
        public const string KeyMode    = "mode";
        public const string KeyPuppets = "puppets";
        public const string KeyClutter = "clutter";
        public const string KeyPhase   = "phase_s";
        public const string KeyWarmup  = "warmup_s";
        public const string KeySkin    = "skin";

        private static readonly string[] KnownKeys = { KeyMode, KeyPuppets, KeyClutter, KeyPhase, KeyWarmup, KeySkin };

        private static readonly IReadOnlyDictionary<string, string> NoArgs = new Dictionary<string, string>();

        /// <summary>Имя режима в параметре <c>mode</c>.</summary>
        public static string ModeName(PerfRunMode mode)
        {
            switch (mode)
            {
                case PerfRunMode.Short:   return "short";
                case PerfRunMode.MapOnly: return "map-only";
                case PerfRunMode.PerSkin: return "per-skin";
                default:                  return "normal";
            }
        }

        /// <summary>
        /// Разбирает параметры. <paramref name="skinCount"/> — число скинов кукол
        /// (<see cref="StressTestServer.CollectAvatarPrefabs"/>): по нему проверяется <c>skin</c>.
        /// </summary>
        /// <returns>null — разобрано; иначе текст ошибки, <paramref name="config"/> = null.</returns>
        public static string Parse(IReadOnlyDictionary<string, string> args, int skinCount,
                                   out PerfRunMode mode, out StressTestConfig config)
        {
            mode = PerfRunMode.Standard;
            config = null;
            args = args ?? NoArgs;

            foreach (string key in args.Keys)
            {
                if (Array.IndexOf(KnownKeys, key) < 0)
                    return $"неизвестный параметр «{key}»; допустимые: {string.Join(", ", KnownKeys)}";
            }

            if (args.TryGetValue(KeyMode, out string modeText) && !TryParseMode(modeText, out mode))
                return $"{KeyMode}=«{modeText}»: допустимо normal, short, map-only, per-skin";

            var defaults = new StressTestConfig();
            int   puppets = defaults.puppetCount;
            int   clutter = defaults.clutterCount;
            float phase   = defaults.phaseSeconds;
            float warmup  = defaults.warmupSeconds;
            int   skin    = defaults.puppetSkin;

            string error = ReadInt(args, KeyPuppets, 0, StressTestConfig.MaxPuppets, ref puppets)
                        ?? ReadInt(args, KeyClutter, 0, int.MaxValue, ref clutter)
                        ?? ReadPositive(args, KeyPhase, ref phase)
                        ?? ReadPositive(args, KeyWarmup, ref warmup)
                        ?? ReadInt(args, KeySkin, StressTestLayout.MixedSkins, int.MaxValue, ref skin)
                        ?? CheckApplicable(args, mode);
            if (error != null) return error;

            if (skin != StressTestLayout.MixedSkins && skin >= skinCount)
            {
                return skinCount > 0
                    ? $"{KeySkin}={skin}: скинов {skinCount}, допустимо {StressTestLayout.MixedSkins} (вперемешку) или 0..{skinCount - 1}"
                    : $"{KeySkin}={skin}: в TeamRegistry нет скинов, допустимо только {StressTestLayout.MixedSkins} (вперемешку)";
            }

            config = StressTestPlan.BuildConfig(mode, skin, puppets);
            // BuildConfig держит минимум планшета (1 кукла); здесь 0 допустим — нагрузка без кукол.
            config.puppetCount  = puppets;
            config.clutterCount = clutter;
            if (args.ContainsKey(KeyPhase))  config.phaseSeconds  = phase;
            if (args.ContainsKey(KeyWarmup)) config.warmupSeconds = warmup;
            return null;
        }

        /// <summary>
        /// Помещается ли план в отведённое время. <paramref name="planSeconds"/> — сумма фаз
        /// (<see cref="StressTestPlan.TotalSeconds"/>), <paramref name="reserveSeconds"/> — запас на подключение и конец.
        /// </summary>
        /// <returns>null — помещается; иначе текст ошибки.</returns>
        public static string TimeoutError(float planSeconds, float reserveSeconds, float availableSeconds)
        {
            float need = planSeconds + reserveSeconds;
            if (!float.IsNaN(need) && need <= availableSeconds) return null;
            return string.Format(CultureInfo.InvariantCulture,
                "план прогона {0:F0} с + запас {1:F0} с = {2:F0} с не помещается в доступные сценарию {3:F0} с",
                planSeconds, reserveSeconds, need, availableSeconds);
        }

        /// <summary>Условия прогона одной строкой: <c>mode=normal puppets=9 clutter=40 phase_s=40 warmup_s=5 skin=-1</c>.</summary>
        public static string Describe(PerfRunMode mode, StressTestConfig config)
        {
            if (config == null) return $"{KeyMode}={ModeName(mode)}";
            return string.Format(CultureInfo.InvariantCulture, "{0}={1} {2}={3} {4}={5} {6}={7:0.##} {8}={9:0.##} {10}={11}",
                KeyMode, ModeName(mode), KeyPuppets, config.puppetCount, KeyClutter, config.clutterCount,
                KeyPhase, config.phaseSeconds, KeyWarmup, config.warmupSeconds,
                KeySkin, mode == PerfRunMode.PerSkin ? "all" : config.puppetSkin.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryParseMode(string text, out PerfRunMode mode)
        {
            switch (text)
            {
                case "normal":   mode = PerfRunMode.Standard; return true;
                case "short":    mode = PerfRunMode.Short;    return true;
                case "map-only": mode = PerfRunMode.MapOnly;  return true;
                case "per-skin": mode = PerfRunMode.PerSkin;  return true;
                default:         mode = PerfRunMode.Standard; return false;
            }
        }

        /// <summary>
        /// Параметр, которого выбранный режим не использует, — ошибка: короткий прогон сам задаёт длительности,
        /// по скинам — скин и без хлама, «только по карте» — без хлама.
        /// </summary>
        private static string CheckApplicable(IReadOnlyDictionary<string, string> args, PerfRunMode mode)
        {
            if (mode == PerfRunMode.PerSkin && args.ContainsKey(KeySkin))
                return $"{KeySkin} не действует в {KeyMode}=per-skin: прогон идёт по всем скинам";
            if ((mode == PerfRunMode.PerSkin || mode == PerfRunMode.MapOnly) && args.ContainsKey(KeyClutter))
                return $"{KeyClutter} не действует в {KeyMode}={ModeName(mode)}: хлама в этом режиме нет";
            if (mode == PerfRunMode.Short && (args.ContainsKey(KeyPhase) || args.ContainsKey(KeyWarmup)))
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} и {1} не действуют в {2}=short: фазы по {3:0} с; другая длительность — {2}=normal {0}=N",
                    KeyPhase, KeyWarmup, KeyMode, StressTestPlan.ShortPhaseSeconds);
            return null;
        }

        private static string ReadInt(IReadOnlyDictionary<string, string> args, string key, int min, int max, ref int value)
        {
            if (!args.TryGetValue(key, out string text)) return null;
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
                return $"{key}=«{text}»: нужно целое число";
            if (parsed < min || parsed > max)
                return max == int.MaxValue ? $"{key}={parsed}: нужно не меньше {min}" : $"{key}={parsed}: допустимо {min}..{max}";
            value = parsed;
            return null;
        }

        private static string ReadPositive(IReadOnlyDictionary<string, string> args, string key, ref float value)
        {
            if (!args.TryGetValue(key, out string text)) return null;
            if (!float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed))
                return $"{key}=«{text}»: нужно число секунд (точка — разделитель дробной части)";
            if (parsed <= 0f)
                return $"{key}={text}: нужно больше 0";
            value = parsed;
            return null;
        }
    }
}
