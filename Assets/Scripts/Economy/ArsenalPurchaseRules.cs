namespace VrBattlegrounds.Economy
{
    /// <summary>Что слот стены предлагает сейчас — от этого зависят подсветка и возможность взять.</summary>
    public enum SlotOffer
    {
        /// <summary>Экономики нет (разминка, режим без денег): бери что хочешь, кто хочешь.</summary>
        Free,

        /// <summary>Владельцу стены хватает денег: ствол подсвечен, взять может только владелец.</summary>
        Affordable,

        /// <summary>Денег не хватает: ствол приглушён и не берётся.</summary>
        TooExpensive,

        /// <summary>У стены нет владельца (игроков меньше, чем стен): не берётся никем.</summary>
        NoOwner
    }

    /// <summary>Решение сервера о взятом со стены предмете.</summary>
    public enum CheckoutVerdict
    {
        /// <summary>Бесплатно — экономики нет.</summary>
        Free,

        /// <summary>Списать цену с покупателя.</summary>
        Charge,

        /// <summary>Отказ: взял не владелец или не хватает денег — предмет возвращается на стену.</summary>
        Reject
    }

    /// <summary>
    /// Правила покупки со стены арсенала — чистые функции. Одни и те же правила видят три места:
    /// подсветка слота (каждая машина), запрет хвата чужой рукой (<c>ArsenalGrabRule</c>, машина
    /// игрока) и серверное списание (<c>ArsenalCheckout</c>). Клиенту сервер не доверяет: он
    /// проверяет то же самое по своему состоянию.
    /// </summary>
    public static class ArsenalPurchaseRules
    {
        /// <param name="economyActive">У активного режима есть экономика (<see cref="MatchEconomy"/>).</param>
        /// <param name="ownerKnown">У стены есть владелец и его деньги известны.</param>
        /// <param name="ownerMoney">Деньги владельца.</param>
        /// <param name="price">Цена предмета в слоте.</param>
        public static SlotOffer Evaluate(bool economyActive, bool ownerKnown, int ownerMoney, int price)
        {
            if (!economyActive) return SlotOffer.Free;
            if (!ownerKnown) return SlotOffer.NoOwner;
            return EconomyRules.CanAfford(ownerMoney, price) ? SlotOffer.Affordable : SlotOffer.TooExpensive;
        }

        /// <summary>Можно ли вообще взять предмет со слота с таким предложением.</summary>
        public static bool AllowsGrab(SlotOffer offer) => offer == SlotOffer.Free || offer == SlotOffer.Affordable;

        /// <summary>
        /// Может ли этот игрок взять предмет со стены: только владелец и только если хватает денег.
        /// Без экономики — любой.
        /// </summary>
        /// <param name="ownerSession"><c>netId</c> сессии владельца стены; 0 — владельца нет.</param>
        /// <param name="takerSession"><c>netId</c> сессии того, чья рука тянется.</param>
        public static bool MayTake(bool economyActive, uint ownerSession, uint takerSession, int ownerMoney, int price)
        {
            if (!economyActive) return true;
            if (ownerSession == 0 || takerSession != ownerSession) return false;
            return EconomyRules.CanAfford(ownerMoney, price);
        }

        /// <summary>
        /// Серверное решение по факту захвата. Взявший неизвестен (событие без руки) — считаем,
        /// что взял владелец: другой рукой предмет со стены не взять (правило хвата), а деньги
        /// всё равно проверяются.
        /// </summary>
        public static CheckoutVerdict Checkout(bool economyActive, uint ownerSession, uint takerSession,
                                               int ownerMoney, int price)
        {
            if (!economyActive) return CheckoutVerdict.Free;

            uint taker = takerSession != 0 ? takerSession : ownerSession;
            return MayTake(true, ownerSession, taker, ownerMoney, price) ? CheckoutVerdict.Charge : CheckoutVerdict.Reject;
        }
    }
}
