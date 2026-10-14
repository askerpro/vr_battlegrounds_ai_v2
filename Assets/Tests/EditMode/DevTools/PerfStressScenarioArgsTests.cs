using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Разбор параметров e2e-сценария <c>perf-stress</c> (<see cref="StressTestArgs"/>): умолчания, явные значения
    /// поверх конфига планшета, отказ до старта на любой ошибке — без молчаливого отката на умолчание.
    /// </summary>
    public class PerfStressScenarioArgsTests
    {
        private const int Skins = 3;

        private static Dictionary<string, string> Args(params string[] pairs)
        {
            var args = new Dictionary<string, string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) args[pairs[i]] = pairs[i + 1];
            return args;
        }

        private static StressTestConfig ParseOk(Dictionary<string, string> args, out PerfRunMode mode)
        {
            string error = StressTestArgs.Parse(args, Skins, out mode, out StressTestConfig config);
            Assert.IsNull(error, error);
            Assert.IsNotNull(config);
            return config;
        }

        private static string ParseError(Dictionary<string, string> args)
        {
            string error = StressTestArgs.Parse(args, Skins, out _, out StressTestConfig config);
            Assert.IsNotNull(error, "ожидался отказ до старта");
            Assert.IsNull(config, "при отказе конфига нет");
            return error;
        }

        [Test]
        public void NoArgs_StandardWithConfigDefaults()
        {
            StressTestConfig config = ParseOk(Args(), out PerfRunMode mode);
            var defaults = new StressTestConfig();

            Assert.AreEqual(PerfRunMode.Standard, mode);
            Assert.AreEqual(defaults.puppetCount, config.puppetCount);
            Assert.AreEqual(defaults.clutterCount, config.clutterCount);
            Assert.AreEqual(defaults.phaseSeconds, config.phaseSeconds);
            Assert.AreEqual(defaults.warmupSeconds, config.warmupSeconds);
            Assert.AreEqual(StressTestLayout.MixedSkins, config.puppetSkin);
            Assert.IsFalse(config.perSkinPhases);
            Assert.IsFalse(config.mapOnly);
        }

        [Test]
        public void NullArgs_SameAsEmpty()
        {
            string error = StressTestArgs.Parse(null, Skins, out PerfRunMode mode, out StressTestConfig config);
            Assert.IsNull(error);
            Assert.AreEqual(PerfRunMode.Standard, mode);
            Assert.AreEqual(new StressTestConfig().puppetCount, config.puppetCount);
        }

        [Test]
        public void ExplicitValues_OverConfig()
        {
            StressTestConfig config = ParseOk(Args("mode", "normal", "puppets", "5", "clutter", "10",
                                                   "phase_s", "12.5", "warmup_s", "3", "skin", "2"), out PerfRunMode mode);

            Assert.AreEqual(PerfRunMode.Standard, mode);
            Assert.AreEqual(5, config.puppetCount);
            Assert.AreEqual(10, config.clutterCount);
            Assert.AreEqual(12.5f, config.phaseSeconds, 1e-4f);
            Assert.AreEqual(3f, config.warmupSeconds, 1e-4f);
            Assert.AreEqual(2, config.puppetSkin);
        }

        [Test]
        public void ZeroPuppets_Allowed()
        {
            // Планшет держит минимум одну куклу; сценарию 0 нужен для нагрузки без кукол.
            Assert.AreEqual(0, ParseOk(Args("puppets", "0"), out _).puppetCount);
        }

        [Test]
        public void Modes_MapToPlanModes()
        {
            Assert.AreEqual(PerfRunMode.Short, Mode("short"));
            Assert.AreEqual(PerfRunMode.MapOnly, Mode("map-only"));
            Assert.AreEqual(PerfRunMode.PerSkin, Mode("per-skin"));
            Assert.IsTrue(ParseOk(Args("mode", "per-skin"), out _).perSkinPhases);
            Assert.IsTrue(ParseOk(Args("mode", "map-only"), out _).mapOnly);
            Assert.AreEqual(StressTestPlan.ShortPhaseSeconds, ParseOk(Args("mode", "short"), out _).phaseSeconds, 1e-4f);
        }

        private static PerfRunMode Mode(string text)
        {
            ParseOk(Args("mode", text), out PerfRunMode mode);
            return mode;
        }

        [Test]
        public void UnknownKey_Refused()
        {
            StringAssert.Contains("puppet", ParseError(Args("puppet", "9")));
        }

        [Test]
        public void UnknownMode_Refused()
        {
            StringAssert.Contains("mode", ParseError(Args("mode", "fast")));
        }

        [TestCase("puppets", "21")]
        [TestCase("puppets", "-1")]
        [TestCase("puppets", "nine")]
        [TestCase("clutter", "-1")]
        [TestCase("phase_s", "0")]
        [TestCase("phase_s", "1,5")]
        [TestCase("warmup_s", "-2")]
        [TestCase("skin", "-2")]
        public void InvalidValue_Refused(string key, string value)
        {
            StringAssert.Contains(key, ParseError(Args(key, value)));
        }

        [Test]
        public void Skin_CheckedAgainstSkinCount()
        {
            Assert.AreEqual(Skins - 1, ParseOk(Args("skin", (Skins - 1).ToString()), out _).puppetSkin);
            ParseError(Args("skin", Skins.ToString()));

            string error = StressTestArgs.Parse(Args("skin", "0"), 0, out _, out StressTestConfig config);
            Assert.IsNotNull(error, "без скинов в реестре допустимо только «вперемешку»");
            Assert.IsNull(config);
        }

        [TestCase("per-skin", "skin", "1")]
        [TestCase("per-skin", "clutter", "5")]
        [TestCase("map-only", "clutter", "5")]
        [TestCase("short", "phase_s", "10")]
        [TestCase("short", "warmup_s", "2")]
        public void ParameterNotUsedByMode_Refused(string mode, string key, string value)
        {
            StringAssert.Contains(key, ParseError(Args("mode", mode, key, value)));
        }

        [Test]
        public void TimeoutError_NullWhenPlanFits()
        {
            Assert.IsNull(StressTestArgs.TimeoutError(177f, 105f, 360f));
            Assert.IsNull(StressTestArgs.TimeoutError(177f, 105f, 282f));
        }

        [Test]
        public void TimeoutError_TextWhenPlanDoesNotFit()
        {
            Assert.IsNotNull(StressTestArgs.TimeoutError(177f, 105f, 260f));
            Assert.IsNotNull(StressTestArgs.TimeoutError(float.NaN, 105f, 360f));
        }

        [Test]
        public void Describe_ListsRunConditions()
        {
            StressTestConfig config = ParseOk(Args(), out PerfRunMode mode);
            Assert.AreEqual("mode=normal puppets=9 clutter=40 phase_s=40 warmup_s=5 skin=-1",
                StressTestArgs.Describe(mode, config));

            StressTestConfig perSkin = ParseOk(Args("mode", "per-skin"), out PerfRunMode perSkinMode);
            StringAssert.Contains("skin=all", StressTestArgs.Describe(perSkinMode, perSkin));
        }

        // ── Обход точек обзора (views) ──────────────────────────────────────

        [Test]
        public void Views_TurnsOnArena_ShortPhasesByDefault()
        {
            StressTestConfig config = ParseOk(Args("mode", "map-only", "views", "lobby-views.json"), out _);
            Assert.IsTrue(config.arenaLayout, "обход ставит кукол на арену");
            Assert.AreEqual(StressTestPlan.ViewTourPhaseSeconds, config.phaseSeconds);
            Assert.IsFalse(config.HasViewTour, "имена точек кладёт сценарий после чтения файла, не разбор");
        }

        [Test]
        public void Views_ExplicitPhase_Wins()
        {
            StressTestConfig config = ParseOk(Args("views", "v.json", "phase_s", "30"), out _);
            Assert.AreEqual(30f, config.phaseSeconds);
        }

        [Test]
        public void Views_ShortMode_KeepsShortPhases()
        {
            StressTestConfig config = ParseOk(Args("mode", "short", "views", "v.json"), out _);
            Assert.AreEqual(StressTestPlan.ShortPhaseSeconds, config.phaseSeconds);
            Assert.IsTrue(config.arenaLayout);
        }

        [Test]
        public void Views_WithLayoutArena_Accepted()
        {
            Assert.IsTrue(ParseOk(Args("views", "v.json", "layout", "arena"), out _).arenaLayout);
        }

        [TestCase("views", " ")]
        [TestCase("views", "v.json", "layout", "default")]
        [TestCase("views", "v.json", "mode", "per-skin")]
        [TestCase("views", "v.json", "clutter", "5")]
        public void Views_Incompatible_Refused(params string[] pairs)
        {
            StringAssert.Contains("views", ParseError(Args(pairs)));
        }

        [Test]
        public void Views_DescribeShowsViewCount()
        {
            StressTestConfig config = ParseOk(Args("views", "v.json"), out PerfRunMode mode);
            StringAssert.DoesNotContain("views=", StressTestArgs.Describe(mode, config));
            config.viewNames = new[] { "a", "b", "c" };
            string text = StressTestArgs.Describe(mode, config);
            StringAssert.Contains("layout=arena", text);
            StringAssert.Contains("views=3", text);
        }
    }
}
