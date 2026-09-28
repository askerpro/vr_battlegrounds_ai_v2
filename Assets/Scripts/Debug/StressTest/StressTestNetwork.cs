using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Сетевой вход стресс-теста: регистрация сообщений и запросы с клиента.
    /// Регистрирует <c>GameNetworkManager</c> в своих <c>OnStartServer</c> /
    /// <c>OnStartClient</c> — Mirror чистит обработчики при каждой остановке сети.
    /// </summary>
    public static class StressTestNetwork
    {
        public static void RegisterServerHandlers()
        {
            NetworkServer.ReplaceHandler<StressTestRequestMessage>(StressTestServer.HandleRequest);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.ReplaceHandler<StressTestStatusMessage>(StressTestClientSession.HandleStatus);
        }

        /// <summary>Попросить сервер запустить прогон, куклы которого повторяют за этим клиентом.</summary>
        public static bool RequestStart(StressTestConfig config, out string reason)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
            {
                reason = "нет подключения к серверу";
                return false;
            }

            if (StressTestClientSession.IsRunning)
            {
                reason = "стресс-тест уже идёт";
                return false;
            }

            NetworkClient.Send(StressTestRequestMessage.Start(config ?? new StressTestConfig()));
            GameLog.Perf.Info("[StressTest] Запрос на старт отправлен серверу.");
            reason = null;
            return true;
        }

        public static void RequestStop()
        {
            if (!NetworkClient.isConnected) return;
            NetworkClient.Send(new StressTestRequestMessage { start = false });
            GameLog.Perf.Info("[StressTest] Запрос на остановку отправлен серверу.");
        }
    }
}
