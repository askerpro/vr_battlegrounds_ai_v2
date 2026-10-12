namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Итог одного прогона на этой машине — снимок только для чтения. Пишет его единственный владелец
    /// прогона в своём <c>Finish</c>: клиентская половина (<see cref="StressTestClientSession.LastRun"/>) или
    /// выделенный сервер (<see cref="StressTestServer.LastRun"/>). Читают наблюдатели (сценарий e2e
    /// <c>perf-stress</c>), ничего в прогоне не меняя.
    /// </summary>
    public sealed class StressTestRunRecord
    {
        /// <summary>Каталог прогона <c>persistentDataPath/perf/&lt;время&gt;_&lt;роль&gt;_N</c>; пусто — своего отчёта нет (хост на сервере).</summary>
        public string Directory { get; }

        /// <summary>Сервер дошёл до конца плана фаз; ложь — прогон прерван.</summary>
        public bool Completed { get; }

        /// <summary>Причина конца: «полный прогон» или причина обрыва.</summary>
        public string EndReason { get; }

        /// <summary>Сколько фаз открыто за прогон, включая разгон и успокоения.</summary>
        public int PhasesSeen { get; }

        public StressTestRunRecord(string directory, bool completed, string endReason, int phasesSeen)
        {
            Directory  = directory ?? string.Empty;
            Completed  = completed;
            EndReason  = endReason ?? string.Empty;
            PhasesSeen = phasesSeen;
        }

        public override string ToString() =>
            $"{(Completed ? "полный" : "прерван")}: {EndReason}; фаз открыто {PhasesSeen}; каталог «{Directory}»";
    }
}
