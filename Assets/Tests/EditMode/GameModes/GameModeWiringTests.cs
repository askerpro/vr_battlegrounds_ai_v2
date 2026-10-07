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
    /// Что доказывает. Разминка (<see cref="WarmupMode"/>) — не режим каталога: отдельное поле
    /// <c>GameModeRegistry.warmup</c>, без команд, не в списке режимов и не в списках карт.
    /// Команд в игре две — Военные и Повстанцы; команды «Разминка» нет. Игрок без команды —
    /// киборг. Лобби — карта реестра без режимов матча с одной нейтральной зоной спавна.
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
        private const string CyborgPrefabPath = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        private const string AvatarStrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";
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
        public void Разминка_не_режим_каталога()
        {
            GameModeData warmup = WarmupData();
            GameModeRegistry registry = Registry();

            Assert.AreEqual("warmup", warmup.modeId);
            Assert.AreEqual("Разминка", warmup.displayName);
            Assert.IsTrue(warmup.teams == null || warmup.teams.Length == 0, "У разминки есть команды — это состояние карты, а не режим матча.");

            Assert.AreSame(warmup, registry.warmup, "Разминка не задана полем warmup в GameModeRegistry.");
            Assert.AreSame(warmup, registry.GetById("warmup"), "Клиент не найдёт разминку по modeId.");
            Assert.IsFalse(registry.modes.Contains(warmup), "Разминка лежит в каталоге режимов матча.");
            Assert.IsFalse(registry.modes.Any(m => m != null && m.modePrefab != null && m.modePrefab.GetComponent<WarmupMode>() != null),
                "В каталоге режимов матча лежит режим с WarmupMode.");

            List<GameModeData> tabs = MenuSessionSetup.TabModes(registry);
            Assert.IsFalse(tabs.Contains(warmup), "Разминка попала во вкладки выбора режима матча.");
            Assert.IsTrue(tabs.Any(m => m.modeId == "elimination"), "Контроль: Elimination в выборе режима матча.");
        }

        /// <summary>
        /// Команд в игре две — Военные и Повстанцы, это и есть команды режимов матча. Команды
        /// «Разминка» нет: игрок без команды — просто без команды (аватар — киборг). Раньше
        /// в лобби планшет предлагал три команды.
        /// </summary>
        [Test]
        public void Команды_только_Военные_и_Повстанцы()
        {
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TeamData>(WarmupTeamPath), $"Команда «Разминка» вернулась: {WarmupTeamPath}.");

            TeamData[] matchTeams = Registry().MatchModes.Where(m => m.teams != null)
                                              .SelectMany(m => m.teams).Where(t => t != null).Distinct().ToArray();
            CollectionAssert.AreEquivalent(new[] { "Военные", "Повстанцы" }, matchTeams.Select(t => t.displayName),
                "Команды режимов матча не Военные и Повстанцы.");
            CollectionAssert.AreEquivalent(matchTeams, TeamRegistry.Instance.teams.Where(t => t != null),
                "В Resources/TeamRegistry не ровно команды матча — в лобби планшет предложит лишние.");
            Assert.IsTrue(TeamRegistry.Instance.Validate(out string error), error);
        }

        /// <summary>
        /// Игрок без команды (ещё не выбрал или его сняли с команды) — киборг: запасной префаб
        /// стратегии аватаров. Он же обязан быть в реестре аватаров, чтобы его проверяли тесты
        /// аватаров (<c>AvatarLoadoutTests</c>, <c>PrefabCompositionTests</c>).
        /// </summary>
        [Test]
        public void Игрок_без_команды_киборг()
        {
            GameObject cyborg = AssetDatabase.LoadAssetAtPath<GameObject>(CyborgPrefabPath);
            Assert.IsNotNull(cyborg, $"Нет {CyborgPrefabPath}.");

            var strategy = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Player.Avatars.TeamAvatarStrategy>(AvatarStrategyPath);
            var fallback = new SerializedObject(strategy).FindProperty("fallbackPrefab").objectReferenceValue as GameObject;
            Assert.AreSame(cyborg, fallback, $"Игрок без команды получит '{(fallback ? fallback.name : "null")}', а не киборга.");

            var avatars = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(AvatarsRegistryPath).avatars;
            Assert.IsTrue(avatars.Any(a => a != null && a.prefab == cyborg), "Киборга нет в реестре аватаров — тесты аватаров его не проверяют.");
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
            foreach (GameModeData mode in Registry().modes.Append(Registry().warmup))
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
            Assert.IsNotNull(context.GetComponent<Series>(),
                "На SessionContext нет Series — серия карт и общий счёт не переживут смену сцены.");
        }

        [Test]
        public void У_админа_на_планшете_есть_экран_игроков_и_команд()
        {
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Menu/Tablet/Tablet.prefab");
            Assert.IsNotNull(tablet);

            var screens = tablet.GetComponentsInChildren<MenuScreen>(true);
            var playersScreen = screens.FirstOrDefault(s => s.ScreenType == MenuScreenType.PlayersTeams);
            Assert.IsNotNull(playersScreen, "На планшете нет экрана «Игроки и команды».");
            Assert.IsInstanceOf<MenuPlayersTeams>(playersScreen);

            Assert.IsTrue(MenuMatchManager.Links.Any(l => l.screen == MenuScreenType.PlayersTeams),
                "Из раздела «Админ» нет входа в экран «Игроки и команды».");
        }

        // ── Карты ────────────────────────────────────────────────────────────

        [Test]
        public void Лобби_карта_реестра_без_режимов_матча()
        {
            MapRegistry maps = Maps();
            MapData lobby = AssetDatabase.LoadAssetAtPath<MapData>(LobbyMapPath);

            Assert.IsNotNull(lobby, $"Нет {LobbyMapPath}.");
            Assert.AreEqual("Lobby", lobby.sceneName);
            Assert.IsTrue(maps.maps.Contains(lobby), "Лобби нет в списке карт реестра.");
            Assert.AreSame(lobby, maps.lobby, "MapRegistry.lobby не указывает на карту-лобби — серии некуда возвращаться.");
            Assert.IsTrue(lobby.supportedModes == null || lobby.supportedModes.Length == 0,
                "У лобби есть режимы — разминка в списки карт не входит, а режимов матча у лобби нет.");

            foreach (GameModeData mode in Registry().MatchModes)
                Assert.IsFalse(MenuSessionSetup.MapsForMode(maps, mode).Contains(lobby),
                    $"Лобби попало в выбор карт под режим {mode.modeId}.");
        }

        [Test]
        public void Боевые_карты_допускают_матч_и_не_держат_разминку_в_списке()
        {
            MapRegistry maps = Maps();
            GameModeData warmup = WarmupData();

            foreach (MapData map in maps.maps.Where(m => m != null && m != maps.lobby))
            {
                Assert.IsNotNull(map.supportedModes, $"{map.sceneName}: нет списка режимов.");
                Assert.IsFalse(map.supportedModes.Contains(warmup),
                    $"{map.sceneName}: разминка в списке режимов карты — она не режим каталога.");
                Assert.IsTrue(map.supportedModes.Any(m => m != null),
                    $"{map.sceneName}: на боевой карте нет ни одного режима матча.");
            }
        }

        // ── Сцены ────────────────────────────────────────────────────────────

        /// <summary>
        /// Оркестратор режима лобби — не сценовый объект: его спавнит <c>MapBootstrap</c> из центрального каталога.
        /// В сцене — ровно один запуск карты (MapRoot + MapBootstrap) и ни одного сценового MapReferee
        /// (сценовый судья был неуправляемым путём). Префаб судьи в каталоге — зарегистрированный сетевой префаб.
        /// </summary>
        [Test]
        public void Оркестратор_режима_лобби_создаёт_запуск_карты()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                Assert.AreEqual(0, InScene<MapReferee>(scene).Count,
                    "В лобби сценовый MapReferee — неуправляемый путь. Судью спавнит MapBootstrap.");
                Assert.AreEqual(1, InScene<VrBattlegrounds.Maps.Runtime.MapBootstrap>(scene).Count,
                    "В лобби должен быть ровно один MapBootstrap (на MapRoot).");

                var catalog = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Maps.Runtime.MapRuntimeCatalog>("Assets/Data/Maps/MapRuntimeCatalog.asset");
                Assert.IsNotNull(catalog, "Нет центрального каталога запуска карт.");
                Assert.IsNotNull(catalog.RefereePrefab, "В каталоге нет префаба судьи.");
                Assert.AreEqual(0UL, catalog.RefereePrefab.GetComponent<NetworkIdentity>().sceneId,
                    "Префаб судьи в каталоге — сценовый объект, а не спавнящийся сетевой префаб.");

                Assert.IsFalse(scene.GetRootGameObjects().Any(r => r.name == "LobbyFreePlay"),
                    "В лобби остался объект LobbyFreePlay — правила лобби теперь на префабе разминки.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void В_лобби_одна_нейтральная_зона_спавна_и_она_не_в_тумбе_арсенала()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                TeamSpawnZone[] zones = InScene<TeamSpawnZone>(scene).Where(z => z.gameObject.activeInHierarchy).ToArray();
                Assert.AreEqual(1, zones.Length,
                    "В лобби должна быть одна зона спавна: " + string.Join(", ", zones.Select(z => z.name + "/" + (z.Team ? z.Team.name : "null"))));
                Assert.IsNull(zones[0].HomeTeam,
                    "Зона спавна лобби чья-то — в лобби появляются все, и без команды тоже: зона обязана быть нейтральной.");

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
