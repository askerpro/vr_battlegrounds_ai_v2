using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Economy
{
    /// <summary>Почему изменились деньги — для уведомлений игроку (HUD, часы).</summary>
    public enum EconomyReason
    {
        RoundWin,
        RoundLoss,
        Kill,
        TeamKill,
        Purchase,
        Refund,
        HalfReset
    }

    /// <summary>Одна денежная операция — для уведомлений («+3250 победа в раунде», «−2900 TR15»).</summary>
    public readonly struct EconomyTransaction
    {
        /// <summary>Ключ игрока (<c>Series.PlayerKey</c>).</summary>
        public readonly string Player;
        public readonly int Delta;
        public readonly int Money;
        public readonly EconomyReason Reason;

        /// <summary>Уточнение: название оружия покупки или убийства. Может быть пустым.</summary>
        public readonly string Detail;

        public EconomyTransaction(string player, int delta, int money, EconomyReason reason, string detail)
        {
            Player = player;
            Delta = delta;
            Money = money;
            Reason = reason;
            Detail = detail;
        }
    }

    /// <summary>
    /// Экономика матча как в CS2 (T-45): деньги игроков, награды за раунд и убийство, покупки.
    ///
    /// <para>
    /// <b>Где живёт.</b> Компонент на префабе режима матча (<c>EliminationMode.prefab</c>) — правило
    /// режима, как <c>RoundMagazineRefill</c>. Режим без этого компонента (разминка, Respawn) —
    /// без денег: всё на стене бесплатно и берётся любым игроком. Кто спрашивает про деньги,
    /// находит экономику через <see cref="Current"/> и типа режима не знает.
    /// </para>
    ///
    /// <para>
    /// <b>Состояние серверное.</b> Правду держит <see cref="EconomyAccounts"/> на сервере, клиентам
    /// уезжает копия в <c>SyncDictionary</c> (ключ — <c>Series.PlayerKey</c>: токен устройства,
    /// поэтому деньги переживают переподключение). Поздний клиент получает деньги начальным
    /// значением спавна. Клиент денег не пишет никогда: покупку списывает сервер в момент захвата
    /// (<see cref="ArsenalCheckout"/>), проверяя владельца стены и деньги по своему состоянию.
    /// </para>
    ///
    /// <para>
    /// <b>Жизненный цикл.</b> Новый режим на карте — новые деньги (800). Начало половины — снова 800
    /// и счётчик поражений 1. Пауза уносит деньги на начало прерванного раунда в снимок
    /// (<see cref="IPauseSnapshotPart"/>), «Продолжить» их возвращает.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchEconomy : NetworkBehaviour, IPauseSnapshotPart
    {
        /// <summary>Деньги игроков: ключ игрока → деньги. Пишет только сервер.</summary>
        private readonly SyncDictionary<string, int> _money = new SyncDictionary<string, int>();

        /// <summary>Доход за последний раунд (для «+3250» на табло стены). Чистится к обратному отсчёту.</summary>
        private readonly SyncDictionary<string, int> _roundIncome = new SyncDictionary<string, int>();

        /// <summary>Счётчики поражений команд (индекс команды → 0…4). Для HUD «следующий бонус».</summary>
        private readonly SyncDictionary<int, int> _lossCounters = new SyncDictionary<int, int>();

        private readonly EconomyAccounts _accounts = new EconomyAccounts();

        /// <summary>Деньги и счётчики на начало идущего раунда — то, что сохранит пауза.</summary>
        private readonly Dictionary<string, int> _moneyAtRoundStart = new Dictionary<string, int>();
        private readonly Dictionary<int, int> _countersAtRoundStart = new Dictionary<int, int>();

        private EliminationMode _mode;
        private MapReferee _referee;

        /// <summary>Деньги игрока изменились на этой машине (ключ игрока, новые деньги). Любая машина.</summary>
        public static event Action<string, int> MoneyChangedLocal;

        /// <summary>Денежная операция (клиент, разово). Для уведомлений; поздний клиент прошлых не получит.</summary>
        public static event Action<EconomyTransaction> TransactionLocal;

        /// <summary>Сервер: денежная операция. Для тестов и логики сервера.</summary>
        public static event Action<EconomyTransaction> TransactionServer;

        // ── Поиск ──────────────────────────────────────────────

        /// <summary>
        /// Экономика активного режима этой машины или null — денег нет (разминка, режим без экономики,
        /// карта без режима).
        /// </summary>
        public static MatchEconomy Current
        {
            get
            {
                MapReferee referee = MapReferee.Instance;
                GameMode mode = referee != null ? referee.ActiveGameMode : null;
                return mode != null ? mode.GetComponent<MatchEconomy>() : null;
            }
        }

        /// <summary>Ключ игрока в экономике — тот же, что у статистики серии.</summary>
        public static string KeyOf(PlayerSession session) => Series.PlayerKey(session);

        // ── Чтение (любая машина) ──────────────────────────────

        public bool TryGetMoney(string player, out int money)
        {
            money = 0;
            return !string.IsNullOrEmpty(player) && _money.TryGetValue(player, out money);
        }

        public bool TryGetMoney(PlayerSession session, out int money) => TryGetMoney(KeyOf(session), out money);

        /// <summary>Деньги игрока; счёта нет — 0.</summary>
        public int GetMoney(PlayerSession session) => TryGetMoney(session, out int money) ? money : 0;

        /// <summary>Доход игрока за последний раунд (до обратного отсчёта следующего); 0 — нет.</summary>
        public int GetRoundIncome(PlayerSession session)
        {
            string key = KeyOf(session);
            return key != null && _roundIncome.TryGetValue(key, out int income) ? income : 0;
        }

        /// <summary>Счётчик поражений команды (0…4).</summary>
        public int GetLossCounter(int team) =>
            _lossCounters.TryGetValue(team, out int counter) ? counter : EconomyRules.HalfStartLossCounter;

        /// <summary>Бонус, который команда получит за следующее поражение.</summary>
        public int NextLossBonus(int team) => EconomyRules.LossBonus(GetLossCounter(team));

        // ── Unity / Mirror ─────────────────────────────────────

        private void Awake()
        {
            _money.OnChange += HandleMoneyChanged;
        }

        private void OnDestroy()
        {
            _money.OnChange -= HandleMoneyChanged;
            UnsubscribeServer();
        }

        private void HandleMoneyChanged(SyncIDictionary<string, int>.Operation op, string key, int _)
        {
            if (key == null || !_money.TryGetValue(key, out int money)) return;
            MoneyChangedLocal?.Invoke(key, money);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            _mode = GetComponent<EliminationMode>();
            if (_mode != null)
            {
                _mode.RoundBeganServer -= ServerOnRoundBegan;
                _mode.RoundBeganServer += ServerOnRoundBegan;
                _mode.RoundScoredServer -= ServerOnRoundScored;
                _mode.RoundScoredServer += ServerOnRoundScored;
            }

            EliminationMode.RoundPhaseChangedServer -= ServerOnRoundPhase;
            EliminationMode.RoundPhaseChangedServer += ServerOnRoundPhase;

            _referee = MapReferee.Instance;
            if (_referee != null)
            {
                _referee.PlayerKilled -= ServerOnPlayerKilled;
                _referee.PlayerKilled += ServerOnPlayerKilled;
            }
        }

        public override void OnStopServer()
        {
            UnsubscribeServer();
            base.OnStopServer();
        }

        private void UnsubscribeServer()
        {
            if (_mode != null)
            {
                _mode.RoundBeganServer -= ServerOnRoundBegan;
                _mode.RoundScoredServer -= ServerOnRoundScored;
            }

            EliminationMode.RoundPhaseChangedServer -= ServerOnRoundPhase;

            if (_referee != null) _referee.PlayerKilled -= ServerOnPlayerKilled;
        }

        // ── Сервер: раунды ─────────────────────────────────────

        /// <summary>
        /// Раунд начался. Первый раунд половины — стартовые деньги всем и счётчик половины,
        /// иначе — счета для новых игроков. Чеки прошлого раунда сгорают: вернуть купленное можно
        /// только в ту же закупку.
        /// </summary>
        [Server]
        public void ServerOnRoundBegan(int round, bool firstRoundOfHalf)
        {
            List<string> players = TeamMemberKeys();

            if (firstRoundOfHalf)
            {
                _accounts.ResetForHalf(players, TeamIndexes());
                GameLog.Match.Info($"[Economy] Раунд {round} — начало половины: у всех {EconomyRules.StartMoney}, счётчик поражений {EconomyRules.HalfStartLossCounter}.");
            }
            else
            {
                foreach (string player in players) _accounts.EnsureAccount(player);
                _accounts.ClearReceipts();
            }

            _accounts.CopyTo(_moneyAtRoundStart, _countersAtRoundStart);
            PublishAll();
        }

        /// <summary>Исход раунда: победителям 3250, проигравшим — бонус по лестнице. Ничья — поражение обеим.</summary>
        [Server]
        public void ServerOnRoundScored(TeamData winner)
        {
            if (_mode == null) return;

            _roundIncome.Clear();

            foreach (TeamRuntimeData state in _mode.TeamStates.Values)
            {
                if (state == null || state.Team == null) continue;

                bool won = winner != null && state.Team.teamIndex == winner.teamIndex;
                List<string> members = KeysOf(state.Sessions);
                int reward = _accounts.ApplyRoundEnd(state.Team.teamIndex, won, members);

                foreach (string member in members)
                {
                    _roundIncome[member] = reward;
                    Report(member, reward, won ? EconomyReason.RoundWin : EconomyReason.RoundLoss, state.Team.Name);
                }

                GameLog.Match.Info($"[Economy] {state.Team.Name}: {(won ? "победа" : "поражение")}, каждому +{reward}, " +
                                   $"счётчик поражений {_accounts.GetLossCounter(state.Team.teamIndex)}.");
            }

            PublishAll();
        }

        private void ServerOnRoundPhase(RoundPhase phase)
        {
            // Уничтоженный компонент (статическое событие пережило объект) — молчим.
            if (this == null) return;

            // «+3250» на табло видно всю подготовку и закупку следующего раунда.
            if (phase == RoundPhase.Countdown && _roundIncome.Count > 0) _roundIncome.Clear();
        }

        // ── Сервер: убийства ───────────────────────────────────

        private void ServerOnPlayerKilled(PlayerSession victim, PlayerSession killer, IReadOnlyList<PlayerSession> assists)
        {
            ServerAwardKill(victim, killer, HeldWeapon(killer));
        }

        /// <summary>
        /// Награда за убийство: <c>KillAward</c> оружия в руке убийцы (нет оружия — 300),
        /// за союзника — штраф 300. Самоубийство и смерть без убийцы — ничего.
        /// </summary>
        [Server]
        public void ServerAwardKill(PlayerSession victim, PlayerSession killer, WeaponInfo weapon)
        {
            if (killer == null || killer == victim) return;

            bool teamKill = victim != null && victim.TeamIndex == killer.TeamIndex && killer.TeamIndex != 0;
            int award = weapon != null ? weapon.KillAward : EconomyRules.DefaultKillAward;
            int reward = EconomyRules.KillReward(award, teamKill);

            string key = KeyOf(killer);
            int applied = _accounts.Add(key, reward);
            Publish(key);
            Report(key, applied, teamKill ? EconomyReason.TeamKill : EconomyReason.Kill, weapon != null ? weapon.DisplayName : "");
        }

        /// <summary>Оружие, которое убийца держит в руке сейчас (первое найденное), или null.</summary>
        private static WeaponInfo HeldWeapon(PlayerSession killer)
        {
            PlayerController avatar = killer != null ? killer.ActiveAvatar : null;
            if (avatar == null) return null;

            foreach (UxrGrabber grabber in avatar.GetComponentsInChildren<UxrGrabber>(true))
            {
                UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
                WeaponComponent weapon = held != null ? held.GetComponentInParent<WeaponComponent>() : null;
                if (weapon != null && weapon.WeaponData != null) return weapon.WeaponData;
            }

            return null;
        }

        // ── Сервер: покупки ────────────────────────────────────

        /// <summary>
        /// Покупка: списывает цену, если хватает денег. Единая точка списания — ей пользуется и стена
        /// (<see cref="ArsenalCheckout"/>), и будущая логика ботов («бот покупает»: <paramref name="itemNetId"/> = 0).
        /// </summary>
        /// <param name="itemNetId"><c>netId</c> купленного предмета — для возврата; 0 — без чека.</param>
        [Server]
        public bool ServerTryPurchase(PlayerSession buyer, WeaponInfo weapon, uint itemNetId)
        {
            if (buyer == null || weapon == null) return false;

            string key = KeyOf(buyer);
            if (!_accounts.TryPurchase(key, weapon.Price, itemNetId))
            {
                GameLog.Arsenal.Info($"[Economy] {buyer.PlayerName}: не хватает денег на {weapon.DisplayName} " +
                                     $"({_accounts.GetMoney(key)} < {weapon.Price}).");
                return false;
            }

            Publish(key);
            Report(key, -weapon.Price, EconomyReason.Purchase, weapon.DisplayName);
            GameLog.Arsenal.Info($"[Economy] {buyer.PlayerName} купил {weapon.DisplayName} за {weapon.Price}, осталось {_accounts.GetMoney(key)}.");
            return true;
        }

        /// <summary>Возврат денег за предмет, купленный в эту закупку и повешенный обратно на стену.</summary>
        [Server]
        public bool ServerTryRefund(uint itemNetId, string what)
        {
            if (!_accounts.TryRefund(itemNetId, out EconomyAccounts.Receipt receipt)) return false;

            Publish(receipt.Buyer);
            Report(receipt.Buyer, receipt.Price, EconomyReason.Refund, what);
            GameLog.Arsenal.Info($"[Economy] Возврат {receipt.Price} за {what}.");
            return true;
        }

        /// <summary>Был ли предмет куплен в этом раунде (есть чек).</summary>
        public bool HasReceipt(uint itemNetId) => _accounts.HasReceipt(itemNetId);

        // ── Пауза ──────────────────────────────────────────────

        public void CaptureInto(PauseSnapshot snapshot)
        {
            foreach (KeyValuePair<string, int> pair in _moneyAtRoundStart) snapshot.Money[pair.Key] = pair.Value;
            foreach (KeyValuePair<int, int> pair in _countersAtRoundStart) snapshot.LossCounters[pair.Key] = pair.Value;
        }

        public void RestoreFrom(PauseSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Money.Count == 0) return;

            _accounts.Restore(snapshot.Money, snapshot.LossCounters);
            _accounts.CopyTo(_moneyAtRoundStart, _countersAtRoundStart);
            PublishAll();
        }

        // ── Сервер: служебное ──────────────────────────────────

        private List<string> TeamMemberKeys()
        {
            var keys = new List<string>();
            if (_mode == null) return keys;

            foreach (TeamRuntimeData state in _mode.TeamStates.Values)
                if (state != null) keys.AddRange(KeysOf(state.Sessions));

            return keys;
        }

        private List<int> TeamIndexes()
        {
            var teams = new List<int>();
            if (_mode == null) return teams;

            foreach (TeamRuntimeData state in _mode.TeamStates.Values)
                if (state != null && state.Team != null) teams.Add(state.Team.teamIndex);

            return teams;
        }

        private static List<string> KeysOf(IEnumerable<PlayerSession> sessions)
        {
            var keys = new List<string>();
            foreach (PlayerSession session in sessions)
            {
                string key = KeyOf(session);
                if (!string.IsNullOrEmpty(key) && !keys.Contains(key)) keys.Add(key);
            }

            return keys;
        }

        /// <summary>Переписывает сетевую копию целиком: только изменившиеся значения уходят дельтой.</summary>
        private void PublishAll()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, int> pair in _money)
                if (!_accounts.Money.ContainsKey(pair.Key)) stale.Add(pair.Key);
            foreach (string key in stale) _money.Remove(key);

            foreach (KeyValuePair<string, int> pair in _accounts.Money) Publish(pair.Key);

            foreach (KeyValuePair<int, int> pair in _accounts.LossCounters)
                if (!_lossCounters.TryGetValue(pair.Key, out int old) || old != pair.Value) _lossCounters[pair.Key] = pair.Value;
        }

        /// <summary>Сетевая копия денег одного игрока — только если изменились.</summary>
        private void Publish(string key)
        {
            if (string.IsNullOrEmpty(key) || !_accounts.TryGetMoney(key, out int money)) return;
            if (_money.TryGetValue(key, out int old) && old == money) return;
            _money[key] = money;
        }

        private void Report(string key, int delta, EconomyReason reason, string detail)
        {
            var transaction = new EconomyTransaction(key, delta, _accounts.GetMoney(key), reason, detail ?? "");
            TransactionServer?.Invoke(transaction);

            if (NetworkServer.active && netIdentity != null && netIdentity.netId != 0)
                RpcTransaction(key, delta, transaction.Money, reason, transaction.Detail);
        }

        [ClientRpc]
        private void RpcTransaction(string key, int delta, int money, EconomyReason reason, string detail)
        {
            TransactionLocal?.Invoke(new EconomyTransaction(key, delta, money, reason, detail));
        }
    }
}
