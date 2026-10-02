using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.Economy;

namespace VrBattlegrounds.Tests.Economy
{
    /// <summary>
    /// Числа экономики CS2 (T-45): старт 800, потолок 16000, победа 3250, лестница поражений
    /// 1400…3400 с уменьшением счётчика после победы (MR12), 1900 за проигранный пистолетный раунд,
    /// награды за убийство. Сверка чисел — <c>Docs/tasks/T-45-economy.md</c>.
    /// </summary>
    public class EconomyRulesTests
    {
        [Test]
        public void Лестница_поражений_от_начала_половины()
        {
            // Начало половины — счётчик 1: пистолетный раунд проигравшему 1900, дальше +500 до 3400.
            int counter = EconomyRules.HalfStartLossCounter;
            var paid = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                RoundIncome income = EconomyRules.RoundEnd(false, counter);
                paid.Add(income.Reward);
                counter = income.LossCounter;
            }

            CollectionAssert.AreEqual(new[] { 1900, 2400, 2900, 3400, 3400, 3400 }, paid);
        }

        [Test]
        public void Первое_поражение_после_победы_в_начале_половины_1400()
        {
            int counter = EconomyRules.RoundEnd(true, EconomyRules.HalfStartLossCounter).LossCounter;
            Assert.AreEqual(0, counter, "Победа в пистолетном раунде опускает счётчик с 1 до 0.");
            Assert.AreEqual(1400, EconomyRules.RoundEnd(false, counter).Reward);
        }

        [Test]
        public void Победа_не_сбрасывает_лестницу_а_опускает_на_ступень()
        {
            // CS2 MR12: пять поражений подряд, победа, поражение — 2900, а не 1400.
            int counter = 0;
            for (int i = 0; i < 5; i++) counter = EconomyRules.RoundEnd(false, counter).LossCounter;

            RoundIncome win = EconomyRules.RoundEnd(true, counter);
            Assert.AreEqual(EconomyRules.RoundWinReward, win.Reward);
            Assert.AreEqual(3250, win.Reward);

            Assert.AreEqual(2900, EconomyRules.RoundEnd(false, win.LossCounter).Reward);
        }

        [Test]
        public void Деньги_не_выше_потолка_и_не_ниже_нуля()
        {
            Assert.AreEqual(800, EconomyRules.StartMoney);
            Assert.AreEqual(16000, EconomyRules.Apply(15000, 3250));
            Assert.AreEqual(0, EconomyRules.Apply(100, -300));
            Assert.AreEqual(1100, EconomyRules.Apply(800, 300));
        }

        [Test]
        public void Награда_за_убийство_и_штраф_за_союзника()
        {
            Assert.AreEqual(600, EconomyRules.KillReward(600, false), "kill_award оружия (ПП в CS2 — 600).");
            Assert.AreEqual(-300, EconomyRules.KillReward(600, true), "Убийство союзника — штраф 300.");
            Assert.AreEqual(300, EconomyRules.DefaultKillAward);
        }

        [Test]
        public void Хватает_ли_денег()
        {
            Assert.IsTrue(EconomyRules.CanAfford(800, 800));
            Assert.IsFalse(EconomyRules.CanAfford(799, 800));
        }
    }
}
