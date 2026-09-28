using Mirror;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Клиент → сервер: запустить или остановить стресс-тест, куклы которого повторяют
    /// за отправителем. Параметры — примитивами: их сериализует weaver Mirror.
    /// </summary>
    public struct StressTestRequestMessage : NetworkMessage
    {
        public bool  start;
        public int   puppetCount;
        public int   clutterCount;
        public float warmupSeconds;
        public float phaseSeconds;
        public float settleSeconds;

        public static StressTestRequestMessage Start(StressTestConfig config) => new StressTestRequestMessage
        {
            start         = true,
            puppetCount   = config.puppetCount,
            clutterCount  = config.clutterCount,
            warmupSeconds = config.warmupSeconds,
            phaseSeconds  = config.phaseSeconds,
            settleSeconds = config.settleSeconds,
        };

        public StressTestConfig ToConfig() => new StressTestConfig
        {
            puppetCount   = puppetCount,
            clutterCount  = clutterCount,
            warmupSeconds = warmupSeconds,
            phaseSeconds  = phaseSeconds,
            settleSeconds = settleSeconds,
        };
    }

    public enum StressTestStatusKind : byte
    {
        /// <summary>Началась фаза — клиент открывает её в своём логе.</summary>
        Phase,
        /// <summary>Прогон окончен (полностью или прерван).</summary>
        Finished,
        /// <summary>Сервер отказал в старте, причина в <c>text</c>.</summary>
        Rejected,
    }

    /// <summary>
    /// Сервер → инициатор: ход прогона. Фазами управляет сервер (он спавнит нагрузку),
    /// а меряет клиент — поэтому клиент открывает и закрывает фазы по этим сообщениям.
    /// </summary>
    public struct StressTestStatusMessage : NetworkMessage
    {
        public StressTestStatusKind kind;
        public string phase;
        public bool   measured;
        public float  seconds;
        public int    puppetCount;
        public int    clutterCount;
        public bool   completed;
        public string text;
    }
}
