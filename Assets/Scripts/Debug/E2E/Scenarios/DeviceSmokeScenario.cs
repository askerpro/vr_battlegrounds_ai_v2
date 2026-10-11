// Ярус C (два процесса) — сценарий device-smoke: самопроверка каркаса прогона на шлеме (контракт quest-device-run).
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>device-smoke</c>: проверяет сам каркас прогона на устройстве, а не игровую логику.
    ///
    /// <para>
    /// <b>Топология.</b> Сервер — выделенный Windows-плеер на ПК (<c>-batchmode -nographics</c>), клиент — собранный
    /// плеер на шлеме, подключённый штатным путём игры: Discovery по LAN на изолированной паре портов прогона,
    /// после 30 с тишины — прямое подключение по <see cref="E2EContext.ServerAddress"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Вердикт.</b> Сервер: сеть поднята, пришло хотя бы одно клиентское подключение с сессией. Клиент: подключился
    /// к серверу (в detail — каким путём). С <c>-e2eArg artifact=1</c> каждая сторона пишет маленький файл
    /// <c>persistentDataPath/e2e-smoke/&lt;utc&gt;.txt</c> и объявляет его через <see cref="E2EResult.AddArtifact"/> —
    /// так проверяется сбор артефактов дирижёром с обеих сторон.
    /// </para>
    ///
    /// <para>Игровое состояние сценарий не меняет: только наблюдает за сетью и пишет свой файл.</para>
    /// </summary>
    public class DeviceSmokeScenario : IE2EScenario
    {
        public string Name => "device-smoke";

        private const string CheckServerUp = "сервер поднят";
        private const string CheckClientJoined = "клиент подключился";
        private const string CheckArtifact = "файл-артефакт записан и объявлен";

        /// <summary>Запас до общего таймаута исполнителя: вердикт сценария должен успеть записаться раньше.</summary>
        private const float TimeoutMargin = 10f;

        /// <summary>Сколько ждать Discovery до прямого подключения — как в остальных клиентских сценариях.</summary>
        private const float DiscoveryWait = 30f;

        /// <summary>Выдержка сервера после подключения: клиент успевает записать свой вердикт до выхода сервера.</summary>
        private const float ServerHold = 5f;

        private static float Now => E2EWait.Now;

        public IEnumerator Run(E2EContext context, E2EResult result) =>
            context.IsServerRole ? RunServer(context, result) : RunClient(context, result);

        // ── Сервер ───────────────────────────────────────────────────────────

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            bool wantArtifact = WantsArtifact(context);
            if (wantArtifact) result.Declare(CheckServerUp, CheckClientJoined, CheckArtifact);
            else result.Declare(CheckServerUp, CheckClientJoined);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            float deadline = Now + context.Timeout - TimeoutMargin;

            var up = new E2EWaitOutcome();
            yield return E2EWait.Until(up, "NetworkServer.active", Mathf.Min(60f, Remaining(deadline)),
                () => NetworkServer.active, DescribeServer);
            result.Set(CheckServerUp, up.Succeeded, up.Diagnosis);
            if (!up.Succeeded) { result.Summary = "сервер не поднялся"; yield break; }

            if (wantArtifact) WriteArtifact(context, result);

            int expected = Math.Max(1, context.ExpectedClients);
            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, $"клиентских подключений с сессией: {expected}", Remaining(deadline),
                () => ClientConnections() >= expected && SessionCount() >= expected,
                DescribeServer,
                () => NetworkServer.active ? null : "сервер погас");
            result.Set(CheckClientJoined, joined.Succeeded, joined.Diagnosis);
            if (!joined.Succeeded) { result.Summary = "клиент не подключился к серверу"; yield break; }

            // Сервер выходит по вердикту: дать клиенту записать свой до разрыва.
            yield return E2EWait.Hold(Mathf.Min(ServerHold, Remaining(deadline)));
        }

        /// <summary>Подключения удалённых клиентов (на выделенном сервере локального соединения нет).</summary>
        private static int ClientConnections()
        {
            int count = 0;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection != null && connection != NetworkServer.localConnection)
                    count++;
            }
            return count;
        }

        private static int SessionCount() => PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;

        private static string DescribeServer() =>
            $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}, " +
            $"подключений={ClientConnections()}, сессий={SessionCount()}";

        // ── Клиент ───────────────────────────────────────────────────────────

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            bool wantArtifact = WantsArtifact(context);
            if (wantArtifact) result.Declare(CheckClientJoined, CheckArtifact);
            else result.Declare(CheckClientJoined);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            float deadline = Now + context.Timeout - TimeoutMargin;

            if (wantArtifact) WriteArtifact(context, result);

            var discovered = new E2EWaitOutcome();
            yield return E2EWait.Until(discovered, "подключение через Discovery", Mathf.Min(DiscoveryWait, Remaining(deadline)),
                () => NetworkClient.isConnected, DescribeClient);

            string via = "Discovery";
            if (!discovered.Succeeded && !string.IsNullOrEmpty(context.ServerAddress))
            {
                via = $"прямое подключение к {context.ServerAddress}";
                GameLog.Debug.Info($"[E2E] Discovery молчит {DiscoveryWait:F0} с, подключаюсь напрямую к {context.ServerAddress}");
                ConnectDirectly(context.ServerAddress);
            }

            // Транспортного соединения мало: сервер считает игрока по сессии. Клиент, вышедший раньше (-e2eQuitOnFinish),
            // оставит сервер без подключения — поэтому ждём свою PlayerSession и держим соединение ещё ClientHold секунд.
            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, "своя PlayerSession на клиенте", Remaining(deadline),
                () => NetworkClient.isConnected && PlayerSession.LocalSession != null, DescribeClient);
            result.Set(CheckClientJoined, joined.Succeeded, $"путь: {via}. {joined.Diagnosis}");
            if (!joined.Succeeded)
            {
                result.Summary = "клиент не получил сессию на сервере";
                yield break;
            }

            yield return E2EWait.Hold(Mathf.Min(ClientHold, Remaining(deadline)));
        }

        /// <summary>Сколько клиент держит соединение после своей сессии, чтобы сервер успел её засчитать.</summary>
        private const float ClientHold = 2f;

        /// <summary>
        /// Прямое подключение мимо Discovery — через владельца сети (<see cref="GameNetworkDiscovery.RequestClientConnect"/>),
        /// чтобы шов подготовки сети применил пару портов прогона. Прямой StartClient ушёл бы на порт сцены.
        /// </summary>
        private static void ConnectDirectly(string address)
        {
            GameNetworkDiscovery owner = NetworkManager.singleton != null ? NetworkManager.singleton.GetComponent<GameNetworkDiscovery>() : null;
            if (owner == null || !GameNetworkDiscovery.ClientManagerReady)
            {
                GameLog.Error("[E2E] Владелец сети не готов к подключению — прямой путь недоступен.");
                return;
            }

            if (NetworkClient.active)
                return;

            try { owner.RequestClientConnect(address); }
            catch (InvalidOperationException e) { GameLog.Error($"[E2E] Прямое подключение отклонено владельцем сети: {e.Message}"); }
        }

        private static string DescribeClient()
        {
            NetworkManager manager = NetworkManager.singleton;
            return $"адрес менеджера={(manager != null ? manager.networkAddress : "менеджера нет")}, " +
                   $"NetworkClient.active={NetworkClient.active}, isConnected={NetworkClient.isConnected}";
        }

        // ── Артефакт ─────────────────────────────────────────────────────────

        private static bool WantsArtifact(E2EContext context) => context.Arg("artifact") == "1";

        /// <summary>Пишет маленький файл в persistentDataPath/e2e-smoke/ и объявляет его артефактом.</summary>
        private static void WriteArtifact(E2EContext context, E2EResult result)
        {
            DateTime now = DateTime.UtcNow;
            string path = Path.Combine(Application.persistentDataPath, "e2e-smoke",
                now.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + ".txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string text = $"scenario={context.Scenario}\nrole={context.Role}\nutc={now.ToString("o", CultureInfo.InvariantCulture)}\n";
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                result.Set(CheckArtifact, false, $"запись '{path}' не удалась: {e.GetType().Name}: {e.Message}");
                return;
            }

            bool accepted = result.AddArtifact(path);
            result.Set(CheckArtifact, accepted, accepted ? path : $"файл записан, но AddArtifact отклонил '{path}'");
        }

        private static float Remaining(float deadline) => Mathf.Max(0f, deadline - Now);
    }
}
#endif
