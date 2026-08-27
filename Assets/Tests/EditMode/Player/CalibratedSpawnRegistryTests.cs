using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Maps;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Две ветки выбора точки спавна — задача <b>T-30</b>, находка <b>CAL-01</b>.
    ///
    /// <para>
    /// Что доказывают тесты. До калибровки игра не знает, где игрок находится внутри
    /// арены, и вправе поставить его куда угодно — зона своей команды и есть разумное
    /// «куда угодно». После калибровки его место задано физически, и смена карты
    /// не имеет права его двигать. Проверяется, что ветки действительно разные
    /// и что выбор зависит от признака <c>PlayerSession.IsCalibrated</c>, а не от чего
    /// придётся.
    /// </para>
    ///
    /// <para>
    /// Ярус A (<see cref="MirrorTestHarness"/>) нужен по двум причинам: сессии нужен
    /// настоящий <c>netId</c> — по нему реестр и хранит место, — а созданные объекты
    /// (зоны, якоря) обязаны убираться между тестами, иначе <c>FindObjectsByType</c>
    /// найдёт чужие.
    /// </para>
    /// </summary>
    public class CalibratedSpawnRegistryTests : MirrorTestHarness
    {
        // ── Расстановка арены проекта ────────────────────────────────────────

        private static readonly Vector3 AnchorZero = new Vector3(-2.74f, -0.01f, -3.60f);
        private static readonly Vector3 AnchorOne  = new Vector3(2.75f, -0.01f, -3.60f);

        /// <summary>Поворот арены в <c>TestMap1</c> относительно <c>TestMap2</c>.</summary>
        private static readonly Quaternion Map1Rotation = Quaternion.Euler(0f, 90f, 0f);

        [SetUp]
        public void ForgetPlacements()
        {
            CalibratedSpawnRegistry.Clear();
        }

        [TearDown]
        public void ForgetPlacementsAfter()
        {
            CalibratedSpawnRegistry.Clear();
        }

        // ── Заготовки сцены ──────────────────────────────────────────────────

        private void PlaceAnchors(Quaternion mapRotation)
        {
            GameObject first = CreateObject("Anchor1");
            first.transform.position = mapRotation * AnchorZero;
            first.AddComponent<PhysicalSpaceAnchor>().id = 0;

            GameObject second = CreateObject("Anchor2");
            second.transform.position = mapRotation * AnchorOne;
            second.AddComponent<PhysicalSpaceAnchor>().id = 1;
        }

        private TeamSpawnZone CreateZone(string name, TeamData team, Vector3 position)
        {
            GameObject go = CreateObject(name);
            go.transform.position = position;
            go.AddComponent<BoxCollider>();

            TeamSpawnZone zone = go.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", team);

            return zone;
        }

        private static TeamData CreateTeam(string displayName, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.displayName = displayName;
            team.teamIndex = index;
            return team;
        }

        private PlayerSession CreateSession(string name, bool calibrated)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);

            session.PlayerName = name;
            session.IsCalibrated = calibrated;

            Assert.AreNotEqual(0u, session.netId,
                "Без netId реестру нечем различать игроков — заготовка теста сломана.");

            return session;
        }

        /// <summary>Система координат якорей текущей сцены. Тесту нужна как эталон.</summary>
        private static PhysicalSpaceAnchorFrame SceneFrame()
        {
            PhysicalSpaceAnchorFrame frame;
            string diagnosis;

            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out diagnosis),
                "Якоря на сцене теста стоят, система координат обязана построиться: " + diagnosis);

            return frame;
        }

        // ── Проверки ─────────────────────────────────────────────────────────

        [Test]
        public void Откалиброванный_игрок_возвращается_на_своё_место_а_не_в_зону()
        {
            SilenceMirrorNoise();
            PlaceAnchors(Quaternion.identity);

            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            PlayerSession session = CreateSession("Откалиброванный", true);

            // Место, снятое на прошлой карте: центр арены в координатах якорей.
            Vector3 place = SceneFrame().ToLocal(Vector3.zero);
            CalibratedSpawnRegistry.Remember(session.netId, place, Quaternion.identity, "TestMap1");

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red, session);

            Assert.AreEqual(AvatarSpawnPointSource.CalibratedPlace, point.Source,
                "Откалиброванного игрока переносить в базу нельзя: он стоит в комнате, " +
                "и картинка разъедется с телом.");
            Assert.AreEqual(0f, Vector3.Distance(Vector3.zero, point.Position), 1e-3f,
                "Игрок обязан вернуться ровно туда, где стоял.");
        }

        [Test]
        public void Место_переносится_на_повёрнутую_карту_вместе_с_ареной()
        {
            SilenceMirrorNoise();

            // Первая карта: арена без поворота (как TestMap2).
            PlaceAnchors(Quaternion.identity);

            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            PlayerSession session = CreateSession("Откалиброванный", true);

            Vector3 stood = new Vector3(1.5f, 0f, 4f);
            Vector3 place = SceneFrame().ToLocal(stood);
            CalibratedSpawnRegistry.Remember(session.netId, place, Quaternion.identity, "TestMap2");

            // Вторая карта: та же арена, повёрнутая на 90° (как TestMap1).
            DestroyAnchorsAndZones();
            PlaceAnchors(Map1Rotation);
            CreateZone("ЗонаКрасных", red, Map1Rotation * new Vector3(0f, 0f, 8.06f));

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red, session);

            Assert.AreEqual(AvatarSpawnPointSource.CalibratedPlace, point.Source);
            Assert.AreEqual(0f, Vector3.Distance(Map1Rotation * stood, point.Position), 1e-3f,
                "Место обязано повернуться вместе с ареной. Сохранение мировой позиции " +
                "поставило бы игрока в точку, развёрнутую относительно арены на 90°.");
            Assert.Greater(Vector3.Distance(stood, point.Position), 1f,
                "Опыт бессмысленен, если повёрнутая карта дала ту же мировую точку.");
        }

        [Test]
        public void Неоткалиброванный_игрок_идёт_в_зону_даже_при_готовом_снимке()
        {
            SilenceMirrorNoise();
            PlaceAnchors(Quaternion.identity);

            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            PlayerSession session = CreateSession("Обычный", false);

            // Снимок есть, но игрок калибровку не объявлял: место у него не задано,
            // и ставить его надо туда, куда решает игра.
            CalibratedSpawnRegistry.Remember(session.netId, Vector3.zero, Quaternion.identity, "TestMap1");

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red, session);

            Assert.AreEqual(AvatarSpawnPointSource.TeamSpawnZone, point.Source,
                "Признак калибровки — единственное, что разводит две ветки. Без него " +
                "точку выбирает игра.");
            Assert.AreEqual(new Vector3(0f, 0f, 8.06f), point.Position);
        }

        [Test]
        public void Без_снимка_откалиброванный_игрок_идёт_в_зону()
        {
            SilenceMirrorNoise();
            PlaceAnchors(Quaternion.identity);

            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            PlayerSession session = CreateSession("Откалиброванный", true);

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red, session);

            Assert.AreEqual(AvatarSpawnPointSource.TeamSpawnZone, point.Source,
                "Игрок откалибровался, но снимка его места ещё нет (первый спавн). " +
                "Запасной вариант обязан быть определённым, а не начальными координатами.");
        }

        [Test]
        public void Карта_без_якорей_возвращает_игрока_в_зону()
        {
            SilenceMirrorNoise();

            // Якорей нет: привязать место не к чему.
            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            PlayerSession session = CreateSession("Откалиброванный", true);
            CalibratedSpawnRegistry.Remember(session.netId, new Vector3(2.74f, 0f, 3.6f),
                                             Quaternion.identity, "TestMap1");

            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red, session);

            Assert.AreEqual(AvatarSpawnPointSource.TeamSpawnZone, point.Source,
                "Без пары якорей на новой карте место откалиброванного игрока пересчитать нечем — " +
                "остаётся зона, и это должно быть видно в логе, а не тихо стать началом координат.");
        }

        [Test]
        public void Без_сессии_ветка_калибровки_не_спрашивается()
        {
            SilenceMirrorNoise();
            PlaceAnchors(Quaternion.identity);

            TeamData red = CreateTeam("Красные", 1);
            CreateZone("ЗонаКрасных", red, new Vector3(0f, 0f, 8.06f));

            // Так резолвера зовут при смене команды: аватар жив, физически ничего
            // не произошло, и восстанавливать место не нужно.
            AvatarSpawnPoint point = AvatarSpawnPointResolver.Resolve(red);

            Assert.AreEqual(AvatarSpawnPointSource.TeamSpawnZone, point.Source);
        }

        /// <summary>
        /// Убирает якоря и зоны первой карты, оставляя сессию: так выглядит смена карты
        /// с точки зрения резолвера.
        /// </summary>
        private static void DestroyAnchorsAndZones()
        {
            foreach (PhysicalSpaceAnchor anchor in Object.FindObjectsByType<PhysicalSpaceAnchor>(FindObjectsSortMode.None))
                Object.DestroyImmediate(anchor.gameObject);

            foreach (TeamSpawnZone zone in Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.None))
                Object.DestroyImmediate(zone.gameObject);
        }
    }
}
