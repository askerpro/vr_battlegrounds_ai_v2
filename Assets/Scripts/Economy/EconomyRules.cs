using UnityEngine;

namespace VrBattlegrounds.Economy
{
    /// <summary>
    /// Правила денег раунда как в Counter-Strike 2 (MR12) — чистые функции без состояния.
    /// Единственный источник чисел экономики: <see cref="EconomyAccounts"/> и всё, что над ним,
    /// считают только через этот класс. Сверка с CS2 и отличия — <c>Docs/tasks/T-45-economy.md</c>.
    ///
    /// <para>
    /// <b>Лестница поражений.</b> У команды счётчик поражений 0…4. Поражение платит
    /// 1400 + 500 × счётчик (1400 / 1900 / 2400 / 2900 / 3400) и поднимает счётчик на 1 (не выше 4).
    /// Победа опускает счётчик на 1, а не сбрасывает — так в CS2 MR12: после серии из пяти поражений
    /// победа, а за ней поражение дают 2900, а не 1400. Начало половины ставит счётчик в 1:
    /// проигравший пистолетный раунд получает 1900.
    /// </para>
    /// </summary>
    public static class EconomyRules
    {
        /// <summary>Деньги в начале каждой половины (и нового матча на карте).</summary>
        public const int StartMoney = 800;

        /// <summary>Потолок денег: больше игрок не накопит, лишнее сгорает.</summary>
        public const int MaxMoney = 16000;

        /// <summary>Награда каждому игроку команды-победителя раунда (устранение, как в CS2).</summary>
        public const int RoundWinReward = 3250;

        /// <summary>Бонус за первое поражение подряд.</summary>
        public const int LossBonusBase = 1400;

        /// <summary>Прибавка бонуса за каждое следующее поражение.</summary>
        public const int LossBonusStep = 500;

        /// <summary>Потолок счётчика поражений: 4 → 3400.</summary>
        public const int MaxLossCounter = 4;

        /// <summary>Счётчик поражений на старте половины: первое поражение даёт 1900.</summary>
        public const int HalfStartLossCounter = 1;

        /// <summary>Награда за убийство, если оружие убийцы неизвестно (стандарт CS2 для винтовок и пистолетов).</summary>
        public const int DefaultKillAward = 300;

        /// <summary>Штраф за убийство союзника (CS2).</summary>
        public const int TeamKillPenalty = -300;

        /// <summary>Бонус за поражение при счётчике <paramref name="lossCounter"/> до этого поражения (0…4).</summary>
        public static int LossBonus(int lossCounter) =>
            LossBonusBase + LossBonusStep * Mathf.Clamp(lossCounter, 0, MaxLossCounter);

        /// <summary>Счётчик после поражения.</summary>
        public static int CounterAfterLoss(int lossCounter) => Mathf.Clamp(lossCounter + 1, 0, MaxLossCounter);

        /// <summary>Счётчик после победы: на ступень вниз, не ниже нуля.</summary>
        public static int CounterAfterWin(int lossCounter) => Mathf.Clamp(lossCounter - 1, 0, MaxLossCounter);

        /// <summary>
        /// Итог раунда для одной команды: сколько получает каждый её игрок и каким становится счётчик.
        /// </summary>
        /// <param name="won">Команда выиграла раунд.</param>
        /// <param name="lossCounter">Счётчик поражений команды до раунда.</param>
        /// <remarks>
        /// Ничья (время вышло, никто не выбыл целиком) у нас бывает, в CS2 — нет (там время
        /// играет за защиту). Ничья — поражение для обеих команд: бонус и рост счётчика.
        /// </remarks>
        public static RoundIncome RoundEnd(bool won, int lossCounter)
        {
            if (won) return new RoundIncome(RoundWinReward, CounterAfterWin(lossCounter));

            return new RoundIncome(LossBonus(lossCounter), CounterAfterLoss(lossCounter));
        }

        /// <summary>Награда за убийство: <c>kill_award</c> оружия убийцы, за союзника — штраф.</summary>
        public static int KillReward(int killAward, bool teamKill) =>
            teamKill ? TeamKillPenalty : Mathf.Max(0, killAward);

        /// <summary>Деньги после изменения на <paramref name="delta"/>: от нуля до потолка.</summary>
        public static int Apply(int money, int delta)
        {
            long result = (long)money + delta;
            if (result < 0) return 0;
            return result > MaxMoney ? MaxMoney : (int)result;
        }

        /// <summary>Хватает ли денег на покупку.</summary>
        public static bool CanAfford(int money, int price) => price <= money;
    }

    /// <summary>Итог раунда для команды: выплата каждому игроку и новый счётчик поражений.</summary>
    public readonly struct RoundIncome
    {
        public readonly int Reward;
        public readonly int LossCounter;

        public RoundIncome(int reward, int lossCounter)
        {
            Reward = reward;
            LossCounter = lossCounter;
        }
    }
}
