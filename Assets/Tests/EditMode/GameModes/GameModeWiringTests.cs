using System.Collections.Generic;
using System.IO;
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

namespace VrBattlegrounds.Tests.Modes
{
    /// <summary>
    /// Проводка лобби-режима в ассетах и сценах: данные, команда, префаб, сцена лобби.
    ///
    /// <para>
    /// Что доказывает. Лобби — режим сцены (<see cref="LobbyMode"/>): его задаёт поле
    /// <c>GameplayManager</c> в <c>Lobby.unity</c>, а не выбор администратора; в списке
    /// режимов матча его нет; у него одна команда «Лобби» со всеми аватарами и одна
    /// зона спавна, из которой игрок не попадает внутрь тумбы арсенала. Правила лобби
    /// живут на префабе режима, объекта <c>LobbyFreePlay</c> в сцене больше нет.
    /// </para>
    /// </summary>
    public class GameModeWiringTests
    {
        public const string LobbyModeDataPath = "Assets/Data/GameModes/Lobby_GameModeData.asset";
        public const string EliminationDataPath = "Assets/Data/GameModes/Elimination_GameModeData.asset";
        private const string RegistryPath = "Assets/Data/GameModes/GameModeRegistry.asset";
        private const string LobbyTeamPath = "Assets/Data/Teams/Lobby_Team.asset";
        private const string LobbyModePrefabPath = "Assets/Prefabs/GameModes/LobbyMode.prefab";
        private const string AvatarsRegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string MapsFolder = "Assets/Scenes/Maps";

        private static GameModeData LobbyData()
        {
            GameModeData data = AssetDatabase.LoadAssetAtPath<GameModeData>(LobbyModeDataPath);
            Assert.IsNotNull(data, $"Нет данных лобби-режима {LobbyModeDataPath}.");
            return data;
        }

        private static List<T> InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToList();

        // ── Данные ───────────────────────────────────────────────────────────

        [Test]
        public void Лобби_режима_нет_в_списке_режимов_матча()
        {
            GameModeData lobby = LobbyData();
            var registry = AssetDatabase.LoadAssetAtPath<GameModeRegistry>(RegistryPath);

            Assert.IsFalse(registry.modes.Contains(lobby),
                "Лобби-режим попал в GameModeRegistry — администратор выберет его режимом матча.");
            Assert.IsTrue(registry.modes.Any(m => m != null && m.modeId == "elimination"), "Контроль: Elimination в реестре.");
        }

        [Test]
        public void У_лобби_одна_команда_со_всеми_аватарами()
        {
            GameModeData lobby = LobbyData();
            TeamData team = AssetDatabase.LoadAssetAtPath<TeamData>(LobbyTeamPath);

            Assert.AreEqual(1, lobby.teams.Length, "В лобби команд нет — есть одна общая команда «Лобби».");
            Assert.AreSame(team, lobby.teams[0], $"Команда лобби-режима не {LobbyTeamPath}.");

            var avatars = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(AvatarsRegistryPath).avatars;
            CollectionAssert.AreEquivalent(avatars, team.avatars,
                "В лобби выбирается любой скин — в команде «Лобби» обязаны быть все аватары реестра.");

            Assert.IsTrue(TeamRegistry.Instance.teams.Contains(team),
                "Команды «Лобби» нет в Resources/TeamRegistry: клиент не восстановит её по индексу.");
            Assert.IsTrue(TeamRegistry.Instance.Validate(out string error), error);
        }

        [Test]
        public void Правила_лобби_на_префабе_режима()
        {
            GameModeData lobby = LobbyData();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyModePrefabPath);

            Assert.IsNotNull(prefab, $"Нет {LobbyModePrefabPath}.");
            Assert.AreSame(prefab, lobby.modePrefab, "modePrefab лобби-режима не LobbyMode.prefab.");
            Assert.IsNotNull(prefab.GetComponent<LobbyMode>());
            Assert.IsNotNull(prefab.GetComponent<NetworkIdentity>());
            Assert.IsNotNull(prefab.GetComponent<LobbyMagazineSupply>(), "Нет бесконечного кармана.");

            var sweeper = prefab.GetComponent<LooseItemSweeper>();
            Assert.IsNotNull(sweeper, "Нет LooseItemSweeper — мусор в лобби копится без предела.");
            var action = (LooseWeaponAction)typeof(LooseItemSweeper)
                .GetField("_weaponAction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(sweeper);
            Assert.AreEqual(LooseWeaponAction.ReturnHome, action, "В лобби оружие с пола обязано возвращаться в свой слот.");
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
            Assert.AreEqual(TeamAssignmentKind.AutoBalance, LobbyData().teamAssignment,
                "Лобби раздаёт свою единственную команду само.");

            var registry = AssetDatabase.LoadAssetAtPath<GameModeRegistry>(RegistryPath);
            foreach (GameModeData mode in registry.modes)
                Assert.AreEqual(TeamAssignmentKind.PlayerChoice, mode.teamAssignment,
                    $"{mode.modeId}: команду матча выбирает игрок на карте (или выдаёт админ), автобаланс — только разовой кнопкой.");
        }

