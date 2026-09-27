using System.Collections.Generic;
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
    /// «Пауза» и «Продолжить» у админа на карте.
    ///
    /// <para>
    /// Что доказывает. Пауза прерывает идущий раунд без победителя (он не засчитывается ни
    /// в счёт карты, ни в общий счёт серии), карта уходит в разминку, снаряжение забирается.
    /// «Продолжить» возвращает матч с того же номера раунда: сеты, счёт раундов и команды
    /// сохранены. Матч прокручивается настоящим <c>EliminationMode.ServerTick</c>
    /// (<see cref="RoundFlowDriver"/>), игроки — заглушка реестра.
    /// </para>
    /// </summary>
    public class MatchPauseTests : MirrorTestHarness
    {
        private readonly List<Object> _assets = new List<Object>();
        private TeamData _a, _b, _warmupTeam;
        private GameModeData _warmup, _elimination;
        private StubPlayerRoster _roster;
        private MatchSeries _series;
        private GameplayManager _manager;
        private int _strips;

        private EliminationMode Match => _manager.ActiveGameMode as EliminationMode;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);
            _warmupTeam = TeamRegistry.Instance.GetByIndex(3);

            _warmup = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _warmup.modeId = "warmup";
            _warmup.isWarmup = true;
            _warmup.teamAssignment = TeamAssignmentKind.KeepOrDefault;
            _warmup.teams = new[] { _warmupTeam };

            _elimination = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _elimination.modeId = "elimination";
            _elimination.minPlayersToStart = 2;
            _elimination.teams = new[] { _a, _b };

            var registry = Asset(ScriptableObject.CreateInstance<GameModeRegistry>());
            registry.modes = new[] { _warmup, _elimination };

            var map = Asset(ScriptableObject.CreateInstance<MapData>());
            map.sceneName = "MapA";
            map.supportedModes = new[] { _warmup, _elimination };
            var maps = Asset(ScriptableObject.CreateInstance<MapRegistry>());
            maps.maps = new[] { map };

            SessionManager session = CreateNetworkComponent<SessionManager>("SessionManager");
            MatchFlowTests.InstallSessionManager(session, registry, maps);
            SpawnOnServer(session);
            session.SetSession("MapA", "elimination");

            _series = CreateNetworkComponent<MatchSeries>("MatchSeries");
            InvokeLifecycleMethod(_series, "Awake");
            _series.MapLoader = _ => { };
            SpawnOnServer(_series);
            _series.ServerBegin(new[] { "MapA" });

            _roster = new StubPlayerRoster();
            _roster.Add(_a, ReadySession("PA", _a));
            _roster.Add(_b, ReadySession("PB", _b));

            _manager = CreateNetworkComponent<GameplayManager>("GameplayManager");
            InvokeLifecycleMethod(_manager, "Awake");
            _manager.SceneNameOverride = "MapA";
            _manager.ModeFactory = data =>
            {
                GameMode mode = data.isWarmup
                    ? (GameMode)CreateNetworkComponent<WarmupMode>("WarmupMode")
                    : CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokeLifecycleMethod(mode, "Awake");
                mode.PlayerRoster = _roster;
                return mode.gameObject;
            };
            SpawnOnServer(_manager);

            _strips = 0;
            EquipmentStrip.ServerStripAllRequested += CountStrip;
        }

        [TearDown]
        public void Drop()
        {
            EquipmentStrip.ServerStripAllRequested -= CountStrip;
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        private void CountStrip(string reason) => _strips++;

        private T Asset<T>(T o) where T : Object { _assets.Add(o); return o; }

        private PlayerSession ReadySession(string name, TeamData team)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            SpawnOnServer(session);
            session.TeamIndex = team.teamIndex;
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        private RoundFlowDriver Driver() => new RoundFlowDriver(
            dt =>
            {
                _roster.DeclareAllReady();
                Match.ServerTick(dt);
            },
            () => Match.CurrentRoundState);

        /// <summary>Раунд 1 выигрывает A, раунд 2 доходит до боя — его и прерывает пауза.</summary>
        private void PlayToSecondRoundCombat()
        {
            Assert.IsTrue(_manager.StartMatch(), "Контроль: матч начался.");
            RoundFlowDriver driver = Driver();

            driver.AdvanceUntil(() => Match.CurrentRoundState == RoundState.Combat, "боя раунда 1");
            Match.RoundManager.RequestRoundEnd(_a);
            driver.AdvanceUntil(() => Match.CurrentRoundNumber == 2, "начала раунда 2");
            driver.AdvanceUntil(() => Match.CurrentRoundState == RoundState.Combat, "боя раунда 2");

            Assert.AreEqual(1, Match.GetRoundScore(_a), "Контроль: раунд 1 за A.");
        }

        [Test]
        public void Пауза_прерывает_раунд_без_засчёта_и_возвращает_разминку()
        {
            PlayToSecondRoundCombat();
            int stripsBefore = _strips;

            // Раунд 2 вот-вот выиграет B, но пауза успевает раньше.
            Match.RoundManager.RequestRoundEnd(_b);
            Assert.IsTrue(_manager.PauseMatch(), "Пауза во время матча не сработала.");

            Assert.IsInstanceOf<WarmupMode>(_manager.ActiveGameMode, "На паузе карта не в разминке.");
            Assert.IsTrue(_manager.IsPaused);
            Assert.Greater(_strips, stripsBefore, "Пауза не забрала снаряжение.");
            Assert.AreEqual(1, _series.GetRoundsWon(_a, MatchSeries.Total), "Раунд 1 не попал в общий счёт.");
            Assert.AreEqual(0, _series.GetRoundsWon(_b, MatchSeries.Total), "Прерванный паузой раунд засчитан в общий счёт.");
            Assert.IsFalse(_manager.StartMatch(), "На паузе «Начать матч» начал бы матч заново — только «Продолжить».");
        }

        [Test]
        public void Продолжить_возвращает_тот_же_номер_раунда_и_счёт()
        {
            PlayToSecondRoundCombat();
            PlayerSession pa = null;
            foreach (PlayerSession s in _roster.GetAllPlayers()) if (s.TeamIndex == _a.teamIndex) pa = s;
            _series.ServerRecordKill(null, pa, null);

            Assert.IsTrue(_manager.PauseMatch());
            int stripsBefore = _strips;

            Assert.IsTrue(_manager.ResumeMatch(), "«Продолжить» не сработал.");
            Assert.IsNotNull(Match, "После «Продолжить» режим не Elimination.");
            Assert.IsFalse(_manager.IsPaused);
            Assert.IsTrue(_manager.IsMatchActive);
            Assert.Greater(_strips, stripsBefore, "«Продолжить» не забрал снаряжение разминки.");

            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            Assert.AreEqual(2, Match.CurrentRoundNumber, "Матч продолжился не с прерванного раунда.");
            Assert.AreEqual(1, Match.GetRoundScore(_a), "Счёт раундов сета потерян на паузе.");
            Assert.AreEqual(0, Match.GetRoundScore(_b));
            Assert.AreEqual(_a.teamIndex, pa.TeamIndex, "Пауза сменила команду.");
            Assert.AreEqual(1, _series.GetKills(pa, MatchSeries.Total), "Статистика не пережила паузу.");
        }

        /// <summary>
        /// Play mode: корутина старта режима (<c>WaitAndStartGameplayRoutine</c>) зовёт
        /// <c>StartGameplay</c> кадром позже, а <c>ServerTick</c> к этому моменту уже поднял
        /// продолженный сет. Раньше <c>StartGameplay</c> сбрасывал состояние в
        /// <c>WaitingForPlayers</c>, и следующий тик начинал сет заново с раунда 1 — в EditMode
        /// корутины не крутятся, поэтому поздний вызов подаётся явно.
        /// </summary>
        [Test]
        public void Поздний_старт_режима_не_сбрасывает_продолженный_матч()
        {
            PlayToSecondRoundCombat();
            Assert.IsTrue(_manager.PauseMatch());
            Assert.IsTrue(_manager.ResumeMatch());
            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            InvokePrivateMethod(Match, "StartGameplay"); // та самая поздняя корутина
            Driver().Advance();

            Assert.AreEqual(EliminationMatchState.Active, Match.CurrentMatchState, "Поздний StartGameplay сбросил идущий матч.");
            Assert.AreEqual(2, Match.CurrentRoundNumber, "После позднего StartGameplay матч начался с раунда 1.");
            Assert.AreEqual(1, Match.GetRoundScore(_a));
        }

        [Test]
        public void Сеты_переживают_паузу()
        {
            Assert.IsTrue(_manager.StartMatch());
            Driver().AdvanceUntil(() => Match.CurrentRoundNumber == 1, "начала матча");
            Match.SetScore(_a, 1); // сет уже выигран A

            Assert.IsTrue(_manager.PauseMatch());
            Assert.IsTrue(_manager.ResumeMatch());
            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            Assert.AreEqual(1, Match.GetScore(_a), "Выигранный сет потерян на паузе.");
        }
    }
}
