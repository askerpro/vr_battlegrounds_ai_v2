using System;
using System.Collections.Generic;
using VrBattlegrounds;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Что произошло с машиной раунда за один тик. Переход возвращается значением,
    /// а не выполняется побочным эффектом внутри обработчика — именно из-за побочных
    /// эффектов фазы Resolution и Scoreboard раньше пропускались (MATCH-02).
    /// </summary>
    public readonly struct RoundTickResult
    {
        /// <summary>Машина сменила фазу сама.</summary>
        public readonly bool PhaseChanged;

        /// <summary>
        /// Раунд прожит целиком: экран итогов показан. Машина остановлена и ждёт,
        /// пока владелец решит — начать следующий раунд или закончить сет.
        /// </summary>
        public readonly bool CycleCompleted;

        /// <summary>Фаза, из которой шёл переход.</summary>
        public readonly RoundState From;

        /// <summary>Фаза, в которую шёл переход.</summary>
        public readonly RoundState To;

        private RoundTickResult(bool phaseChanged, bool cycleCompleted, RoundState from, RoundState to)
        {
            PhaseChanged = phaseChanged;
            CycleCompleted = cycleCompleted;
            From = from;
            To = to;
        }

        /// <summary>Ничего не произошло: фаза продолжается.</summary>
        public static RoundTickResult Nothing =>
            new RoundTickResult(false, false, RoundState.Setup, RoundState.Setup);

        /// <summary>Машина перешла в новую фазу.</summary>
        public static RoundTickResult Moved(RoundState from, RoundState to) =>
            new RoundTickResult(true, false, from, to);

        /// <summary>Цикл раунда закончен, переход применяет владелец машины.</summary>
        public static RoundTickResult Completed(RoundState from, RoundState to) =>
            new RoundTickResult(false, true, from, to);
    }

    /// <summary>
    /// Машина состояний одного раунда. Обычный C#-класс: без MonoBehaviour, без сети
    /// и без доступа к сцене — поэтому весь раунд прогоняется EditMode-тестом
    /// за миллисекунды.
    ///
    /// **Кто меняет фазу.** Только <see cref="Tick"/> и только по таблице
    /// <see cref="Transitions"/>. Всё, что приходит снаружи (гибель игрока), — это
    /// заявка <see cref="RequestRoundEnd"/>, а не смена фазы. Раньше фазу меняли из
    /// трёх мест, причём внутри обработчиков событий, и цепочка
    /// «конец раунда → SetManager → старт следующего раунда» замыкалась в одном кадре:
    /// фаза Resolution жила до следующей строки, а ветки Resolution и Scoreboard
    /// в Tick были недостижимы (MATCH-02, корень 4).
    ///
    /// **Чего машина не делает.** Она не начинает следующий раунд. Переход
    /// «итоги показаны → новый раунд» помечен в таблице как <c>appliedByOwner</c>:
    /// его применяет <see cref="SetManager"/>, потому что только он знает, не пора ли
    /// вместо нового раунда закончить сет. Второй владелец этого перехода обошёл бы
    /// счётчик раундов и <c>RpcOnRoundStarted</c> (MATCH-06).
    /// </summary>
    public class RoundManager
    {
        /// <summary>Техническая подготовка: очистка и телепортация.</summary>
        public const float SetupDuration = 1.0f;

        /// <summary>Пауза после победы, до экрана итогов.</summary>
        public const float ResolutionDuration = 3.0f;

        /// <summary>Экран итогов раунда.</summary>
        public const float ScoreboardDuration = 5.0f;

        /// <summary>
        /// Таблица переходов раунда — единственное место, где описано, из какой фазы
        /// в какую и по какому условию переходит раунд. Читается сверху вниз как
        /// сценарий: строки идут в порядке фаз, а два выхода из Combat стоят рядом.
        ///
        /// Порядок строк значим только внутри одной фазы: побеждает первая подошедшая.
        /// </summary>
        private static readonly PhaseTransition[] Transitions =
        {
            new PhaseTransition(RoundState.Setup, RoundState.Equipment,
                m => m._stateTimer >= SetupDuration,
                "подготовка закончена"),

            new PhaseTransition(RoundState.Equipment, RoundState.Countdown,
                m => m.AreAllPlayersReady(),
                "все живые игроки готовы"),

            new PhaseTransition(RoundState.Countdown, RoundState.Combat,
                m => m._stateTimer >= m._countdownDuration,
                "обратный отсчёт истёк"),

            new PhaseTransition(RoundState.Combat, RoundState.Resolution,
                m => m._roundEndRequested,
                "исход раунда определён"),

            new PhaseTransition(RoundState.Combat, RoundState.Resolution,
                m => m._roundTimer >= m._roundDuration,
                "время раунда истекло — ничья"),

            new PhaseTransition(RoundState.Resolution, RoundState.Scoreboard,
                m => m._stateTimer >= ResolutionDuration,
                "пауза после победы истекла"),

            // Единственная строка, которую машина не применяет сама: следующий раунд
            // начинает владелец. См. комментарий к классу.
            new PhaseTransition(RoundState.Scoreboard, RoundState.Setup,
                m => m._stateTimer >= ScoreboardDuration,
                "итоги показаны", appliedByOwner: true)
        };

        private float _countdownDuration;
        private float _roundDuration;

        private float _stateTimer;
        private float _roundTimer;

        private RoundState _roundState = RoundState.Setup;

        /// <summary>Победитель текущего раунда. null — ничья либо раунд ещё идёт.</summary>
        private TeamData _roundWinner;

        /// <summary>Пришла заявка на завершение боя. Применяет её ближайший <see cref="Tick"/>.</summary>
        private bool _roundEndRequested;

        /// <summary>Цикл раунда прожит: машина стоит, пока владелец не начнёт следующий раунд.</summary>
        private bool _awaitingOwner;

        /// <summary>Матч остановлен принудительно — тикать больше нечего.</summary>
        private bool _stopped;

        /// <summary>Откуда машина узнаёт о живых игроках. В игре — обёртка над PlayersManager.</summary>
        private readonly IPlayerRoster _roster;

        /// <summary>Команды текущего раунда. Нужны только чтобы спросить о готовности.</summary>
        private IReadOnlyList<TeamData> _teams = new TeamData[0];

        /// <param name="roster">Источник данных об игроках. null — боевой PlayersManager.</param>
        public RoundManager(IPlayerRoster roster = null)
        {
            _roster = roster ?? new PlayersManagerRoster();
        }

        public RoundState State => _roundState;

        /// <summary>Победитель раунда. Осмысленен с момента входа в Resolution. null — ничья.</summary>
        public TeamData RoundWinner => _roundWinner;

        public float RoundTimeRemaining => Math.Max(0f, _roundDuration - _roundTimer);

        public float CountdownTimeRemaining
        {
            get
            {
                if (_roundState == RoundState.Countdown) return Math.Max(0f, _countdownDuration - _stateTimer);
                if (_roundState == RoundState.Scoreboard) return Math.Max(0f, ScoreboardDuration - _stateTimer);
                if (_roundState == RoundState.Resolution) return Math.Max(0f, ResolutionDuration - _stateTimer);
                return 0f;
            }
        }

        /// <summary>
        /// Начинает раунд с фазы Setup. Единственный способ вернуть машину в работу
        /// после того, как она доиграла цикл. Зовётся только владельцем.
        /// </summary>
        public void StartRound(IReadOnlyList<TeamData> teams, float countdownDuration, float roundDuration)
        {
            _teams = teams ?? new TeamData[0];
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;

            _stateTimer = 0f;
            _roundTimer = 0f;

            _roundWinner = null;
            _roundEndRequested = false;
            _awaitingOwner = false;
            _stopped = false;

            _roundState = RoundState.Setup;

            GameLog.Match.Info(
                "[RoundManager] Раунд начат: очистка и телепортация (Setup)");
        }

        /// <summary>
        /// Заявка «бой пора заканчивать», с победителем или без (ничья).
        /// Фазу не меняет: её сменит ближайший <see cref="Tick"/> по таблице переходов.
        /// Так у машины остаётся ровно одна точка смены состояния.
        /// </summary>
        public void RequestRoundEnd(TeamData winner)
        {
            if (_roundState != RoundState.Combat || _roundEndRequested) return;

            _roundWinner = winner;
            _roundEndRequested = true;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info(
                $"[RoundManager] Исход боя определён. Победитель: {winnerName}");
        }

        /// <summary>
        /// Шаг машины. Возвращает произошедший переход — применять его последствия
        /// (счёт, оповещение клиентов, следующий раунд) обязан вызывающий код.
        /// </summary>
        public RoundTickResult Tick(float deltaTime)
        {
            if (_stopped || _awaitingOwner) return RoundTickResult.Nothing;

            _stateTimer += deltaTime;
            if (_roundState == RoundState.Combat) _roundTimer += deltaTime;

            foreach (PhaseTransition transition in Transitions)
            {
                if (transition.From != _roundState) continue;
                if (!transition.When(this)) continue;

                if (transition.AppliedByOwner)
                {
                    // Машина доиграла раунд и останавливается. Что дальше — новый раунд
                    // или конец сета — решает владелец, он же и применит переход.
                    _awaitingOwner = true;

                    GameLog.Match.Info(
                        $"[RoundManager] {transition.From}: {transition.Reason}. Цикл раунда завершён.");

                    return RoundTickResult.Completed(transition.From, transition.To);
                }

                _roundState = transition.To;
                _stateTimer = 0f;

                GameLog.Match.Info(
                    $"[RoundManager] {transition.From} → {transition.To}: {transition.Reason}");

                return RoundTickResult.Moved(transition.From, transition.To);
            }

            return RoundTickResult.Nothing;
        }

        /// <summary>Останавливает машину: тики перестают что-либо делать до StartRound.</summary>
        public void ForceStop()
        {
            _stopped = true;
            GameLog.Match.Info("[RoundManager] Раунд принудительно остановлен");
        }

        private bool AreAllPlayersReady()
        {
            int totalAlive = 0;
            int notReadyCount = 0;

            foreach (TeamData team in _teams)
            {
                // Ожидаем готовности только от тех, кто жив — то есть участвует в раунде.
                foreach (var session in _roster.GetAlivePlayers(team))
                {
                    totalAlive++;
                    if (!session.IsReadyForRound) notReadyCount++;
                }
            }

            // Пока никто не заспавнился, переходить к отсчёту не с кем.
            if (totalAlive == 0) return false;
            if (notReadyCount > 0) return false;

            GameLog.Match.Verbose(
                "[RoundManager] Все живые игроки готовы к раунду");
            return true;
        }

        /// <summary>Строка таблицы переходов.</summary>
        private readonly struct PhaseTransition
        {
            /// <summary>Фаза, из которой возможен переход.</summary>
            public readonly RoundState From;

            /// <summary>Фаза, в которую он ведёт.</summary>
            public readonly RoundState To;

            /// <summary>Условие перехода. Аргумент — сама машина, чтобы правило читало её таймеры.</summary>
            public readonly Func<RoundManager, bool> When;

            /// <summary>Причина перехода. Уходит в лог, чтобы траектория раунда читалась по логам.</summary>
            public readonly string Reason;

            /// <summary>Переход применяет владелец машины, а не она сама.</summary>
            public readonly bool AppliedByOwner;

            public PhaseTransition(RoundState from, RoundState to, Func<RoundManager, bool> when,
                                   string reason, bool appliedByOwner = false)
            {
                From = from;
                To = to;
                When = when;
                Reason = reason;
                AppliedByOwner = appliedByOwner;
            }
        }

#if UNITY_EDITOR
        /// <summary>Почему раунд стоит в фазе Equipment — для инспектора режима.</summary>
        public string GetPendingReadinessStatus()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool allReady = true;

            foreach (TeamData team in _teams)
            {
                foreach (var s in _roster.GetAlivePlayers(team))
                {
                    if (!s.IsReadyForRound)
                    {
                        allReady = false;
                        sb.AppendLine($"- {s.PlayerName}:");
                        if (!s.IsInSpawnZone) sb.AppendLine("   [!] Not in spawn zone");
                        if (!s.HasGrabbedDogTag) sb.AppendLine("   [!] Dog tag not grabbed");
                    }
                }
            }

            if (allReady) return "All Players Ready!";
            return sb.ToString();
        }
#endif
    }

    public enum RoundState
    {
        /// <summary>Техническая микрофаза. Очистка, телепортация.</summary>
        Setup,
        /// <summary>Основное время закупки. Арсенал открыт.</summary>
        Equipment,
        /// <summary>Все готовы. Идет таймер 3-5 секунд. Арсенал закрывается, патроны спавнятся.</summary>
        Countdown,
        /// <summary>Активный бой. Урон включен.</summary>
        Combat,
        /// <summary>Кто-то победил. Короткая пауза (SlowMo).</summary>
        Resolution,
        /// <summary>Вывод итогов (Scoreboard) на несколько секунд.</summary>
        Scoreboard
    }
}
