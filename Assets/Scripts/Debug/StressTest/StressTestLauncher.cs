using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Запуск и остановка стресс-теста с этой машины — общий вход для планшета (экран «Перф-тесты»,
    /// <c>MenuPerfTests</c>) и меню редактора (<c>StressTestMenu</c>). Конфиг и план фаз —
    /// <see cref="StressTestPlan"/>.
    ///
    /// <para>
    /// Раньше здесь жил жест «оба стика 2 с» (+ грипы — по скинам). Его заменил планшет: жест
    /// нельзя было ни настроить (скин, режим, число кукол), ни увидеть. Удержание обоих стиков
    /// теперь включает режим отладки; шлем без ПК тем же жестом при включённом режиме становится
    /// хостом (<see cref="DebugGestureInput"/>), дальше — планшет.
    /// </para>
    /// </summary>
    public static class StressTestLauncher
    {
        /// <summary>Сколько фаз ждать в запущенном отсюда прогоне (по <see cref="StressTestPlan"/>); 0 — неизвестно.</summary>
        public static int PlannedPhaseCount { get; private set; }

        /// <summary>Описание прогона для таблички и лога.</summary>
        public static string Describe(StressTestConfig config) =>
            (config.perSkinPhases ? "по скинам" : config.mapOnly ? "только по карте" : "обычный") +
            (config.phaseSeconds <= StressTestPlan.ShortPhaseSeconds ? ", короткий" : "");

        /// <summary>
        /// Просит сервер начать прогон. Ложь и причина — если просить некого (нет подключения,
        /// выделенный сервер без клиента) или прогон уже идёт. Отказ самого сервера (не
        /// разминка, нет аватара) придёт позже сообщением — <see cref="StressTestClientSession.LastServerText"/>.
        /// </summary>
        public static bool TryStart(StressTestConfig config, out string message)
        {
            if (!NetworkClient.isConnected && NetworkServer.active)
            {
                message = "выделенный сервер без клиента — запускать с шлема";
                return false;
            }

            if (!StressTestNetwork.RequestStart(config, out string reason))
            {
                message = reason;
                return false;
            }

            PlannedPhaseCount = StressTestPlan.Phases(config, StressTestServer.CollectAvatarPrefabs().Count).Count;
            message = $"Стресс-тест ({Describe(config)}): запрос отправлен серверу.";
            PerfOverlay.Show(message, 4f);
            return true;
        }

        public static void Stop()
        {
            if (!StressTestClientSession.IsRunning) return;
            GameLog.Perf.Info("[StressTest] Остановка по запросу с этой машины.");
            StressTestNetwork.RequestStop();
        }
    }
}
