// Ярус C (два процесса) — разбор аргументов командной строки.
#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    /// -e2eResult        &lt;путь&gt;  куда писать JSON-вердикт; относительный путь — от Application.persistentDataPath
    /// -e2eTimeout       &lt;сек&gt;   общий таймаут сценария, по умолчанию 240
    /// -e2eMap           &lt;сцена&gt; карта, которую грузит сервер, по умолчанию TestMap2
    /// -e2eClients       &lt;N&gt;     сколько клиентов в прогоне (client-1..client-N), по умолчанию 2
    /// -e2eLate          &lt;роль&gt;  поздний клиент прогона (client-N); всем процессам, см. ниже
    /// -e2eServerAddress &lt;ip&gt;    фоллбэк прямого подключения, если Discovery молчит
    /// -e2eDeviceToken   &lt;токен&gt; уникальный DeviceToken клиента (см. ниже)
    /// -e2eNetworkPort   &lt;порт&gt;  UDP-порт KCP прогона (только вместе с -e2eDiscoveryPort)
    /// -e2eDiscoveryPort &lt;порт&gt;  UDP-порт Discovery прогона (только вместе с -e2eNetworkPort)
    /// -e2eArg   &lt;ключ&gt;=&lt;значение&gt; параметр сценария-потребителя, повторяемый (читается через Arg)
    /// -e2eQuitOnFinish             после записи вердикта закрыть процесс в любой роли (прогон на шлеме)
    /// -e2eHeadPose      eye=&lt;м&gt;|calib симуляция позы головы на шлеме (<see cref="E2EHeadPose"/>, контракт r5)
    /// </code>
    ///
    /// Про <c>-e2eHeadPose</c>. <c>eye=1.6</c> — высота глаз над корнем аватара в метрах (в пределах
    /// <c>PlayerCalibrationRules.MinEyeHeight..MaxEyeHeight</c>), <c>calib</c> — цель из применённой калибровки
    /// (<c>Applied.EyeHeight</c>, без неё 1.6). Без флага симуляции нет. Ошибка разбора (нет значения, неизвестный вид,
    /// не число, вне пределов) — <see cref="HeadPoseError"/>, симуляция не включается.
    ///
    /// Про <c>-e2eArg</c>. Единственный канал параметров сценария (контракт quest-device-run). Ключ без <c>=</c>,
    /// пустой ключ или повтор ключа — не молчаливый пропуск, а <see cref="ArgError"/>: исполнитель валит прогон
    /// до запуска сценария, как при ошибке портов.
    ///
    /// Про порты. Пара изолирует прогон от соседних плееров и редакторов на той же машине: без неё все
    /// процессы сидят на 7778/47777, и клиент через Discovery может найти чужой сервер. Ошибка в паре
    /// (задан один аргумент, не целое число, вне 1..65535, совпадение) не откатывается на порты по умолчанию:
    /// она попадает в <see cref="PortError"/>, и исполнитель валит прогон до запуска сценария.
    ///
    /// Про <c>-e2eLate</c>. Дирижёр передаёт его всем процессам с ролью позднего клиента: сервер исключает её из
    /// барьеров без <c>includeLate</c>, сам поздний клиент видит <see cref="IsLateClient"/>. Нет значения или роль вне
    /// client-1..client-N — <see cref="TopologyError"/>, исполнитель валит прогон до сценария.
    ///
    /// Про шаги. <see cref="Step"/> и <see cref="ServerStep"/> пишут журнал шагов и ведут барьер
    /// (<c>E2EBarrier</c>): клиент — серверу, сервер — клиентам. Работают только у контекста, запущенного исполнителем.
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
        /// <summary>Единственный канал diagnostic evidence; null у legacy-прогона.</summary>
        public E2EDiagnosticSession Diagnostics => _diagnostics;
        private E2EDiagnosticSession _diagnostics;
        /// <summary>Ошибка immutable diagnostic inputs; не разрешает запуск сценария или сети.</summary>
        public string DiagnosticError { get; internal set; }
        internal E2EDiagnosticInput DiagnosticInput { get; private set; }
        internal void AttachDiagnostics(E2EDiagnosticSession session) => _diagnostics = session;
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
        /// <summary>Параметры сценария из <c>-e2eArg ключ=значение</c>; только для чтения, ключи регистрозависимы.</summary>
        public IReadOnlyDictionary<string, string> Args { get; private set; } = EmptyArgs;
        /// <summary>Ошибка разбора <c>-e2eArg</c>; null — параметры заданы корректно или не заданы вовсе.</summary>
        public string ArgError { get; private set; }
        /// <summary>
        /// Закрыть процесс после записи вердикта в любой роли (<c>-e2eQuitOnFinish</c>). Без флага закрывается
        /// только сервер: клиентов на ПК гасит дирижёр.
        /// </summary>
        public bool QuitOnFinish { get; private set; }
        /// <summary>Роль позднего клиента прогона (<c>-e2eLate</c>); null — позднего клиента нет.</summary>
        public string LateRole { get; private set; }
        /// <summary>Ошибка топологии (<c>-e2eLate</c>); null — задана корректно или не задана.</summary>
        public string TopologyError { get; private set; }
        /// <summary>Сколько клиентов в прогоне: роли client-1..client-N (<c>-e2eClients</c>); то же, что <see cref="ExpectedClients"/>.</summary>
        public int ClientCount => ExpectedClients;
        /// <summary>Индекс процесса: сервер/хост 0, <c>client-N</c> → N, роль вне схемы → -1.</summary>
        public int ClientIndex => E2EStepRules.ClientIndexOf(Role);
        /// <summary>Этот процесс — поздний клиент прогона.</summary>
        public bool IsLateClient => LateRole != null && Role == LateRole;
        /// <summary>Сторона сервера барьера: выделенный сервер или хост.</summary>
        public bool IsServerSide => E2EStepRules.IsServerSideRole(Role);

        /// <summary>
        /// Шаг достигнут: запись в журнал шагов; клиент вдобавок отправляет его серверу (после e2e-ready, до него —
        /// копит). Повтор с тем же data идемпотентен, с другим — обновляет data. false — шаг отброшен по лимиту
        /// (имя ≤64, data ≤256, ≤512 шагов), ошибка — в журнале Unity.
        /// </summary>
        public bool Step(string name, string data = null) =>
            E2EBarrierCore.Shared.Reach(this, name, data, DateTime.UtcNow);

        /// <summary>Шаг сервера: журнал и рассылка клиентам (их <c>E2EBarrier.WaitServer</c>). Только сервер/хост.</summary>
        public bool ServerStep(string name, string data = null) =>
            E2EBarrierCore.Shared.ReachServer(this, name, data, DateTime.UtcNow);

        /// <summary>Симуляция позы головы запрошена корректным <c>-e2eHeadPose</c>.</summary>
        public bool HeadPoseEnabled { get; private set; }
        /// <summary>Цель симуляции, м (<c>eye=</c>); NaN — режим <c>calib</c> или симуляция не запрошена.</summary>
        public float HeadPoseEye { get; private set; } = float.NaN;
        /// <summary>Ошибка разбора <c>-e2eHeadPose</c>; null — флаг задан корректно или не задан вовсе.</summary>
        public string HeadPoseError { get; private set; }

        private static readonly IReadOnlyDictionary<string, string> EmptyArgs =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        /// <summary>Значение параметра сценария <c>-e2eArg ключ=значение</c> или <paramref name="fallback"/>.</summary>
        public string Arg(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            return Args.TryGetValue(key, out string value) ? value : fallback;
        }

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

            return FromArgs(args);
        }

        /// <summary>
        /// То же, что <see cref="FromCommandLine"/>, но из переданного массива аргументов (проверки харнесса).
        /// null — нет <c>-e2eScenario</c>.
        /// </summary>
        public static E2EContext FromArgs(string[] args)
        {
            if (args == null)
                return null;

            string scenario = Read(args, "-e2eScenario");
            if (string.IsNullOrEmpty(scenario))
            {
                if (!Has(args, "-e2eRunSpec") && !Has(args, "-e2eRunSpecSha256")) return null;
                // Неполный diagnostic CLI обязан отказать до автоматического старта сети.
                scenario = "<missing-diagnostic-scenario>";
            }

            string role = Read(args, "-e2eRole");
            if (string.IsNullOrEmpty(role))
                role = Mirror.Utils.IsHeadless() ? "server" : "client";

            float timeout = ReadFloat(args, "-e2eTimeout", 240f);
            if (float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0) timeout = 240f;
            float clients = ReadFloat(args, "-e2eClients", 2f);
            if (float.IsNaN(clients) || float.IsInfinity(clients) || clients < 0 || clients >= int.MaxValue) clients = 2f;
            string portError = ReadPorts(args, out int networkPort, out int discoveryPort);
            bool portsValid = portError == null;
            string argError = ReadScenarioArgs(args, out Dictionary<string, string> scenarioArgs);
            string topologyError = ReadLate(args, (int)clients, out string lateRole);
            E2EContext context = Create(scenario, role, ResolveResultPath(Read(args, "-e2eResult")), timeout,
                ReadOr(args, "-e2eMap", "TestMap2"), (int)clients,
                Read(args, "-e2eServerAddress"), Read(args, "-e2eDeviceToken"),
                networkPort: portsValid ? networkPort : 0,
                discoveryPort: portsValid ? discoveryPort : 0,
                args: argError == null ? scenarioArgs : null,
                quitOnFinish: Has(args, "-e2eQuitOnFinish"),
                lateRole: topologyError == null ? lateRole : null);
            // Не бросаем: исполнитель обязан записать ошибку в файл результата и не запускать сценарий.
            context.PortError = portError;
            context.ArgError = argError;
            context.TopologyError = topologyError;
            context.HeadPoseError = ReadHeadPose(args, out bool headPose, out float headPoseEye);
            context.HeadPoseEnabled = headPose;
            context.HeadPoseEye = headPoseEye;
            context.DiagnosticError = E2EDiagnosticInput.TryRead(args, context, out E2EDiagnosticInput input);
            context.DiagnosticInput = input;
            if (input != null)
            {
                // Legacy-сценарии маршрутизируют по scene.name; actual identity session сверяет только по scene.path.
                context.Map = Path.GetFileNameWithoutExtension(input.RequestedSceneId);
                context.HeadPoseEnabled = input.HeadPoseEnabled;
                context.HeadPoseEye = input.HeadPoseEye;
                var merged = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in context.Args) merged.Add(pair.Key, pair.Value);
                if (input.ViewsPath != null) merged[E2EViews.ArgKey] = input.ViewsPath;
                context.Args = Snapshot(merged);
            }
            return context;
        }

        /// <summary>Общий контекст для CLI и Editor; не запускает сеть и не меняет профиль.</summary>
        public static E2EContext Create(string scenario, string role, string resultPath = null, float timeout = 240f,
            string map = "TestMap2", int expectedClients = 2, string serverAddress = null, string deviceToken = null, bool externallyLaunched = false,
            int networkPort = 0, int discoveryPort = 0, IReadOnlyDictionary<string, string> args = null, bool quitOnFinish = false,
            string lateRole = null)
        {
            if (string.IsNullOrEmpty(scenario) || string.IsNullOrEmpty(role)) throw new ArgumentException("Нужны сценарий и роль E2E.");
            if (float.IsNaN(timeout) || float.IsInfinity(timeout) || timeout <= 0 || expectedClients < 0)
                throw new ArgumentException("Некорректные ожидания E2E.");
            if (lateRole != null)
            {
                string lateError = E2EStepRules.ValidateClientRole(lateRole, expectedClients);
                if (lateError != null) throw new ArgumentException("Некорректный поздний клиент E2E: " + lateError);
            }
            if (networkPort != 0 || discoveryPort != 0)
            {
                string portError = ValidatePorts(networkPort, discoveryPort);
                if (portError != null) throw new ArgumentException("Некорректные порты E2E: " + portError);
            }
            return new E2EContext { Scenario = scenario, Role = role, ResultPath = resultPath, Timeout = timeout,
                Map = map, ExpectedClients = expectedClients, ServerAddress = serverAddress, DeviceToken = deviceToken, ExternallyLaunched = externallyLaunched,
                NetworkPort = networkPort, DiscoveryPort = discoveryPort, QuitOnFinish = quitOnFinish, LateRole = lateRole,
                Args = args == null || args.Count == 0 ? EmptyArgs : Snapshot(args) };
        }

        /// <summary>Снимок параметров: вызывающий не изменит словарь контекста задним числом.</summary>
        private static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string> source)
        {
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in source)
            {
                if (string.IsNullOrEmpty(pair.Key)) throw new ArgumentException("Пустой ключ параметра E2E.");
                copy[pair.Key] = pair.Value ?? string.Empty;
            }
            return new ReadOnlyDictionary<string, string>(copy);
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
              .Append(", индекс=").Append(ClientIndex)
              .Append(", таймаут=").Append(Timeout).Append("с")
              .Append(", порты=").Append(HasPorts ? $"KCP {NetworkPort} / Discovery {DiscoveryPort}" : "(по умолчанию сцены)")
              .Append(", результат=").Append(string.IsNullOrEmpty(ResultPath) ? "(не задан)" : ResultPath);
            if (LateRole != null)
                sb.Append(", поздний=").Append(LateRole).Append(IsLateClient ? " (этот процесс)" : string.Empty);
            if (QuitOnFinish)
                sb.Append(", выход по вердикту");
            if (HeadPoseEnabled)
                sb.Append(", поза головы=").Append(float.IsNaN(HeadPoseEye)
                    ? "calib"
                    : "eye=" + HeadPoseEye.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (Args.Count > 0)
            {
                sb.Append(", параметры=");
                bool first = true;
                foreach (KeyValuePair<string, string> pair in Args)
                {
                    if (!first) sb.Append(' ');
                    sb.Append(pair.Key).Append('=').Append(pair.Value);
                    first = false;
                }
            }
            if (PortError != null)
                sb.Append(", ошибка портов: ").Append(PortError);
            if (ArgError != null)
                sb.Append(", ошибка параметров: ").Append(ArgError);
            if (HeadPoseError != null)
                sb.Append(", ошибка позы головы: ").Append(HeadPoseError);
            if (TopologyError != null)
                sb.Append(", ошибка топологии: ").Append(TopologyError);
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

        /// <summary>
        /// Относительный путь результата — от <see cref="Application.persistentDataPath"/>: на шлеме дирижёр знает
        /// только каталог приложения, а рабочий каталог процесса Android не определён. Абсолютный путь — как есть.
        /// </summary>
        private static string ResolveResultPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return Path.Combine(Application.persistentDataPath, path);
        }

        /// <summary>
        /// Разбирает все <c>-e2eArg ключ=значение</c>. Возвращает текст ошибки или null. При ошибке словарь
        /// не применяется целиком: сценарий с частью параметров запускать нельзя.
        /// </summary>
        private static string ReadScenarioArgs(string[] args, out Dictionary<string, string> result)
        {
            result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-e2eArg", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (i + 1 >= args.Length)
                    return "-e2eArg без значения ключ=значение";

                string pair = args[++i] ?? string.Empty;
                int separator = pair.IndexOf('=');
                if (separator < 0)
                    return $"-e2eArg без '=': '{pair}'";
                if (separator == 0)
                    return $"-e2eArg с пустым ключом: '{pair}'";

                string key = pair.Substring(0, separator);
                if (result.ContainsKey(key))
                    return $"-e2eArg: ключ '{key}' задан повторно";
                result.Add(key, pair.Substring(separator + 1));
            }

            return null;
        }

        /// <summary>
        /// Разбирает <c>-e2eLate &lt;роль&gt;</c>. Флага нет — позднего клиента нет, без ошибки. Возвращает текст ошибки
        /// или null; роль обязана быть из client-1..client-<paramref name="clientCount"/>.
        /// </summary>
        private static string ReadLate(string[] args, int clientCount, out string lateRole)
        {
            lateRole = null;
            const string key = "-e2eLate";
            if (!Has(args, key))
                return null;

            string value = Read(args, key);
            if (string.IsNullOrEmpty(value) || value.StartsWith("-", StringComparison.Ordinal))
                return key + " без роли: нужно -e2eLate client-<N>";

            string error = E2EStepRules.ValidateClientRole(value, clientCount);
            if (error != null)
                return key + ": " + error;

            lateRole = value;
            return null;
        }

        /// <summary>
        /// Разбирает <c>-e2eHeadPose eye=&lt;м&gt;|calib</c>. Флага нет — симуляции нет, без ошибки. Возвращает текст
        /// ошибки или null; при ошибке симуляция выключена (<paramref name="enabled"/> = false).
        /// </summary>
        private static string ReadHeadPose(string[] args, out bool enabled, out float eye)
        {
            enabled = false;
            eye = float.NaN;
            const string key = "-e2eHeadPose";
            if (!Has(args, key))
                return null;

            string value = Read(args, key);
            if (string.IsNullOrEmpty(value) || value.StartsWith("-", StringComparison.Ordinal))
                return key + " без значения: нужно eye=<м> или calib";

            if (string.Equals(value, "calib", StringComparison.OrdinalIgnoreCase))
            {
                enabled = true;
                return null;
            }

            const string eyePrefix = "eye=";
            if (!value.StartsWith(eyePrefix, StringComparison.OrdinalIgnoreCase))
                return $"{key}: неизвестное значение '{value}' (нужно eye=<м> или calib)";

            string number = value.Substring(eyePrefix.Length);
            if (!float.TryParse(number, System.Globalization.NumberStyles.AllowDecimalPoint,
                                System.Globalization.CultureInfo.InvariantCulture, out float parsed) ||
                float.IsNaN(parsed) || float.IsInfinity(parsed))
                return $"{key}: высота глаз не число: '{number}'";

            float min = PhysicalSpaceUtils.PlayerCalibrationRules.MinEyeHeight;
            float max = PhysicalSpaceUtils.PlayerCalibrationRules.MaxEyeHeight;
            if (parsed < min || parsed > max)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}: высота глаз {1} м вне {2}..{3} м (PlayerCalibrationRules)", key, parsed, min, max);

            enabled = true;
            eye = parsed;
            return null;
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
