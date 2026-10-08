using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Economy
{
    /// <summary>
    /// Экономика матча на сервере (T-45, ярус A): настоящий <see cref="EliminationMode"/> с
    /// <see cref="MatchEconomy"/> на том же объекте, раунды крутит <see cref="RoundFlowDriver"/>.
    /// Проверяет выплаты за раунд и половину, награду за убийство, серверное списание и возврат
    /// со стены, отказ в покупке в долг, подсветку слотов по деньгам владельца, раздачу стен по
    /// зонам, табло и снимок паузы.
    /// </summary>
    public class MatchEconomyServerTests : MirrorTestHarness
    {
        private TeamData _a, _b;
        private EliminationMode _mode;
        private MatchEconomy _economy;
        private StubPlayerRoster _roster;
        private PlayerSession _pa, _pb, _pa2;
        private RoundFlowDriver _driver;
        private readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();
            _a = TeamRegistry.Instance.GetByIndex(1);
            _b = TeamRegistry.Instance.GetByIndex(2);

            _mode = CreateMode("EliminationMode", out _economy);

            _roster = new StubPlayerRoster();
            _pa = Session("PA", _a, "dev_a");
            _pa2 = Session("PA2", _a, "dev_a2");
            _pb = Session("PB", _b, "dev_b");
            _roster.Add(_a, _pa);
            _roster.Add(_a, _pa2);
            _roster.Add(_b, _pb);
            _mode.PlayerRoster = _roster;
            _mode.Initialize(new[] { _a, _b });

            _driver = new RoundFlowDriver(
                dt =>
                {
                    _roster.DeclareAllReady();
                    _mode.ServerTick(dt);
                },
                () => _mode.CurrentRoundPhase);
        }

        [TearDown]
        public void Drop()
        {
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        private EliminationMode CreateMode(string name, out MatchEconomy economy)
        {
            GameObject go = CreateNetworkObject(name);
            EliminationMode mode = go.AddComponent<EliminationMode>();
            economy = go.AddComponent<MatchEconomy>();
            go.AddComponent<ArsenalCheckout>();
            EnableNetworking(go);
            InvokeLifecycleMethod(economy, "Awake");
            SpawnOnServer(mode);
            return mode;
        }

        private PlayerSession Session(string name, TeamData team, string token)
        {
            GameObject go = CreateNetworkObject(name);
            PlayerSession session = go.AddComponent<PlayerSession>();
            EnableNetworking(go);
            SpawnOnServer(session);
            session.TeamIndex = team.teamIndex;
            session.DeviceToken = token;
            session.PlayerName = name;
            session.ServerEnterSpawnZone(session.TeamIndex);
            return session;
        }

        private void StartMatch() =>
            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber == 1, "первого раунда");

        /// <summary>
        /// Доводит раунд до боя, объявляет исход и останавливается на входе в Resolution — деньги за
        /// раунд уже начислены, а следующий раунд (и сброс половины) ещё не начался.
        /// </summary>
        private void PlayRound(TeamData winner)
        {
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Combat, "боя");
            _mode.RoundPhases.RequestRoundEnd(winner);
            _driver.AdvanceUntil(() => _mode.CurrentRoundPhase == RoundPhase.Resolution, "итогов раунда");
        }

        private WeaponInfo Weapon(int price, int killAward = 300)
        {
            WeaponInfo info = ScriptableObject.CreateInstance<WeaponInfo>();
            SetPrivateField(info, "_price", price);
            SetPrivateField(info, "_killAward", killAward);
            SetPrivateField(info, "_displayName", "Test" + price);
            _assets.Add(info);
            return info;
        }

        // ── Раунды и половины ─────────────────────────────────

        [Test]
        public void Старт_матча_у_всех_800()
        {
            SilenceMirrorNoise();
            StartMatch();

            Assert.AreEqual(800, _economy.GetMoney(_pa));
            Assert.AreEqual(800, _economy.GetMoney(_pa2));
            Assert.AreEqual(800, _economy.GetMoney(_pb));
        }

        [Test]
        public void Победа_3250_каждому_поражение_по_лестнице()
        {
            SilenceMirrorNoise();
            StartMatch();

            PlayRound(_a);
            Assert.AreEqual(800 + 3250, _economy.GetMoney(_pa));
            Assert.AreEqual(800 + 3250, _economy.GetMoney(_pa2), "Награда — каждому игроку команды.");
            Assert.AreEqual(800 + 1900, _economy.GetMoney(_pb), "Проигранный пистолетный раунд — 1900.");
            Assert.AreEqual(3250, _economy.GetRoundIncome(_pa), "Доход за раунд — для «+3250» на табло.");

            PlayRound(_a);
            Assert.AreEqual(800 + 1900 + 2400, _economy.GetMoney(_pb), "Второе поражение подряд — 2400.");

            PlayRound(null);
            Assert.AreEqual(800 + 3250 * 2 + 1400, _economy.GetMoney(_pa),
                "Ничья — поражение обеим; у A счётчик был 0 → 1400.");
        }

        [Test]
        public void Начало_второй_половины_сбрасывает_деньги()
        {
            SilenceMirrorNoise();
            StartMatch();

            // _roundsPerHalf = 3 по умолчанию: раунды 1–3 — первая половина. Счёт 2:1, чтобы карта не кончилась.
            PlayRound(_a);
            PlayRound(_b);
            PlayRound(_a);
            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber == 4, "раунда 4");

            Assert.AreEqual(4, _mode.CurrentRoundNumber, "Контроль: начался раунд 4 — вторая половина.");
            Assert.AreEqual(800, _economy.GetMoney(_pa));
            Assert.AreEqual(800, _economy.GetMoney(_pb));
            Assert.AreEqual(1, _economy.GetLossCounter(_b.teamIndex));
        }

        // ── Убийства ──────────────────────────────────────────

        [Test]
        public void Убийство_даёт_kill_award_оружия_а_союзник_штраф()
        {
            SilenceMirrorNoise();
            StartMatch();

            _economy.ServerAwardKill(_pb, _pa, Weapon(1050, 600));
            Assert.AreEqual(800 + 600, _economy.GetMoney(_pa));

            _economy.ServerAwardKill(_pb, _pa, null);
            Assert.AreEqual(800 + 600 + 300, _economy.GetMoney(_pa), "Оружие неизвестно — 300.");

            _economy.ServerAwardKill(_pa2, _pa, Weapon(1050, 600));
            Assert.AreEqual(800 + 600, _economy.GetMoney(_pa), "Союзник — минус 300.");

            _economy.ServerAwardKill(_pa, _pa, null);
            Assert.AreEqual(800 + 600, _economy.GetMoney(_pa), "Самоубийство денег не даёт.");
        }

        // ── Стена: владелец, подсветка, касса ─────────────────

        private ArsenalWallController Wall(string name, WeaponInfo info, Transform parent = null)
        {
            GameObject wallObject = CreateNetworkObject(name);
            if (parent != null) wallObject.transform.SetParent(parent, false);

            GameObject slotObject = new GameObject("Slot");
            slotObject.transform.SetParent(wallObject.transform);
            GameObject anchorObject = new GameObject("ItemAnchor");
            anchorObject.transform.SetParent(slotObject.transform);

            ArsenalSlotController slot = slotObject.AddComponent<ArsenalSlotController>();
            SetPrivateField(slot, "_itemAnchor", anchorObject.AddComponent<UxrGrabbableObjectAnchor>());
            SetPrivateField(slot, "_weaponInfo", info);

            ArsenalWallController wall = wallObject.AddComponent<ArsenalWallController>();
            EnableNetworking(wallObject);
            InvokeLifecycleMethod(wall, "Awake");
            InvokeLifecycleMethod(wall, "Start");
            SpawnOnServer(wall);
            return wall;
        }

        /// <summary>Режим — активный у менеджера карты: так его находит <see cref="MatchEconomy.Current"/>.</summary>
        private void MakeModeActive()
        {
            var referee = CreateNetworkComponent<MapReferee>("MapReferee");
            InvokeLifecycleMethod(referee, "Awake");
            InvokePrivateMethod(referee, "RegisterActiveGameMode", _mode);
        }

        private UxrGrabbableObject NetworkItem(string name)
        {
            GameObject go = CreateNetworkObject(name);
            UxrGrabbableObject grabbable = go.AddComponent<UxrGrabbableObject>();
            EnableNetworking(go);
            NetworkServer.Spawn(go);
            return grabbable;
        }

        [Test]
        public void Слоты_подсвечены_по_деньгам_владельца_и_обновляются()
        {
            SilenceMirrorNoise();
            StartMatch();
            MakeModeActive();

            ArsenalWallController wall = Wall("Wall", Weapon(1000));
            ArsenalSlotController slot = wall.Slots[0];

            wall.RefreshOffers();
            Assert.AreEqual(SlotOffer.NoOwner, slot.Offer, "Ничья стена при экономике — не продаёт.");

            wall.ServerSetOwner(_pa.netId);
            wall.RefreshOffers();
            Assert.AreEqual(SlotOffer.TooExpensive, slot.Offer, "800 < 1000.");

            _economy.ServerAwardKill(_pb, _pa, null);
            wall.RefreshOffers();
            Assert.AreEqual(SlotOffer.Affordable, slot.Offer, "Деньги выросли — подсветка обновилась.");
        }

        [Test]
        public void Без_экономики_стена_бесплатная()
        {
            SilenceMirrorNoise();
            ArsenalWallController wall = Wall("Wall", Weapon(5000));
            wall.ServerSetOwner(_pa.netId);
            wall.RefreshOffers();
            Assert.AreEqual(SlotOffer.Free, wall.Slots[0].Offer, "Режима с экономикой нет (разминка) — всё бесплатно.");
        }

        [Test]
        public void Касса_списывает_с_владельца_и_возвращает_за_повешенный_обратно()
        {
            SilenceMirrorNoise();
            StartMatch();

            WeaponInfo pistol = Weapon(700);
            ArsenalWallController wall = Wall("Wall", pistol);
            wall.ServerSetOwner(_pa.netId);
            wall.OpenArsenal(immediate: true);
            Assert.IsTrue(wall.CanTrade, "Покупка и возврат проверяются на открытой торговой стене.");
            ArsenalCheckout checkout = _mode.GetComponent<ArsenalCheckout>();
            InvokeLifecycleMethod(checkout, "Awake");
            UxrGrabbableObject item = NetworkItem("Item");

            checkout.HandleItemTaken(wall, wall.Slots[0], item, null);
            Assert.AreEqual(100, _economy.GetMoney(_pa), "Списано на сервере в момент взятия.");
            Assert.AreEqual(0, checkout.PendingRejections);

            checkout.HandleItemReturned(wall, wall.Slots[0], item);
            Assert.AreEqual(800, _economy.GetMoney(_pa), "Повесил обратно в ту же закупку — деньги вернулись.");

            checkout.HandleItemReturned(wall, wall.Slots[0], item);
            Assert.AreEqual(800, _economy.GetMoney(_pa), "Второй возврат без покупки — ничего.");
        }

        [Test]
        public void Касса_отменяет_покупку_в_долг()
        {
            SilenceMirrorNoise();
            StartMatch();

            ArsenalWallController wall = Wall("Wall", Weapon(2900));
            wall.ServerSetOwner(_pa.netId);
            wall.OpenArsenal(immediate: true);
            Assert.IsTrue(wall.CanTrade, "Отказ должен быть вызван нехваткой денег, а не закрытой стеной.");
            ArsenalCheckout checkout = _mode.GetComponent<ArsenalCheckout>();
            InvokeLifecycleMethod(checkout, "Awake");

            checkout.HandleItemTaken(wall, wall.Slots[0], NetworkItem("Item"), null);

            Assert.AreEqual(800, _economy.GetMoney(_pa), "Клиенту не доверяем: денег нет — не списано.");
            Assert.AreEqual(1, checkout.PendingRejections, "Захват отменяется — ствол вернётся на стену.");
        }

        [Test]
        public void Касса_отменяет_покупку_с_ничьей_стены()
        {
            SilenceMirrorNoise();
            StartMatch();

            ArsenalWallController wall = Wall("Wall", Weapon(200));
            wall.OpenArsenal(immediate: true);
            Assert.IsTrue(wall.CanTrade, "Отказ должен быть вызван отсутствием владельца, а не закрытой стеной.");
            ArsenalCheckout checkout = _mode.GetComponent<ArsenalCheckout>();
            InvokeLifecycleMethod(checkout, "Awake");

            checkout.HandleItemTaken(wall, wall.Slots[0], NetworkItem("Item"), null);
            Assert.AreEqual(1, checkout.PendingRejections);
        }

        [Test]
        public void Стены_зоны_закрепляются_за_игроками_её_команды()
        {
            SilenceMirrorNoise();
            StartMatch();

            GameObject zoneObject = CreateObject("Zone_A");
            TeamSpawnZone zone = zoneObject.AddComponent<TeamSpawnZone>();
            SetPrivateField(zone, "_team", _a);

            ArsenalWallController w1 = Wall("W1", Weapon(200), zoneObject.transform);
            ArsenalWallController w2 = Wall("W2", Weapon(200), zoneObject.transform);
            ArsenalWallController w3 = Wall("W3", Weapon(200), zoneObject.transform);
            ArsenalWallController outside = Wall("Outside", Weapon(200));

            ArsenalOwnershipPolicy policy = _mode.gameObject.AddComponent<ArsenalOwnershipPolicy>();
            policy.ServerAssign();

            var owners = new HashSet<uint> { w1.OwnerSessionNetId, w2.OwnerSessionNetId, w3.OwnerSessionNetId };
            Assert.IsTrue(owners.Contains(_pa.netId) && owners.Contains(_pa2.netId), "Каждому игроку A — своя стена.");
            Assert.IsTrue(owners.Contains(0u), "Третья стена A ничья: игроков двое.");
            Assert.IsFalse(owners.Contains(_pb.netId), "Игрок B не получает стену в зоне A.");
            Assert.AreEqual(0u, outside.OwnerSessionNetId, "Стена вне командной зоны ничья.");

            uint before = w1.OwnerSessionNetId;
            policy.ServerAssign();
            Assert.AreEqual(before, w1.OwnerSessionNetId, "Повторная раздача никого не пересаживает.");
        }

        [Test]
        public void Табло_стены_показывает_деньги_владельца()
        {
            SilenceMirrorNoise();
            StartMatch();
            MakeModeActive();

            Assert.IsNull(ArsenalWalletDisplay.Compose(null, _pa, true), "Без экономики табло пустое.");

            string text = ArsenalWalletDisplay.Compose(_economy, _pa, true);
            StringAssert.Contains("PA", text);
            StringAssert.Contains("$800", text);

            PlayRound(_a);
            text = ArsenalWalletDisplay.Compose(_economy, _pa, true);
            StringAssert.Contains("$4050", text);
            StringAssert.Contains("+3250", text);
        }

        // ── Пауза ─────────────────────────────────────────────

        [Test]
        public void Пауза_уносит_деньги_на_начало_раунда()
        {
            SilenceMirrorNoise();
            StartMatch();
            PlayRound(_a);
            _driver.AdvanceUntil(() => _mode.CurrentRoundNumber == 2, "раунда 2"); // на начало раунда 2: A 4050, B 2700
            _economy.ServerAwardKill(_pb, _pa, null); // в прерванном раунде — не сохраняется

            PauseSnapshot snapshot = _mode.CaptureSnapshot();

            EliminationMode resumed = CreateMode("Resumed", out MatchEconomy restored);
            resumed.RestoreSnapshot(snapshot);

            Assert.AreEqual(4050, restored.GetMoney(_pa), "Деньги — на начало прерванного раунда.");
            Assert.AreEqual(2700, restored.GetMoney(_pb));
            Assert.AreEqual(2, restored.GetLossCounter(_b.teamIndex));
        }

        // ── Префабы режимов ───────────────────────────────────

        [Test]
        public void Экономика_есть_только_у_режима_матча_Elimination()
        {
            var elimination = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameModes/EliminationMode.prefab");
            var warmup = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameModes/WarmupMode.prefab");
            Assert.IsNotNull(elimination);
            Assert.IsNotNull(warmup);

            Assert.IsNotNull(elimination.GetComponent<MatchEconomy>(), "Elimination без MatchEconomy — денег нет.");
            Assert.IsNotNull(elimination.GetComponent<ArsenalCheckout>(), "Нет кассы — покупки не списываются.");
            Assert.IsNotNull(elimination.GetComponent<ArsenalOwnershipPolicy>(), "Стены не закрепляются за игроками.");
            Assert.IsNotNull(elimination.GetComponent<StartingSidearmPolicy>(), "Нет выдачи стартового пистолета.");
            Assert.IsNull(warmup.GetComponent<MatchEconomy>(), "Разминка бесплатная (решение T-45).");
        }

        [Test]
        public void Стартовый_пистолет_живому_без_пистолета_раз_за_раунд()
        {
            Assert.IsTrue(StartingSidearmPolicy.ShouldGrant(true, false, false, RoundPhase.Setup));
            Assert.IsTrue(StartingSidearmPolicy.ShouldGrant(true, false, false, RoundPhase.Equipment));
            Assert.IsFalse(StartingSidearmPolicy.ShouldGrant(false, false, false, RoundPhase.Equipment), "Выбывший.");
            Assert.IsFalse(StartingSidearmPolicy.ShouldGrant(true, true, false, RoundPhase.Equipment), "Уже выдан.");
            Assert.IsFalse(StartingSidearmPolicy.ShouldGrant(true, false, true, RoundPhase.Equipment), "Пистолет есть.");
            Assert.IsFalse(StartingSidearmPolicy.ShouldGrant(true, false, false, RoundPhase.Combat), "Бой.");
        }
    }
}