        [Test]
        public void У_админа_на_планшете_есть_экран_игроков_и_команд()
        {
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Menu/VR/PlayerAdmin/Lobby/PlayerAdminLobbyTabletMenu.prefab");
            Assert.IsNotNull(tablet);

            var screens = tablet.GetComponentsInChildren<VrBattlegrounds.UI.Menu.MenuScreen>(true);
            var playersScreen = screens.FirstOrDefault(s => s.ScreenType == VrBattlegrounds.UI.Menu.MenuScreenType.PlayersTeams);
            Assert.IsNotNull(playersScreen, "На планшете нет экрана «Игроки и команды».");
            Assert.IsInstanceOf<VrBattlegrounds.UI.Menu.MenuPlayersTeams>(playersScreen);

            Assert.IsTrue(tablet.GetComponentsInChildren<VrBattlegrounds.UI.Menu.SwitchMenuButton>(true)
                    .Any(b => b.TargetScreen == VrBattlegrounds.UI.Menu.MenuScreenType.PlayersTeams),
                "На планшете нет кнопки перехода к экрану «Игроки и команды».");
        }

        // ── Сцены ────────────────────────────────────────────────────────────

        [Test]
        public void В_сцене_лобби_режим_сцены_лобби()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                List<GameplayManager> managers = InScene<GameplayManager>(scene);
                Assert.AreEqual(1, managers.Count, "В лобби должен быть ровно один GameplayManager (MatchManager).");

                GameplayManager manager = managers[0];
                Assert.IsTrue(manager.gameObject.activeSelf, "MatchManager выключен — Mirror включит его при спавне сам (сетевой объект).");
                Assert.AreSame(LobbyData(), manager.SceneGameMode, "В лобби режим сцены не лобби-режим.");
                Assert.AreNotEqual(0UL, manager.GetComponent<NetworkIdentity>().sceneId, "У MatchManager в лобби нулевой sceneId.");

                Assert.IsFalse(scene.GetRootGameObjects().Any(r => r.name == "LobbyFreePlay"),
                    "В лобби остался объект LobbyFreePlay — правила лобби теперь на префабе режима.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void На_картах_режим_сцены_не_задан()
        {
            foreach (string path in Directory.GetFiles(MapsFolder, "*.unity", SearchOption.AllDirectories))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    foreach (GameplayManager manager in InScene<GameplayManager>(scene))
                        Assert.IsNull(manager.SceneGameMode, $"{path}: у карты задан режим сцены — выбор администратора игнорируется.");
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        [Test]
        public void В_лобби_одна_зона_спавна_и_она_не_в_тумбе_арсенала()
        {
            TeamData lobbyTeam = AssetDatabase.LoadAssetAtPath<TeamData>(LobbyTeamPath);
            Assert.IsNotNull(lobbyTeam, $"Нет {LobbyTeamPath}.");

            Scene scene = EditorSceneManager.OpenPreviewScene(LobbyScenePath);
            try
            {
                TeamSpawnZone[] zones = InScene<TeamSpawnZone>(scene).Where(z => z.gameObject.activeInHierarchy).ToArray();
                Assert.AreEqual(1, zones.Length,
                    "В лобби должна быть одна зона спавна: " + string.Join(", ", zones.Select(z => z.name + "/" + (z.Team ? z.Team.name : "null"))));
                Assert.AreSame(lobbyTeam, zones[0].Team, "Зона спавна лобби не команды «Лобби».");

                List<ArsenalWallController> walls = InScene<ArsenalWallController>(scene);
                Assert.IsNotEmpty(walls, "Контроль: тумба арсенала в лобби есть.");

                Bounds tumba = new Bounds(walls[0].transform.position, Vector3.zero);
                foreach (ArsenalWallController wall in walls)
                    foreach (Renderer r in wall.GetComponentsInChildren<Renderer>(true))
                        tumba.Encapsulate(r.bounds);
                tumba.Expand(new Vector3(1f, 0f, 1f)); // метр на руки и корпус

                // Точка спавна — сам transform зоны (AvatarSpawnPointResolver).
                Vector3 spawn = zones[0].transform.position;
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
