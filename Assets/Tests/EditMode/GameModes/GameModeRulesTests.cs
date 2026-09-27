using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Правила, которые активный режим объявляет остальной игре: стена арсенала,
    /// блокировка оружия, раздача команд. Разминка (бывшее лобби) — такой же режим (<see cref="WarmupMode"/>).
    ///
    /// <para>
    /// Что доказывает. Раньше стена и <c>GameplayManager</c> знали конкретный режим
    /// (<c>is EliminationMode</c>, подписка на его фазы), а лобби жило особым случаем —
    /// правилом сцены <c>LobbyFreePlay</c>. Теперь режим объявляет правила через базовый
    /// <see cref="GameMode"/>, а стена и оружие исполняют их, не зная типа режима.
    /// Тесты гоняют одну и ту же стену под <see cref="WarmupMode"/> и под
    /// <see cref="EliminationMode"/> и проверяют, что поведение задаёт режим.
    /// </para>
    ///
    /// <para>
    /// Перенесено из <c>LobbyFreePlayTests</c> (правило сцены удалено): открытие стены
    /// в лобби, повторное открытие закрытой извне, отсутствие жетона, включение оружия
    /// после матча.
    /// </para>
    /// </summary>
    public class GameModeRulesTests : MirrorTestHarness
    {
        private const ArsenalWallController.ArsenalState Closed = ArsenalWallController.ArsenalState.Closed;
        private const ArsenalWallController.ArsenalState Open = ArsenalWallController.ArsenalState.Open;

        private UxrWeaponManager _weaponManager;
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void ReleaseSingletons()
        {
            // Синглтон SDK статический: без Release следующий тест увидел бы
            // уничтоженный менеджер как живой.
            if (_weaponManager != null)
            {
                InvokePrivateMethod(_weaponManager, "OnDestroy");
                Object.DestroyImmediate(_weaponManager.gameObject);
                _weaponManager = null;
            }

            foreach (Object o in _assets) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        // ── Заготовки ────────────────────────────────────────────────────────

        private GameplayManager CreateGameplayManager()
        {
            GameplayManager manager = CreateNetworkComponent<GameplayManager>("GameplayManager");
            InvokeLifecycleMethod(manager, "Awake");
            return manager;
        }

        private T CreateActiveMode<T>(GameplayManager manager) where T : GameMode
        {
            T mode = CreateNetworkComponent<T>(typeof(T).Name);
            InvokePrivateMethod(manager, "RegisterActiveGameMode", mode);
            return mode;
        }

        private static void SetPhase(EliminationMode mode, RoundState phase)
        {
            SetPrivateField(mode, "_roundState", phase);
        }

        private ArsenalWallController CreateWall(string name, out DogTagController dogTag)
        {
            GameObject wallObject = CreateNetworkObject(name);

            GameObject slotObject = new GameObject("Slot");
            slotObject.transform.SetParent(wallObject.transform);

            GameObject anchorObject = new GameObject("ItemAnchor");
            anchorObject.transform.SetParent(slotObject.transform);

            ArsenalSlotController slot = slotObject.AddComponent<ArsenalSlotController>();
            SetPrivateField(slot, "_itemAnchor", anchorObject.AddComponent<UxrGrabbableObjectAnchor>());

            GameObject tagPanel = new GameObject("DogTagPanel");
            tagPanel.transform.SetParent(wallObject.transform);
            GameObject tagObject = new GameObject("DogTag");
            tagObject.transform.SetParent(tagPanel.transform);

            dogTag = tagPanel.AddComponent<DogTagController>();
            SetPrivateField(dogTag, "_tagObject", tagObject.AddComponent<UxrGrabbableObject>());

            ArsenalWallController wall = wallObject.AddComponent<ArsenalWallController>();
            EnableNetworking(wallObject);

            InvokeLifecycleMethod(wall, "Awake");
            InvokeLifecycleMethod(wall, "Start");

            return wall;
        }

        private ArsenalWallController CreateServerWall(out DogTagController dogTag)
        {
            ArsenalWallController wall = CreateWall("ServerWall", out dogTag);
            SpawnOnServer(wall);
            return wall;
        }

        private static void Tick(ArsenalWallController wall) => wall.ApplyModeRules(0.1f);

        private UxrWeaponManager CreateWeaponManager(bool enabled)
        {
            _weaponManager = new GameObject("WeaponManager").AddComponent<UxrWeaponManager>();
            InvokeLifecycleMethod(_weaponManager, "Awake");
            Assert.IsTrue(UxrWeaponManager.HasInstance, "Контроль: менеджер оружия зарегистрирован.");
            _weaponManager.SetWeaponSystemEnabled(enabled);
            return _weaponManager;
        }

        private TeamData CreateTeam(string name, int index)
        {
            TeamData team = ScriptableObject.CreateInstance<TeamData>();
            team.displayName = name;
            team.teamIndex = index;
            _assets.Add(team);
            return team;
        }

        private PlayerSession CreateSession(string name, int teamIndex)
        {
            PlayerSession session = CreateNetworkComponent<PlayerSession>(name);
            SpawnOnServer(session);
            session.PlayerName = name;
            session.TeamIndex = teamIndex;
            return session;
        }

        private sealed class ListRoster : IPlayerRoster
        {
            public readonly List<PlayerSession> Players = new List<PlayerSession>();
            public IEnumerable<PlayerSession> GetPlayers(TeamData team) => Players.Where(p => team != null && p.TeamIndex == team.teamIndex);
            public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team) => Enumerable.Empty<PlayerSession>();
            public IEnumerable<PlayerSession> GetAllPlayers() => Players;
        }

        // ── Стена арсенала ───────────────────────────────────────────────────

        [Test]
        public void Без_режима_стена_не_трогается()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out _);
            Tick(wall);

            Assert.AreEqual(Closed, wall.CurrentState, "Режима нет — правил нет, стена стоит как стояла.");
        }

        [Test]
        public void В_разминке_серверная_стена_открыта()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out _);
            Assert.AreEqual(Closed, wall.CurrentState, "Контроль: без режима стена стоит закрытой.");

            CreateActiveMode<WarmupMode>(CreateGameplayManager());
            Tick(wall);

            Assert.AreEqual(Open, wall.CurrentState,
                "Лобби-режим объявил «арсенал открыт», а стена осталась закрытой. Правило " +
                "обязан исполнить сервер — клиенты получат состояние репликацией.");
        }

        [Test]
        public void В_разминке_стена_закрытая_извне_открывается_снова()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out _);
            CreateActiveMode<WarmupMode>(CreateGameplayManager());
            Tick(wall);

            wall.SetClosedImmediate();
            Tick(wall);

            Assert.AreEqual(Open, wall.CurrentState, "В разминке арсенал открыт всегда, а не только на старте.");
        }

        [Test]
        public void В_разминке_жетона_готовности_нет()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out DogTagController dogTag);
            GameObject tagObject = dogTag.transform.Find("DogTag").gameObject;
            Assert.IsTrue(tagObject.activeSelf, "Контроль: без режима жетон на стене есть.");

            CreateActiveMode<WarmupMode>(CreateGameplayManager());
            Tick(wall);

            Assert.AreEqual(Open, wall.CurrentState, "Контроль: стена открыта.");
            Assert.IsFalse(tagObject.activeSelf,
                "В разминке раунда нет, а жетон объявляет готовность к раунду. Схваченный " +
                "в разминке, он записал бы готовность, которую никто не ждёт.");
        }

        [Test]
        public void В_Elimination_стена_открыта_только_в_закупку()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out _);
            EliminationMode mode = CreateActiveMode<EliminationMode>(CreateGameplayManager());

            SetPhase(mode, RoundState.Setup);
            Tick(wall);
            Assert.AreEqual(Closed, wall.CurrentState, "В Setup арсенал ещё закрыт.");

            SetPhase(mode, RoundState.Equipment);
            Tick(wall);
            Assert.AreEqual(Open, wall.CurrentState, "Закупка началась, а стена не открылась.");

            SetPhase(mode, RoundState.Countdown);
            Tick(wall);
            Assert.AreEqual(Closed, wall.CurrentState, "Обратный отсчёт начался, а арсенал остался открытым.");

            SetPhase(mode, RoundState.Combat);
            Tick(wall);
            Assert.AreEqual(Closed, wall.CurrentState, "В бою арсенал открылся.");
        }

        [Test]
        public void В_Elimination_жетон_нужен_при_старте_по_готовности()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall(out DogTagController dogTag);
            EliminationMode mode = CreateActiveMode<EliminationMode>(CreateGameplayManager());

            SetPhase(mode, RoundState.Equipment);
            Tick(wall);

            Assert.IsTrue(mode.ArsenalRules.UsesReadinessTag, "RoundStartRule.Readiness — жетон нужен.");
            Assert.IsTrue(dogTag.transform.Find("DogTag").gameObject.activeSelf,
                "Закупка по готовности, а жетона на открытой стене нет.");
        }

        // ── Оружие ───────────────────────────────────────────────────────────

        [Test]
        public void В_разминке_оружие_включается_после_матча()
        {
            SilenceMirrorNoise();

            // Ровно так его оставляет матч, если карту выгрузили вне фазы Combat:
            // UxrWeaponManager переживает смену сцены.
            UxrWeaponManager weapons = CreateWeaponManager(enabled: false);

            GameplayManager manager = CreateGameplayManager();
            CreateActiveMode<WarmupMode>(manager);
            InvokePrivateMethod(manager, "Update");

            Assert.IsTrue(weapons.WeaponSystemEnabled, "В разминке оружие обязано стрелять.");
        }

        [Test]
        public void В_Elimination_оружие_стреляет_только_в_бою()
        {
            SilenceMirrorNoise();

            UxrWeaponManager weapons = CreateWeaponManager(enabled: true);

            GameplayManager manager = CreateGameplayManager();
            EliminationMode mode = CreateActiveMode<EliminationMode>(manager);

            SetPhase(mode, RoundState.Equipment);
            InvokePrivateMethod(manager, "Update");
            Assert.IsFalse(weapons.WeaponSystemEnabled, "В закупке оружие стреляет.");

            SetPhase(mode, RoundState.Combat);
            InvokePrivateMethod(manager, "Update");
            Assert.IsTrue(weapons.WeaponSystemEnabled, "В бою оружие не стреляет.");
        }

        // Тест «Режим_сцены_бьёт_выбор_администратора» удалён вместе с полем «режим сцены»
        // у GameplayManager: какой режим запустить на карте, теперь решает совместимость
        // режимов с картой (MapData.supportedModes) — см. MapModeRulesTests и MatchFlowTests.

        // ── Планшет ──────────────────────────────────────────────────────────

        [Test]
        public void В_разминке_планшет_не_предлагает_команду()
        {
            SilenceMirrorNoise();

            TeamData warmupTeam = CreateTeam("Разминка", 3);
            GameModeData elimination = AssetDatabase.LoadAssetAtPath<GameModeData>(GameModeWiringTests.EliminationDataPath);

            WarmupMode warmup = CreateNetworkComponent<WarmupMode>("WarmupMode");
            SpawnOnServer(warmup);
            warmup.Initialize(new[] { warmupTeam });

            TeamData[] teams = VrBattlegrounds.UI.Menu.MenuTeamSelection.ResolveAvailableTeams(warmup, elimination, TeamRegistry.Instance.teams);

            CollectionAssert.AreEqual(new[] { warmupTeam }, teams,
                "В разминке планшет предлагает команды выбранного матча, а не команду разминки.");
            Assert.IsFalse(VrBattlegrounds.UI.Menu.MenuTeamSelection.OffersTeamChoice(teams),
                "В разминке команда одна — выбирать её незачем, сразу скины.");

            TeamData[] match = VrBattlegrounds.UI.Menu.MenuTeamSelection.ResolveAvailableTeams(null, elimination, TeamRegistry.Instance.teams);
            Assert.IsTrue(VrBattlegrounds.UI.Menu.MenuTeamSelection.OffersTeamChoice(match), "Контроль: у матча команд две.");
        }

        /// <summary>
        /// Разминка на карте посреди серии: у игрока команда матча (CT). Планшет обязан
        /// предложить только её скины — иначе выбор скина «Разминки» молча перевёл бы его
        /// из команды матча. А сервер обязан принять смену скина в своей команде, хотя
        /// команды матча среди команд разминки нет.
        /// </summary>
        [Test]
        public void В_разминке_с_командой_матча_планшет_меняет_только_скин()
        {
            SilenceMirrorNoise();

            TeamData warmupTeam = CreateTeam("Разминка", 3);
            TeamData ct = TeamRegistry.Instance.GetByIndex(1);
            Assert.IsNotNull(ct, "Контроль: команда 1 есть в TeamRegistry.");

            WarmupMode warmup = CreateNetworkComponent<WarmupMode>("WarmupMode");
            SpawnOnServer(warmup);
            warmup.TeamAssignment = TeamAssignmentKind.KeepOrDefault;
            warmup.Initialize(new[] { warmupTeam });

            TeamData[] teams = VrBattlegrounds.UI.Menu.MenuTeamSelection.ResolveAvailableTeams(
                warmup, null, TeamRegistry.Instance.teams, ct.teamIndex);
            CollectionAssert.AreEqual(new[] { ct }, teams,
                "В разминке игроку с командой матча предложена не его команда — выбор скина сменит ему команду.");

            TeamData[] fresh = VrBattlegrounds.UI.Menu.MenuTeamSelection.ResolveAvailableTeams(
                warmup, null, TeamRegistry.Instance.teams, 0);
            CollectionAssert.AreEqual(new[] { warmupTeam }, fresh, "Игроку без команды — команда разминки.");

            Assert.IsTrue(TeamChangeRules.CanPlayerChoose(warmup, ct.teamIndex, ct.teamIndex, out string reason),
                "Смена скина в своей команде матча в разминке отклонена: " + reason);
        }

        // ── Раздача команд ───────────────────────────────────────────────────

        /// <summary>
        /// Разминка не сбрасывает команды: раньше лобби-режим раздавал свою команду всем
        /// (автобаланс по одной команде). Теперь разминка идёт и на картах посреди серии,
        /// и команда матча (CT/T) обязана пережить её; команду разминки получает только
        /// игрок без команды.
        /// </summary>
        [Test]
        public void Разминка_даёт_свою_команду_только_игроку_без_команды()
        {
            SilenceMirrorNoise();

            TeamData warmupTeam = CreateTeam("Разминка", 3);
            var roster = new ListRoster();
            PlayerSession fresh = CreateSession("fresh", 0);
            PlayerSession ct = CreateSession("ct", 1);
            PlayerSession t = CreateSession("t", 2);
            roster.Players.Add(fresh);
            roster.Players.Add(ct);
            roster.Players.Add(t);

            WarmupMode mode = CreateNetworkComponent<WarmupMode>("WarmupMode");
            SpawnOnServer(mode);
            mode.PlayerRoster = roster;
            mode.TeamAssignment = TeamAssignmentKind.KeepOrDefault;
            mode.Initialize(new[] { warmupTeam });

            mode.ServerAssignTeams();

            Assert.AreEqual(3, fresh.TeamIndex, "Игрок без команды не получил команду разминки.");
            Assert.AreEqual(1, ct.TeamIndex, "Разминка сменила игроку команду матча (CT).");
            Assert.AreEqual(2, t.TeamIndex, "Разминка сменила игроку команду матча (T).");
        }

        [Test]
        public void Автобаланс_раскидывает_пришедших_из_лобби_поровну()
        {
            SilenceMirrorNoise();

            TeamData a = CreateTeam("A", 1), b = CreateTeam("B", 2);
            var roster = new ListRoster();
            for (int i = 0; i < 4; i++) roster.Players.Add(CreateSession("p" + i, 3));

            EliminationMode mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
            SpawnOnServer(mode);
            mode.PlayerRoster = roster;
            // Политика задана явно: без данных режима (GameModeData) политика по умолчанию —
            // выбор игроком (этап Б), а здесь проверяется сама раздача автобалансом.
            mode.TeamAssignment = TeamAssignmentKind.AutoBalance;
            mode.Initialize(new[] { a, b });

            mode.ServerAssignTeams();

            Assert.AreEqual(2, roster.Players.Count(p => p.TeamIndex == 1), "Команды неравны.");
            Assert.AreEqual(2, roster.Players.Count(p => p.TeamIndex == 2), "Команды неравны.");
        }
    }
}
