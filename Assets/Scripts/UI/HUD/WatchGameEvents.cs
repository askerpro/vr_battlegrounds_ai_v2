using System;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Переводчик событий игры в нотификации часов (T-46): слушает локальные события менеджеров и режимов
    /// и опрашивает реплицированное состояние, а говорит через <see cref="WatchNotifications.Post(WatchNotification)"/>.
    /// Что и как важно — <see cref="WatchNotificationTexts"/>.
    ///
    /// <para>
    /// Живёт не на аватаре, а у <see cref="WatchNotificationRunner"/> (один на машину, переживает смену
    /// аватара и сцены): «вы погибли» случается ровно тогда, когда аватар меняется на призрака.
    /// Сеть не добавляет — только события, которые до этой машины уже доехали.
    /// </para>
    ///
    /// <para>
    /// Первая фаза, которую переводчик узнаёт в режиме, — не смена, а текущее состояние (поздний
    /// клиент, смена режима): её показывает статус часов, нотификации нет.
    /// </para>
    /// </summary>
    public sealed class WatchGameEvents : IDisposable
    {
        private const float ReminderCheckInterval = 1f;
        private const float ReminderInterval = 8f;
        private const float CountdownReminderInterval = 4f;

        private bool _subscribed;
        private bool _phaseSeen;
        private MapState? _lastMapState;
        private RoundPhase? _lowTimeState;
        private float _lastSeconds = float.NaN;
        private bool _awaitingRespawn;
        private float _nextReminderCheck;

        public void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;

            GameMode.ModeStartedLocal += HandleModeStarted;
            GameMode.ModeFinishedLocal += HandleModeFinished;
            MapReferee.PlayerKilledLocal += HandlePlayerKilled;
            MapReferee.ActiveGameModeChangedLocal += HandleActiveModeChanged;
            MapLoader.MapLoadStarted += HandleMapLoadStarted;
            EliminationMode.RoundEndedLocal += HandleRoundEnded;
            EliminationMode.RoundPhaseChangedLocal += HandleRoundPhaseChanged;
            EliminationMode.SidesSwappedLocal += HandleSidesSwapped;
            MatchEconomy.TransactionLocal += HandleTransaction;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            _subscribed = false;

            GameMode.ModeStartedLocal -= HandleModeStarted;
            GameMode.ModeFinishedLocal -= HandleModeFinished;
            MapReferee.PlayerKilledLocal -= HandlePlayerKilled;
            MapReferee.ActiveGameModeChangedLocal -= HandleActiveModeChanged;
            MapLoader.MapLoadStarted -= HandleMapLoadStarted;
            EliminationMode.RoundEndedLocal -= HandleRoundEnded;
            EliminationMode.RoundPhaseChangedLocal -= HandleRoundPhaseChanged;
            EliminationMode.SidesSwappedLocal -= HandleSidesSwapped;
            MatchEconomy.TransactionLocal -= HandleTransaction;
        }

        // ── Опрос состояния (раз в кадр) ──────────────────────

        /// <summary>
        /// То, у чего нет события: пауза (SyncVar состояния карты), «мало времени», респаун, напоминания.
        /// </summary>
        public void Poll(float now)
        {
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;

            PollPause(referee);
            PollLowTime(mode);
            PollRespawn(mode);

            if (now >= _nextReminderCheck)
                PollReminder(mode, now);
        }

        private void PollPause(MapReferee referee)
        {
            MapState? state = referee != null ? referee.CurrentState : (MapState?)null;
            if (state == _lastMapState) return;

            MapState? before = _lastMapState;
            _lastMapState = state;

            if (before == MapState.Live && state == MapState.Paused) WatchNotifications.Post(WatchNotificationTexts.Paused(true));
            else if (before == MapState.Paused && state == MapState.Live) WatchNotifications.Post(WatchNotificationTexts.Paused(false));
        }

        private void PollLowTime(GameMode mode)
        {
            // В Elimination — только бой: остаток закупки и отсчёта показывает статус и табло зоны.
            bool timed = RoundClock.TryGetTimeRemaining(mode, out float seconds, out RoundPhase? state) &&
                         (state == null || state == RoundPhase.Combat);
            if (!timed || state != _lowTimeState)
            {
                _lowTimeState = timed ? state : null;
                _lastSeconds = timed ? seconds : float.NaN;
                return;
            }

            int crossed = WatchNotificationTexts.CrossedLowTime(_lastSeconds, seconds);
            _lastSeconds = seconds;
            if (crossed > 0) WatchNotifications.Post(WatchNotificationTexts.LowTime(crossed));
        }

        private void PollRespawn(GameMode mode)
        {
            if (!_awaitingRespawn) return;
            if (mode == null || !mode.CanRespawn())
            {
                // В Elimination живым становятся в начале раунда — об этом говорит фаза.
                _awaitingRespawn = false;
                return;
            }

            PlayerController avatar = PlayerSession.LocalSession != null ? PlayerSession.LocalSession.ActiveAvatar : null;
            if (avatar == null || !avatar.IsAlive) return;

            _awaitingRespawn = false;
            WatchNotifications.Post(WatchNotificationTexts.Respawned());
        }

        /// <summary>
        /// Напоминание «выберите команду» / «вернитесь в зону»: пока условие держится, раз в
        /// <see cref="ReminderInterval"/> с (на отсчёте чаще — он стоит, пока игрок вне зоны).
        /// </summary>
        private void PollReminder(GameMode activeMode, float now)
        {
            _nextReminderCheck = now + ReminderCheckInterval;

            PlayerSession local = PlayerSession.LocalSession;
            PlayerController avatar = local != null ? local.ActiveAvatar : null;
            var mode = activeMode as EliminationMode;
            if (avatar == null || mode == null) return;

            bool hasModeTeam = mode.Teams != null &&
                               Array.Exists(mode.Teams, t => t != null && t.teamIndex == local.TeamIndex);

            switch (WatchNotificationTexts.Reminder(hasModeTeam, avatar.IsAlive, local.IsInSpawnZone, mode.CurrentRoundPhase))
            {
                case WatchReminder.ChooseTeam:
                    WatchNotifications.Post(WatchNotificationTexts.ChooseTeam());
                    _nextReminderCheck = now + ReminderInterval;
                    break;
                case WatchReminder.ReturnForCountdown:
                    WatchNotifications.Post(WatchNotificationTexts.ReturnForCountdown());
                    _nextReminderCheck = now + CountdownReminderInterval;
                    break;
                case WatchReminder.ReturnToBase:
                    WatchNotifications.Post(WatchNotificationTexts.ReturnToBase());
                    _nextReminderCheck = now + ReminderInterval;
                    break;
            }
        }

        // ── События ───────────────────────────────────────────

        private static int LocalTeam => PlayerSession.LocalSession != null ? PlayerSession.LocalSession.TeamIndex : -1;

        private void HandleActiveModeChanged(GameMode mode)
        {
            // Новый режим: первая фаза — состояние, а не смена; таймеры — с чистого листа.
            _phaseSeen = false;
            _lowTimeState = null;
            _lastSeconds = float.NaN;
        }

        private void HandleMapLoadStarted(string sceneName)
        {
            // Уходим с карты — всё сказанное о ней уже неправда.
            WatchNotifications.Clear();
            _phaseSeen = false;
            _awaitingRespawn = false;
        }

        private void HandleModeStarted() => WatchNotifications.Post(WatchNotificationTexts.ModeStarted());

        private void HandleModeFinished(TeamData winner) =>
            WatchNotifications.Post(WatchNotificationTexts.MapFinished(winner != null ? winner.teamIndex : (int?)null,
                                                                       winner != null ? winner.Name : null, LocalTeam));

        private void HandleRoundEnded(TeamData winner) =>
            WatchNotifications.Post(WatchNotificationTexts.RoundEnded(winner != null ? winner.teamIndex : (int?)null,
                                                                      winner != null ? winner.Name : null, LocalTeam));

        private void HandleSidesSwapped() => WatchNotifications.Post(WatchNotificationTexts.SidesSwapped());

        private void HandleRoundPhaseChanged(RoundPhase phase)
        {
            if (!_phaseSeen)
            {
                _phaseSeen = true;
                return;
            }

            string score = null;
            MapReferee referee = MapReferee.Instance;
            if (WatchScore.TryGet(referee != null ? referee.ActiveGameMode : null, LocalTeam, out int own, out int enemy))
                score = WristDisplayFace.FormatScore(own, enemy);

            WatchNotifications.Post(WatchNotificationTexts.Phase(phase, score));
        }

        private void HandlePlayerKilled(KillNotice kill)
        {
            PlayerSession local = PlayerSession.LocalSession;
            uint localId = local != null ? local.netId : 0u;
            if (localId != 0 && kill.VictimNetId == localId) _awaitingRespawn = true;

            WatchNotifications.Post(WatchNotificationTexts.Kill(kill, localId, LocalTeam));
        }

        private void HandleTransaction(EconomyTransaction transaction)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null || transaction.Player != MatchEconomy.KeyOf(local)) return;
            WatchNotifications.Post(WatchNotificationTexts.Money(transaction));
        }
    }
}
