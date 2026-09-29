using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Проводка разминки, режимов и карт в ассетах и сценах.
    ///
    /// <para>
    /// Что доказывает. Разминка (<see cref="WarmupMode"/>, бывший лобби-режим) лежит
    /// в <c>GameModeRegistry</c> рядом с режимами матча, с флагом <c>isWarmup</c>, и в выбор
    /// режима матча не попадает. Совместимость режимов — у карты: лобби — обычная карта
    /// реестра с одной разминкой, у боевых карт первой идёт разминка, дальше режимы матча.
    /// Поля «режим сцены» у <c>GameplayManager</c> больше нет — прежние тесты
    /// <c>В_сцене_лобби_режим_сцены_лобби</c> и <c>На_картах_режим_сцены_не_задан</c>
    /// заменены проверками данных карт.
    /// </para>
    /// </summary>
    public class GameModeWiringTests
    {
        public const string WarmupModeDataPath = "Assets/Data/GameModes/Warmup_GameModeData.asset";
        public const string EliminationDataPath = "Assets/Data/GameModes/Elimination_GameModeData.asset";
        public const string RegistryPath = "Assets/Data/GameModes/GameModeRegistry.asset";
        public const string MapRegistryPath = "Assets/Data/Maps/MapRegistry.asset";
        public const string LobbyMapPath = "Assets/Data/Maps/MapData_Lobby.asset";
        private const string WarmupTeamPath = "Assets/Data/Teams/Warmup_Team.asset";
        private const string WarmupModePrefabPath = "Assets/Prefabs/GameModes/WarmupMode.prefab";
        private const string AvatarsRegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string SessionContextPath = "Assets/Prefabs/Managers/SessionContext.prefab";
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";

        private static GameModeData WarmupData()
        {
            GameModeData data = AssetDatabase.LoadAssetAtPath<GameModeData>(WarmupModeDataPath);
            Assert.IsNotNull(data, $"Нет данных разминки {WarmupModeDataPath}.");
            return data;
        }

        private static GameModeRegistry Registry() => AssetDatabase.LoadAssetAtPath<GameModeRegistry>(RegistryPath);

        private static MapRegistry Maps()
        {
            var maps = AssetDatabase.LoadAssetAtPath<MapRegistry>(MapRegistryPath);
            Assert.IsNotNull(maps, $"Нет {MapRegistryPath}.");
            return maps;
        }

        private static List<T> InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToList();

        // ── Данные режимов ───────────────────────────────────────────────────

        [Test]
        public void Разминка_в_реестре_с_флагом_и_не_в_выборе_режима_матча()
        {
            GameModeData warmup = WarmupData();
            GameModeRegistry registry = Registry();

            Assert.AreEqual("warmup", warmup.modeId);
            Assert.AreEqual("Разминка", warmup.displayName);
            Assert.IsTrue(warmup.isWarmup, "У разминки не стоит флаг isWarmup.");

            Assert.IsTrue(registry.modes.Contains(warmup), "Разминки нет в GameModeRegistry — клиент не найдёт её по modeId.");
            Assert.AreEqual(1, registry.modes.Count(m => m != null && m.isWarmup), "В реестре разминка должна быть ровно одна.");
            Assert.AreSame(warmup, registry.Warmup);

            List<GameModeData> tabs = MenuSessionSetup.TabModes(registry);
            Assert.IsFalse(tabs.Contains(warmup), "Разминка попала во вкладки выбора режима матча.");
            Assert.IsTrue(tabs.Any(m => m.modeId == "elimination"), "Контроль: Elimination в выборе режима матча.");
        }

        /// <summary>
        /// Разминка (и лобби) — нейтральная «Разминка» первой (её получает новичок, KeepOrDefault) и все
        /// команды матча: команду серии игрок выбирает ещё в лобби, и она живёт до конца серии —
        /// сброс в конце серии не трогает команды разминки (<c>MatchSeries.ReleaseMatchTeams</c>).
        /// </summary>
        [Test]
        public void У_разминки_нейтральная_команда_первой_и_все_команды_матча()
        {
            GameModeData warmup = WarmupData();
            TeamData team = AssetDatabase.LoadAssetAtPath<TeamData>(WarmupTeamPath);

            Assert.AreSame(team, warmup.teams[0], $"Первая команда разминки не {WarmupTeamPath} — новичок получит команду матча.");

            TeamData[] matchTeams = AssetDatabase.FindAssets("t:GameModeData")
                                                 .Select(AssetDatabase.GUIDToAssetPath)
                                                 .Select(AssetDatabase.LoadAssetAtPath<GameModeData>)
                                                 .Where(m => m != null && !m.isWarmup && m.teams != null)
                                                 .SelectMany(m => m.teams).Where(t => t != null).Distinct().ToArray();
            Assert.IsNotEmpty(matchTeams);
            foreach (TeamData matchTeam in matchTeams)
                Assert.Contains(matchTeam, warmup.teams, $"Команды '{matchTeam.displayName}' нет в разминке — в лобби её не выбрать.");
            Assert.AreEqual("Разминка", team.displayName);

            var avatars = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(AvatarsRegistryPath).avatars;
            CollectionAssert.AreEquivalent(avatars, team.avatars,
                "В лобби выбирается любой скин — в команде «Разминка» обязаны быть все аватары реестра.");

            Assert.IsTrue(TeamRegistry.Instance.teams.Contains(team),
                "Команды «Разминка» нет в Resources/TeamRegistry: клиент не восстановит её по индексу.");
            Assert.IsTrue(TeamRegistry.Instance.Validate(out string error), error);
        }

        [Test]
        public void Правила_разминки_на_префабе_режима()
        {
            GameModeData warmup = WarmupData();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WarmupModePrefabPath);

            Assert.IsNotNull(prefab, $"Нет {WarmupModePrefabPath}.");
            Assert.AreSame(prefab, warmup.modePrefab, "modePrefab разминки не WarmupMode.prefab.");
            Assert.IsNotNull(prefab.GetComponent<WarmupMode>());
            Assert.IsNotNull(prefab.GetComponent<NetworkIdentity>());
            Assert.IsNotNull(prefab.GetComponent<WarmupMagazineSupply>(), "Нет бесконечного кармана.");

            var sweeper = prefab.GetComponent<LooseItemSweeper>();
            Assert.IsNotNull(sweeper, "Нет LooseItemSweeper — мусор в разминке копится без предела.");
            var action = (LooseWeaponAction)typeof(LooseItemSweeper)
                .GetField("_weaponAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(sweeper);
            Assert.AreEqual(LooseWeaponAction.ReturnHome, action, "В разминке оружие с пола обязано возвращаться в свой слот.");
        }

        /// <summary>
        /// Режим на карте меняется на месте — мусор прошлого режима убирает новый при
        /// появлении (<see cref="ModeStartCleanup"/>). Без него разминочное оружие на полу
        /// дожило бы до первого раунда.
        /// </summary>
        [Test]
        public void Каждый_режим_начинается_с_чистого_пола()
        {
            foreach (GameModeData mode in Registry().modes)
            {
                Assert.IsNotNull(mode.modePrefab, $"{mode.modeId}: нет префаба.");
                Assert.IsNotNull(mode.modePrefab.GetComponent<ModeStartCleanup>(),
                    $"{mode.modeId}: на префабе нет ModeStartCleanup — мусор прошлого режима останется на полу.");
            }
        }

        [Test]
        public void Префабы_всех_режимов_зарегистрированы_в_Mirror()
        {
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefabPath).GetComponentInChildren<NetworkManager>(true);
            Assert.IsNotNull(manager, "Контроль: NetworkManager в MANAGERS найден.");

            var missing = AssetDatabase.FindAssets("t:GameModeData")
                .Select(g => AssetDatabase.LoadAssetAtPath<GameModeData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d.modePrefab != null && !manager.spawnPrefabs.Contains(d.modePrefab))
                .Select(d => d.modePrefab.name).ToArray();

            Assert.IsEmpty(missing, "Префаб режима не в spawnPrefabs — клиент не заспавнит режим: " + string.Join(", ", missing));
        }

        [Test]
        public void Политика_команд_задана_данными_режима()
        {
            Assert.AreEqual(TeamAssignmentKind.KeepOrDefault, WarmupData().teamAssignment,
                "Разминка не сбрасывает команду матча и даёт свою только игроку без команды.");

            foreach (GameModeData mode in Registry().MatchModes)
                Assert.AreEqual(TeamAssignmentKind.PlayerChoice, mode.teamAssignment,
                    $"{mode.modeId}: команду матча выбирает игрок на карте (или выдаёт админ), автобаланс — только разовой кнопкой.");
        }

        [Test]
        public void Серия_живёт_на_объекте_сессии()
        {
            var context = AssetDatabase.LoadAssetAtPath<GameObject>(SessionContextPath);
            Assert.IsNotNull(context, $"Нет {SessionContextPath}.");
            Assert.IsNotNull(context.GetComponent<SessionManager>(), "Контроль: SessionManager на SessionContext.");
            Assert.IsNotNull(context.GetComponent<MatchSeries>(),
                "На SessionContext нет MatchSeries — серия карт и общий счёт не переживут смену сцены.");
        }

        [Test]
        public void У_админа_на_планшете_есть_экран_игроков_и_команд()
        {
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Menu/VR/PlayerAdmin/Lobby/PlayerAdminLobbyTabletMenu.prefab");
            Assert.IsNotNull(tablet);

            var screens = tablet.GetComponentsInChildren<MenuScreen>(true);
            var playersScreen = screens.FirstOrDefault(s => s.ScreenType == MenuScreenType.PlayersTeams);
            Assert.IsNotNull(playersScreen, "На планшете нет экрана «Игроки и команды».");
            Assert.IsInstanceOf<MenuPlayersTeams>(playersScreen);

            Assert.IsTrue(tablet.GetComponentsInChildren<SwitchMenuButton>(true)
                    .Any(b => b.TargetScreen == MenuScreenType.PlayersTeams),
                "На планшете нет кнопки перехода к экрану «Игроки и команды».");
        }

        // ── Карты ────────────────────────────────────────────────────────────

        [Test]
        public void Лобби_обычная_карта_реестра_только_с_разминкой()
        {
            MapRegistry maps = Maps();
            MapData lobby = AssetDatabase.LoadAssetAtPath<MapData>(LobbyMapPath);

            Assert.IsNotNull(lobby, $"Нет {LobbyMapPath}.");
            Assert.AreEqual("Lobby", lobby.sceneName);
            Assert.IsTrue(maps.maps.Contains(lobby), "Лобби нет в списке карт реестра.");
            Assert.AreSame(lobby, maps.lobby, "MapRegistry.lobby не указывает на карту-лобби — серии некуда возвращаться.");
            CollectionAssert.AreEqual(new[] { WarmupData() }, lobby.supportedModes, "У лобби должна быть только разминка.");

            foreach (GameModeData mode in Registry().MatchModes)
                Assert.IsFalse(MenuSessionSetup.MapsForMode(maps, mode).Contains(lobby),
                    $"Лобби попало в выбор карт под режим {mode.modeId}.");
        }

        [Test]
        public void Боевые_карты_начинаются_с_разминки_и_допускают_матч()
        {
            MapRegistry maps = Maps();
            GameModeData warmup = WarmupData();

            foreach (MapData map in maps.maps.Where(m => m != null && m != maps.lobby))
            {
                Assert.IsNotNull(map.supportedModes, $"{map.sceneName}: нет списка режимов.");
                Assert.AreSame(warmup, MapModeRules.ResolveWarmup(map, Registry()),
                    $"{map.sceneName}: карта стартует не с разминки.");
                Assert.IsTrue(map.supportedModes.Any(m => m != null && !m.isWarmup),
                    $"{map.sceneName}: на боевой карте нет ни одного режима матча.");
            }
        }

        // ── Сцены ────────────────────────────────────────────────────────────

        [Test]
        public void В_сцене_лобби_есть_оркестратор_режима()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                List<GameplayManager> managers = InScene<GameplayManager>(scene);
                Assert.AreEqual(1, managers.Count, "В лобби должен быть ровно один GameplayManager (MatchManager).");

                GameplayManager manager = managers[0];
                Assert.IsTrue(manager.gameObject.activeSelf, "MatchManager выключен — Mirror включит его при спавне сам (сетевой объект).");
                Assert.AreNotEqual(0UL, manager.GetComponent<NetworkIdentity>().sceneId, "У MatchManager в лобби нулевой sceneId.");

                Assert.IsFalse(scene.GetRootGameObjects().Any(r => r.name == "LobbyFreePlay"),
                    "В лобби остался объект LobbyFreePlay — правила лобби теперь на префабе разминки.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void В_лобби_одна_зона_спавна_и_она_не_в_тумбе_арсенала()
        {
            TeamData warmupTeam = AssetDatabase.LoadAssetAtPath<TeamData>(WarmupTeamPath);
            Assert.IsNotNull(warmupTeam, $"Нет {WarmupTeamPath}.");

            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                TeamSpawnZone[] zones = InScene<TeamSpawnZone>(scene).Where(z => z.gameObject.activeInHierarchy).ToArray();
                Assert.AreEqual(1, zones.Length,
                    "В лобби должна быть одна зона спавна: " + string.Join(", ", zones.Select(z => z.name + "/" + (z.Team ? z.Team.name : "null"))));
                Assert.AreSame(warmupTeam, zones[0].Team, "Зона спавна лобби не команды «Разминка».");

                List<ArsenalWallController> walls = InScene<ArsenalWallController>(scene);
                Assert.IsNotEmpty(walls, "Контроль: тумба арсенала в лобби есть.");

                Bounds tumba = new Bounds(walls[0].transform.position, Vector3.zero);
                foreach (ArsenalWallController wall in walls)
                    foreach (Renderer r in wall.GetComponentsInChildren<Renderer>(true))
                        tumba.Encapsulate(r.bounds);
                tumba.Expand(new Vector3(1f, 0f, 1f)); // метр на руки и корпус

                // Точка спавна зоны (AvatarSpawnPointResolver): своя или центр зоны. Зона лобби — на всю
                // арену, её центр — в тумбе, поэтому точка задана отдельно.
                Vector3 spawn = zones[0].SpawnPoint.position;
                bool inside = Mathf.Abs(spawn.x - tumba.center.x) <= tumba.extents.x &&
                              Mathf.Abs(spawn.z - tumba.center.z) <= tumba.extents.z;
                Assert.IsFalse(inside, $"Точка спавна {spawn} внутри тумбы арсенала {tumba}.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
