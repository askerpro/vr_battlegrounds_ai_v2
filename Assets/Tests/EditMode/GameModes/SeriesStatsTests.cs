using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Сквозная статистика серии: убийства, смерти, раунды — по каждой карте и TOTAL.
    ///
    /// <para>
    /// Что доказывает. Убийца определяется по урону: источник смертельного урона
    /// (<c>UxrDamageEventArgs.ActorSource</c>) доходит до <c>PlayerController.Die</c> на сервере,
    /// оттуда через <c>MapReferee.PlayerKilled</c> — в <see cref="Series"/>. Смерть
    /// подаётся настоящим уроном (<c>UxrActor.ReceiveImpact</c> / <c>ReceiveDamage</c>), а не
    /// прямым вызовом. Самоубийство и урон без источника убийства не дают, смерть — дают.
    /// Статистика ведётся по сессии и переживает смену карты: строки карт отдельно, TOTAL — сумма.
    /// </para>
    /// </summary>
    public class SeriesStatsTests : MirrorTestHarness
    {
        private TeamData _a, _b;
        private readonly List<string> _loads = new List<string>();

        [SetUp]
        public void Prepare()
        {
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);
            _loads.Clear();
        }

        private Series CreateSeries()
        {
            Series series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(series, "Awake");
            series.LoadMapOverride = scene => _loads.Add(scene);
            SpawnOnServer(series);
            series.ServerBegin(new[] { "MapA", "MapB" });
            return series;
        }

        /// <summary>Оркестратор с матчем: урон по игрокам проходит.</summary>
        private MapReferee CreateMatch()
        {
            MapReferee manager = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(manager, "Awake");
            InvokePrivateMethod(manager, "RegisterActiveGameMode", CreateNetworkComponent<EliminationMode>("EliminationMode"));
            return manager;
        }

        private PlayerController CreatePlayer(string name, TeamData team)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name + "_Session");
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = team.teamIndex;

            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            InvokePrivateMethod(player, "LinkSession", session);
            actor.Life = 100f;
            return player;
        }

        private static UxrActor Actor(PlayerController p) => p.GetComponent<UxrActor>();

        [Test]
        public void Убийство_засчитывается_убийце_а_смерть_жертве()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            CreateMatch();
            PlayerController killer = CreatePlayer("Killer", _a);
            PlayerController victim = CreatePlayer("Victim", _b);

            Actor(victim).ReceiveImpact(Actor(killer), default(RaycastHit), 500f);

            Assert.IsFalse(victim.IsAlive, "Контроль: жертва погибла от урона.");
            Assert.AreEqual(1, series.GetKills(killer.Session, 0), "Убийство не засчитано убийце в строке карты.");
            Assert.AreEqual(1, series.GetKills(killer.Session, Series.Total), "Убийство не попало в TOTAL.");
            Assert.AreEqual(1, series.GetDeaths(victim.Session, 0), "Смерть не засчитана жертве.");
            Assert.AreEqual(0, series.GetKills(victim.Session, Series.Total), "Жертве засчитано убийство.");
            Assert.AreEqual(0, series.GetDeaths(killer.Session, Series.Total), "Убийце засчитана смерть.");
        }

        [Test]
        public void Ассист_засчитывается_тому_кто_ранил_но_не_добил()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            CreateMatch();
            PlayerController helper = CreatePlayer("Helper", _a);
            PlayerController killer = CreatePlayer("Killer", _a);
            PlayerController victim = CreatePlayer("Victim", _b);

            Actor(victim).ReceiveImpact(Actor(helper), default(RaycastHit), 30f);
            Actor(victim).ReceiveImpact(Actor(killer), default(RaycastHit), 500f);

            Assert.AreEqual(1, series.GetKills(killer.Session, Series.Total));
            Assert.AreEqual(0, series.GetKills(helper.Session, Series.Total), "Раненому засчитано убийство.");
            Assert.AreEqual(1, series.GetAssists(helper.Session, Series.Total), "Ассист не засчитан.");
        }

        [Test]
        public void Самоубийство_и_урон_без_источника_не_дают_убийства()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            CreateMatch();
            PlayerController self = CreatePlayer("Self", _a);
            PlayerController fallen = CreatePlayer("Fallen", _b);

            Actor(self).ReceiveImpact(Actor(self), default(RaycastHit), 500f);
            Actor(fallen).ReceiveDamage(500f);

            Assert.IsFalse(self.IsAlive);
            Assert.IsFalse(fallen.IsAlive);
            Assert.AreEqual(0, series.GetKills(self.Session, Series.Total), "Самоубийство засчитано как убийство.");
            Assert.AreEqual(0, series.GetKills(fallen.Session, Series.Total));
            Assert.AreEqual(1, series.GetDeaths(self.Session, Series.Total), "Смерть от своей руки не засчитана.");
            Assert.AreEqual(1, series.GetDeaths(fallen.Session, Series.Total), "Смерть без источника не засчитана.");
        }

        [Test]
        public void Статистика_переживает_смену_карты_строки_раздельно_TOTAL_сумма()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            PlayerSession k = CreateNetworkComponent<PlayerSession>("K");
            PlayerSession v = CreateNetworkComponent<PlayerSession>("V");
            SpawnOnServer(k);
            SpawnOnServer(v);

            series.ServerRecordKill(v, k, null);
            series.ServerRecordRoundWin(_a);
            series.ServerAdvance(); // → MapB
            series.ServerRecordKill(v, k, null);
            series.ServerRecordKill(v, k, null);
            series.ServerRecordRoundWin(_a);
            series.ServerRecordRoundWin(_b);

            Assert.AreEqual(1, series.GetKills(k, 0), "Строка первой карты потерялась при смене карты.");
            Assert.AreEqual(2, series.GetKills(k, 1));
            Assert.AreEqual(3, series.GetKills(k, Series.Total));
            Assert.AreEqual(3, series.GetDeaths(v, Series.Total));

            Assert.AreEqual(1, series.GetRoundsWon(_a, 0));
            Assert.AreEqual(1, series.GetRoundsWon(_a, 1));
            Assert.AreEqual(2, series.GetRoundsWon(_a, Series.Total));
            Assert.AreEqual(1, series.GetRoundsWon(_b, Series.Total));
        }

        // ── Карта без серии ──────────────────────────────────────────────────

        /// <summary>Серия есть на сессии, но не начата — карта загружена напрямую (отладка, E2E).</summary>
        private Series CreateIdleSeries()
        {
            Series series = CreateNetworkComponent<Series>("Series");
            InvokeLifecycleMethod(series, "Awake");
            series.LoadMapOverride = scene => _loads.Add(scene);
            SpawnOnServer(series);
            return series;
        }

        private MapReferee CreateMatchOn(string scene)
        {
            MapReferee manager = CreateMatch();
            manager.SceneNameOverride = scene;
            return manager;
        }

        [Test]
        public void Карта_без_серии_ведёт_статистику_по_себе()
        {
            SilenceMirrorNoise();
            Series series = CreateIdleSeries();
            CreateMatchOn("MapX");
            PlayerController killer = CreatePlayer("Killer", _a);
            PlayerController victim = CreatePlayer("Victim", _b);

            Actor(victim).ReceiveImpact(Actor(killer), default(RaycastHit), 500f);

            Assert.IsFalse(series.IsRunning, "Прямая загрузка карты начала серию — со следующей картой и лобби.");
            CollectionAssert.AreEqual(new[] { "MapX" }, series.Maps, "Статистика без серии не привязана к карте.");
            Assert.AreEqual(1, series.GetKills(killer.Session, 0), "Убийство на карте без серии не записано.");
            Assert.AreEqual(1, series.GetKills(killer.Session, Series.Total), "TOTAL без серии не равен строке карты.");
            Assert.AreEqual(1, series.GetDeaths(victim.Session, Series.Total));

            string text = VrBattlegrounds.UI.Menu.MenuStatistics.BuildText(series);
            StringAssert.Contains("MapX", text, "Экран статистики не показывает карту без серии.");
            StringAssert.Contains(SeriesStatsTable.TotalTitle, text);

            Assert.IsEmpty(_loads, "Статистика без серии загрузила карту.");
        }

        [Test]
        public void Прямая_загрузка_другой_карты_начинает_статистику_заново()
        {
            SilenceMirrorNoise();
            Series series = CreateIdleSeries();
            MapReferee first = CreateMatchOn("MapX");
            PlayerController killer = CreatePlayer("Killer", _a);
            PlayerController victim = CreatePlayer("Victim", _b);
            Actor(victim).ReceiveImpact(Actor(killer), default(RaycastHit), 500f);
            Assert.AreEqual(1, series.GetKills(killer.Session, Series.Total), "Контроль: убийство на первой карте.");

            // Новая сцена: прежний оркестратор ушёл вместе с ней, пришёл новый.
            Object.DestroyImmediate(first.gameObject);
            CreateMatchOn("MapY");
            victim.Respawn();
            Actor(victim).ReceiveImpact(Actor(killer), default(RaycastHit), 500f);

            CollectionAssert.AreEqual(new[] { "MapY" }, series.Maps, "Статистика другой карты без серии не начата заново.");
            Assert.AreEqual(1, series.GetKills(killer.Session, Series.Total),
                "Статистика прошлой карты без серии перетекла в новую.");
        }

        [Test]
        public void Таблица_статистики_группирует_по_картам_и_считает_TOTAL()
        {
            SilenceMirrorNoise();
            Series series = CreateSeries();
            PlayerSession k = CreateNetworkComponent<PlayerSession>("K");
            SpawnOnServer(k);
            k.PlayerName = "Петя";
            PlayerSession v = CreateNetworkComponent<PlayerSession>("V");
            SpawnOnServer(v);
            v.PlayerName = "Вася";

            series.ServerRecordKill(v, k, null);
            series.ServerAdvance();
            series.ServerRecordKill(v, k, null);

            List<SeriesStatsTable.Section> table = SeriesStatsTable.Build(series.Maps, series.TeamStats, series.PlayerStats);

            Assert.AreEqual(3, table.Count, "Ожидались секции: MapA, MapB, TOTAL.");
            Assert.AreEqual("MapA", table[0].Title);
            Assert.AreEqual("MapB", table[1].Title);
            Assert.AreEqual(SeriesStatsTable.TotalTitle, table[2].Title);

            SeriesStatsTable.PlayerRow petya = table[2].Players.Find(r => r.Name == "Петя");
            Assert.IsNotNull(petya, "В TOTAL нет строки убийцы.");
            Assert.AreEqual(2, petya.Kills, "TOTAL не сложил убийства по картам.");

            string text = SeriesStatsTable.Format(table, i => "Команда " + i);
            StringAssert.Contains("MapA", text);
            StringAssert.Contains("MapB", text);
            StringAssert.Contains(SeriesStatsTable.TotalTitle, text);
            StringAssert.Contains("Петя — 2 / 0 / 0", text, "Строка TOTAL экрана статистики не та.");
        }
    }
}
