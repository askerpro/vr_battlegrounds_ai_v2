using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Автобаланс команд режима — временная раздача этапа А (<see cref="TeamAutoBalance"/>).
    ///
    /// <para>
    /// Что доказывает. Лобби стало режимом со своей командой «Лобби», поэтому на карту
    /// игроки приходят без команды матча. До ручного выбора (этап Б) режим раскладывает
    /// их сам: поровну, не трогая тех, у кого команда режима уже есть, и одинаково
    /// от прогона к прогону. Тот же расчёт в лобби с одной командой отдаёт её всем.
    /// </para>
    ///
    /// Игроки — строки: планировщик не знает о сессиях, команда игрока подаётся функцией.
    /// </summary>
    public class TeamAutoBalanceTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private TeamData Team(string name, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.displayName = name;
            team.teamIndex = index;
            _created.Add(team);
            return team;
        }

        private static Dictionary<string, int> Apply(
            IReadOnlyList<KeyValuePair<string, TeamData>> plan, Dictionary<string, int> players)
        {
            var result = new Dictionary<string, int>(players);
            foreach (var pair in plan) result[pair.Key] = pair.Value.teamIndex;
            return result;
        }

        [Test]
        public void Игроки_без_команды_делятся_поровну()
        {
            TeamData a = Team("A", 1), b = Team("B", 2);
            var players = new Dictionary<string, int> { { "p1", 0 }, { "p2", 0 }, { "p3", 0 }, { "p4", 0 } };

            var plan = TeamAutoBalance.Plan(new[] { a, b }, players.Keys, p => players[p]);
            var after = Apply(plan, players);

            Assert.AreEqual(4, plan.Count, "Каждый игрок без команды обязан её получить.");
            Assert.AreEqual(2, after.Values.Count(t => t == 1), "Команды неравны: " + string.Join(", ", after));
            Assert.AreEqual(2, after.Values.Count(t => t == 2), "Команды неравны: " + string.Join(", ", after));
        }

        [Test]
        public void Команда_из_чужого_режима_считается_отсутствием_команды()
        {
            // Пришли из лобби: у всех команда «Лобби», которой в матче нет.
            TeamData a = Team("A", 1), b = Team("B", 2);
            var players = new Dictionary<string, int> { { "p1", 3 }, { "p2", 3 }, { "p3", 3 } };

            var plan = TeamAutoBalance.Plan(new[] { a, b }, players.Keys, p => players[p]);
            var after = Apply(plan, players);

            Assert.AreEqual(3, plan.Count);
            Assert.IsTrue(after.Values.All(t => t == 1 || t == 2), "Кто-то остался в команде лобби: " + string.Join(", ", after));
            Assert.LessOrEqual(Mathf.Abs(after.Values.Count(t => t == 1) - after.Values.Count(t => t == 2)), 1);
        }

        [Test]
        public void Уже_стоящие_в_команде_режима_не_двигаются_но_учитываются()
        {
            TeamData a = Team("A", 1), b = Team("B", 2);
            var players = new Dictionary<string, int> { { "old1", 1 }, { "old2", 1 }, { "new1", 0 }, { "new2", 0 } };

            var plan = TeamAutoBalance.Plan(new[] { a, b }, players.Keys, p => players[p]);

            Assert.IsFalse(plan.Any(p => p.Key.StartsWith("old")), "Автобаланс переставил игрока, у которого команда режима уже есть.");
            Assert.IsTrue(plan.All(p => p.Value == b),
                "В A уже двое — оба новичка обязаны уйти в B: " + string.Join(", ", plan.Select(p => p.Key + "→" + p.Value.displayName)));
        }

        [Test]
        public void Одна_команда_достаётся_всем()
        {
            TeamData lobby = Team("Лобби", 3);
            var players = new Dictionary<string, int> { { "p1", 0 }, { "p2", 1 }, { "p3", 2 } };

            var plan = TeamAutoBalance.Plan(new[] { lobby }, players.Keys, p => players[p]);

            Assert.AreEqual(3, plan.Count);
            Assert.IsTrue(plan.All(p => p.Value == lobby));
        }

        [Test]
        public void Результат_детерминирован()
        {
            TeamData a = Team("A", 1), b = Team("B", 2);
            var players = new Dictionary<string, int> { { "p1", 0 }, { "p2", 0 }, { "p3", 0 } };

            var first = TeamAutoBalance.Plan(new[] { a, b }, players.Keys, p => players[p]).Select(p => p.Key + p.Value.teamIndex);
            var second = TeamAutoBalance.Plan(new[] { a, b }, players.Keys, p => players[p]).Select(p => p.Key + p.Value.teamIndex);

            CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
            CollectionAssert.AreEqual(new[] { "p11", "p22", "p31" }, first.ToArray(),
                "При равенстве игрок уходит в команду, что раньше в списке.");
        }

        [Test]
        public void Скин_сохраняется_если_он_есть_в_новой_команде()
        {
            AvatarData x = ScriptableObject.CreateInstance<AvatarData>(); _created.Add(x);
            AvatarData y = ScriptableObject.CreateInstance<AvatarData>(); _created.Add(y);
            AvatarData z = ScriptableObject.CreateInstance<AvatarData>(); _created.Add(z);

            TeamData team = Team("A", 1);
            team.avatars = new List<AvatarData> { y, x };

            Assert.AreEqual(1, team.IndexOfAvatar(x), "Скин есть в новой команде — индекс обязан указывать на него.");
            Assert.AreEqual(0, team.IndexOfAvatar(z), "Скина в команде нет — первый скин команды.");
            Assert.AreEqual(0, team.IndexOfAvatar(null));
        }
    }
}
