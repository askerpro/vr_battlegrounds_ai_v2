using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Что пишут табло лазерной сетки (T-47) в какой фазе: крупный отсчёт, готовность, фаза и время.
    /// Нотификаций часов (убийства, деньги) на табло нет.
    /// </summary>
    public class LaserGridBoardRulesTests
    {
        private static LaserGridRoundInfo Round(LaserGridBoardPhase phase, int seconds = -1) => new LaserGridRoundInfo
        {
            Phase = phase, Seconds = seconds, Round = 3, TotalRounds = 8, HasScore = true, Own = 2, Enemy = 1,
            TeamPlayers = 4, TeamPending = 0
        };

        // ── Фаза ──────────────────────────────────────────────

        [TestCase(false, true, EliminationState.Active, RoundPhase.Equipment, LaserGridBoardPhase.None)]
        [TestCase(true, false, EliminationState.Active, RoundPhase.Equipment, LaserGridBoardPhase.OtherMode)]
        [TestCase(true, true, EliminationState.WaitingForPlayers, RoundPhase.Combat, LaserGridBoardPhase.WaitingForPlayers)]
        [TestCase(true, true, EliminationState.Finished, RoundPhase.Scoreboard, LaserGridBoardPhase.MapOver)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Setup, LaserGridBoardPhase.Setup)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Equipment, LaserGridBoardPhase.Equipment)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Countdown, LaserGridBoardPhase.Countdown)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Combat, LaserGridBoardPhase.Combat)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Resolution, LaserGridBoardPhase.RoundOver)]
        [TestCase(true, true, EliminationState.Active, RoundPhase.Scoreboard, LaserGridBoardPhase.RoundOver)]
        public void Фаза_табло_по_режиму(bool hasMode, bool elimination, EliminationState state, RoundPhase phase,
                                         LaserGridBoardPhase expected)
        {
            Assert.AreEqual(expected, LaserGridBoardRules.Classify(hasMode, elimination, state, phase));
        }

        // ── Крупное ───────────────────────────────────────────

        [Test]
        public void Отсчёт_крупно_секундами()
        {
            LaserGridBoardText text = LaserGridBoardRules.Common(Round(LaserGridBoardPhase.Countdown, 3), null);
            Assert.AreEqual("3", text.Big);
            Assert.AreEqual("ПРИГОТОВЬТЕСЬ!", text.Title);
        }

        [Test]
        public void Закупка_крупно_остаток_закупки()
        {
            LaserGridBoardText text = LaserGridBoardRules.Common(Round(LaserGridBoardPhase.Equipment, 45), null);
            Assert.AreEqual("0:45", text.Big);
            StringAssert.Contains("РАУНД 3 ИЗ 8", text.Title);
            StringAssert.Contains("ЗАКУПКА", text.Title);
        }

        [Test]
        public void Закупка_без_предела_крупного_нет()
        {
            Assert.AreEqual(string.Empty, LaserGridBoardRules.Common(Round(LaserGridBoardPhase.Equipment), null).Big);
        }

        [Test]
        public void Бой_время_и_счёт()
        {
            LaserGridBoardText text = LaserGridBoardRules.Common(Round(LaserGridBoardPhase.Combat, 95), null);
            Assert.AreEqual("1:35", text.Big);
            Assert.AreEqual("Счёт 2 : 1", text.Detail);
        }

        [Test]
        public void Итог_раунда_крупно_счёт()
        {
            Assert.AreEqual("2 : 1", LaserGridBoardRules.Common(Round(LaserGridBoardPhase.RoundOver, 4), null).Big);
        }

        // ── Готовность ────────────────────────────────────────

        [Test]
        public void Закупка_сколько_готовы_и_кого_ждём()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Equipment, 30);
            r.TeamPending = 2;
            string detail = LaserGridBoardRules.Common(r, new List<string> { "Петя", "Вася" }).Detail;

            StringAssert.Contains("Готовы 2 из 4", detail);
            StringAssert.Contains("ждём: Петя, Вася", detail);
        }

        [Test]
        public void Ждём_многих_показываем_троих_и_счёт_остальных()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Equipment, 30);
            r.TeamPlayers = 5;
            r.TeamPending = 5;
            string detail = LaserGridBoardRules.Common(r, new List<string> { "A", "B", "C", "D", "E" }).Detail;

            StringAssert.Contains("ждём: A, B, C и ещё 2", detail);
            StringAssert.DoesNotContain("D", detail);
        }

        [Test]
        public void Своя_команда_готова_ждём_соперника()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Equipment, 30);
            r.EnemyPending = 1;
            StringAssert.Contains("ждём соперника (1)", LaserGridBoardRules.Common(r, null).Detail);
        }

        [Test]
        public void Старт_по_таймеру_готовность_не_спрашивается()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Equipment, 30);
            r.ByTimer = true;
            r.TeamPending = 2;
            string detail = LaserGridBoardRules.Common(r, new List<string> { "Петя" }).Detail;

            StringAssert.Contains("по таймеру", detail);
            StringAssert.DoesNotContain("Петя", detail);
        }

        [Test]
        public void Отсчёт_стоит_предупреждение()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Countdown, 5);
            r.CountdownHeld = true;
            StringAssert.Contains("Отсчёт стоит", LaserGridBoardRules.Common(r, null).Detail);
        }

        // ── Персональный кусок ────────────────────────────────

        private static LaserGridOwnerInfo Owner(bool pending, bool inZone = true, bool alive = true, bool local = false) =>
            new LaserGridOwnerInfo { HasOwner = true, Name = "Петя", Pending = pending, InZone = inZone, Alive = alive, IsLocal = local };

        [Test]
        public void Персональный_чья_стена_и_тот_же_отсчёт()
        {
            LaserGridRoundInfo r = Round(LaserGridBoardPhase.Countdown, 2);
            LaserGridBoardText text = LaserGridBoardRules.Personal(r, Owner(false, local: true));

            Assert.AreEqual("ПЕТЯ · ВЫ", text.Title);
            Assert.AreEqual(LaserGridBoardRules.Common(r, null).Big, text.Big, "Отсчёт на куске другой, чем на общих табло.");
        }

        [Test]
        public void Персональный_готов()
        {
            StringAssert.Contains("ГОТОВ", LaserGridBoardRules.Personal(Round(LaserGridBoardPhase.Equipment, 20), Owner(false)).Detail);
        }

        [TestCase(true, true, "жетон", TestName = "Персональный_в_зоне_не_готов_зовёт_взять_жетон")]
        [TestCase(false, true, "Вне зоны", TestName = "Персональный_вне_зоны")]
        [TestCase(false, false, "Выбыл", TestName = "Персональный_выбыл_идёт_на_базу")]
        public void Персональный_почему_не_готов(bool inZone, bool alive, string expected)
        {
            string detail = LaserGridBoardRules.Personal(Round(LaserGridBoardPhase.Equipment, 20), Owner(true, inZone, alive)).Detail;
            StringAssert.Contains(expected, detail);
            StringAssert.DoesNotContain("ГОТОВ", detail);
        }

        [Test]
        public void Ничья_стена_стена_команды()
        {
            Assert.AreEqual("СТЕНА КОМАНДЫ",
                LaserGridBoardRules.Personal(Round(LaserGridBoardPhase.Equipment, 20), default).Title);
        }

        // ── Перерисовка только при изменении ─────────────────

        [Test]
        public void Снимок_равен_себе_и_меняется_с_секундой()
        {
            LaserGridRoundInfo a = Round(LaserGridBoardPhase.Countdown, 3);
            LaserGridRoundInfo b = a;
            Assert.IsTrue(a.Equals(b));
            b.Seconds = 2;
            Assert.IsFalse(a.Equals(b), "Секунда сменилась, а табло не перерисуется.");
            b = a;
            b.PendingKey = 17;
            Assert.IsFalse(a.Equals(b), "Состав ожидаемых сменился, а имена на табло старые.");
        }

        [Test]
        public void Строка_табло_содержит_все_три_части()
        {
            string rich = new LaserGridBoardText("ЗАГОЛОВОК", "0:30", "пояснение").ToRichText();
            StringAssert.Contains("ЗАГОЛОВОК", rich);
            StringAssert.Contains("0:30", rich);
            StringAssert.Contains("пояснение", rich);
        }
    }
}
