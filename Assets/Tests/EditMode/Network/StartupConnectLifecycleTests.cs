using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Network;
using Step = VrBattlegrounds.Network.GameNetworkManager.PublicationStep;
using Phase = VrBattlegrounds.Network.GameNetworkDiscovery.InitialConnectPhase;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Старт сервера и первое подключение клиента — контракт
    /// <c>tasks/bots-validation/contracts/startup-lifecycle-design.md</c> (решение пользователя 2026-10-19).
    ///
    /// <para>
    /// Что доказывают тесты. S1: сервер открывает порт только после конца старта — смена сцены и её
    /// подписчики отработали, карта закоммитила режим, прошёл ещё один кадр; отказ сборки карты —
    /// именованный отказ, а не публикация. В trace829 (discovery8) сокет открывался раньше карты, и
    /// клиент рвался по тишине KCP в длинном кадре сервера. S2: клиент с явным адресом, начавший раньше
    /// публикации, повторяет попытки с паузой 1→2→4→5 с в общем сроке 180 с; после первого подключения
    /// и после отмены повторов нет. В trace829 (localhost8) клиент один раз получал SocketException и
    /// навсегда оставался Offline.
    /// </para>
    ///
    /// <para>
    /// Поведение проверил пользователь в worker (аренды 844/847): discovery и localhost с 8 ботами,
    /// localhost без ботов, Host с ботами — Ready; localhost-клиенты подключились со второй попытки.
    /// </para>
    /// </summary>
    public class StartupConnectLifecycleTests
    {
        // ── S1: решение о публикации ─────────────────────────────────────────

        private static Step Decide(bool active = true, bool loading = false, bool pending = false,
                                   bool hasBootstrap = true, bool failed = false, bool ready = true,
                                   int armedFrame = -1, int frame = 10) =>
            GameNetworkManager.DecidePublication(active, loading, pending, hasBootstrap, failed, ready, armedFrame, frame);

        [Test]
        public void Сервер_не_публикуется_пока_старт_не_закончен()
        {
            Assert.AreEqual(Step.Wait, Decide(active: false), "Сервер не активен — публиковать нечего.");
            Assert.AreEqual(Step.Wait, Decide(loading: true), "Mirror ещё грузит сцену.");
            Assert.AreEqual(Step.Wait, Decide(pending: true),
                "Подписчики конца смены сцены (там стенд создаёт ботов) ещё не отработали.");
            Assert.AreEqual(Step.Wait, Decide(ready: false),
                "Карта не закоммитила режим: клиент вошёл бы в тяжёлый старт и оборвался по тишине KCP.");
        }

        [Test]
        public void Публикация_только_через_кадр_после_готовности()
        {
            Assert.AreEqual(Step.Arm, Decide(armedFrame: -1, frame: 10), "Первый готовый кадр только взводит публикацию.");
            Assert.AreEqual(Step.Arm, Decide(armedFrame: 10, frame: 10), "В том же кадре порт не открывается.");
            Assert.AreEqual(Step.Publish, Decide(armedFrame: 10, frame: 11), "Следующий кадр — граница сетевого цикла: публикация.");
        }

        [Test]
        public void Сцена_без_MapBootstrap_готова_сразу()
        {
            Assert.AreEqual(Step.Arm, Decide(hasBootstrap: false, ready: false),
                "Стенд или сцена без MapRoot не ждёт квитанции карты, которой у неё нет.");
        }

        [Test]
        public void Отказ_сборки_карты_это_отказ_а_не_публикация()
        {
            Assert.AreEqual(Step.Fail, Decide(failed: true, ready: false),
                "Сломанная карта не публикуется: клиенты получат свой срок ожидания и именованный отказ.");
            Assert.AreEqual(Step.Wait, Decide(pending: true, failed: true),
                "Пока идёт смена сцены, судить о её отказе рано.");
        }

        // ── S2: первое подключение клиента ───────────────────────────────────

        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly System.Type Discovery = typeof(GameNetworkDiscovery);

        private static void SetField(string name, object value)
        {
            FieldInfo field = Discovery.GetField(name, PrivateStatic);
            Assert.IsNotNull(field, $"Поле {name} не найдено — тест устарел.");
            field.SetValue(null, value);
        }

        private static T GetField<T>(string name) => (T)Discovery.GetField(name, PrivateStatic).GetValue(null);

        private static void Invoke(string name, params object[] args)
        {
            MethodInfo method = Discovery.GetMethod(name, PrivateStatic);
            Assert.IsNotNull(method, $"Метод {name} не найден — тест устарел.");
            method.Invoke(null, args);
        }

        /// <summary>Попытка <paramref name="attempt"/> идёт к адресу, срок не истёк.</summary>
        private static void Connecting(int attempt)
        {
            SetField("_initialAddress", "127.0.0.1");
            SetField("_initialAttempt", attempt);
            SetField("_initialDeadline", Time.realtimeSinceStartup + GameNetworkDiscovery.InitialConnectDeadlineSeconds);
            SetField("_initialPhase", Phase.Connecting);
        }

        [SetUp]
        public void ResetState() => Invoke("ResetInitialConnect");

        [TearDown]
        public void CleanState() => Invoke("ResetInitialConnect");

        [Test]
        public void Срок_первого_подключения_по_умолчанию_180_секунд()
        {
            Assert.AreEqual(180f, GameNetworkDiscovery.InitialConnectDeadlineSeconds,
                "Срок выбран пользователем 2026-10-19; меняется только его решением.");
        }

        [TestCase(1, 1f)]
        [TestCase(2, 2f)]
        [TestCase(3, 4f)]
        [TestCase(4, 5f)]
        [TestCase(9, 5f)]
        public void Обрыв_до_первого_подключения_ставит_повтор_с_растущей_паузой(int attempt, float delay)
        {
            Connecting(attempt);
            float before = Time.realtimeSinceStartup;

            GameNetworkDiscovery.NotifyClientStopped(false);

            Assert.AreEqual(Phase.WaitingRetry, GameNetworkDiscovery.InitialPhase,
                "Клиент начал раньше публикации сервера: обрыв (SocketException) — повод для новой попытки, " +
                "а не вечный Offline, как в trace829 localhost8.");
            float wait = GetField<float>("_initialNextAttemptAt") - before;
            Assert.That(wait, Is.InRange(delay - 0.05f, delay + 0.5f), $"Пауза перед попыткой {attempt + 1}.");
        }

        [Test]
        public void После_первого_подключения_повторов_нет()
        {
            Connecting(2);
            GameNetworkDiscovery.NotifyClientConnected();
            Assert.AreEqual(Phase.Online, GameNetworkDiscovery.InitialPhase, "Handshake прошёл — фаза первого подключения закрыта.");

            GameNetworkDiscovery.NotifyClientStopped(false);
            Assert.AreEqual(Phase.Online, GameNetworkDiscovery.InitialPhase,
                "Обрыв после первого подключения — штатный: самовольного переподключения нет.");
        }

        [Test]
        public void Отмена_прекращает_попытки()
        {
            Connecting(1);
            GameNetworkDiscovery.NotifyClientStopped(false);
            Assert.AreEqual(Phase.WaitingRetry, GameNetworkDiscovery.InitialPhase, "Контроль: повтор поставлен.");

            Invoke("CancelInitialConnect", "тест");
            Assert.AreEqual(Phase.Cancelled, GameNetworkDiscovery.InitialPhase, "Пользователь остановил роль.");

            GameNetworkDiscovery.NotifyClientStopped(false);
            Assert.AreEqual(Phase.Cancelled, GameNetworkDiscovery.InitialPhase, "После отмены попытки не возобновляются.");
        }

        // ── Хвосты ревью (2026-10-20): отключение между попытками ────────────

        private static GameNetworkDiscovery CreateDiscovery(out GameObject go)
        {
            go = new GameObject("TestDiscovery") { hideFlags = HideFlags.DontSave };
            return go.AddComponent<GameNetworkDiscovery>(); // NetworkDiscovery добавит RequireComponent
        }

        [Test]
        public void Отключение_между_попытками_прекращает_их_без_ошибки()
        {
            GameNetworkDiscovery discovery = CreateDiscovery(out GameObject go);
            try
            {
                Connecting(1);
                GameNetworkDiscovery.NotifyClientStopped(false);
                Assert.AreEqual(Phase.WaitingRetry, GameNetworkDiscovery.InitialPhase, "Контроль: клиент между попытками, не активен.");

                Assert.DoesNotThrow(() => discovery.RequestClientDisconnect(),
                    "Отключиться между попытками — значит прекратить их; ClientNotActive вводил бы вызывающего в заблуждение.");
                Assert.AreEqual(Phase.Cancelled, GameNetworkDiscovery.InitialPhase);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Отключение_без_клиента_и_без_попыток_остаётся_ошибкой()
        {
            GameNetworkDiscovery discovery = CreateDiscovery(out GameObject go);
            try
            {
                var error = Assert.Throws<System.InvalidOperationException>(() => discovery.RequestClientDisconnect());
                Assert.AreEqual("ClientNotActive", error.Message, "Нечего отключать и нечего отменять — прежний ответ.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void По_истечении_срока_именованный_отказ()
        {
            Connecting(5);
            GameNetworkDiscovery.NotifyClientError("Timeout: тест");
            GameNetworkDiscovery.NotifyClientStopped(false);
            SetField("_initialDeadline", Time.realtimeSinceStartup - 1f);

            LogAssert.Expect(LogType.Error, new Regex("initial-connect-timeout.*попыток 5.*Timeout: тест"));
            Invoke("TickInitialConnect");

            Assert.AreEqual(Phase.Failed, GameNetworkDiscovery.InitialPhase,
                "Сервера нет весь срок — процесс не висит без действия, а завершается названной ошибкой.");
        }
    }
}
