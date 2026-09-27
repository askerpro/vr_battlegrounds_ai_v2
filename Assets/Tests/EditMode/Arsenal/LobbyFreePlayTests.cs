using System.IO;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Свободная игра в лобби — правило сцены <see cref="LobbyFreePlay"/>.
    ///
    /// Что доказывает. В лобби нет ни <c>GameplayManager</c>, ни режима, поэтому стену
    /// арсенала там никто не открывал, а оружие оставалось таким, каким его выключил
    /// прошедший матч (<c>UxrWeaponManager</c> переживает смену сцены). Правило сцены
    /// обязано: открыть стену на машине, владеющей её состоянием; не трогать
    /// клиентскую стену; убрать жетон готовности; включить оружие.
    ///
    /// Пополнение слотов здесь не проверяется: ему нужен зарегистрированный сетевой
    /// префаб оружия. Путь выдачи тот же, что у фазы <c>Setup</c>.
    /// </summary>
    public class LobbyFreePlayTests : MirrorTestHarness
    {
        private const ArsenalWallController.ArsenalState Closed = ArsenalWallController.ArsenalState.Closed;
        private const ArsenalWallController.ArsenalState Open = ArsenalWallController.ArsenalState.Open;

        private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        private const string MapsFolder = "Assets/Scenes/Maps";
        private const string ScriptPath = "Assets/Scripts/GameModes/LobbyFreePlay.cs";

        private UxrWeaponManager _weaponManager;

        [TearDown]
        public void ReleaseWeaponManager()
        {
            // Синглтон SDK статический: без Release следующий тест увидел бы
            // уничтоженный менеджер как живой.
            if (_weaponManager != null)
            {
                InvokePrivateMethod(_weaponManager, "OnDestroy");
                Object.DestroyImmediate(_weaponManager.gameObject);
                _weaponManager = null;
            }
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

        private LobbyFreePlay CreateRule()
        {
            LobbyFreePlay rule = CreateObject("LobbyRules").AddComponent<LobbyFreePlay>();

            // В EditMode Unity не зовёт OnEnable у обычных скриптов.
            InvokeLifecycleMethod(rule, "OnEnable");
            return rule;
        }

        private static void Tick(LobbyFreePlay rule, float deltaTime = 0.1f)
        {
            InvokePrivateMethod(rule, "Tick", deltaTime);
        }

        // ── Стена ───────────────────────────────────────────────────────────

        [Test]
        public void Серверная_стена_в_лобби_открыта_без_раунда()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateWall("ServerWall", out _);
            SpawnOnServer(wall);
            Assert.AreEqual(Closed, wall.CurrentState, "Контроль: без правила стена стоит закрытой.");

            LobbyFreePlay rule = CreateRule();
            Tick(rule);

            Assert.AreEqual(Open, wall.CurrentState,
                "В лобби фазы раунда нет, и открыть стену больше некому. Правило сцены " +
                "обязано открыть её на сервере — клиенты получат состояние репликацией.");
        }

        [Test]
        public void Стена_закрытая_извне_открывается_снова()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateWall("ServerWall", out _);
            SpawnOnServer(wall);

            LobbyFreePlay rule = CreateRule();
            wall.SetClosedImmediate();
            Tick(rule);

            Assert.AreEqual(Open, wall.CurrentState, "В лобби арсенал открыт всегда, а не только на старте.");
        }

        [Test]
        public void Клиентская_стена_не_открывается_правилом()
        {
            SilenceMirrorNoise();

            // Сервер активен (харнесс его поднял), а стена не заспавнена на нём —
            // так выглядит клиентский двойник. Писать состояние ему нельзя.
            ArsenalWallController client = CreateWall("ClientWall", out _);

            LobbyFreePlay rule = CreateRule();
            Tick(rule);

            Assert.AreEqual(Closed, client.CurrentState,
                "Правило открыло стену, которой эта машина не владеет. Состояние стены общее " +
                "и приходит с сервера (NET-07) — клиент его не пишет.");
        }

        [Test]
        public void Жетон_готовности_в_лобби_убран()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateWall("ServerWall", out DogTagController dogTag);
            SpawnOnServer(wall);

            GameObject tagObject = dogTag.transform.Find("DogTag").gameObject;
            Assert.IsTrue(tagObject.activeSelf, "Контроль: без правила жетон на стене есть.");

            LobbyFreePlay rule = CreateRule();
            Tick(rule);

            Assert.IsFalse(tagObject.activeSelf,
                "В лобби раунда нет, а жетон объявляет готовность к раунду. Схваченный " +
                "в лобби, он записал бы готовность, которую никто не ждёт.");
        }

        // ── Оружие ──────────────────────────────────────────────────────────

        [Test]
        public void Оружие_включается_после_матча()
        {
            SilenceMirrorNoise();

            _weaponManager = new GameObject("WeaponManager").AddComponent<UxrWeaponManager>();
            InvokeLifecycleMethod(_weaponManager, "Awake");
            Assert.IsTrue(UxrWeaponManager.HasInstance, "Контроль: менеджер оружия зарегистрирован.");

            // Ровно так его оставляет GameplayManager, если карту выгрузили вне фазы Combat.
            _weaponManager.SetWeaponSystemEnabled(false);

            LobbyFreePlay rule = CreateRule();
            Tick(rule);

            Assert.IsTrue(_weaponManager.WeaponSystemEnabled,
                "UxrWeaponManager переживает смену сцены, а GameplayManager в лобби нет. " +
                "Без правила оружие оставалось выключенным с прошедшего матча.");
        }

        // ── Сцены ───────────────────────────────────────────────────────────

        [Test]
        public void Правило_лежит_в_сцене_лобби()
        {
            string guid = AssetDatabase.AssetPathToGUID(ScriptPath);
            Assert.IsNotEmpty(guid, $"Контроль: скрипт {ScriptPath} найден.");

            Assert.IsTrue(File.ReadAllText(LobbyScenePath).Contains(guid),
                "В Lobby.unity нет LobbyFreePlay — арсенал в лобби останется закрытым.");
        }

        [Test]
        public void Правила_нет_на_картах()
        {
            string guid = AssetDatabase.AssetPathToGUID(ScriptPath);

            foreach (string scene in Directory.GetFiles(MapsFolder, "*.unity", SearchOption.AllDirectories))
            {
                Assert.IsFalse(File.ReadAllText(scene).Contains(guid),
                    $"LobbyFreePlay в {scene}: на карте он спорил бы с фазой раунда за стену и оружие.");
            }
        }
    }
}
