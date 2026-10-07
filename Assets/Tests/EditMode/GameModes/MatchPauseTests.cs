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
    /// «Продолжить» возвращает матч с того же номера раунда: счёт карты и команды
    /// сохранены. Матч прокручивается настоящим <c>EliminationMode.ServerTick</c>
    /// (<see cref="RoundFlowDriver"/>), игроки — заглушка реестра.
    /// </para>
    /// </summary>
    public class MatchPauseTests : MirrorTestHarness
    {
        private readonly List<Object> _assets = new List<Object>();
        private TeamData _a, _b;
        private GameModeData _warmup, _elimination;
        private StubPlayerRoster _roster;
        private Series _series;
        private MapReferee _manager;
        private int _strips;

        private EliminationMode Match => _manager.ActiveGameMode as EliminationMode;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);

            _warmup = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _warmup.modeId = "warmup";

            _elimination = Asset(ScriptableObject.CreateInstance<GameModeData>());
            _elimination.modeId = "elimination";
            _elimination.minPlayersToStart = 2;
            _elimination.teams = new[] { _a, _b };

            var registry = Asset(ScriptableObject.CreateInstance<GameModeRegistry>());
            registry.warmup = _warmup;
            registry.modes = new[] { _elimination };

            var map = Asset(ScriptableObject.CreateInstance<MapData>());
            map.sceneName = "MapA";
            map.supportedModes = new[] { _elimination };
            var maps = Asset(ScriptableObject.CreateInstance<MapRegistry>());
            maps.maps = new[] { map };

            SessionManager session = CreateNetworkComponent<SessionManager>("SessionManager");
            MatchFlowTests.InstallSessionManager(session, registry, maps);
            SpawnOnServer(session);
            session.SetSession("MapA", "elimination");

            _series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(_series, "Awake");
            _series.LoadMapOverride = _ => { };
            SpawnOnServer(_series);
            _series.ServerBegin(new[] { "MapA" });

            _roster = new StubPlayerRoster();
            _roster.Add(_a, ReadySession("PA", _a));
            _roster.Add(_b, ReadySession("PB", _b));

            _manager = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(_manager, "Awake");
            _manager.SceneNameOverride = "MapA";
            _manager.ModeFactory = data =>
            {
                GameMode mode = data == _warmup
                    ? (GameMode)CreateNetworkComponent<WarmupMode>("WarmupMode")
                    : CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokeLifecycleMethod(mode, "Awake");
                mode.PlayerRoster = _roster;
                return mode.gameObject;
            };
            // Судью запускает запуск карты, как MapBootstrap: режим матча серии согласован при загрузке.
            StartMapRun(_manager, "MapA", _warmup.modeId, _series.CapturedModeId, _elimination.modeId);

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
            () => Match.CurrentRoundPhase);

        /// <summary>Раунд 1 выигрывает A, раунд 2 доходит до боя — его и прерывает пауза.</summary>
        private void PlayToSecondRoundCombat()
        {
            Assert.IsTrue(_manager.GoLive(), "Контроль: матч начался.");
            RoundFlowDriver driver = Driver();

            driver.AdvanceUntil(() => Match.CurrentRoundPhase == RoundPhase.Combat, "боя раунда 1");
            Match.RoundPhases.RequestRoundEnd(_a);
            driver.AdvanceUntil(() => Match.CurrentRoundNumber == 2, "начала раунда 2");
            driver.AdvanceUntil(() => Match.CurrentRoundPhase == RoundPhase.Combat, "боя раунда 2");

            Assert.AreEqual(1, Match.GetScore(_a), "Контроль: раунд 1 за A.");
        }

        [Test]
        public void Пауза_прерывает_раунд_без_засчёта_и_возвращает_разминку()
        {
            PlayToSecondRoundCombat();
            int stripsBefore = _strips;

            // Раунд 2 вот-вот выиграет B, но пауза успевает раньше.
            Match.RoundPhases.RequestRoundEnd(_b);
            Assert.IsTrue(_manager.Pause(), "Пауза во время матча не сработала.");

            Assert.IsInstanceOf<WarmupMode>(_manager.ActiveGameMode, "На паузе карта не в разминке.");
            Assert.IsTrue(_manager.IsPaused);
            Assert.Greater(_strips, stripsBefore, "Пауза не забрала снаряжение.");
            Assert.AreEqual(1, _series.GetRoundsWon(_a, Series.Total), "Раунд 1 не попал в общий счёт.");
            Assert.AreEqual(0, _series.GetRoundsWon(_b, Series.Total), "Прерванный паузой раунд засчитан в общий счёт.");
            Assert.IsFalse(_manager.GoLive(), "На паузе «Начать матч» начал бы матч заново — только «Продолжить».");
        }

        [Test]
        public void Продолжить_возвращает_тот_же_номер_раунда_и_счёт()
        {
            PlayToSecondRoundCombat();
            PlayerSession pa = null;
            foreach (PlayerSession s in _roster.GetAllPlayers()) if (s.TeamIndex == _a.teamIndex) pa = s;
            _series.ServerRecordKill(null, pa, null);

            Assert.IsTrue(_manager.Pause());
            int stripsBefore = _strips;

            Assert.IsTrue(_manager.Resume(), "«Продолжить» не сработал.");
            Assert.IsNotNull(Match, "После «Продолжить» режим не Elimination.");
            Assert.IsFalse(_manager.IsPaused);
            Assert.IsTrue(_manager.IsLiveOrPaused);
            Assert.Greater(_strips, stripsBefore, "«Продолжить» не забрал снаряжение разминки.");

            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            Assert.AreEqual(2, Match.CurrentRoundNumber, "Матч продолжился не с прерванного раунда.");
            Assert.AreEqual(1, Match.GetScore(_a), "Счёт карты потерян на паузе.");
            Assert.AreEqual(0, Match.GetScore(_b));
            Assert.AreEqual(_a.teamIndex, pa.TeamIndex, "Пауза сменила команду.");
            Assert.AreEqual(1, _series.GetKills(pa, Series.Total), "Статистика не пережила паузу.");
        }

        /// <summary>
        /// Play mode: корутина старта режима (<c>WaitAndBeginRoutine</c>) зовёт
        /// <c>Begin</c> кадром позже, а <c>ServerTick</c> к этому моменту уже поднял
        /// продолженный матч. Раньше <c>Begin</c> сбрасывал состояние в
        /// <c>WaitingForPlayers</c>, и следующий тик начинал матч заново с раунда 1 — в EditMode
        /// корутины не крутятся, поэтому поздний вызов подаётся явно.
        /// </summary>
        [Test]
        public void Поздний_старт_режима_не_сбрасывает_продолженный_матч()
        {
            PlayToSecondRoundCombat();
            Assert.IsTrue(_manager.Pause());
            Assert.IsTrue(_manager.Resume());
            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            InvokePrivateMethod(Match, "Begin"); // та самая поздняя корутина
            Driver().Advance();

            Assert.AreEqual(EliminationState.Active, Match.CurrentState, "Поздний Begin сбросил идущий матч.");
            Assert.AreEqual(2, Match.CurrentRoundNumber, "После позднего Begin матч начался с раунда 1.");
            Assert.AreEqual(1, Match.GetScore(_a));
        }

        /// <summary>
        /// Очко за раунд начисляется на входе в Resolution, а раунд доигран только после
        /// экрана итогов. Пауза между ними обязана вернуть счёт на начало раунда: иначе
        /// прерванный раунд засчитался бы и сыгрался заново — очко дважды.
        /// </summary>
        [Test]
        public void Пауза_на_итогах_раунда_не_засчитывает_его()
        {
            PlayToSecondRoundCombat();
            RoundFlowDriver driver = Driver();

            Match.RoundPhases.RequestRoundEnd(_b);
            driver.AdvanceUntil(() => Match.CurrentRoundPhase == RoundPhase.Resolution, "итогов раунда 2");
            Assert.AreEqual(1, Match.GetScore(_b), "Контроль: очко за раунд 2 уже на табло.");

            Assert.IsTrue(_manager.Pause());
            Assert.IsTrue(_manager.Resume());
            Driver().AdvanceUntil(() => Match.CurrentRoundNumber > 0, "начала матча после паузы");

            Assert.AreEqual(2, Match.CurrentRoundNumber, "Прерванный раунд обязан сыграться заново.");
            Assert.AreEqual(1, Match.GetScore(_a), "Счёт до прерванного раунда потерян.");
            Assert.AreEqual(0, Match.GetScore(_b), "Прерванный паузой раунд засчитан.");
        }
    }
}
