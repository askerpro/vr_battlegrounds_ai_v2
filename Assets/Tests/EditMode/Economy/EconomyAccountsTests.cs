using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.Economy;

namespace VrBattlegrounds.Tests.Economy
{
    /// <summary>
    /// Счета матча (T-45): покупка списывает только при достатке, возврат — только по чеку этого раунда,
    /// начало половины — всем 800 и счётчик 1, итог раунда платит каждому игроку команды.
    /// </summary>
    public class EconomyAccountsTests
    {
        private EconomyAccounts _accounts;

        [SetUp]
        public void SetUp()
        {
            _accounts = new EconomyAccounts();
            _accounts.ResetForHalf(new[] { "a", "b" }, new[] { 1, 2 });
        }

        [Test]
        public void Начало_половины_даёт_800_и_счётчик_1()
        {
            _accounts.Add("a", 5000);
            _accounts.ResetForHalf(new[] { "a", "b" }, new[] { 1, 2 });

            Assert.AreEqual(800, _accounts.GetMoney("a"));
            Assert.AreEqual(800, _accounts.GetMoney("b"));
            Assert.AreEqual(1, _accounts.GetLossCounter(1));
        }

        [Test]
        public void Покупка_не_в_долг()
        {
            Assert.IsFalse(_accounts.TryPurchase("a", 2900, 7), "У игрока 800 — TR15 за 2900 не купить.");
            Assert.AreEqual(800, _accounts.GetMoney("a"), "Неудачная покупка не меняет денег.");
            Assert.IsFalse(_accounts.HasReceipt(7));

            Assert.IsTrue(_accounts.TryPurchase("a", 700, 8));
            Assert.AreEqual(100, _accounts.GetMoney("a"));
        }

        [Test]
        public void Возврат_только_по_чеку_и_один_раз()
        {
            _accounts.TryPurchase("a", 700, 8);

            Assert.IsTrue(_accounts.TryRefund(8, out EconomyAccounts.Receipt receipt));
            Assert.AreEqual("a", receipt.Buyer);
            Assert.AreEqual(800, _accounts.GetMoney("a"));

            Assert.IsFalse(_accounts.TryRefund(8, out _), "Повторный возврат — деньги из воздуха.");
            Assert.IsFalse(_accounts.TryRefund(99, out _), "Бесплатный ствол (без чека) денег не возвращает.");
        }

        [Test]
        public void Чеки_живут_один_раунд()
        {
            _accounts.TryPurchase("a", 700, 8);
            _accounts.ClearReceipts();
            Assert.IsFalse(_accounts.TryRefund(8, out _));
        }

        [Test]
        public void Итог_раунда_платит_каждому_игроку_команды()
        {
            _accounts.EnsureAccount("c");
            int win = _accounts.ApplyRoundEnd(1, true, new[] { "a", "c" });
            int loss = _accounts.ApplyRoundEnd(2, false, new[] { "b" });

            Assert.AreEqual(3250, win);
            Assert.AreEqual(1900, loss, "Пистолетный раунд проигравшим — 1900.");
            Assert.AreEqual(800 + 3250, _accounts.GetMoney("a"));
            Assert.AreEqual(800 + 3250, _accounts.GetMoney("c"));
            Assert.AreEqual(800 + 1900, _accounts.GetMoney("b"));
            Assert.AreEqual(0, _accounts.GetLossCounter(1));
            Assert.AreEqual(2, _accounts.GetLossCounter(2));
        }

        [Test]
        public void Снимок_для_паузы_возвращает_деньги_и_счётчики()
        {
            _accounts.ApplyRoundEnd(2, false, new[] { "b" });
            var money = new Dictionary<string, int>();
            var counters = new Dictionary<int, int>();
            _accounts.CopyTo(money, counters);

            var restored = new EconomyAccounts();
            restored.Restore(money, counters);

            Assert.AreEqual(2700, restored.GetMoney("b"));
            Assert.AreEqual(2, restored.GetLossCounter(2));
        }
    }
}
