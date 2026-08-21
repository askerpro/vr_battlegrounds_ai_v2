using System;
using System.Collections.Generic;
using VrBattlegrounds;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Чистая серверная логика сета: проводит N раундов, считает очки, определяет победителя.
    /// Не является MonoBehaviour — создаётся через new SetManager() из EliminationMode.
    ///
    /// **Владелец машины раунда.** Тикает <see cref="RoundManager"/> и применяет
    /// единственный переход, который тот не делает сам, — «итоги показаны → новый раунд».
    /// Больше этот переход не делает никто: второй владелец обошёл бы счётчик раундов
    /// и <c>RpcOnRoundStarted</c> (MATCH-06).
    ///
    /// **Когда что происходит.** Очко за раунд начисляется при входе в фазу Resolution —
    /// чтобы экран итогов показывал уже новый счёт. Исход сета решается позже, при выходе
    /// из Scoreboard: сет не может закончиться раньше, чем показали итоги последнего раунда.
    ///
    /// Сетевая синхронизация (SyncVar, ClientRpc) — в EliminationMode.
    /// </summary>
    public class SetManager
    {
        private readonly RoundManager _roundManager;

        /// <summary>
        /// Кому сообщить об исходе сета. Передаётся конструктором, а не событием:
        /// связывание в единственном месте делает двойную подписку (MATCH-01)
        /// невозможной по построению — подписаться дважды просто не на что.
        /// </summary>
        private readonly Action<TeamData> _onSetEnded;

        private int _roundsPerSet;
        private int _currentRound;

        /// <summary>Сет доигран: тик больше ничего не двигает до следующего StartSet.</summary>
        private bool _setFinished;

        private readonly Dictionary<int, int> _teamRoundScores = new Dictionary<int, int>();

        private EliminationMode _eliminationMode;
        private TeamData[] _teams = new TeamData[0];
        private float _countdownDuration;
        private float _roundDuration;

        public IReadOnlyDictionary<int, int> TeamRoundScores => _teamRoundScores;

        /// <summary>Номер идущего раунда в сете, начиная с 1. Растёт только здесь (MATCH-06).</summary>
        public int CurrentRound => _currentRound;

        public SetManager(RoundManager roundManager, Action<TeamData> onSetEnded)
        {
            _roundManager = roundManager;
            _onSetEnded = onSetEnded;
        }

        public void StartSet(TeamData[] teams, EliminationMode mode,
                             int roundsPerSet, float countdownDuration, float roundDuration)
        {
            _eliminationMode = mode;
            _teams = teams ?? new TeamData[0];
            _roundsPerSet = roundsPerSet;
            _countdownDuration = countdownDuration;
            _roundDuration = roundDuration;
            _currentRound = 0;
            _setFinished = false;

            _teamRoundScores.Clear();
            foreach (var t in _teams)
            {
                if (t != null) _teamRoundScores[t.teamIndex] = 0;
            }

            GameLog.Match.Info(
                $"[SetManager] Сет начат, раундов: {_roundsPerSet}");

            StartNextRound();
        }

        /// <summary>
        /// Шаг сета. Единственное место, где раунд переходит в раунд, а сет — в конец.
        /// Зовётся из <c>EliminationMode.ServerTick</c>.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_setFinished) return;

            RoundTickResult tick = _roundManager.Tick(deltaTime);

            // Исход раунда известен с момента входа в Resolution — с него и начинается пауза.
            if (tick.PhaseChanged && tick.To == RoundState.Resolution)
                ScoreRound(_roundManager.RoundWinner);

            // Итоги показаны. Решаем, что дальше: ещё раунд или конец сета.
            if (tick.CycleCompleted)
                DecideAfterScoreboard();
        }

        /// <summary>Начисляет очко за раунд и оповещает клиентов об исходе.</summary>
        private void ScoreRound(TeamData winner)
        {
            if (winner != null && _teamRoundScores.ContainsKey(winner.teamIndex))
            {
                _teamRoundScores[winner.teamIndex]++;
            }

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info(
                $"[SetManager] Раунд {_currentRound}/{_roundsPerSet} завершён, победитель: {winnerName}");

            _eliminationMode.RpcOnRoundEnded(winner != null ? winner.teamIndex : -1);
        }

        /// <summary>
        /// Экран итогов показан целиком. Либо сет продолжается новым раундом,
        /// либо здесь и заканчивается.
        ///
        /// Победитель считается в два прохода: сначала максимум очков, потом — сколько
        /// команд его набрали. Одним проходом это писать нельзя: сравнение с текущим
        /// максимумом на первой же итерации даёт <c>0 == 0</c> и взводит ничью до того,
        /// как посчитан хоть один результат, а дальше исход зависит от порядка обхода
        /// словаря, который в C# не гарантирован (MATCH-04).
        /// </summary>
        private void DecideAfterScoreboard()
        {
            int roundsToWin = _roundsPerSet / 2 + 1;

            // Проход 1 — максимум очков за раунды.
            int highestRounds = 0;
            foreach (var kvp in _teamRoundScores)
            {
                if (kvp.Value > highestRounds) highestRounds = kvp.Value;
            }

            // Проход 2 — сколько команд набрали этот максимум и кто первая из них.
            int leadersCount = 0;
            TeamData leader = null;
            foreach (var kvp in _teamRoundScores)
            {
                if (kvp.Value != highestRounds) continue;

                leadersCount++;
                if (leader == null) leader = FindTeam(kvp.Key);
            }

            // Ничья — это либо несколько лидеров, либо нулевой максимум: если никто
            // не выиграл ни одного раунда, победителя у сета нет даже при одной команде.
            TeamData setWinner = highestRounds > 0 && leadersCount == 1 ? leader : null;

            if (highestRounds >= roundsToWin || _currentRound >= _roundsPerSet)
            {
                FinishSet(setWinner);
            }
            else
            {
                StartNextRound();
            }
        }

        /// <summary>
        /// Команда сета по её индексу. Ищется в составе, переданном в <see cref="StartSet"/>,
        /// а не в глобальном <c>TeamRegistry</c>: сет обязан считаться от того состава,
        /// с которым его начали. Через реестр отсутствующий индекс молча превращался
        /// в <c>null</c>, то есть в «ничью», а сам подсчёт нельзя было проверить
        /// без загруженного реестра (T-08).
        /// </summary>
        private TeamData FindTeam(int teamIndex)
        {
            foreach (TeamData team in _teams)
            {
                if (team != null && team.teamIndex == teamIndex) return team;
            }
            return null;
        }

        /// <summary>
        /// Запускает следующий раунд. Только отсюда растёт счётчик раундов и уходит
        /// <c>RpcOnRoundStarted</c>, поэтому обходить этот метод нельзя (MATCH-06).
        /// </summary>
        private void StartNextRound()
        {
            _currentRound++;
            GameLog.Match.Info(
                $"[SetManager] Раунд {_currentRound}/{_roundsPerSet}");

            _eliminationMode.ServerBeginRound(_currentRound);
            _roundManager.StartRound(_teams, _countdownDuration, _roundDuration);
            _eliminationMode.PrepareNextRound();
        }

        private void FinishSet(TeamData winner)
        {
            _setFinished = true;

            string winnerName = winner != null ? winner.displayName : "ничья";
            GameLog.Match.Info(
                $"[SetManager] Сет завершён, победитель: {winnerName}");

            _eliminationMode.RpcOnSetEnded(winner != null ? winner.teamIndex : -1);

            _onSetEnded?.Invoke(winner);
        }

        /// <summary>Смена сторон (опционально для будущих реализаций N-команд).</summary>
        public void SwapTeams()
        {
            GameLog.Match.Info(
                $"[SetManager] Смена сторон вызвана, но физическая логика смены спавнов пока не реализована.");
        }

        /// <summary>Принудительно останавливает сет. Вызывается EliminationMode.StopGameplay().</summary>
        public void ForceStop()
        {
            _setFinished = true;
            _roundManager.ForceStop();
            GameLog.Match.Info("[SetManager] Сет принудительно остановлен");
        }
    }
}
