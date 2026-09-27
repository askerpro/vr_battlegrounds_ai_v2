using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.ArsenalWall
{
    /// <summary>
    /// Состояние стены арсенала — находка NET-07, задача T-15.
    ///
    /// Что доказывает. <c>ArsenalWallController</c> наследует <c>NetworkBehaviour</c>,
    /// но <c>_currentState</c> был обычным полем: каждая машина открывала и закрывала
    /// стену сама. Захват жетона закрывал арсенал только у того, кто его схватил,
    /// а подключившийся позже видел стену закрытой независимо от фазы раунда.
    ///
    /// Решение — «общая стена»: состояние живёт в <c>SyncVar</c>, и пишет его только
    /// сервер. Разбор — <c>Docs/Arsenal/Arsenal_Code_Architecture_RU.md</c>.
    ///
    /// С T-29 у стены остался ровно один повод закрыться — выход из фазы <c>Equipment</c>.
    /// Жетон стену не трогает: он объявляет готовность своего игрока, а закрытие
    /// по первому жетону оставляло второго игрока без снаряжения (RDY-01).
    ///
    /// Почему репликация проверяется двойником, а не host-режимом: в host-режиме
    /// сервер и клиент делят один экземпляр объекта, и «долетело» получилось бы
    /// зелёным на пустом месте. <see cref="MirrorTestHarness.ReplicateToClient"/>
    /// гоняет состояние через настоящую сериализацию Mirror.
    ///
    /// Аниматор стенам в тесте не даём: без него открытие и закрытие завершаются
    /// синхронно, и проверка не зависит от длины клипа.
    /// </summary>
    public class ArsenalWallStateReplicationTests : MirrorTestHarness
    {
        private const ArsenalWallController.ArsenalState Closed = ArsenalWallController.ArsenalState.Closed;
        private const ArsenalWallController.ArsenalState Open = ArsenalWallController.ArsenalState.Open;

        /// <summary>
        /// Стена с одним слотом. <see cref="WeaponInfo"/> слоту не задаём:
        /// тогда <c>ReplenishWeaponsNetwork</c> его пропускает, и тесту не нужно
        /// регистрировать сетевой префаб оружия.
        /// </summary>
        private ArsenalWallController CreateWall(string name)
        {
            GameObject wallObject = CreateNetworkObject(name);

            GameObject slotObject = new GameObject("Slot");
            slotObject.transform.SetParent(wallObject.transform);

            GameObject anchorObject = new GameObject("ItemAnchor");
            anchorObject.transform.SetParent(slotObject.transform);

            ArsenalSlotController slot = slotObject.AddComponent<ArsenalSlotController>();
            SetPrivateField(slot, "_itemAnchor", anchorObject.AddComponent<UxrGrabbableObjectAnchor>());

            ArsenalWallController wall = wallObject.AddComponent<ArsenalWallController>();
            EnableNetworking(wallObject);

            // В EditMode Unity не зовёт ни Awake, ни Start. Без Awake у стены пуст
            // список слотов, без Start не применено начальное состояние.
            InvokeLifecycleMethod(wall, "Awake");
            InvokeLifecycleMethod(wall, "Start");

            return wall;
        }

        /// <summary>Стена на сервере: заспавнена, значит состояние пишет она.</summary>
        private ArsenalWallController CreateServerWall(string name)
        {
            ArsenalWallController wall = CreateWall(name);
            SpawnOnServer(wall);
            return wall;
        }

        private EliminationMode _mode;

        [SetUp]
        public void ResetMode() => _mode = null;

        /// <summary>
        /// Режим объявляет фазу, стена исполняет его правила. Раньше тест дёргал
        /// серверный обработчик события фазы у самой стены; теперь стена не знает
        /// Elimination и сверяется с <c>GameMode.ArsenalRules</c> активного режима.
        /// </summary>
        private void SendPhaseToServer(ArsenalWallController wall, RoundState phase)
        {
            if (_mode == null)
            {
                var manager = CreateNetworkComponent<VrBattlegrounds.Managers.GameplayManager>("GameplayManager");
                InvokeLifecycleMethod(manager, "Awake");
                _mode = CreateNetworkComponent<EliminationMode>("EliminationMode");
                InvokePrivateMethod(manager, "RegisterActiveGameMode", _mode);
            }

            SetPrivateField(_mode, "_roundState", phase);
            wall.ApplyModeRules(0.1f);
        }

        // ── Сервер ведёт состояние ──────────────────────────────────────────

        [Test]
        public void Серверный_канал_фазы_открывает_стену()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall("ServerWall");
            Assert.AreEqual(Closed, wall.CurrentState, "Контроль: стена стартует закрытой.");

            SendPhaseToServer(wall, RoundState.Equipment);

            Assert.AreEqual(Open, wall.CurrentState,
                "Фаза Equipment пришла по серверному каналу, а стена осталась закрытой. " +
                "Состояние общее — открывать её обязан сервер, иначе клиентам нечего реплицировать.");
        }

        [Test]
        public void Серверный_канал_фазы_закрывает_стену_к_бою()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall("ServerWall");
            SendPhaseToServer(wall, RoundState.Equipment);
            Assert.AreEqual(Open, wall.CurrentState, "Контроль: к бою стена должна подойти открытой.");

            SendPhaseToServer(wall, RoundState.Countdown);

            Assert.AreEqual(Closed, wall.CurrentState,
                "Обратный отсчёт начался, а арсенал остался открытым.");
        }

        // ── Репликация ──────────────────────────────────────────────────────

        [Test]
        public void Состояние_стены_доезжает_до_позднего_клиента()
        {
            SilenceMirrorNoise();

            ArsenalWallController server = CreateServerWall("ServerWall");
            ArsenalWallController client = CreateWall("ClientWall");

            bool openedOnClient = false;
            client.OnArsenalOpened += () => openedOnClient = true;

            SendPhaseToServer(server, RoundState.Equipment);

            Assert.AreEqual(Open, server.CurrentState, "Контроль: на сервере стена открылась.");
            Assert.AreEqual(Closed, client.CurrentState,
                "Контроль: до репликации двойник обязан быть закрыт, иначе тест ничего не проверяет.");

            // initialState=true — ровно то, что получает подключившийся посреди фазы.
            ReplicateToClient(server, client);

            Assert.AreEqual(Open, client.CurrentState,
                "Состояние стены не доехало до клиента. Подключившийся в фазе Equipment " +
                "видит закрытый арсенал — это NET-07.");
            Assert.IsTrue(openedOnClient,
                "Состояние доехало, но представление не отработало: хук SyncVar не проиграл " +
                "открытие, слоты у клиента остались заблокированными.");
        }

        [Test]
        public void Закрытие_по_фазе_доезжает_до_остальных()
        {
            SilenceMirrorNoise();

            ArsenalWallController server = CreateServerWall("ServerWall");
            ArsenalWallController client = CreateWall("ClientWall");

            SendPhaseToServer(server, RoundState.Equipment);
            ReplicateToClient(server, client);
            Assert.AreEqual(Open, client.CurrentState, "Контроль: у клиента стена открыта.");

            // Единственная точка, из которой стена теперь закрывается: сервер объявил
            // выход из фазы закупки. До T-29 сюда же приходила команда клиента,
            // схватившего жетон, — и закрывала арсенал всем сразу (RDY-01).
            SendPhaseToServer(server, RoundState.Countdown);

            Assert.AreEqual(Closed, server.CurrentState,
                "Отсчёт начался, а сервер стену не закрыл — закрывать её больше некому.");

            ReplicateToClient(server, client);

            Assert.AreEqual(Closed, client.CurrentState,
                "Сервер закрыл стену, а у остальных арсенал остался открытым — это NET-07.");
        }

        // ── Жетон больше не закрывает общую стену (RDY-01) ───────────────────

        [Test]
        public void Жетон_не_закрывает_общую_стену()
        {
            SilenceMirrorNoise();

            ArsenalWallController wall = CreateServerWall("ServerWall");
            SendPhaseToServer(wall, RoundState.Equipment);
            Assert.AreEqual(Open, wall.CurrentState, "Контроль: стена открыта, жетон брать есть с чего.");

            // Ровно то, что поднимает DogTagController при настоящем захвате.
            InvokePrivateMethod(wall, "HandleTagGrabbed", (PlayerController)null);

            Assert.AreEqual(Open, wall.CurrentState,
                "Жетон закрыл общую стену. После T-15 стена одна на всех, поэтому первый же " +
                "взявший жетон оставлял остальных без снаряжения — это RDY-01.\n" +
                "Закрывать стену вправе только выход из фазы Equipment; жетон объявляет " +
                "готовность своего игрока и больше ничего.\n" +
                "Кросс-процессное доказательство — сценарий яруса C round-readiness-match.");
        }

        // ── Кто вправе менять состояние ─────────────────────────────────────

        // Тест «Локальный_обработчик_фазы_не_трогает_состояние_заспавненной_стены» удалён
        // вместе с самим локальным обработчиком: стена больше не подписана на фазы
        // Elimination. Кто вправе писать состояние, решает CanWriteState внутри
        // ApplyModeRules, а то, что клиент получает состояние только репликацией,
        // доказывают тесты раздела «Репликация» выше.

        [Test]
        public void Стена_вне_сети_ведёт_состояние_сама()
        {
            SilenceMirrorNoise();

            // Не спавним: так выглядит сцена, открытая без сети, и стена без sceneId (NET-14).
            ArsenalWallController wall = CreateWall("OfflineWall");

            SendPhaseToServer(wall, RoundState.Equipment);

            Assert.AreEqual(Open, wall.CurrentState,
                "Реплицировать состояние некому, а правила режима стена вне сети не исполнила — " +
                "стена не откроется никогда.");
        }
    }
}
