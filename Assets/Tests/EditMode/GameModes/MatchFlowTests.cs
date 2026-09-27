using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Жизнь режима на карте и серия карт матча.
    ///
    /// <para>
    /// Что доказывает. Любая карта стартует в разминке, пока админ не нажал «Начать матч»;
    /// в лобби режим матча не запускается; режим меняется на месте (тот же
    /// <see cref="GameplayManager"/>, без перезагрузки сцены) — разминка → Elimination →
    /// разминка — и при этом не сбрасываются ни команды, ни общий счёт серии; разминка
    /// оставляет игроку команду матча и даёт свою только игроку без команды. Матч на карте
    /// кончился — серия переходит к следующей карте, после последней — в лобби, и команды
    /// матча отпускаются. Сцены здесь не грузятся: загрузчик карт серии подменён, и тест
    /// видит, какую сцену серия попросила.
    /// </para>
    /// </summary>
    public class MatchFlowTests : MirrorTestHarness
    {
        private const int WarmupTeamIndex = 3;

        private readonly List<Object> _assets = new List<Object>();
        private readonly List<string> _loads = new List<string>();

        private TeamData _a, _b, _warmupTeam;
        private GameModeData _warmup, _elimination, _respawn;
        private GameModeRegistry _registry;
        private MapData _mapA, _mapB, _lobby;
        private MapRegistry _maps;
        private ListRoster _roster;

        [SetUp]
        public void BuildData()
        {
            _loads.Clear();
            _roster = new ListRoster();

            _a = Team("A", 1);
            _b = Team("B", 2);
            _warmupTeam = Team("Разминка", WarmupTeamIndex);

            _warmup = Mode("warmup", TeamAssignmentKind.KeepOrDefault, _warmupTeam);
            _warmup.isWarmup = true;
            _elimination = Mode("elimination", TeamAssignmentKind.PlayerChoice, _a, _b);
            _respawn = Mode("respawn", TeamAssignmentKind.PlayerChoice, _a, _b);

            _registry = ScriptableObject.CreateInstance<GameModeRegistry>();
            _registry.modes = new[] { _warmup, _elimination, _respawn };
            _assets.Add(_registry);

            _lobby = Map("Lobby", _warmup);
            _mapA = Map("MapA", _warmup, _elimination);
            _mapB = Map("MapB", _warmup, _elimination, _respawn);

            _maps = ScriptableObject.CreateInstance<MapRegistry>();
            _maps.maps = new[] { _lobby, _mapA, _mapB };
            _maps.lobby = _lobby;
            _assets.Add(_maps);
        }

        [TearDown]
        public void DropData()
        {
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        // ── Заготовки ────────────────────────────────────────────────────────

        private TeamData Team(string name, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.name = name;
            team.displayName = name;
            team.teamIndex = index;
            _assets.Add(team);
            return team;
        }

        private GameModeData Mode(string id, TeamAssignmentKind kind, params TeamData[] teams)
        {
            GameModeData data = ScriptableObject.CreateInstance<GameModeData>();
            data.modeId = id;
            data.displayName = id;
            data.teams = teams;
            data.minPlayersToStart = 2;
            data.teamAssignment = kind;
            _assets.Add(data);
            return data;
        }

        private MapData Map(string scene, params GameModeData[] modes)
        {
            MapData map = ScriptableObject.CreateInstance<MapData>();
            map.sceneName = scene;
            map.supportedModes = modes;
            _assets.Add(map);
            return map;
        }

        private sealed class ListRoster : IPlayerRoster
        {
            public readonly List<PlayerSession> Players = new List<PlayerSession>();
            public IEnumerable<PlayerSession> GetPlayers(TeamData team) => Players.Where(p => team != null && p.TeamIndex == team.teamIndex);
            public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team) => GetPlayers(team);
            public IEnumerable<PlayerSession> GetAllPlayers() => Players;
        }

        private PlayerSession Player(string name, int teamIndex)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = teamIndex;
            _roster.Players.Add(session);
            return session;
        }

        /// <summary>
        /// Ставит <see cref="SessionManager"/> синглтоном с реестрами. <c>Awake</c> не зовётся:
        /// он делает <c>DontDestroyOnLoad</c>, запрещённый в EditMode.
        /// </summary>
        public static void InstallSessionManager(SessionManager session, GameModeRegistry modes, MapRegistry maps)
        {
            typeof(SessionManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(true).Invoke(null, new object[] { session });
            SetPrivateField(session, "_gameModeRegistry", modes);
            SetPrivateField(session, "_mapRegistry", maps);
        }

        private SessionManager CreateSession(string selectedModeId)
        {
            SessionManager session = CreateNetworkComponent<SessionManager>("SessionManager");
            InstallSessionManager(session, _registry, _maps);
            SpawnOnServer(session);
            if (selectedModeId != null) session.SetSession(_mapA.sceneName, selectedModeId);
            return session;
        }

        private MatchSeries CreateSeries(float nextMapDelay = 0f)
        {
            MatchSeries series = CreateNetworkComponent<MatchSeries>("MatchSeries");
            InvokeLifecycleMethod(series, "Awake");
            SetPrivateField(series, "_nextMapDelay", nextMapDelay);
            series.PlayerRoster = _roster;
            series.MapLoader = scene => _loads.Add(scene);
            SpawnOnServer(series);
            return series;
        }

        /// <summary>
        /// Оркестратор на карте <paramref name="scene"/>. Режимы создаёт фабрика: в EditMode
        /// Unity не зовёт <c>Awake</c> у инстанцированных префабов, а харнесс умеет
        /// собирать сетевые объекты сам.
        /// </summary>
        private GameplayManager CreateMapManager(string scene)
        {
            GameplayManager manager = CreateNetworkComponent<GameplayManager>("GameplayManager");
            InvokeLifecycleMethod(manager, "Awake");
            manager.SceneNameOverride = scene;
            manager.ModeFactory = data =>
            {
                GameMode mode = data.isWarmup
                    ? (GameMode)CreateNetworkComponent<WarmupMode>("WarmupMode")
                    : data.modeId == "respawn"
                        ? CreateNetworkComponent<RespawnMode>("RespawnMode")
                        : CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokeLifecycleMethod(mode, "Awake"); // как в игре: Awake подписывает колбэк SyncList до Initialize
                mode.PlayerRoster = _roster;
                return mode.gameObject;
            };
            SpawnOnServer(manager); // OnStartServer — карта стартует сама
            return manager;
        }

        private static void EndMatch(GameMode mode, TeamData winner)
        {
            InvokePrivateMethod(mode, "RaiseGameplayEnded", winner);
        }

        // ── Старт карты ──────────────────────────────────────────────────────

        [Test]
        public void Карта_без_выбранного_режима_стартует_в_разминке()
        {
            SilenceMirrorNoise();
            CreateSession(selectedModeId: null);

            GameplayManager manager = CreateMapManager(_mapA.sceneName);

            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode, "Карта стартовала не в разминке.");
            Assert.IsTrue(manager.ActiveGameMode.IsWarmup);
            Assert.IsFalse(manager.IsMatchActive, "Разминка — не матч.");
        }

        [Test]
        public void Карта_с_выбранным_режимом_тоже_стартует_в_разминке_до_кнопки()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");

            GameplayManager manager = CreateMapManager(_mapA.sceneName);

            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode,
                "Выбор администратора запустился сам — матч начинается только кнопкой «Начать матч».");
        }

        [Test]
        public void В_лобби_несовместимый_режим_не_запускается()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");

            GameplayManager manager = CreateMapManager(_lobby.sceneName);
            GameMode warmup = manager.ActiveGameMode;

            Assert.IsFalse(manager.StartMatch(), "В лобби «Начать матч» запустил режим матча.");
            Assert.AreSame(warmup, manager.ActiveGameMode, "В лобби режим сменился — там совместима только разминка.");
            Assert.IsFalse(manager.IsMatchActive);
        }

        [Test]
        public void Несовместимый_с_картой_выбор_заменяется_первым_совместимым()
        {
            SilenceMirrorNoise();
            CreateSession("respawn"); // на MapA только разминка и Elimination

            GameplayManager manager = CreateMapManager(_mapA.sceneName);

            Assert.IsTrue(manager.StartMatch());
            Assert.IsInstanceOf<EliminationMode>(manager.ActiveGameMode,
                "Несовместимый с картой режим не заменён первым совместимым.");
        }

        /// <summary>
        /// Автостарт отладки зовёт «Начать матч» по сигналу <c>Awake</c> оркестратора — раньше
        /// его спавна. Режим дочерним объектом незаспавненного менеджера не спавнится: запрос
        /// ждёт <c>OnStartServer</c> и выполняется после разминки.
        /// </summary>
        [Test]
        public void Начать_матч_до_спавна_карты_выполняется_после_разминки()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");

            // Как в игре: сигнал приходит из Awake оркестратора, когда NetworkIdentity ещё
            // не связала компоненты — у менеджера нет netIdentity, и isServer там падает с NRE.
            GameObject go = CreateNetworkObject("GameplayManager");
            GameplayManager manager = go.AddComponent<GameplayManager>();
            manager.SceneNameOverride = _mapA.sceneName;
            manager.ModeFactory = data =>
            {
                GameMode mode = data.isWarmup
                    ? (GameMode)CreateNetworkComponent<WarmupMode>("WarmupMode")
                    : CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokeLifecycleMethod(mode, "Awake"); // как в игре: Awake подписывает колбэк SyncList до Initialize
                mode.PlayerRoster = _roster;
                return mode.gameObject;
            };

            Assert.IsFalse(manager.StartMatch(), "Режим заспавнен дочерним объектом незаспавненного менеджера.");
            Assert.IsNull(manager.ActiveGameMode);

            EnableNetworking(go);
            InvokeLifecycleMethod(manager, "Awake");
            SpawnOnServer(manager);

            Assert.IsInstanceOf<EliminationMode>(manager.ActiveGameMode, "Отложенный «Начать матч» потерялся.");
            Assert.IsTrue(manager.IsMatchActive);
        }

        // ── Смена режима на месте ────────────────────────────────────────────

        /// <summary>
        /// Разминка → Elimination → разминка на одном и том же <see cref="GameplayManager"/>.
        /// Команды матча и общий счёт серии живут вне режима и смену переживают;
        /// игрок без команды получает «Разминку», а в Elimination выбирает команду сам.
        /// </summary>
        [Test]
        public void Разминка_матч_разминка_на_месте_не_сбрасывает_команды_и_общий_счёт()
        {
            SilenceMirrorNoise();
            SessionManager session = CreateSession("elimination");
            MatchSeries series = CreateSeries(nextMapDelay: 60f);
            Assert.IsTrue(series.ServerBegin(new[] { _mapA.sceneName, _mapB.sceneName }));

            PlayerSession ct = Player("ct", _a.teamIndex);
            PlayerSession t = Player("t", _b.teamIndex);
            PlayerSession fresh = Player("fresh", 0);

            GameplayManager manager = CreateMapManager(_mapA.sceneName);

            // Разминка: команда матча не тронута, игрок без команды — в «Разминке».
            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode);
            Assert.AreEqual(_a.teamIndex, ct.TeamIndex, "Разминка сменила команду матча.");
            Assert.AreEqual(_b.teamIndex, t.TeamIndex, "Разминка сменила команду матча.");
            Assert.AreEqual(WarmupTeamIndex, fresh.TeamIndex, "Игрок без команды не получил «Разминку».");

            // «Начать матч» — на месте.
            GameMode warmup = manager.ActiveGameMode;
            Assert.IsTrue(manager.StartMatch(), "«Начать матч» не запустил Elimination.");
            var elimination = manager.ActiveGameMode as EliminationMode;
            Assert.IsNotNull(elimination, "После «Начать матч» режим не Elimination.");
            Assert.AreSame(manager, GameplayManager.Instance, "Смена режима пересоздала оркестратор — это уже не «на месте».");
            Assert.IsTrue(warmup == null, "Разминка осталась жить рядом с матчем.");
            Assert.IsTrue(manager.IsMatchActive);
            Assert.AreEqual(_a.teamIndex, ct.TeamIndex, "Старт матча сменил команду.");
            Assert.AreEqual(WarmupTeamIndex, fresh.TeamIndex, "Elimination сам раздал команду — выбирать должен игрок.");
            Assert.IsFalse(elimination.TeamChoiceLocked, "Игрок без команды не может выбрать команду матча.");

            // Карта сыграна: победа A. Назад в разминку на той же карте.
            EndMatch(elimination, _a);

            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode, "После конца матча карта не вернулась в разминку.");
            Assert.IsFalse(manager.IsMatchActive);
            Assert.AreEqual(1, series.GetMapWins(_a), "Итог карты не попал в общий счёт серии.");
            Assert.AreEqual(_a.teamIndex, ct.TeamIndex, "Возврат в разминку сменил команду матча.");
            Assert.AreEqual(_b.teamIndex, t.TeamIndex, "Возврат в разминку сменил команду матча.");

            // Ещё одна смена режима счёт серии не трогает.
            Assert.IsTrue(manager.StartMatch());
            manager.StopMatch();
            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode, "Стоп матча не вернул разминку.");
            Assert.AreEqual(1, series.GetMapWins(_a), "Смена режима на карте сбросила общий счёт серии.");
            Assert.IsNotNull(session);
        }

        // ── Серия карт ───────────────────────────────────────────────────────

        [Test]
        public void Серия_идёт_по_картам_и_после_последней_возвращается_в_лобби()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");
            MatchSeries series = CreateSeries();

            PlayerSession ct = Player("ct", _a.teamIndex);

            Assert.IsTrue(series.ServerBegin(new[] { _mapA.sceneName, _mapB.sceneName }));
            CollectionAssert.AreEqual(new[] { "MapA" }, _loads, "Серия не загрузила первую карту.");
            Assert.IsTrue(series.IsRunning);

            Assert.AreEqual("MapB", series.ServerAdvance(), "После первой карты загружена не вторая.");
            Assert.AreEqual(1, series.CurrentIndex);
            Assert.AreEqual(_a.teamIndex, ct.TeamIndex, "Переход к следующей карте сбросил команду матча.");

            Assert.AreEqual("Lobby", series.ServerAdvance(), "После последней карты серия не вернулась в лобби.");
            CollectionAssert.AreEqual(new[] { "MapA", "MapB", "Lobby" }, _loads);
            Assert.IsFalse(series.IsRunning, "Серия после лобби всё ещё идёт.");
            Assert.AreEqual(WarmupTeamIndex, ct.TeamIndex,
                "Конец серии не отпустил команду матча — в лобби игрок остался бы в CT без зоны и чужих скинов.");
        }

        /// <summary>
        /// Конец матча на карте сам ведёт серию дальше: <c>GameplayEnded</c> → итог в счёт →
        /// разминка на этой карте → следующая карта. Задержка перед переходом обнулена.
        /// </summary>
        [Test]
        public void Конец_матча_на_карте_ведёт_серию_к_следующей_карте()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");
            MatchSeries series = CreateSeries(nextMapDelay: 0f);
            series.ServerBegin(new[] { _mapA.sceneName, _mapB.sceneName });
            Player("ct", _a.teamIndex);

            GameplayManager manager = CreateMapManager(_mapA.sceneName);
            Assert.IsTrue(manager.StartMatch());

            EndMatch(manager.ActiveGameMode, _b);

            Assert.IsInstanceOf<WarmupMode>(manager.ActiveGameMode, "Карта не вернулась в разминку.");
            Assert.AreEqual(1, series.GetMapWins(_b));
            CollectionAssert.AreEqual(new[] { "MapA", "MapB" }, _loads, "Серия не перешла к следующей карте.");
        }

        [Test]
        public void Остановка_серии_ведёт_в_лобби_и_счёт_виден_до_новой_серии()
        {
            SilenceMirrorNoise();
            CreateSession("elimination");
            MatchSeries series = CreateSeries();
            series.ServerBegin(new[] { _mapA.sceneName, _mapB.sceneName });
            series.ServerRecordMapResult(_a);

            Assert.AreEqual("Lobby", series.ServerEnd());
            Assert.IsFalse(series.IsRunning);
            Assert.AreEqual(1, series.GetMapWins(_a), "Счёт серии стёрся раньше начала новой.");

            series.ServerBegin(new[] { _mapB.sceneName });
            Assert.AreEqual(0, series.GetMapWins(_a), "Новая серия началась со старым счётом.");
        }

        // ── Снаряжение не переживает переходов ───────────────────────────────

        /// <summary>
        /// Любая смена режима на карте и переход на другую карту забирают снаряжение у всех
        /// (<see cref="EquipmentStrip.ServerStripAll"/>). Само снятие проверяет
        /// <c>EquipmentStripTests</c> на настоящем аватаре; здесь — что его зовут.
        /// </summary>
        [Test]
        public void Смена_режима_и_переход_на_карту_забирают_снаряжение()
        {
            SilenceMirrorNoise();
            var reasons = new List<string>();
            System.Action<string> count = reasons.Add;
            EquipmentStrip.ServerStripAllRequested += count;
            try
            {
                CreateSession("elimination");
                MatchSeries series = CreateSeries(nextMapDelay: 60f);
                series.ServerBegin(new[] { _mapA.sceneName, _mapB.sceneName });
                GameplayManager manager = CreateMapManager(_mapA.sceneName);

                int before = reasons.Count;
                Assert.IsTrue(manager.StartMatch());
                Assert.Greater(reasons.Count, before, "Разминка → матч: снаряжение не забрано.");

                before = reasons.Count;
                EndMatch(manager.ActiveGameMode, _a);
                Assert.Greater(reasons.Count, before, "Матч → разминка: снаряжение не забрано.");

                before = reasons.Count;
                series.ServerAdvance();
                Assert.Greater(reasons.Count, before, "Переход на следующую карту: снаряжение не забрано.");

                before = reasons.Count;
                series.ServerEnd();
                Assert.Greater(reasons.Count, before, "Возврат в лобби: снаряжение не забрано.");
            }
            finally
            {
                EquipmentStrip.ServerStripAllRequested -= count;
            }
        }

        // ── Один путь поиска данных режима ───────────────────────────────────

        [Test]
        public void Данные_режима_ищутся_одним_путём()
        {
            Assert.IsNull(typeof(GameMode).Assembly.GetType("VrBattlegrounds.GameModes.GameModeCatalog"),
                "GameModeCatalog вернулся — второй путь поиска данных режима по modeId.");
            Assert.IsNull(typeof(GameplayManager).GetProperty("SceneGameMode"),
                "У GameplayManager снова «режим сцены» — режим карты задаёт MapData.supportedModes.");
        }
    }
}
