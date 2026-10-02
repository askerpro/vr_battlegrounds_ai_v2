using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Economy;

namespace VrBattlegrounds.Tests.Economy
{
    /// <summary>
    /// Правила стены при экономике (T-45): доступность слота по деньгам владельца, «только своя стена»,
    /// серверное решение о захвате; раздача стен игрокам по зонам.
    /// </summary>
    public class ArsenalPurchaseRulesTests
    {
        // ── Доступность слота ─────────────────────────────────

        [Test]
        public void Без_экономики_всё_бесплатно_и_любому()
        {
            Assert.AreEqual(SlotOffer.Free, ArsenalPurchaseRules.Evaluate(false, false, 0, 5000));
            Assert.IsTrue(ArsenalPurchaseRules.MayTake(false, 0, 42, 0, 5000));
            Assert.AreEqual(CheckoutVerdict.Free, ArsenalPurchaseRules.Checkout(false, 0, 42, 0, 5000));
        }

        [Test]
        public void Слот_подсвечен_по_деньгам_владельца()
        {
            Assert.AreEqual(SlotOffer.Affordable, ArsenalPurchaseRules.Evaluate(true, true, 800, 700));
            Assert.AreEqual(SlotOffer.TooExpensive, ArsenalPurchaseRules.Evaluate(true, true, 800, 2900));
            Assert.AreEqual(SlotOffer.NoOwner, ArsenalPurchaseRules.Evaluate(true, false, 16000, 200));

            Assert.IsTrue(ArsenalPurchaseRules.AllowsGrab(SlotOffer.Affordable));
            Assert.IsFalse(ArsenalPurchaseRules.AllowsGrab(SlotOffer.TooExpensive));
            Assert.IsFalse(ArsenalPurchaseRules.AllowsGrab(SlotOffer.NoOwner));
        }

        [Test]
        public void Чужая_стена_не_покупается()
        {
            Assert.IsTrue(ArsenalPurchaseRules.MayTake(true, 5, 5, 3000, 2900));
            Assert.IsFalse(ArsenalPurchaseRules.MayTake(true, 5, 6, 3000, 2900), "Рука не владельца.");
            Assert.IsFalse(ArsenalPurchaseRules.MayTake(true, 0, 6, 3000, 2900), "Ничья стена.");
            Assert.IsFalse(ArsenalPurchaseRules.MayTake(true, 5, 5, 2000, 2900), "Не хватает денег.");
        }

        [Test]
        public void Сервер_отменяет_захват_не_владельцем_и_в_долг()
        {
            Assert.AreEqual(CheckoutVerdict.Charge, ArsenalPurchaseRules.Checkout(true, 5, 5, 3000, 2900));
            Assert.AreEqual(CheckoutVerdict.Reject, ArsenalPurchaseRules.Checkout(true, 5, 6, 3000, 2900));
            Assert.AreEqual(CheckoutVerdict.Reject, ArsenalPurchaseRules.Checkout(true, 5, 5, 100, 2900));
            Assert.AreEqual(CheckoutVerdict.Charge, ArsenalPurchaseRules.Checkout(true, 5, 0, 3000, 2900),
                "Рука неизвестна — платит владелец, деньги проверяются.");
        }

        // ── Стена ↔ игрок ─────────────────────────────────────

        private static ArsenalOwnership.Wall W(uint id, int team, uint owner = 0) => new ArsenalOwnership.Wall(id, team, owner);
        private static ArsenalOwnership.Player P(uint session, int team) => new ArsenalOwnership.Player(session, team);

        [Test]
        public void Стены_зоны_раздаются_игрокам_её_команды_по_одной()
        {
            var walls = new[] { W(10, 1), W(11, 1), W(20, 2), W(21, 2) };
            var players = new[] { P(100, 1), P(101, 1), P(200, 2) };

            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(walls, players);

            Assert.AreEqual(100u, owners[10]);
            Assert.AreEqual(101u, owners[11]);
            Assert.AreEqual(200u, owners[20]);
            Assert.AreEqual(0u, owners[21], "Игроков команды меньше, чем стен, — лишняя стена ничья.");
        }

        [Test]
        public void Владелец_сохраняет_свою_стену()
        {
            var walls = new[] { W(10, 1), W(11, 1, 100) };
            var players = new[] { P(100, 1), P(101, 1) };

            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(walls, players);

            Assert.AreEqual(100u, owners[11], "Новый раунд/переподключение не пересаживает игрока.");
            Assert.AreEqual(101u, owners[10]);
        }

        [Test]
        public void После_смены_сторон_стены_уходят_другой_команде()
        {
            // Стены 10/11 были у команды 1, зона теперь команды 2.
            var walls = new[] { W(10, 2, 100), W(11, 2, 101), W(20, 1, 200) };
            var players = new[] { P(100, 1), P(101, 1), P(200, 2) };

            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(walls, players);

            Assert.AreEqual(200u, owners[10]);
            Assert.AreEqual(0u, owners[11]);
            Assert.AreEqual(100u, owners[20]);
        }

        [Test]
        public void Стена_вне_командной_зоны_ничья()
        {
            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(new[] { W(10, -1, 100) }, new[] { P(100, 1) });
            Assert.AreEqual(0u, owners[10]);
        }

        [Test]
        public void Игроков_больше_чем_стен_лишний_без_стены()
        {
            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(new[] { W(10, 1) }, new[] { P(101, 1), P(100, 1) });
            Assert.AreEqual(100u, owners[10], "Порядок по id сессии, а не по порядку обхода.");
        }
    }
}
