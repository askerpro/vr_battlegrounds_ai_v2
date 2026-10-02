using NUnit.Framework;
using VrBattlegrounds.Bots;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>Закупка ботов по деньгам (T-48): эко / пистолетный / форс / полная, «лучшее по карману».</summary>
    public class BotBuyPlanTests
    {
        private const int Starter = 200;

        // Цены реестра оружия (T-38): Viper 200, PPK 200, Revolver 700, Uzi 1050 (пистолетный слот),
        // MP5K 1250, Nova 1050, SRM12 1700, Scar 2700, M16 2900, Mk14 5000.
        private static readonly BotShopItem[] Items =
        {
            new BotShopItem(200, false),  // 0 Viper
            new BotShopItem(200, false),  // 1 PPK
            new BotShopItem(700, false),  // 2 Revolver
            new BotShopItem(1050, false), // 3 Uzi
            new BotShopItem(1250, true),  // 4 MP5K
            new BotShopItem(1050, true),  // 5 Nova
            new BotShopItem(1700, true),  // 6 SRM12
            new BotShopItem(2700, true),  // 7 Scar
            new BotShopItem(2900, true),  // 8 M16
            new BotShopItem(5000, true),  // 9 Mk14
        };

        [Test]
        public void Тип_закупки()
        {
            Assert.AreEqual(BotBuyKind.Pistol, BotBuyPlan.Kind(800, pistolRound: true, mustWin: false));
            Assert.AreEqual(BotBuyKind.Pistol, BotBuyPlan.Kind(16000, pistolRound: true, mustWin: true), "пистолетный раунд — всегда пистолеты");
            Assert.AreEqual(BotBuyKind.Full, BotBuyPlan.Kind(BotBuyPlan.FullBuyMoney, false, false));
            Assert.AreEqual(BotBuyKind.Eco, BotBuyPlan.Kind(BotBuyPlan.FullBuyMoney - 1, false, false));
            Assert.AreEqual(BotBuyKind.Force, BotBuyPlan.Kind(1900, false, mustWin: true));
            Assert.AreEqual(BotBuyKind.Full, BotBuyPlan.Kind(4000, false, mustWin: true));
        }

        [Test]
        public void Нельзя_проиграть_последний_раунд_половины_и_матчбол_соперника()
        {
            Assert.IsTrue(BotBuyPlan.MustWin(round: 3, roundsPerHalf: 3, enemyScore: 0, roundsToWin: 4), "конец первой половины");
            Assert.IsTrue(BotBuyPlan.MustWin(6, 3, 0, 4), "последний раунд карты");
            Assert.IsTrue(BotBuyPlan.MustWin(5, 3, 3, 4), "у соперника матчбол");
            Assert.IsFalse(BotBuyPlan.MustWin(2, 3, 1, 4));
            Assert.IsFalse(BotBuyPlan.MustWin(4, 3, 2, 4));
        }

        [Test]
        public void Эко_ничего_не_покупает()
        {
            Assert.AreEqual(-1, BotBuyPlan.Choose(BotBuyKind.Eco, 2600, Items, Starter));
        }

        [Test]
        public void Пистолетный_раунд_лучший_пистолет_по_карману()
        {
            Assert.AreEqual(2, BotBuyPlan.Choose(BotBuyKind.Pistol, 800, Items, Starter), "800 — Revolver, не Uzi");
            Assert.AreEqual(3, BotBuyPlan.Choose(BotBuyKind.Pistol, 1100, Items, Starter));
            Assert.AreEqual(-1, BotBuyPlan.Choose(BotBuyKind.Pistol, 650, Items, Starter), "дороже стартового не по карману");
        }

        [Test]
        public void Полная_закупка_самое_дорогое_основное_по_карману()
        {
            Assert.AreEqual(8, BotBuyPlan.Choose(BotBuyKind.Full, 3000, Items, Starter));
            Assert.AreEqual(9, BotBuyPlan.Choose(BotBuyKind.Full, 16000, Items, Starter));
            Assert.AreEqual(7, BotBuyPlan.Choose(BotBuyKind.Full, 2700, Items, Starter));
        }

        [Test]
        public void Форс_основное_иначе_пистолет()
        {
            Assert.AreEqual(6, BotBuyPlan.Choose(BotBuyKind.Force, 1900, Items, Starter));
            Assert.AreEqual(2, BotBuyPlan.Choose(BotBuyKind.Force, 900, Items, Starter), "на основное не хватает — пистолет");
            Assert.AreEqual(-1, BotBuyPlan.Choose(BotBuyKind.Force, 100, Items, Starter));
        }

        [Test]
        public void Пустой_магазин_ничего()
        {
            Assert.AreEqual(-1, BotBuyPlan.Choose(BotBuyKind.Full, 16000, new BotShopItem[0], Starter));
            Assert.AreEqual(-1, BotBuyPlan.Choose(BotBuyKind.Full, 16000, null, Starter));
        }
    }
}
