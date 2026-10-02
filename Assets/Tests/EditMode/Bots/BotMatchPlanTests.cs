using NUnit.Framework;
using VrBattlegrounds.Bots;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>«Матч с ботами» без админки (T-48): кто вправе, сколько ботов, какой режим, кнопка на «Обзоре».</summary>
    public class BotMatchPlanTests
    {
        [Test]
        public void Один_игрок_вправе_без_админки()
        {
            Assert.IsTrue(BotMatchPlan.CanRequest(isAdmin: false, isPlayer: true, otherHumans: 0));
            Assert.IsFalse(BotMatchPlan.CanRequest(false, true, 1), "есть другие люди — решает админ");
            Assert.IsFalse(BotMatchPlan.CanRequest(false, false, 0), "зритель");
            Assert.IsTrue(BotMatchPlan.CanRequest(true, false, 3), "админ — всегда");
        }

        [Test]
        public void Состав_два_на_два_для_одного_и_полные_команды()
        {
            Assert.AreEqual(2, BotMatchPlan.TeamSize(1, 2));
            Assert.AreEqual(3, BotMatchPlan.BotsToAdd(humans: 1, bots: 0, teams: 2), "человек и три бота — два на два");
            Assert.AreEqual(1, BotMatchPlan.BotsToAdd(1, 2, 2), "существующие боты засчитываются");
            Assert.AreEqual(0, BotMatchPlan.BotsToAdd(1, 5, 2), "лишних не убирает");
            Assert.AreEqual(3, BotMatchPlan.TeamSize(5, 2));
            Assert.AreEqual(1, BotMatchPlan.BotsToAdd(5, 0, 2), "пять людей — три на три");
            Assert.AreEqual(BotMatchPlan.MaxTeamSize, BotMatchPlan.TeamSize(20, 2), "не больше стен в зоне");
            Assert.AreEqual(0, BotMatchPlan.BotsToAdd(1, 0, 0), "нет команд — некуда");
        }

        [Test]
        public void Ожидающий_запрос_не_теряется_если_матч_уже_идёт()
        {
            Assert.AreEqual(BotMatchPlan.PendingStep.Wait, BotMatchPlan.Step(true, 100f, 60f, false, false), "карта грузится — ждать");
            Assert.AreEqual(BotMatchPlan.PendingStep.GoLive, BotMatchPlan.Step(false, 5f, 60f, true, false), "разминка — начать");
            Assert.AreEqual(BotMatchPlan.PendingStep.Fill, BotMatchPlan.Step(false, 5f, 60f, false, true),
                            "матч начал другой (админ, автозапуск) — добрать ботов и раздать команды");
            Assert.AreEqual(BotMatchPlan.PendingStep.Fill, BotMatchPlan.Step(false, 100f, 60f, false, true),
                            "идущий матч важнее таймаута");
            Assert.AreEqual(BotMatchPlan.PendingStep.Wait, BotMatchPlan.Step(false, 5f, 60f, false, false));
            Assert.AreEqual(BotMatchPlan.PendingStep.Expire, BotMatchPlan.Step(false, 61f, 60f, false, false));
        }

        [Test]
        public void Режим_элиминация_иначе_первый()
        {
            Assert.AreEqual("elimination", BotMatchPlan.PickMode(new[] { "respawn", "elimination" }));
            Assert.AreEqual("respawn", BotMatchPlan.PickMode(new[] { "respawn" }));
            Assert.IsNull(BotMatchPlan.PickMode(new string[0]));
            Assert.IsNull(BotMatchPlan.PickMode(null));
        }

        [Test]
        public void Кнопка_главная_у_игрока_и_первая_в_ряду_у_админа()
        {
            var plan = new OverviewAdminPlan();
            OverviewBotMatch.Apply(plan, OverviewContext.Lobby, allowed: true);
            Assert.IsNotNull(plan.Primary);
            Assert.IsTrue(plan.Primary.BotMatch);
            Assert.AreEqual(OverviewBotMatch.Label, plan.Primary.Label);

            OverviewAdminPlan admin = OverviewAdminActions.Plan(OverviewContext.Warmup, false, c => true);
            OverviewAction primary = admin.Primary;
            OverviewBotMatch.Apply(admin, OverviewContext.Warmup, true);
            Assert.AreSame(primary, admin.Primary, "главное действие админа не отбирается");
            Assert.IsTrue(admin.Others[0].BotMatch);
        }

        [Test]
        public void Кнопки_нет_в_бою_офлайн_и_без_права()
        {
            foreach (OverviewContext context in new[] { OverviewContext.Offline, OverviewContext.Live, OverviewContext.Paused })
            {
                var plan = new OverviewAdminPlan();
                OverviewBotMatch.Apply(plan, context, true);
                Assert.IsNull(plan.Primary, context.ToString());
                Assert.IsEmpty(plan.Others, context.ToString());
            }

            var denied = new OverviewAdminPlan();
            OverviewBotMatch.Apply(denied, OverviewContext.Warmup, false);
            Assert.IsNull(denied.Primary);

            var finished = new OverviewAdminPlan();
            OverviewBotMatch.Apply(finished, OverviewContext.MapFinished, true);
            Assert.IsNotNull(finished.Primary, "итог карты — сыграть ещё");
        }
    }
}
