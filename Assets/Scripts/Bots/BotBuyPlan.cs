using System.Collections.Generic;

namespace VrBattlegrounds.Bots
{
    /// <summary>Тип закупки в духе CS.</summary>
    public enum BotBuyKind
    {
        /// <summary>Эко: копим, воюем стартовым пистолетом.</summary>
        Eco,
        /// <summary>Пистолетный раунд (первый раунд половины): лучший пистолет по карману.</summary>
        Pistol,
        /// <summary>Форс: денег на полную нет, но раунд проигрывать нельзя — лучшее по карману.</summary>
        Force,
        /// <summary>Полная закупка: лучшее основное оружие по карману.</summary>
        Full
    }

    /// <summary>Позиция магазина для бота: цена и основное ли это оружие (не пистолет).</summary>
    public readonly struct BotShopItem
    {
        public readonly int Price;
        public readonly bool Primary;

        public BotShopItem(int price, bool primary)
        {
            Price = price;
            Primary = primary;
        }
    }

    /// <summary>
    /// Закупка бота по деньгам (T-48) — чистые правила, тесты <c>BotBuyPlanTests</c>. Логика CS упрощена до трёх
    /// развилок: пистолетный раунд — лучший пистолет; хватает на полноценную винтовку (<see cref="FullBuyMoney"/>) —
    /// лучшее основное оружие по карману; не хватает — эко (копим), кроме раунда, который нельзя проиграть (форс).
    /// «Лучшее» — самое дорогое из доступного: цена в CS2 и у нас (<c>WeaponInfo.Price</c>) растёт с силой оружия.
    /// </summary>
    public static class BotBuyPlan
    {
        /// <summary>С этой суммы закупка полная: хватает на винтовку уровня Scar/M16 (2700–2900).</summary>
        public const int FullBuyMoney = 2700;

        /// <summary>Тип закупки.</summary>
        /// <param name="money">Деньги бота.</param>
        /// <param name="pistolRound">Первый раунд половины.</param>
        /// <param name="mustWin">Раунд нельзя проиграть (см. <see cref="MustWin"/>).</param>
        public static BotBuyKind Kind(int money, bool pistolRound, bool mustWin)
        {
            if (pistolRound) return BotBuyKind.Pistol;
            if (money >= FullBuyMoney) return BotBuyKind.Full;
            return mustWin ? BotBuyKind.Force : BotBuyKind.Eco;
        }

        /// <summary>
        /// Раунд, который нельзя проиграть: последний раунд половины (дальше деньги сгорят сменой сторон или концом
        /// карты) или у соперника матчбол.
        /// </summary>
        public static bool MustWin(int round, int roundsPerHalf, int enemyScore, int roundsToWin) =>
            round == roundsPerHalf || round == roundsPerHalf * 2 || enemyScore >= roundsToWin - 1;

        /// <summary>
        /// Что купить: индекс в <paramref name="items"/> или -1 — ничего. Дешевле или равное
        /// <paramref name="starterPrice"/> не берётся: стартовый пистолет и так выдан бесплатно.
        /// </summary>
        public static int Choose(BotBuyKind kind, int money, IReadOnlyList<BotShopItem> items, int starterPrice)
        {
            switch (kind)
            {
                case BotBuyKind.Pistol:
                    return Best(items, money, starterPrice, primary: false);
                case BotBuyKind.Full:
                case BotBuyKind.Force:
                    int best = Best(items, money, starterPrice, primary: true);
                    return best >= 0 ? best : Best(items, money, starterPrice, primary: false);
                default:
                    return -1;
            }
        }

        /// <summary>Самое дорогое по карману нужного вида; при равной цене — первое.</summary>
        private static int Best(IReadOnlyList<BotShopItem> items, int money, int starterPrice, bool primary)
        {
            if (items == null) return -1;

            int best = -1;
            for (int i = 0; i < items.Count; i++)
            {
                BotShopItem item = items[i];
                if (item.Primary != primary || item.Price > money || item.Price <= starterPrice) continue;
                if (best < 0 || item.Price > items[best].Price) best = i;
            }
            return best;
        }
    }
}
