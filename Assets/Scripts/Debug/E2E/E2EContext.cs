// Ярус C (два процесса) — разбор аргументов командной строки.
#if !VRBG_NO_E2E
using System;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Параметры прогона, снятые с командной строки плеера.
    ///
    /// Полный набор аргументов:
    /// <code>
    /// -e2eScenario      &lt;имя&gt;   обязателен; без него харнесс не поднимается вообще
    /// -e2eRole          &lt;роль&gt;  server | client-1 | client-2 (по умолчанию — по IsHeadless)
    /// -e2eResult        &lt;путь&gt;  куда писать JSON-вердикт (абсолютный путь)
    /// -e2eTimeout       &lt;сек&gt;   общий таймаут сценария, по умолчанию 240
    /// -e2eMap           &lt;сцена&gt; карта, которую грузит сервер, по умолчанию TestMap2
    /// -e2eClients       &lt;N&gt;     сколько клиентов ждёт сервер, по умолчанию 2
    /// -e2eServerAddress &lt;ip&gt;    фоллбэк прямого подключения, если Discovery молчит
    /// -e2eDeviceToken   &lt;токен&gt; уникальный DeviceToken клиента (см. ниже)
    /// -e2eNetworkPort   &lt;порт&gt;  UDP-порт KCP прогона (только вместе с -e2eDiscoveryPort)
    /// -e2eDiscoveryPort &lt;порт&gt;  UDP-порт Discovery прогона (только вместе с -e2eNetworkPort)
    /// </code>
    ///
    /// Про порты. Пара изолирует прогон от соседних плееров и редакторов на той же машине: без неё все
    /// процессы сидят на 7778/47777, и клиент через Discovery может найти чужой сервер. Ошибка в паре
    /// (задан один аргумент, не целое число, вне 1..65535, совпадение) не откатывается на порты по умолчанию:
    /// она попадает в <see cref="PortError"/>, и исполнитель валит прогон до запуска сценария.
    ///
    /// Про <c>-e2eDeviceToken</c>. <see cref="Network.GameNetworkManager"/> берёт токен
    /// устройства через <see cref="Core.ClientDeviceIdentity"/>, а два процесса одного билда на одной
    /// машине делят один и тот же PlayerPrefs — без переопределения оба клиента
    /// представятся серверу одним устройством.
    /// </summary>
    public class E2EContext
    {
        public string Scenario { get; private set; }
        public string Role { get; private set; }
        public string ResultPath { get; private set; }
        public float Timeout { get; private set; }
        public string Map { get; private set; }
        public int ExpectedClients { get; private set; }
        public string ServerAddress { get; private set; }
        public string DeviceToken { get; private set; }
        public bool ExternallyLaunched { get; private set; }
        /// <summary>UDP-порт KCP прогона; 0 — не задан (остаётся порт сцены).</summary>
        public int NetworkPort { get; private set; }
        /// <summary>UDP-порт Discovery прогона; 0 — не задан (остаётся порт сцены).</summary>
        public int DiscoveryPort { get; private set; }
        /// <summary>Ошибка разбора пары портов; null — пара задана корректно или не задана вовсе.</summary>
        public string PortError { get; private set; }
        /// <summary>Пара портов прогона задана.</summary>
        public bool HasPorts => NetworkPort != 0 && DiscoveryPort != 0;

        /// <summary>Роль «сервер»: процесс поднимает выделенный сервер и выносит вердикт.</summary>
        public bool IsServerRole => Role == "server";

        /// <summary>
        /// Собирает контекст из командной строки. Возвращает null, если
        /// <c>-e2eScenario</c> не задан — тогда харнесс не должен вмешиваться в игру.
        /// </summary>
        public static E2EContext FromCommandLine()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                return null;
            }

            string scenario = Read(args, "-e2eScenario");
            if (string.IsNullOrEmpty(scenario))
                return null;

            string role = Read(args, "-e2eRole");
            if (string.IsNullOrEmpty(role))
                role = Mirror.Utils.IsHeadless() ? "server" : "client";

            float timeout = ReadFloat(args, "-e2eTimeout", 240f);
            if (float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0) timeout = 240f;
            float clients = ReadFloat(args, "-e2eClients", 2f);
            if (float.IsNaN(clients) || float.IsInfinity(clients) || clients < 0 || clients >= int.MaxValue) clients = 2f;
            string portError = ReadPorts(args, out int networkPort, out int discoveryPort);
            bool portsValid = portError == null;
            E2EContext context = Create(scenario, role, Read(args, "-e2eResult"), timeout,
                ReadOr(args, "-e2eMap", "TestMap2"), (int)clients,
                Read(args, "-e2eServerAddress"), Read(args, "-e2eDeviceToken"),
                networkPort: portsValid ? networkPort : 0,
                discoveryPort: portsValid ? discoveryPort : 0);
            // Не бросаем: исполнитель обязан записать ошибку в файл результата и не запускать сценарий.
            context.PortError = portError;
            return context;
        }

        /// <summary>Общий контекст для CLI и Editor; не запускает сеть и не меняет профиль.</summary>
        public static E2EContext Create(string scenario, string role, string resultPath = null, float timeout = 240f,
            string map = "TestMap2", int expectedClients = 2, string serverAddress = null, string deviceToken = null, bool externallyLaunched = false,
            int networkPort = 0, int discoveryPort = 0)
        {
            if (string.IsNullOrEmpty(scenario) || string.IsNullOrEmpty(role)) throw new ArgumentException("Нужны сценарий и роль E2E.");
            if (float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0 || expectedClients < 0)
                throw new ArgumentException("Некорректные ожидания E2E.");
            if (networkPort != 0 || discoveryPort != 0)
            {
                string portError = ValidatePorts(networkPort, discoveryPort);
                if (portError != null) throw new ArgumentException("Некорректные порты E2E: " + portError);
            }
            return new E2EContext { Scenario = scenario, Role = role, ResultPath = resultPath, Timeout = timeout,
                Map = map, ExpectedClients = expectedClients, ServerAddress = serverAddress, DeviceToken = deviceToken, ExternallyLaunched = externallyLaunched,
                NetworkPort = networkPort, DiscoveryPort = discoveryPort };
        }

        /// <summary>
        /// Проверка пары портов: обе в 1..65535 и различны. Возвращает текст ошибки или null.
        /// Общая для разбора командной строки и для <see cref="E2EPlayerPorts"/>.
        /// </summary>
        public static string ValidatePorts(int networkPort, int discoveryPort)
        {
            if (networkPort < 1 || networkPort > 65535)
                return $"порт KCP вне 1..65535: {networkPort}";
            if (discoveryPort < 1 || discoveryPort > 65535)
                return $"порт Discovery вне 1..65535: {discoveryPort}";
            if (networkPort == discoveryPort)
                return $"порты KCP и Discovery совпадают: {networkPort}";
            return null;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("сценарий=").Append(Scenario)
              .Append(", роль=").Append(Role)
              .Append(", карта=").Append(Map)
              .Append(", клиентов=").Append(ExpectedClients)
              .Append(", таймаут=").Append(Timeout).Append("с")
              .Append(", порты=").Append(HasPorts ? $"KCP {NetworkPort} / Discovery {DiscoveryPort}" : "(по умолчанию сцены)")
              .Append(", результат=").Append(string.IsNullOrEmpty(ResultPath) ? "(не задан)" : ResultPath);
            if (PortError != null)
                sb.Append(", ошибка портов: ").Append(PortError);
            return sb.ToString();
        }

        private static string Read(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static string ReadOr(string[] args, string key, string fallback)
        {
            string value = Read(args, key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        /// <summary>
        /// Разбирает пару портов. Оба аргумента отсутствуют — порты не заданы (0/0, без ошибки).
        /// Неполная или некорректная пара — текст ошибки; на порты по умолчанию молча не откатываемся.
        /// </summary>
        private static string ReadPorts(string[] args, out int networkPort, out int discoveryPort)
        {
            networkPort = discoveryPort = 0;
            bool hasNetwork = Has(args, "-e2eNetworkPort");
            bool hasDiscovery = Has(args, "-e2eDiscoveryPort");
            if (!hasNetwork && !hasDiscovery)
                return null;
            if (hasNetwork != hasDiscovery)
                return hasNetwork ? "задан -e2eNetworkPort без -e2eDiscoveryPort" : "задан -e2eDiscoveryPort без -e2eNetworkPort";

            string networkRaw = Read(args, "-e2eNetworkPort");
            string discoveryRaw = Read(args, "-e2eDiscoveryPort");
            if (!TryParsePort(networkRaw, out networkPort))
                return $"-e2eNetworkPort не целое число: '{networkRaw}'";
            if (!TryParsePort(discoveryRaw, out discoveryPort))
                return $"-e2eDiscoveryPort не целое число: '{discoveryRaw}'";
            return ValidatePorts(networkPort, discoveryPort);
        }

        private static bool TryParsePort(string value, out int port)
        {
            // Без знака, пробелов и разделителей: «7778» — да, «+7778», «7 778», «7778.0» — нет.
            return int.TryParse(value, System.Globalization.NumberStyles.None,
                                System.Globalization.CultureInfo.InvariantCulture, out port);
        }

        /// <summary>Ключ есть в командной строке, даже если за ним нет значения.</summary>
        private static bool Has(string[] args, string key)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static float ReadFloat(string[] args, string key, float fallback)
        {
            string value = Read(args, key);
            if (string.IsNullOrEmpty(value))
                return fallback;

            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }
    }
}
#endif
