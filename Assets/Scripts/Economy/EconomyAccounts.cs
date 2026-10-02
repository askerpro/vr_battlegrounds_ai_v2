using System.Collections.Generic;

namespace VrBattlegrounds.Economy
{
    /// <summary>
    /// Состояние экономики матча на карте — чистый C#-объект, без сети и без Unity.
    /// Деньги игроков (по ключу игрока, <c>Series.PlayerKey</c>: переживает переподключение),
    /// счётчики поражений команд и чеки покупок раунда. Числа берёт только из
    /// <see cref="EconomyRules"/>. Сетевую копию для клиентов ведёт <see cref="MatchEconomy"/>.
    /// </summary>
    public sealed class EconomyAccounts
    {
        private readonly Dictionary<string, int> _money = new Dictionary<string, int>();
        private readonly Dictionary<int, int> _lossCounters = new Dictionary<int, int>();
        private readonly Dictionary<uint, Receipt> _receipts = new Dictionary<uint, Receipt>();

        /// <summary>Чек покупки: кто купил и сколько заплатил. Нужен для возврата денег.</summary>
        public readonly struct Receipt
        {
            public readonly string Buyer;
            public readonly int Price;

            public Receipt(string buyer, int price)
            {
                Buyer = buyer;
                Price = price;
            }
        }

        public IReadOnlyDictionary<string, int> Money => _money;
        public IReadOnlyDictionary<int, int> LossCounters => _lossCounters;

        /// <summary>Заводит счёт со стартовыми деньгами, если его ещё нет. true — счёт новый.</summary>
        public bool EnsureAccount(string player)
        {
            if (string.IsNullOrEmpty(player) || _money.ContainsKey(player)) return false;
            _money[player] = EconomyRules.StartMoney;
            return true;
        }

        public bool TryGetMoney(string player, out int money)
        {
            money = 0;
            return !string.IsNullOrEmpty(player) && _money.TryGetValue(player, out money);
        }

        /// <summary>Деньги игрока; нет счёта — ноль.</summary>
        public int GetMoney(string player) => TryGetMoney(player, out int money) ? money : 0;

        /// <summary>Счётчик поражений команды; не заводился — как на старте половины.</summary>
        public int GetLossCounter(int team) =>
            _lossCounters.TryGetValue(team, out int counter) ? counter : EconomyRules.HalfStartLossCounter;

        /// <summary>
        /// Начисление (или штраф — отрицательное) с потолком и полом. Счёта нет — заводится.
        /// </summary>
        /// <returns>Сколько реально изменилось (потолок съедает лишнее).</returns>
        public int Add(string player, int delta)
        {
            if (string.IsNullOrEmpty(player)) return 0;

            EnsureAccount(player);
            int before = _money[player];
            int after = EconomyRules.Apply(before, delta);
            _money[player] = after;
            return after - before;
        }

        /// <summary>Списывает цену, если хватает денег. Нет счёта или денег — false, ничего не меняется.</summary>
        public bool TrySpend(string player, int price)
        {
            if (string.IsNullOrEmpty(player) || price < 0 || !_money.TryGetValue(player, out int money)) return false;
            if (!EconomyRules.CanAfford(money, price)) return false;

            _money[player] = money - price;
            return true;
        }

        /// <summary>
        /// Покупка предмета <paramref name="itemId"/> (сетевой id): списание и чек для возврата.
        /// <paramref name="itemId"/> = 0 — покупка без предмета (бот), чек не пишется.
        /// </summary>
        public bool TryPurchase(string player, int price, uint itemId)
        {
            if (!TrySpend(player, price)) return false;
            if (itemId != 0) _receipts[itemId] = new Receipt(player, price);
            return true;
        }

        /// <summary>
        /// Возврат денег за купленный в этом раунде предмет (повесили обратно на стену).
        /// Чека нет — предмет не покупали (выдан бесплатно, чужой с пола) — false.
        /// </summary>
        public bool TryRefund(uint itemId, out Receipt receipt)
        {
            if (!_receipts.TryGetValue(itemId, out receipt)) return false;

            _receipts.Remove(itemId);
            Add(receipt.Buyer, receipt.Price);
            return true;
        }

        public bool HasReceipt(uint itemId) => _receipts.ContainsKey(itemId);

        /// <summary>Чеки живут один раунд: возврат возможен только в ту же закупку.</summary>
        public void ClearReceipts() => _receipts.Clear();

        /// <summary>
        /// Начало половины (и первого раунда матча): у всех игроков — стартовые деньги, у всех
        /// команд — счётчик поражений половины. Как в CS2 после смены сторон.
        /// </summary>
        public void ResetForHalf(IEnumerable<string> players, IEnumerable<int> teams)
        {
            _money.Clear();
            _lossCounters.Clear();
            _receipts.Clear();

            foreach (string player in players) EnsureAccount(player);
            foreach (int team in teams) _lossCounters[team] = EconomyRules.HalfStartLossCounter;
        }

        /// <summary>
        /// Итог раунда для команды: выплата каждому её игроку и новый счётчик.
        /// </summary>
        /// <returns>Выплата по правилам (до потолка денег).</returns>
        public int ApplyRoundEnd(int team, bool won, IEnumerable<string> members)
        {
            RoundIncome income = EconomyRules.RoundEnd(won, GetLossCounter(team));
            _lossCounters[team] = income.LossCounter;

            foreach (string member in members) Add(member, income.Reward);
            return income.Reward;
        }

        /// <summary>Снимок денег и счётчиков — для паузы (раунд сыграется заново с этих денег).</summary>
        public void CopyTo(Dictionary<string, int> money, Dictionary<int, int> lossCounters)
        {
            money.Clear();
            lossCounters.Clear();
            foreach (KeyValuePair<string, int> pair in _money) money[pair.Key] = pair.Value;
            foreach (KeyValuePair<int, int> pair in _lossCounters) lossCounters[pair.Key] = pair.Value;
        }

        /// <summary>Возвращает состояние из снимка. Чеки не переживают паузу: снаряжение изымается.</summary>
        public void Restore(IReadOnlyDictionary<string, int> money, IReadOnlyDictionary<int, int> lossCounters)
        {
            _money.Clear();
            _lossCounters.Clear();
            _receipts.Clear();

            if (money != null)
                foreach (KeyValuePair<string, int> pair in money) _money[pair.Key] = EconomyRules.Apply(0, pair.Value);
            if (lossCounters != null)
                foreach (KeyValuePair<int, int> pair in lossCounters) _lossCounters[pair.Key] = pair.Value;
        }
    }
}
