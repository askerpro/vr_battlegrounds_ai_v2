using NUnit.Framework;
using VrBattlegrounds.Bots;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>
    /// Приказы бота по фазам раунда (T-48): закупка на базе, бой, возврат на базу, призрак идёт оживать.
    /// </summary>
    public class BotOrdersTests
    {
        private static BotSituation S(BotStage stage, bool alive = true, bool home = false, bool known = false, bool visible = false) =>
            new BotSituation { Stage = stage, Alive = alive, InOwnZone = home, EnemyKnown = known, EnemyVisible = visible };

        [Test]
        public void Без_матча_бот_стоит()
        {
            Assert.AreEqual(BotOrder.Idle, BotOrders.Decide(S(BotStage.NoMatch)));
            Assert.AreEqual(BotOrder.Idle, BotOrders.Decide(S(BotStage.NoMatch, alive: false)));
        }

        [Test]
        public void Закупка_вне_базы_идёт_домой_на_базе_готовится()
        {
            Assert.AreEqual(BotOrder.GoHome, BotOrders.Decide(S(BotStage.Equipment, home: false)));
            Assert.AreEqual(BotOrder.PrepareAtBase, BotOrders.Decide(S(BotStage.Equipment, home: true)));
        }

        [Test]
        public void Отсчёт_держит_базу()
        {
            Assert.AreEqual(BotOrder.HoldAtBase, BotOrders.Decide(S(BotStage.Countdown, home: true)));
            Assert.AreEqual(BotOrder.GoHome, BotOrders.Decide(S(BotStage.Countdown, home: false)));
        }

        [Test]
        public void Бой_видит_стреляет_не_видит_ищет()
        {
            Assert.AreEqual(BotOrder.Engage, BotOrders.Decide(S(BotStage.Combat, home: true, known: true, visible: true)));
            Assert.AreEqual(BotOrder.Hunt, BotOrders.Decide(S(BotStage.Combat, home: true, known: true)));
            Assert.AreEqual(BotOrder.HoldAtBase, BotOrders.Decide(S(BotStage.Combat, known: false)));
        }

        [Test]
        public void После_раунда_возвращается_на_базу()
        {
            Assert.AreEqual(BotOrder.GoHome, BotOrders.Decide(S(BotStage.BetweenRounds, home: false)));
            Assert.AreEqual(BotOrder.HoldAtBase, BotOrders.Decide(S(BotStage.BetweenRounds, home: true)));
        }

        [Test]
        public void Призрак_в_любой_фазе_идёт_оживать_в_свою_зону()
        {
            foreach (BotStage stage in new[] { BotStage.BetweenRounds, BotStage.Equipment, BotStage.Countdown, BotStage.Combat })
            {
                Assert.AreEqual(BotOrder.GoHome, BotOrders.Decide(S(stage, alive: false, known: true, visible: true)), stage.ToString());
                Assert.AreEqual(BotOrder.HoldAtBase, BotOrders.Decide(S(stage, alive: false, home: true)), stage.ToString());
            }
        }

        [Test]
        public void Стадия_по_режиму_и_фазе()
        {
            Assert.AreEqual(BotStage.NoMatch, BotOrders.StageOf(false, false, true, true, RoundPhase.Combat));
            Assert.AreEqual(BotStage.NoMatch, BotOrders.StageOf(true, true, false, false, RoundPhase.Setup));
            Assert.AreEqual(BotStage.NoMatch, BotOrders.StageOf(true, false, true, false, RoundPhase.Combat), "матч ждёт игроков");
            Assert.AreEqual(BotStage.Combat, BotOrders.StageOf(true, false, false, false, RoundPhase.Setup), "режим без раундов — бой");
            Assert.AreEqual(BotStage.BetweenRounds, BotOrders.StageOf(true, false, true, true, RoundPhase.Setup));
            Assert.AreEqual(BotStage.Equipment, BotOrders.StageOf(true, false, true, true, RoundPhase.Equipment));
            Assert.AreEqual(BotStage.Countdown, BotOrders.StageOf(true, false, true, true, RoundPhase.Countdown));
            Assert.AreEqual(BotStage.Combat, BotOrders.StageOf(true, false, true, true, RoundPhase.Combat));
            Assert.AreEqual(BotStage.BetweenRounds, BotOrders.StageOf(true, false, true, true, RoundPhase.Resolution));
            Assert.AreEqual(BotStage.BetweenRounds, BotOrders.StageOf(true, false, true, true, RoundPhase.Scoreboard));
        }

        [Test]
        public void Покупка_раз_за_раунд_только_на_базе_живым()
        {
            Assert.IsTrue(BotOrders.ShouldShop(BotOrder.PrepareAtBase, true, false));
            Assert.IsFalse(BotOrders.ShouldShop(BotOrder.PrepareAtBase, true, true), "уже покупал");
            Assert.IsFalse(BotOrders.ShouldShop(BotOrder.PrepareAtBase, false, false), "призрак");
            Assert.IsFalse(BotOrders.ShouldShop(BotOrder.GoHome, true, false), "не на базе");
            Assert.IsFalse(BotOrders.ShouldShop(BotOrder.Hunt, true, false), "бой — время закупки вышло");
            Assert.IsTrue(BotOrders.ShouldShop(BotOrder.Hunt, true, false, free: true), "без экономики (Respawn) — сразу");
            Assert.IsFalse(BotOrders.ShouldShop(BotOrder.Idle, true, false, free: true), "без матча — нет");
        }

        [Test]
        public void Оружие_в_руку_после_покупки_или_с_отсчёта()
        {
            Assert.IsFalse(BotOrders.ShouldArm(BotStage.Equipment, true, false), "в закупке ждём решения о покупке");
            Assert.IsTrue(BotOrders.ShouldArm(BotStage.Equipment, true, true));
            Assert.IsTrue(BotOrders.ShouldArm(BotStage.Countdown, true, false), "не дошёл до базы — пистолет");
            Assert.IsTrue(BotOrders.ShouldArm(BotStage.Combat, true, false));
            Assert.IsFalse(BotOrders.ShouldArm(BotStage.Combat, false, true), "призрак");
            Assert.IsFalse(BotOrders.ShouldArm(BotStage.BetweenRounds, true, true), "снаряжение изымается на итоге раунда");
            Assert.IsFalse(BotOrders.ShouldArm(BotStage.NoMatch, true, true));
        }

        [Test]
        public void Готовность_на_базе_после_покупки()
        {
            Assert.IsTrue(BotOrders.ShouldDeclareReady(BotOrder.PrepareAtBase, true, true, true, false));
            Assert.IsFalse(BotOrders.ShouldDeclareReady(BotOrder.PrepareAtBase, true, true, false, false), "покупка не решена");
            Assert.IsFalse(BotOrders.ShouldDeclareReady(BotOrder.PrepareAtBase, true, false, true, false), "вне зоны готовность снимется");
            Assert.IsFalse(BotOrders.ShouldDeclareReady(BotOrder.GoHome, true, false, true, false));
            Assert.IsFalse(BotOrders.ShouldDeclareReady(BotOrder.PrepareAtBase, false, true, true, false), "призрак");
            Assert.IsFalse(BotOrders.ShouldDeclareReady(BotOrder.PrepareAtBase, true, true, true, true), "уже готов");
        }
    }
}
