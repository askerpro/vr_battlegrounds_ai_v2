using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests
{
    /// <summary>
    /// Подставной реестр игроков для тестов машины раунда.
    ///
    /// Живой аватар в EditMode не поднимается (см. <c>Docs/testing.md</c>, «Чего харнесс
    /// не умеет»), а без него <c>PlayersManager.GetAlivePlayers</c> всегда пуст и раунд
    /// навсегда застревает в фазе <c>Equipment</c>. Заглушка отвечает на единственный
    /// вопрос, который машина задаёт реестру: кто жив и готов.
    /// </summary>
    public sealed class StubPlayerRoster : IPlayerRoster
    {
        private readonly Dictionary<int, List<PlayerSession>> _byTeam =
            new Dictionary<int, List<PlayerSession>>();

        public void Add(TeamData team, PlayerSession session)
        {
            if (team == null || session == null) return;

            if (!_byTeam.TryGetValue(team.teamIndex, out List<PlayerSession> list))
            {
                list = new List<PlayerSession>();
                _byTeam[team.teamIndex] = list;
            }
            list.Add(session);
        }

        /// <summary>
        /// Все игроки команды. В заглушке совпадает с живыми: мёртвых сюда не кладут,
        /// а вопрос «сколько всего в команде» задаёт <c>TeamRuntimeData</c>.
        /// </summary>
        public IEnumerable<PlayerSession> GetPlayers(TeamData team)
        {
            return GetAlivePlayers(team);
        }

        public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team)
        {
            if (team == null) return new PlayerSession[0];

            return _byTeam.TryGetValue(team.teamIndex, out List<PlayerSession> list)
                ? (IEnumerable<PlayerSession>)list
                : new PlayerSession[0];
        }

        /// <summary>Все положенные в заглушку сессии — тем же числом, что видит режим.</summary>
        public IEnumerable<PlayerSession> GetAllPlayers()
        {
            foreach (List<PlayerSession> list in _byTeam.Values)
            {
                foreach (PlayerSession session in list) yield return session;
            }
        }
    }

    /// <summary>
    /// Прокрутка матча фиксированным шагом времени с записью наблюдённых фаз.
    ///
    /// Шаг 0.25 с выбран не случайно: это двоично точное число, поэтому сумма шагов
    /// не «уползает» и число тиков в фазе точно равно длительность / шаг. С шагом 0.1
    /// накопление 30 раз даёт 2.9999998 и тест на длительность врал бы на один тик.
    ///
    /// Как тикать и где читать фазу — задаёт вызывающий тест: одни тесты гоняют
    /// боевой путь <c>EliminationMode.ServerTick</c>, другие — голый <c>SetManager.Tick</c>.
    /// </summary>
    public sealed class RoundFlowDriver
    {
        /// <summary>Шаг тика, секунды.</summary>
        public const float Step = 0.25f;

        /// <summary>Потолок прокрутки: 4000 шагов — 1000 с игрового времени.</summary>
        private const int MaxSteps = 4000;

        private readonly Action<float> _tick;
        private readonly Func<RoundState> _phase;
        private readonly List<RoundState> _samples = new List<RoundState>();

        public RoundFlowDriver(Action<float> tick, Func<RoundState> phase)
        {
            _tick = tick;
            _phase = phase;
        }

        /// <summary>Фаза, наблюдённая после каждого шага, по порядку.</summary>
        public IReadOnlyList<RoundState> Samples => _samples;

        /// <summary>Сколько шагов длится фаза при шаге <see cref="Step"/>.</summary>
        public static int StepsFor(float duration)
        {
            return Mathf.RoundToInt(duration / Step);
        }

        /// <summary>Один шаг матча.</summary>
        public void Advance()
        {
            _tick(Step);
            _samples.Add(_phase());
        }

        /// <summary>
        /// Крутит матч, пока условие не выполнится. Условие проверяется до первого шага:
        /// если оно уже верно, ни одного тика не делается.
        /// </summary>
        public void AdvanceUntil(Func<bool> reached, string whatWeWaitFor)
        {
            for (int i = 0; i < MaxSteps; i++)
            {
                if (reached()) return;
                Advance();
            }

            Assert.Fail("Не дождались " + whatWeWaitFor + " за " + MaxSteps + " шагов по " +
                        Step + " с.\nПройденные фазы: " + DumpSequence());
        }

        /// <summary>Последовательность фаз без повторов подряд — то, как раунд выглядел снаружи.</summary>
        public List<RoundState> PhaseSequence()
        {
            List<RoundState> sequence = new List<RoundState>();
            foreach (RoundState sample in _samples)
            {
                if (sequence.Count == 0 || sequence[sequence.Count - 1] != sample)
                    sequence.Add(sample);
            }
            return sequence;
        }

        /// <summary>
        /// Длина первого непрерывного отрезка фазы в шагах.
        /// Ноль означает, что фазу не наблюдали ни разу.
        /// </summary>
        public int FirstRunLength(RoundState state)
        {
            int start = _samples.IndexOf(state);
            if (start < 0) return 0;

            int length = 0;
            while (start + length < _samples.Count && _samples[start + length] == state) length++;
            return length;
        }

        /// <summary>Читаемый след прогона для сообщения об ошибке: «Setup ×4 → Equipment ×1 → …».</summary>
        public string DumpSequence()
        {
            if (_samples.Count == 0) return "(ни одного шага)";

            StringBuilder sb = new StringBuilder();
            RoundState current = _samples[0];
            int count = 0;

            foreach (RoundState sample in _samples)
            {
                if (sample == current)
                {
                    count++;
                    continue;
                }
                sb.Append(current).Append(" x").Append(count).Append(" -> ");
                current = sample;
                count = 1;
            }
            sb.Append(current).Append(" x").Append(count);
            return sb.ToString();
        }
    }
}
