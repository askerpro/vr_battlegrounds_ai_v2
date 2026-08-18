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
    /// </code>
    ///
    /// Про <c>-e2eDeviceToken</c>. <see cref="Network.GameNetworkManager"/> берёт токен
    /// устройства из <see cref="PlayerPrefs"/>, а два процесса одного билда на одной
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

            return new E2EContext
            {
                Scenario        = scenario,
                Role            = role,
                ResultPath      = Read(args, "-e2eResult"),
                Timeout         = ReadFloat(args, "-e2eTimeout", 240f),
                Map             = ReadOr(args, "-e2eMap", "TestMap2"),
                ExpectedClients = (int)ReadFloat(args, "-e2eClients", 2f),
                ServerAddress   = Read(args, "-e2eServerAddress"),
                DeviceToken     = Read(args, "-e2eDeviceToken")
            };
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("сценарий=").Append(Scenario)
              .Append(", роль=").Append(Role)
              .Append(", карта=").Append(Map)
              .Append(", клиентов=").Append(ExpectedClients)
              .Append(", таймаут=").Append(Timeout).Append("с")
              .Append(", результат=").Append(string.IsNullOrEmpty(ResultPath) ? "(не задан)" : ResultPath);
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
