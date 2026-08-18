using System;
using NUnit.Framework;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Срок годности снимков отключившихся игроков — находка NET-10, задача T-20.
    ///
    /// Что доказывают тесты. Запись из <c>_disconnectedSessions</c> удалялась ровно в одном
    /// случае: тот же <c>deviceToken</c> вернулся и сессия восстановилась. Игрок, ушедший
    /// насовсем, оставлял снимок навсегда, и хранилище росло всё время жизни сервера.
    /// Отдельная неприятность — снимок годичной давности применялся к новому подключению
    /// как ни в чём не бывало.
    ///
    /// Часы менеджера подменяются: иначе проверка срока годности стоила бы теста,
    /// который семь минут ничего не делает.
    /// </summary>
    public class SessionRecoveryTtlTests : MirrorTestHarness
    {
        private const float LifetimeMinutes = 5f;

        private SessionRecoveryManager _recovery;
        private PlayerSession _session;
        private double _now;

        [SetUp]
        public void PrepareRecovery()
        {
            SilenceMirrorNoise();

            _now = 1000.0; // не ноль: так видно, что сравнивается разница, а не абсолют

            _recovery = CreateManager<SessionRecoveryManager>("SessionRecoveryManager");
            SetPrivateField(_recovery, "_snapshotLifetimeMinutes", LifetimeMinutes);
            SetPrivateField(_recovery, "_timeSource", (Func<double>)(() => _now));

            _session = CreateNetworkComponent<PlayerSession>("Session");
            SpawnOnServer(_session);
            _session.PlayerName = "Rambo";
            _session.TeamIndex = 1;
        }

        /// <summary>Прокрутка часов менеджера, минуты.</summary>
        private void AdvanceMinutes(double minutes)
        {
            _now += minutes * 60.0;
        }

        // ── Срок годности ───────────────────────────────────────────────────

        [Test]
        public void Снимок_старше_срока_годности_не_восстанавливается()
        {
            SilenceMirrorNoise();

            _recovery.SaveDisconnectedSession("token-gone", _session, null);
            AdvanceMinutes(LifetimeMinutes + 1.0);

            Assert.IsNull(_recovery.GetAndRemoveSavedSession("token-gone"),
                "Игрок вернулся спустя срок годности, и сервер применил к нему снимок " +
                "прошлого матча — счёт, команду и место гибели.");
        }

        [Test]
        public void Снимок_в_пределах_срока_годности_восстанавливается()
        {
            SilenceMirrorNoise();

            _recovery.SaveDisconnectedSession("token-back", _session, null);
            AdvanceMinutes(LifetimeMinutes - 1.0);

            SessionSnapshot snapshot = _recovery.GetAndRemoveSavedSession("token-back");

            Assert.IsNotNull(snapshot,
                "Игрок вернулся вовремя, а сессию ему не вернули — уборка стала слишком жадной.");
            Assert.AreEqual("Rambo", snapshot.PlayerName, "Вернулся не тот снимок.");
        }

        [Test]
        public void Протухшие_снимки_уходят_из_хранилища_сами()
        {
            SilenceMirrorNoise();

            _recovery.SaveDisconnectedSession("token-old-1", _session, null);
            _recovery.SaveDisconnectedSession("token-old-2", _session, null);
            Assert.AreEqual(2, _recovery.StoredSnapshotsCount, "Контроль: оба снимка сохранились.");

            AdvanceMinutes(LifetimeMinutes + 1.0);
            _recovery.SaveDisconnectedSession("token-fresh", _session, null);

            Assert.AreEqual(1, _recovery.StoredSnapshotsCount,
                "Старые снимки остались в словаре. Записи удаляются только при удачном " +
                "переподключении того же устройства, значит ушедшие насовсем копятся вечно.");
        }

        // ── Потолок размера ─────────────────────────────────────────────────

        [Test]
        public void Хранилище_не_растёт_дальше_потолка()
        {
            SilenceMirrorNoise();

            SetPrivateField(_recovery, "_maxStoredSnapshots", 3);

            // Пять отключений подряд, все в пределах срока годности:
            // срок тут ни при чём, обрезать обязан именно потолок.
            for (int i = 0; i < 5; i++)
            {
                _recovery.SaveDisconnectedSession("token-" + i, _session, null);
                AdvanceMinutes(0.01);
            }

            Assert.AreEqual(3, _recovery.StoredSnapshotsCount,
                "Потолок размера не работает: хранилище отключённых сессий ничем не ограничено.");

            Assert.IsNull(_recovery.GetAndRemoveSavedSession("token-0"),
                "Выбрасывать надо самые старые записи — token-0 сохранён первым.");
            Assert.IsNotNull(_recovery.GetAndRemoveSavedSession("token-4"),
                "Последний сохранённый снимок обязан пережить уборку — иначе игрок, " +
                "отключившийся только что, вернуться не сможет.");
        }

        [Test]
        public void Нулевой_срок_годности_отключает_уборку()
        {
            SilenceMirrorNoise();

            SetPrivateField(_recovery, "_snapshotLifetimeMinutes", 0f);

            _recovery.SaveDisconnectedSession("token-forever", _session, null);
            AdvanceMinutes(600.0);

            Assert.IsNotNull(_recovery.GetAndRemoveSavedSession("token-forever"),
                "Ноль в инспекторе означает «без срока годности» — снимок пропадать не должен.");
        }
    }
}
