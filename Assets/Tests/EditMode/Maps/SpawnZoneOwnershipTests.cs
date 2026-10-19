using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.SpawnZones
{
    /// <summary>
    /// Кому зона спавна засчитывает «в зоне» — находка <b>RDY-04</b>.
    ///
    /// <para>
    /// Что доказывают тесты. <c>TeamSpawnZone</c> писала признак «в зоне» <b>любому</b>
    /// вошедшему, не спрашивая команду, — хотя соседние методы того же класса команду
    /// проверяли. Следствий два, и оба про готовность к раунду (T-29): игрок, забежавший
    /// в базу противника, считался стоящим у себя на спавне и сохранял право объявить
    /// готовность оттуда; а выход из <b>чужой</b> зоны снимал признак игроку, который
    /// в этот момент уже стоял в своей.
    /// </para>
    ///
    /// <para>
    /// Признак теперь выводится из <c>PlayerSession.SpawnZoneTeamIndex</c> — «в зоне
    /// какой команды я стою», — поэтому оба вопроса получают ответ из одного места,
    /// а зона сообщает только факт и свою команду.
    /// </para>
    ///
    /// <para>
    /// Ярус A (<see cref="MirrorTestHarness"/>): нужны настоящий <c>netId</c> сессии
    /// (по нему аватар её и находит), ручной вызов <c>Awake</c> и уборка объектов
    /// между тестами.
    /// </para>
    /// </summary>
    public class SpawnZoneOwnershipTests : MirrorTestHarness
    {
        private const int OwnTeam = 1;
        private const int EnemyTeam = 2;

        // ── Заготовки ────────────────────────────────────────────────────────

        private static TeamData CreateTeam(string displayName, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.displayName = displayName;
            team.teamIndex = index;
            return team;
        }

        private TeamSpawnZone CreateZone(string name, TeamData team)
        {
            GameObject go = CreateObject(name);
            go.AddComponent<BoxCollider>();

            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);

            InvokeLifecycleMethod(zone, "Awake");
            return zone;
        }

        private PlayerSession CreateSession(string name, int teamIndex)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);

            session.PlayerName = name;
            session.TeamIndex = teamIndex;

            Assert.AreNotEqual(0u, session.netId,
                "Без netId аватар не найдёт сессию — заготовка теста сломана.");

            return session;
        }

        /// <summary>Аватар, привязанный к сессии тем же способом, что и в игре — через netId.</summary>
        private PlayerController CreateAvatar(string name, PlayerSession session)
        {
            GameObject go = CreateNetworkObject(name);
            go.AddComponent<UxrActor>(); // обязателен по RequireComponent у PlayerController
            PlayerController avatar = go.AddComponent<PlayerController>();
            EnableNetworking(go);

            avatar.SessionNetId = session.netId;

            Assert.AreSame(session, avatar.Session,
                "Аватар не разрешил свою сессию — дальше проверять нечего.");

            return avatar;
        }

        /// <summary>Зовёт то же, что зовут <c>OnTriggerStay</c> и <c>OnTriggerExit</c>.</summary>
        private static void Report(TeamSpawnZone zone, PlayerController player, bool inside)
        {
            InvokePrivateMethod(zone, "ReportZoneState", player, inside);
        }

        // ── Сама находка ─────────────────────────────────────────────────────

        [Test]
        public void Зона_противника_не_засчитывает_игроку_нахождение_в_своей_зоне()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateSession("Беглец", OwnTeam);
            session.ServerEnterSpawnZone(EnemyTeam);

            Assert.IsFalse(session.IsInSpawnZone,
                "Игрок стоит в базе противника, а считается стоящим у себя на спавне. " +
                "Это RDY-04: признак писался любому вошедшему, без проверки команды.");

            Assert.IsTrue(session.IsInEnemySpawnZone,
                "Факт «игрок в чужой базе» обязан быть виден: ради него признак и стал " +
                "индексом команды, а не булевым флагом.");
        }

        [Test]
        public void Выход_из_чужой_зоны_не_снимает_нахождение_в_своей()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateSession("Свой", OwnTeam);
            session.ServerEnterSpawnZone(OwnTeam);

            Assert.IsTrue(session.IsInSpawnZone, "Контроль: игрок вошёл в зону своей команды.");

            // Так выглядит перенос между базами: «вошёл в новую» приходит раньше,
            // чем «вышел из старой», и порядок этих событий Unity не гарантирует.
            session.ServerExitSpawnZone(EnemyTeam);

            Assert.IsTrue(session.IsInSpawnZone,
                "Выход из чужой зоны снял признак игроку, который стоит в своей. " +
                "Это обратная сторона RDY-04: признак был один на игрока, и его затирал кто угодно.");
        }

        [Test]
        public void Готовность_не_переживает_переход_в_базу_противника()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateSession("Готовый", OwnTeam);
            session.ServerEnterSpawnZone(OwnTeam);
            session.ServerSetReady(true, "тест");

            Assert.IsTrue(session.ReadyState, "Контроль: готовность объявлена у себя на спавне.");

            session.ServerEnterSpawnZone(EnemyTeam);

            Assert.IsFalse(session.IsInSpawnZone, "Игрок в базе противника — не у себя на спавне.");
            Assert.IsFalse(session.ReadyState,
                "Игрока перенесло в базу противника, а готовность осталась. Зона — условие " +
                "готовности: раунд не вправе начаться, пока игрок не у себя на спавне (T-29).");
        }

        [Test]
        public void Возврат_в_свою_зону_готовность_не_возвращает()
        {
            SilenceMirrorNoise();

            PlayerSession session = CreateSession("Вернувшийся", OwnTeam);
            session.ServerEnterSpawnZone(OwnTeam);
            session.ServerSetReady(true, "тест");

            session.ServerEnterSpawnZone(EnemyTeam);
            session.ServerEnterSpawnZone(OwnTeam);

            Assert.IsTrue(session.IsInSpawnZone, "Контроль: игрок вернулся к себе на спавн.");
            Assert.IsFalse(session.ReadyState,
                "Возврат в свою зону сам собой вернул готовность. Готовность — намерение, " +
                "и объявить его игрок обязан заново.");
        }

        // ── Зона сообщает свою команду, а не команду игрока ──────────────────

        [Test]
        public void Зона_сообщает_сессии_свою_команду_а_не_команду_игрока()
        {
            SilenceMirrorNoise();

            TeamSpawnZone enemyZone = CreateZone("ЗонаПротивника", CreateTeam("Синие", EnemyTeam));

            PlayerSession session = CreateSession("Беглец", OwnTeam);
            PlayerController avatar = CreateAvatar("АватарБеглеца", session);

            Report(enemyZone, avatar, true);

            Assert.AreEqual(EnemyTeam, session.SpawnZoneTeamIndex,
                "Зона обязана назвать себя, а не согласиться с игроком: команду зоны " +
                "решает художник на карте, а не тот, кто в неё вошёл.");

            Assert.IsFalse(session.IsInSpawnZone,
                "RDY-04 в чистом виде: зона противника засчитала игроку нахождение в своей зоне.");
        }

        [Test]
        public void Своя_зона_по_прежнему_засчитывается()
        {
            SilenceMirrorNoise();

            TeamSpawnZone ownZone = CreateZone("СвояЗона", CreateTeam("Красные", OwnTeam));

            PlayerSession session = CreateSession("Свой", OwnTeam);
            PlayerController avatar = CreateAvatar("АватарСвоего", session);

            Report(ownZone, avatar, true);

            Assert.IsTrue(session.IsInSpawnZone,
                "Контроль: без него «починка» свелась бы к тому, что в зоне не бывает никого.");

            Report(ownZone, avatar, false);

            Assert.IsFalse(session.IsInSpawnZone, "Выход из своей зоны обязан сниматься.");
            Assert.AreEqual(PlayerSession.NoSpawnZone, session.SpawnZoneTeamIndex,
                "После выхода игрок не в зоне вообще, а не «в зоне команды 0».");
        }

        // ── Тело уничтожено внутри зоны ──────────────────────────────────────
        //
        // Смерть → призрак, возрождение и выбывание меняют тело уничтожением. Unity не присылает
        // OnTriggerExit за уничтоженный коллайдер, и до правки 2026-10-19 признак «в зоне» оставался
        // у сессии навсегда. После «Играть заново» (стороны сброшены) бот считал себя на базе,
        // стоял в центре карты, а режим каждый раунд ждал его возвращения.

        private static void ReleaseDestroyed(TeamSpawnZone zone)
        {
            InvokePrivateMethod(zone, "ServerReleaseDestroyedPlayers");
        }

        [Test]
        public void Тело_уничтоженное_в_зоне_снимает_признак()
        {
            SilenceMirrorNoise();

            TeamSpawnZone ownZone = CreateZone("СвояЗона", CreateTeam("Красные", OwnTeam));
            PlayerSession session = CreateSession("Погибший", OwnTeam);
            PlayerController body = CreateAvatar("ТелоПогибшего", session);

            Report(ownZone, body, true);
            Assert.IsTrue(session.IsInSpawnZone, "Контроль: тело в своей зоне.");

            Object.DestroyImmediate(body.gameObject);
            ReleaseDestroyed(ownZone);

            Assert.AreEqual(PlayerSession.NoSpawnZone, session.SpawnZoneTeamIndex,
                "Тело уничтожено в зоне, а сессия по-прежнему «в зоне»: OnTriggerExit за уничтоженный " +
                "коллайдер не приходит, и снять признак обязана сама зона.");
        }

        [Test]
        public void Уничтоженное_тело_выходит_с_командой_входа_после_сброса_сторон()
        {
            SilenceMirrorNoise();

            TeamData home = CreateTeam("Военные", OwnTeam);
            TeamData away = CreateTeam("Повстанцы", EnemyTeam);
            try
            {
                // Вторая половина: домашняя зона «Военных» принадлежит «Повстанцам».
                SpawnSides.Set(home, away, true);
                TeamSpawnZone zone = CreateZone("ЗонаВторойПоловины", home);
                Assert.AreSame(away, zone.Team, "Контроль: стороны поменяны.");

                PlayerSession session = CreateSession("Союзник", EnemyTeam);
                PlayerController body = CreateAvatar("ТелоСоюзника", session);
                Report(zone, body, true);
                Assert.IsTrue(session.IsInSpawnZone, "Контроль: союзник у себя на базе второй половины.");

                // Погиб на базе, затем матч кончился, и стороны вернулись на место.
                Object.DestroyImmediate(body.gameObject);
                SpawnSides.Reset();
                ReleaseDestroyed(zone);

                Assert.IsFalse(session.IsInSpawnZone,
                    "Сценарий «Играть заново»: признак остался с прошлой половины, бот считает себя " +
                    "на базе и не идёт домой. Выход обязан сниматься с командой, с которой был вход.");
            }
            finally
            {
                SpawnSides.Reset();
            }
        }

        [Test]
        public void Возрождение_на_месте_в_зоне_признак_сохраняет()
        {
            SilenceMirrorNoise();

            TeamSpawnZone ownZone = CreateZone("СвояЗона", CreateTeam("Красные", OwnTeam));
            PlayerSession session = CreateSession("Возрождённый", OwnTeam);
            PlayerController oldBody = CreateAvatar("СтароеТело", session);
            Report(ownZone, oldBody, true);

            // Новое тело той же сессии появилось в зоне и отчиталось раньше, чем зона убрала старое.
            PlayerController newBody = CreateAvatar("НовоеТело", session);
            Report(ownZone, newBody, true);
            Object.DestroyImmediate(oldBody.gameObject);
            ReleaseDestroyed(ownZone);

            Assert.IsTrue(session.IsInSpawnZone,
                "Уборка старого тела сняла признак, хотя новое тело той же сессии стоит в зоне: " +
                "зона уже считает его внутри и повторно не сообщит.");
        }

        [Test]
        public void Зона_без_команды_молчит()
        {
            SilenceMirrorNoise();

            TeamSpawnZone orphanZone = CreateZone("ЗонаБезКоманды", null);

            PlayerSession session = CreateSession("Свой", OwnTeam);
            PlayerController avatar = CreateAvatar("АватарСвоего", session);

            Report(orphanZone, avatar, true);

            Assert.AreEqual(PlayerSession.NoSpawnZone, session.SpawnZoneTeamIndex,
                "Зона без назначенной команды не знает, чья она, и сказать о себе ей нечего. " +
                "Молча приписать игроку команду 0 — худший из вариантов: это команда из реестра.");
        }
    }
}
