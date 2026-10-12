using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// План прогона для экрана «Перф-тесты»: конфиг из выбора режима, фазы по порядку сервера
    /// (<c>StressTestServer.Run</c>), длительность, перенос «только по карте» и потолка кукол
    /// через сетевое сообщение.
    /// </summary>
    public class StressTestPlanTests
    {
        private static string[] Names(StressTestConfig config, int skins) =>
            StressTestPlan.Phases(config, skins).Select(p => p.Name).ToArray();

        [Test]
        public void Standard_AllPhasesInServerOrder()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.Standard, 2, 9);
            Assert.IsFalse(config.perSkinPhases);
            Assert.IsFalse(config.mapOnly);
            Assert.AreEqual(2, config.puppetSkin);

            CollectionAssert.AreEqual(new[]
            {
                "разгон", "база",
                "куклы~успокоение", "куклы",
                "куклы+хлам~успокоение", "куклы+хлам",
                "куклы: по карте~успокоение", "куклы: по карте",
            }, Names(config, 3));

            // 5 + 4×40 + 3×4 = 177 с — «~3 мин» из perf-stress-test.md.
            Assert.AreEqual(177f, StressTestPlan.TotalSeconds(StressTestPlan.Phases(config, 3)), 0.01f);
        }

        [Test]
        public void PerSkin_PairPerSkin_IgnoresSkinChoice()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.PerSkin, 2, 9);
            Assert.IsTrue(config.perSkinPhases);
            Assert.AreEqual(StressTestLayout.MixedSkins, config.puppetSkin, "В прогоне по скинам выбор скина не действует.");
            Assert.AreEqual(2 + 2 * 4, StressTestPlan.Phases(config, 4).Count);
        }

        [Test]
        public void Short_TenSecondPhases()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.Short, StressTestLayout.MixedSkins, 9);
            Assert.AreEqual(StressTestPlan.ShortPhaseSeconds, config.phaseSeconds);
            Assert.AreEqual(8, StressTestPlan.Phases(config, 3).Count, "Короткий — те же фазы, что обычный.");
        }

        [Test]
        public void MapOnly_SingleLoadPhase()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.MapOnly, StressTestLayout.MixedSkins, 9);
            Assert.IsTrue(config.mapOnly);
            CollectionAssert.AreEqual(new[] { "разгон", "база", "куклы: по карте~успокоение", "куклы: по карте" }, Names(config, 3));
        }

        [Test]
        public void PuppetCount_ClampedOnClientAndServer()
        {
            Assert.AreEqual(StressTestConfig.MaxPuppets, StressTestPlan.BuildConfig(PerfRunMode.Standard, -1, 500).puppetCount);
            Assert.AreEqual(1, StressTestPlan.BuildConfig(PerfRunMode.Standard, -1, 0).puppetCount);

            var forged = new StressTestConfig { puppetCount = 500 };
            Assert.AreEqual(StressTestConfig.MaxPuppets, StressTestRequestMessage.Start(forged).ToConfig().puppetCount,
                "Сервер обрезает число кукол из запроса, что бы ни прислал клиент.");
        }

        [Test]
        public void MapOnly_SurvivesNetworkMessage()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.MapOnly, 1, 5);
            StressTestConfig back = StressTestRequestMessage.Start(config).ToConfig();
            Assert.IsTrue(back.mapOnly);
            Assert.AreEqual(5, back.puppetCount);
            Assert.AreEqual(1, back.puppetSkin);
        }

        [Test]
        public void Describe_NamesPhaseCountAndDuration()
        {
            StressTestConfig config = StressTestPlan.BuildConfig(PerfRunMode.Standard, -1, 9);
            string text = StressTestPlan.Describe(PerfRunMode.Standard, config, 3);
            StringAssert.Contains("Фаз 8", text);
            StringAssert.Contains("2 мин 57 с", text);
        }

        // ── Раскладка «арена» ───────────────────────────────────────────────

        private static StressTestConfig ArenaConfig(string mode)
        {
            var args = new System.Collections.Generic.Dictionary<string, string> { ["layout"] = "arena" };
            if (mode != null) args["mode"] = mode;
            Assert.IsNull(StressTestArgs.Parse(args, 3, out _, out StressTestConfig config));
            return config;
        }

        [Test]
        public void Arena_BaseThenSingleArenaPair()
        {
            StressTestConfig config = ArenaConfig("map-only");
            Assert.IsTrue(config.arenaLayout);
            Assert.AreEqual(0, config.clutterCount);
            CollectionAssert.AreEqual(new[]
            {
                "разгон", "база",
                StressTestPlan.ArenaPhaseName + "~успокоение", StressTestPlan.ArenaPhaseName,
            }, Names(config, 3));
        }

        [Test]
        public void Arena_NormalMode_SamePlan()
        {
            CollectionAssert.AreEqual(Names(ArenaConfig("map-only"), 3), Names(ArenaConfig(null), 3));
        }

        [Test]
        public void Arena_NoPuppets_OnlyBase()
        {
            StressTestConfig config = ArenaConfig("map-only");
            config.puppetCount = 0;
            CollectionAssert.AreEqual(new[] { "разгон", "база" }, Names(config, 3));
        }

        [Test]
        public void Arena_SurvivesNetworkMessage()
        {
            StressTestConfig back = StressTestRequestMessage.Start(ArenaConfig("map-only")).ToConfig();
            Assert.IsTrue(back.arenaLayout);
        }

        [TestCase("per-skin", null)]
        [TestCase("map-only", "5")]
        public void Arena_IncompatibleArgs_Refused(string mode, string clutter)
        {
            var args = new System.Collections.Generic.Dictionary<string, string> { ["layout"] = "arena", ["mode"] = mode };
            if (clutter != null) args["clutter"] = clutter;
            Assert.IsNotNull(StressTestArgs.Parse(args, 3, out _, out StressTestConfig config));
            Assert.IsNull(config);
        }

        [Test]
        public void Arena_UnknownLayout_Refused_DescribeShowsArena()
        {
            var args = new System.Collections.Generic.Dictionary<string, string> { ["layout"] = "rows" };
            Assert.IsNotNull(StressTestArgs.Parse(args, 3, out _, out _));
            StringAssert.Contains("layout=arena", StressTestArgs.Describe(PerfRunMode.MapOnly, ArenaConfig("map-only")));
        }
    }
}
