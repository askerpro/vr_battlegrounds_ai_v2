using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Mechanics.Weapons;
using UltimateXR.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using VrBattlegrounds.Tests.Network;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// Смена тела — только с верхнего уровня (death-body-defer). Запрос изнутри синхронизации UltimateXR
    /// (<c>BeginSync</c> … <c>EndSync</c>) <see cref="AvatarManager"/> не исполняет, а ставит в очередь — одна запись
    /// на сессию — и исполняет в <c>LateUpdate</c>, выбирая тело по состоянию сессии на этот момент. Иначе
    /// <c>NetworkServer.Destroy</c> снимал бы тело раньше <c>EndSync</c>, и Relay отбрасывал бы событие смерти.
    ///
    /// <para>
    /// <b>Как устроено.</b> Сервер Mirror поднят <see cref="MirrorTestHarness"/>. Смерть — настоящая: урон
    /// (<c>UxrActor.ReceiveDamage</c>) → <c>DieInternal</c> (<c>BeginSync</c>) → <c>Died</c> →
    /// <see cref="PlayerController.Die"/> → <see cref="AvatarManager.ServerReconcileBody"/>. Открытый кадр синхронизации
    /// без смерти — публичный <see cref="UxrStateSyncImplementer{T}"/> SDK (<c>BeginSync</c> / <c>CancelSync</c>),
    /// без рефлексии в SDK. Рефлексией читается только приватная очередь менеджера и задаются его сериализованные поля
    /// (как в соседних тестах). <c>LateUpdate</c> зовётся руками: в EditMode PlayerLoop не крутится.
    /// </para>
    ///
    /// <para>
    /// <b>Шаблоны тел</b> — пустые объекты сцены: замена наблюдается по <see cref="AvatarManager.AvatarSpawned"/>,
    /// снятому прежнему телу и имени клона выбранного шаблона. У клона нет <c>NetworkIdentity</c>: Mirror пишет ошибку
    /// спавна и пропускает его (заглушено <see cref="MirrorTestHarness.SilenceMirrorNoise"/>). Настоящий сетевой спавн
    /// призрака и доставку смерти клиентам проверяют E2E (<c>PlayerDeathSignalScenario</c>,
    /// <c>AvatarSwapDeathReplicationScenario</c>) и стенд manipulation.
    /// </para>
    /// </summary>
    public class AvatarManagerDeferredBodyTests : MirrorTestHarness
    {
        private const string Prefix = "DeferBodyTest_";

        private AvatarManager _manager;
        private TeamAvatarStrategy _strategy;
        private GameObject _ghostTemplate;
        private GameObject _livingPrefab;
        private PlayerSession _session;
        private PlayerController _body;
        private int _replacements;
        private readonly List<string> _managerErrors = new List<string>();
        private bool _hadNetworkManager;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();

            // Урон (UxrActor.OnReceiveDamage → UxrNetworkManager.NoSessionOrSessionOwner) автосоздаёт синглтон сети SDK
            // без реализации. Был ли он до теста — HasInstance, не Instance: тот создал бы его сам.
            TestEnvironmentContract.ResetDestroyedSingleton<UxrNetworkManager>();
            _hadNetworkManager = UxrNetworkManager.HasInstance;

            Assert.That(UxrStateSyncImplementer.SyncCallDepth, Is.Zero, "[TestEnvironment] Открыт чужой кадр синхронизации UltimateXR.");
            Assert.That(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync, Is.False,
                        "[TestEnvironment] UxrManager исполняет принятое событие.");

            _replacements = 0;
            _managerErrors.Clear();
            AvatarManager.AvatarSpawned += CountReplacement;
            Application.logMessageReceived += CatchManagerError;

            GameObject bodyTemplate = CreateObject(Prefix + "Body");
            _ghostTemplate = CreateObject(Prefix + "Ghost");
            _strategy = ScriptableObject.CreateInstance<TeamAvatarStrategy>();
            SetPrivateField(_strategy, "fallbackPrefab", bodyTemplate);
            SetPrivateField(_strategy, "ghostPrefab", _ghostTemplate);

            _manager = CreateManager<AvatarManager>("AvatarManager");
            SetPrivateField(_manager, "_combatAvatarStrategy", _strategy);
            SetPrivateField(_manager, "_playerPrefab", bodyTemplate);
            Assert.That(AvatarManager.Instance, Is.SameAs(_manager), "[TestEnvironment] AvatarManager.Instance — не менеджер теста.");

            _session = CreateNetworkComponent<PlayerSession>(Prefix + "Session");
            SpawnOnServer(_session);
            _session.PlayerName = "A";

            // Префаб живого тела — тем же правилом, каким его выберет менеджер: тело живого игрока уже то.
            _livingPrefab = AvatarManager.PrefabFor(_session.TeamIndex, _session.AvatarIndex, _session, _strategy, bodyTemplate);
            Assert.That(_livingPrefab, Is.Not.Null, "[TestEnvironment] Стратегия не выбрала тело живого игрока.");
            Assert.That(_livingPrefab, Is.Not.SameAs(_ghostTemplate), "[TestEnvironment] Живому выбран призрак.");

            _body = CreateBody(Prefix + "Avatar");
        }

        [TearDown]
        public void Cleanup()
        {
            AvatarManager.AvatarSpawned -= CountReplacement;
            Application.logMessageReceived -= CatchManagerError;

            // Клоны шаблонов (ReplaceBody) лежат в корне сцены и в сеть не попали — харнесс о них не знает.
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix) && root.name.Contains(" [")) Object.DestroyImmediate(root);
            }

            if (_strategy != null) Object.DestroyImmediate(_strategy);

            RemoveAutoCreatedNetworkManager();
        }

        // ── Постановка изнутри синхронизации ─────────────────────────────

        [Test]
        public void Смерть_внутри_синхронизации_не_меняет_тело_а_ставит_запрос()
        {
            SilenceMirrorNoise();
            int depthAtDeath = -1;
            int pendingAtDeath = -1;
            PlayerController activeAtDeath = null;

            // Подписка после PlayerController (его Awake): обработчик идёт уже после Die.
            _body.GetComponent<UxrActor>().Died += _ =>
            {
                depthAtDeath = UxrStateSyncImplementer.SyncCallDepth;
                pendingAtDeath = Pending().Count;
                activeAtDeath = _session.ActiveAvatar;
            };

            Kill(_body);

            Assert.IsFalse(_body.IsAlive, "Контроль: урон убил игрока.");
            Assert.Greater(depthAtDeath, 0, "Контроль: обработчики смерти исполняются внутри BeginSync (UxrActor.DieInternal).");
            Assert.AreEqual(1, pendingAtDeath, "Смерть внутри синхронизации не поставила запрос смены тела.");
            Assert.AreSame(_body, activeAtDeath, "Тело сменилось внутри события смерти — событие не ушло бы клиентам.");

            Assert.IsTrue(_body != null, "Тело уничтожено до LateUpdate.");
            Assert.AreSame(_body, _session.ActiveAvatar, "Тело сменилось до LateUpdate.");
            Assert.AreEqual(0, _replacements, "Замена тела исполнена изнутри синхронизации.");

            IList pending = Pending();
            Assert.AreEqual(1, pending.Count, "Ожидалась ровно одна запись очереди.");
            Assert.AreSame(_body, EntryField<PlayerController>(pending[0], "Body"), "Запись не о теле на момент постановки.");
            Assert.AreEqual("смерть", EntryField<string>(pending[0], "Reason"));
            Assert.IsFalse(EntryField<bool>(pending[0], "Change"), "Смерть поставлена как смена скина/команды.");
            AssertNoManagerErrors();
        }

        /// <summary>
        /// Слияние: одна запись на сессию, причина — последняя, <c>Change</c> липкий — была в кадре
        /// <see cref="AvatarManager.ChangeAvatar"/>, и тело заменится безусловно, с изъятием снаряжения.
        /// </summary>
        [Test]
        public void Повторная_постановка_не_дублирует_запись_и_смена_скина_липкая()
        {
            SilenceMirrorNoise();

            using (new OpenSync(_body.GetComponent<UxrActor>()))
            {
                _manager.ServerReconcileBody(_session, "смерть");
                _manager.ChangeAvatar(null, _session, _session.TeamIndex, _session.AvatarIndex);
                _manager.ServerReconcileBody(_session, "возрождение");
            }

            IList pending = Pending();
            Assert.AreEqual(1, pending.Count, "Повторные запросы той же сессии завели новые записи.");
            Assert.AreSame(_session, EntryField<PlayerSession>(pending[0], "Session"));
            Assert.AreSame(_body, EntryField<PlayerController>(pending[0], "Body"));
            Assert.AreEqual("возрождение", EntryField<string>(pending[0], "Reason"), "Причина записи — не последняя.");
            Assert.IsTrue(EntryField<bool>(pending[0], "Change"), "Флаг смены скина/команды сброшен поздним ServerReconcileBody.");

            Assert.AreSame(_body, _session.ActiveAvatar, "Тело сменилось изнутри синхронизации.");
            Assert.AreEqual(0, _replacements, "Замена тела исполнена изнутри синхронизации.");
            AssertNoManagerErrors();
        }

        // ── Исполнение в LateUpdate ──────────────────────────────────────

        [Test]
        public void Отложенная_смена_тела_исполняется_в_LateUpdate()
        {
            SilenceMirrorNoise();
            Kill(_body);
            Assert.AreEqual(1, Pending().Count, "Контроль: смерть поставила запрос.");

            RunLateUpdate();

            Assert.AreEqual(1, _replacements, "Отложенная смена тела не исполнена в LateUpdate (или исполнена дважды).");
            Assert.IsTrue(_body == null, "Прежнее тело погибшего не снято.");
            Assert.IsNotNull(FindClone(_ghostTemplate), "Погибший (выбывший) получил не тело призрака.");
            Assert.AreEqual(0, Pending().Count, "Исполненная запись осталась в очереди.");
            AssertNoManagerErrors();
        }

        /// <summary>
        /// Исполнение тоже проверяет верхний уровень: <c>LateUpdate</c> может прийтись внутрь открытого кадра — тогда
        /// очередь ждёт следующего.
        /// </summary>
        [Test]
        public void Пока_синхронизация_открыта_исполнение_ждёт()
        {
            SilenceMirrorNoise();
            Kill(_body);

            using (new OpenSync(_body.GetComponent<UxrActor>()))
            {
                RunLateUpdate();

                Assert.AreEqual(0, _replacements, "Очередь исполнена внутри открытого кадра синхронизации.");
                Assert.IsTrue(_body != null, "Тело снято внутри открытого кадра синхронизации.");
                Assert.AreEqual(1, Pending().Count, "Запись снята, хотя исполнить её было нельзя.");
            }

            RunLateUpdate();

            Assert.AreEqual(1, _replacements, "Ждавшая запись не исполнилась после закрытия кадра.");
            Assert.AreEqual(0, Pending().Count);
            AssertNoManagerErrors();
        }

        [Test]
        public void Во_время_смены_сцены_исполнение_ждёт()
        {
            SilenceMirrorNoise();
            Kill(_body);

            NetworkServer.isLoadingScene = true;
            try
            {
                RunLateUpdate();

                Assert.AreEqual(0, _replacements, "Тело сменено, пока Mirror меняет сцену.");
                Assert.AreEqual(1, Pending().Count, "Запись снята во время смены сцены — отменённая смена её потеряла бы.");
            }
            finally
            {
                NetworkServer.isLoadingScene = false;
            }

            RunLateUpdate();

            Assert.AreEqual(1, _replacements, "Ждавшая запись не исполнилась после загрузки.");
            AssertNoManagerErrors();
        }

        /// <summary>Вне синхронизации (выбывание с верхнего уровня, возрождение из фаз) тело меняется сразу, как до этапа.</summary>
        [Test]
        public void Вне_синхронизации_тело_меняется_сразу()
        {
            SilenceMirrorNoise();

            _body.ServerEliminateSilently("тест");

            Assert.AreEqual(1, _replacements, "Выбывание с верхнего уровня не сменило тело сразу.");
            Assert.IsTrue(_body == null, "Прежнее тело выбывшего не снято.");
            Assert.IsNotNull(FindClone(_ghostTemplate), "Выбывший получил не тело призрака.");
            Assert.AreEqual(0, Pending().Count, "Смена с верхнего уровня встала в очередь.");
            AssertNoManagerErrors();
        }

        /// <summary>Желаемое тело выбирается при исполнении: смерть и возрождение одного кадра — ноль замен, а не две.</summary>
        [Test]
        public void Смерть_и_возрождение_в_одном_кадре_не_меняют_тело()
        {
            SilenceMirrorNoise();
            Kill(_body);
            Assert.AreEqual(1, Pending().Count, "Контроль: смерть поставила запрос.");

            _body.Respawn();
            Assert.IsTrue(_body.IsAlive, "Контроль: игрок возрождён.");

            RunLateUpdate();

            Assert.AreEqual(0, _replacements, "Смерть и возрождение одного кадра заменили тело.");
            Assert.IsTrue(_body != null, "Тело возрождённого снято.");
            Assert.AreSame(_body, _session.ActiveAvatar);
            Assert.AreEqual(0, Pending().Count, "Запись осталась в очереди.");
            AssertNoManagerErrors();
        }

        /// <summary>
        /// Стратегия без призрака: замены нет, тело остаётся мёртвым (руки и снаряжение отпускает
        /// <see cref="AvatarTeardown.ReleaseDeadBodyOnServer"/> — его поведение в <c>AvatarTeardownServerTests</c>).
        /// </summary>
        [Test]
        public void Без_призрака_смерть_не_меняет_тело()
        {
            SilenceMirrorNoise();
            SetPrivateField(_strategy, "ghostPrefab", null);

            Kill(_body);
            RunLateUpdate();

            Assert.AreEqual(0, _replacements, "Без призрака тело погибшего заменено.");
            Assert.AreSame(_body, _session.ActiveAvatar, "Погибший без призрака остался без тела.");
            Assert.IsFalse(_body.IsAlive, "Тело погибшего ожило.");
            Assert.AreEqual(0, Pending().Count);
            AssertNoManagerErrors();
        }

        // ── Устаревшие записи ────────────────────────────────────────────

        /// <summary>Тело уже сменил прямой вызов с верхнего уровня — он выбрал тело по состоянию, запись снимается.</summary>
        [Test]
        public void Запись_о_сменённом_теле_снимается_без_замены()
        {
            SilenceMirrorNoise();
            Kill(_body);
            PlayerController other = CreateBody(Prefix + "Other");

            Assert.DoesNotThrow(RunLateUpdate);

            Assert.AreEqual(0, _replacements, "Устаревшая запись сменила уже сменённое тело.");
            Assert.AreSame(other, _session.ActiveAvatar, "Устаревшая запись тронула новое тело.");
            Assert.AreEqual(0, Pending().Count, "Устаревшая запись осталась в очереди.");
            AssertNoManagerErrors();
        }

        /// <summary>Тело пропало до исполнения (смена карты, отключение) — новое создаст спавн, запись снимается.</summary>
        [Test]
        public void Запись_о_пропавшем_теле_снимается_без_замены()
        {
            SilenceMirrorNoise();
            Kill(_body);
            NetworkServer.Destroy(_body.gameObject);
            Assert.IsNull(_session.ActiveAvatar, "Контроль: у сессии нет тела.");

            Assert.DoesNotThrow(RunLateUpdate);

            Assert.AreEqual(0, _replacements, "Запись о пропавшем теле создала тело.");
            Assert.AreEqual(0, Pending().Count, "Устаревшая запись осталась в очереди.");
            AssertNoManagerErrors();
        }

        [Test]
        public void Запись_уничтоженной_сессии_снимается_без_исключений()
        {
            SilenceMirrorNoise();
            Kill(_body);
            // Сессия харнесса — объект сцены (sceneId ≠ 0): Destroy снимает её с сети, isServer гаснет —
            // ветка «сессия не на сервере» (в игре уничтоженная сессия попадает в ту же проверку как null).
            NetworkServer.Destroy(_session.gameObject);

            Assert.DoesNotThrow(RunLateUpdate);

            Assert.AreEqual(0, _replacements, "Запись уничтоженной сессии сменила тело.");
            Assert.AreEqual(0, Pending().Count, "Запись уничтоженной сессии осталась в очереди.");
            AssertNoManagerErrors();
        }

        // ── Харнесс ──────────────────────────────────────────────────────

        /// <summary>Живое тело сессии — как его создаёт AvatarManager: в сети, связано с сессией, префаб записан.</summary>
        private PlayerController CreateBody(string name)
        {
            GameObject go = CreateNetworkObject(name);
            UxrActor actor = go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();
            NetworkIdentity identity = EnableNetworking(go);

            // В игре тело — клон префаба (Instantiate), у него sceneId = 0, и NetworkServer.Destroy его уничтожает.
            // Объект, собранный в сцене редактора, получает sceneId в OnValidate NetworkIdentity — для Mirror это
            // объект сцены, и Destroy его только снимает с сети (UnSpawn + SetActive(false)), не уничтожая.
            identity.sceneId = 0;

            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            player.SourcePrefab = _livingPrefab;
            _session.ActiveAvatar = player;
            actor.Life = 100f;
            Assert.That(player.Session, Is.SameAs(_session), "[TestEnvironment] Тело не связано с сессией.");
            return player;
        }

        /// <summary>
        /// Убирает <c>UxrNetworkManager</c>, автосозданный уроном этого теста: без реализации сети он переживал бы тест, и
        /// у соседей <c>HasInstance</c> становилось истинным — снятие с якоря (патч 75,
        /// <c>StartPhysicsSyncAfterAnchorRemoval</c>) разыменовывало пустой <c>NetworkImplementation</c>. Менеджер, бывший до
        /// теста, и менеджер с реализацией не трогаются. <c>OnDestroy</c> SDK в EditMode не зовётся — синглтон сбрасывается руками.
        /// </summary>
        private void RemoveAutoCreatedNetworkManager()
        {
            if (_hadNetworkManager || !UxrNetworkManager.HasInstance) return;

            UxrNetworkManager created = UxrNetworkManager.Instance;
            if (created.NetworkImplementation != null) return;

            FieldInfo singleton = null;
            for (Type type = typeof(UxrNetworkManager); type != null && singleton == null; type = type.BaseType)
            {
                singleton = type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            Assert.IsNotNull(singleton, "[TestEnvironment] Изменился контракт singleton UxrNetworkManager.");

            Object.DestroyImmediate(created.gameObject);
            if (ReferenceEquals(singleton.GetValue(null), created)) singleton.SetValue(null, null);
        }

        /// <summary>Смертельный урон — штатный путь смерти (UxrActor.DieInternal внутри BeginSync).</summary>
        private static void Kill(PlayerController body) => body.GetComponent<UxrActor>().ReceiveDamage(500f);

        private void RunLateUpdate() => InvokeLifecycleMethod(_manager, "LateUpdate");

        private IList Pending()
        {
            FieldInfo field = typeof(AvatarManager).GetField("_pendingBodies", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "В AvatarManager нет _pendingBodies — очередь переделана, поправь тест.");
            return (IList)field.GetValue(_manager);
        }

        private static T EntryField<T>(object entry, string name)
        {
            FieldInfo field = entry.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(field, $"В записи очереди нет поля {name} — поправь тест.");
            return (T)field.GetValue(entry);
        }

        private static GameObject FindClone(GameObject template)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root != template && root.name.StartsWith(template.name + " [")) return root;
            }

            return null;
        }

        private void CountReplacement(PlayerController _) => _replacements++;

        private void CatchManagerError(string message, string stackTrace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && message.Contains("[AvatarManager]")) _managerErrors.Add(message);
        }

        private void AssertNoManagerErrors() =>
            Assert.That(_managerErrors, Is.Empty, "AvatarManager сообщил об ошибке (заглушённой SilenceMirrorNoise).");

        /// <summary>Открытый кадр синхронизации UltimateXR — публичным API SDK, как его открывает любой компонент.</summary>
        private sealed class OpenSync : IDisposable
        {
            private readonly UxrStateSyncImplementer<UxrActor> _sync;

            public OpenSync(UxrActor owner)
            {
                _sync = new UxrStateSyncImplementer<UxrActor>(owner);
                _sync.BeginSync();
            }

            public void Dispose() => _sync.CancelSync();
        }
    }
}
