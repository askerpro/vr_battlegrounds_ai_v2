using NUnit.Framework;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Что и с каким приоритетом говорят часы на события игры (<see cref="WatchNotificationTexts"/>, T-46).
    /// Раньше — <c>HudNotificationTextsTests</c> информационного HUD; правила фаз, смертей и напоминаний те же,
    /// добавлены деньги, пауза, мало времени, итог раунда и карты для своей команды.
    /// </summary>
    public class WatchNotificationTextsTests
    {
        // ── Фазы ──────────────────────────────────────────────

        [TestCase(RoundPhase.Setup)]
        [TestCase(RoundPhase.Equipment)]
        [TestCase(RoundPhase.Countdown)]
        [TestCase(RoundPhase.Combat)]
        [TestCase(RoundPhase.Scoreboard)]
        public void Смена_фазы_видна_на_часах(RoundPhase state)
        {
            Assert.IsFalse(WatchNotificationTexts.Phase(state, null).IsEmpty, $"{state}: нет нотификации");
        }

        [Test]
        public void Итог_боя_без_нотификации_чтобы_не_перебить_победителя()
        {
            // Победителя тем же моментом сообщает RoundEndedLocal (RoundEnded) — у него и звук, и вибрация.
            Assert.IsTrue(WatchNotificationTexts.Phase(RoundPhase.Resolution, null).IsEmpty);
        }

        [Test]
        public void Итоги_раунда_показывают_счёт()
        {
            StringAssert.Contains("2 : 1", WatchNotificationTexts.Phase(RoundPhase.Scoreboard, "2 : 1").Text);
        }

        // ── Смерти ────────────────────────────────────────────

        private static KillNotice Kill(uint victim, int victimTeam, uint killer, int killerTeam) =>
            new KillNotice(victim, "V" + victim, victimTeam, killer, killer != 0 ? "K" + killer : "", killerTeam);

        [Test]
        public void Своя_смерть_тревога_высшего_приоритета_и_имя_убийцы()
        {
            WatchNotification m = WatchNotificationTexts.Kill(Kill(10, 1, 20, 2), localSessionNetId: 10, localTeam: 1);
            Assert.AreEqual(WatchSound.Alert, m.Sound);
            Assert.AreEqual(WatchPriority.Critical, m.Priority);
            StringAssert.Contains("Вы погибли", m.Text);
            StringAssert.Contains("K20", m.Text);
        }

        [Test]
        public void Своё_убийство_важнее_чужих()
        {
            WatchNotification mine = WatchNotificationTexts.Kill(Kill(10, 2, 20, 1), localSessionNetId: 20, localTeam: 1);
            WatchNotification other = WatchNotificationTexts.Kill(Kill(10, 2, 30, 1), localSessionNetId: 20, localTeam: 1);
            StringAssert.Contains("Вы убили V10", mine.Text);
            Assert.Greater(mine.Priority, other.Priority);
        }

        [Test]
        public void Смерть_союзника_и_противника_различаются()
        {
            StringAssert.StartsWith("Союзник", WatchNotificationTexts.Kill(Kill(10, 1, 20, 2), 30, 1).Text);
            StringAssert.StartsWith("Противник", WatchNotificationTexts.Kill(Kill(10, 2, 20, 1), 30, 1).Text);
        }

        [Test]
        public void Самоубийство_без_убийцы()
        {
            Assert.AreEqual("Вы погибли", WatchNotificationTexts.Kill(Kill(10, 1, 10, 1), 10, 1).Text);
        }

        // ── Напоминания ───────────────────────────────────────

        [TestCase(false, false, true, TestName = "Мёртвый вне зоны — вернуться")]
        [TestCase(false, true, false, TestName = "Мёртвый в своей зоне — молчать")]
        [TestCase(true, false, false, TestName = "Живой вне зоны — молчать")]
        public void Напоминание_вернуться_на_базу(bool alive, bool inOwnZone, bool expected)
        {
            Assert.AreEqual(expected, WatchNotificationTexts.AskReturnToBase(alive, inOwnZone));
        }

        /// <summary>
        /// Игрок без команды матча (ещё не выбрал или у него «Разминка») в матче выбывший, но
        /// своей зоны у него нет: «вернитесь в свою зону» его никуда не ведёт — его просят выбрать
        /// команду. Раньше на TestMap1 игрок с «Разминкой» висел призраком с «вернитесь в зону».
        /// </summary>
        [TestCase(false, false, RoundPhase.Setup, TestName = "Без команды, мёртвый вне зоны — выбрать команду")]
        [TestCase(true, false, RoundPhase.Countdown, TestName = "Без команды, живой на отсчёте — выбрать команду")]
        public void Без_команды_матча_просим_выбрать_команду(bool alive, bool inOwnZone, RoundPhase phase)
        {
            Assert.AreEqual(WatchReminder.ChooseTeam,
                WatchNotificationTexts.Reminder(hasModeTeam: false, alive, inOwnZone, phase));
        }

        [Test]
        public void С_командой_матча_напоминания_прежние()
        {
            Assert.AreEqual(WatchReminder.ReturnToBase, WatchNotificationTexts.Reminder(true, false, false, RoundPhase.Setup));
            Assert.AreEqual(WatchReminder.ReturnForCountdown, WatchNotificationTexts.Reminder(true, true, false, RoundPhase.Countdown));
            Assert.AreEqual(WatchReminder.None, WatchNotificationTexts.Reminder(true, false, true, RoundPhase.Setup));
        }

        [Test]
        public void Напоминания_одним_ключом_и_неважные()
        {
            WatchNotification[] reminders =
            {
                WatchNotificationTexts.ChooseTeam(),
                WatchNotificationTexts.ReturnToBase(),
                WatchNotificationTexts.ReturnForCountdown()
            };

            foreach (WatchNotification r in reminders)
            {
                Assert.AreEqual(WatchNotificationTexts.ReminderKey, r.Key, r.Text);
                Assert.AreEqual(WatchPriority.Low, r.Priority, r.Text);
            }
        }

        // ── Итоги ─────────────────────────────────────────────

        [Test]
        public void Итог_раунда_для_своей_команды()
        {
            StringAssert.Contains("выигран", WatchNotificationTexts.RoundEnded(winnerTeam: 1, "CT", localTeam: 1).Text);
            StringAssert.Contains("проигран", WatchNotificationTexts.RoundEnded(winnerTeam: 2, "T", localTeam: 1).Text);
            StringAssert.Contains("ничь", WatchNotificationTexts.RoundEnded(winnerTeam: null, null, localTeam: 1).Text.ToLowerInvariant());
            Assert.AreEqual(WatchPriority.High, WatchNotificationTexts.RoundEnded(1, "CT", 1).Priority);
        }

        [Test]
        public void Итог_карты_высшего_приоритета()
        {
            WatchNotification win = WatchNotificationTexts.MapFinished(winnerTeam: 1, "CT", localTeam: 1);
            WatchNotification loss = WatchNotificationTexts.MapFinished(winnerTeam: 2, "T", localTeam: 1);
            StringAssert.Contains("Победа", win.Text);
            StringAssert.Contains("Поражение", loss.Text);
            Assert.AreEqual(WatchPriority.Critical, win.Priority);
        }

        // ── Деньги ────────────────────────────────────────────

        private static EconomyTransaction Tx(int delta, EconomyReason reason, string detail = null) =>
            new EconomyTransaction("me", delta, 800 + delta, reason, detail);

        [Test]
        public void Доход_со_звуком_денег_и_знаком()
        {
            WatchNotification win = WatchNotificationTexts.Money(Tx(3250, EconomyReason.RoundWin));
            Assert.AreEqual(WatchSound.Money, win.Sound);
            StringAssert.StartsWith("+$3250", win.Text);
            StringAssert.Contains("победа", win.Text);
        }

        [Test]
        public void Покупка_со_знаком_минус_и_названием()
        {
            WatchNotification buy = WatchNotificationTexts.Money(Tx(-2900, EconomyReason.Purchase, "TR15"));
            StringAssert.StartsWith("-$2900", buy.Text);
            StringAssert.Contains("TR15", buy.Text);
            Assert.AreEqual(WatchPriority.Low, buy.Priority, "покупку игрок сделал сам — не перебивать ею игру");
        }

        [Test]
        public void Новая_половина_показывает_деньги_а_не_разницу()
        {
            var reset = new EconomyTransaction("me", -4200, 800, EconomyReason.HalfReset, null);
            StringAssert.Contains("$800", WatchNotificationTexts.Money(reset).Text);
        }

        [Test]
        public void Каждая_причина_денег_даёт_текст()
        {
            foreach (EconomyReason reason in System.Enum.GetValues(typeof(EconomyReason)))
                Assert.IsFalse(WatchNotificationTexts.Money(Tx(100, reason, "X")).IsEmpty, reason.ToString());
        }

        // ── Пауза, время ──────────────────────────────────────

        [Test]
        public void Пауза_и_продолжение_различаются()
        {
            Assert.AreNotEqual(WatchNotificationTexts.Paused(true).Text, WatchNotificationTexts.Paused(false).Text);
            Assert.AreEqual(WatchPriority.High, WatchNotificationTexts.Paused(true).Priority);
        }

        [Test]
        public void Мало_времени_срабатывает_при_пересечении_порога()
        {
            Assert.AreEqual(30, WatchNotificationTexts.CrossedLowTime(30.4f, 29.9f));
            Assert.AreEqual(10, WatchNotificationTexts.CrossedLowTime(10.2f, 9.8f));
            Assert.AreEqual(0, WatchNotificationTexts.CrossedLowTime(25f, 24f), "между порогами молчим");
            Assert.AreEqual(0, WatchNotificationTexts.CrossedLowTime(29f, 31f), "время выросло (новая фаза) — не порог");
            Assert.AreEqual(0, WatchNotificationTexts.CrossedLowTime(float.NaN, 5f), "первый кадр — не пересечение");
        }

        [Test]
        public void Мало_времени_говорит_сколько()
        {
            StringAssert.Contains("30", WatchNotificationTexts.LowTime(30).Text);
        }

        [Test]
        public void У_каждой_нотификации_есть_срок_показа()
        {
            WatchNotification[] all =
            {
                WatchNotificationTexts.ModeStarted(), WatchNotificationTexts.SidesSwapped(),
                WatchNotificationTexts.Respawned(), WatchNotificationTexts.LowTime(10),
                WatchNotificationTexts.Paused(true), WatchNotificationTexts.Paused(false),
                WatchNotificationTexts.ChooseTeam(), WatchNotificationTexts.ReturnToBase(),
                WatchNotificationTexts.ReturnForCountdown(),
                WatchNotificationTexts.RoundEnded(1, "CT", 1), WatchNotificationTexts.MapFinished(null, null, 1),
                WatchNotificationTexts.Money(Tx(300, EconomyReason.Kill, "Viper"))
            };

            foreach (WatchNotification n in all)
            {
                Assert.IsFalse(n.IsEmpty);
                Assert.Greater(n.Duration, 0f, n.Text);
            }
        }
    }
}
