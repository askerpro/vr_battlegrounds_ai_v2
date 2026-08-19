namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Порядок инициализации менеджеров — <b>единственное место, где он записан</b>.
    ///
    /// <para>
    /// Числа отсюда подставляются в <c>[DefaultExecutionOrder]</c> на самих менеджерах.
    /// Меньше — раньше. Unity гарантирует такой порядок <c>Awake</c>, поэтому очередь
    /// перестаёт зависеть от того, в каком порядке объекты лежат в префабе
    /// <c>--- MANAGERS ---</c> и в каком Unity их подхватил.
    /// </para>
    ///
    /// <para>
    /// Почему константы, а не список объектов: менеджеры уже лежат готовыми компонентами
    /// на префабе, их создаёт Unity при загрузке сцены <c>Offline</c>. Компонент-дирижёр
    /// не может «создать их в нужном порядке» — он может только объявить порядок и
    /// проверить, что тот соблюдён. Проверку делает <see cref="ManagerBootstrap" />.
    /// </para>
    ///
    /// <para>
    /// Правило при добавлении менеджера: сначала строка здесь, потом атрибут на классе.
    /// Значения раздвинуты с шагом 10, чтобы вставка в середину не требовала пересчёта.
    /// </para>
    ///
    /// Времена жизни каждого менеджера — таблицей в <c>Docs/session-architecture.md</c>.
    /// </summary>
    public static class ManagerOrder
    {
        // ── Постоянные менеджеры: префаб «--- MANAGERS ---», сцена Offline ──────
        //
        // Живут от запуска процесса до его конца: PersistentRoot делает
        // DontDestroyOnLoad всей ветке, поэтому смена сцены их не трогает.

        /// <summary>Корень. Первым: он делает <c>DontDestroyOnLoad</c>, от которого зависят все ниже.</summary>
        public const int PersistentRoot = -2000;

        /// <summary>Реестр сессий. Раньше сети: её колбэки зовут его сразу на подключении.</summary>
        public const int PlayersManager = -1900;

        /// <summary>Снимки отключившихся. Раньше <see cref="PlayersManager" />-потребителя, но после него самого не нужен.</summary>
        public const int SessionRecoveryManager = -1890;

        /// <summary>Спавн и горячая замена аватаров. Зовётся из тех же сетевых колбэков.</summary>
        public const int AvatarManager = -1880;

        /// <summary>Смена карт. Должен быть готов до первого <c>LoadMap</c> из меню или отладки.</summary>
        public const int MapManager = -1870;

        /// <summary>Калибровка физического пространства. Ни от кого не зависит, но её результат читает сессия.</summary>
        public const int PhysicalSpaceSyncManager = -1860;

        /// <summary>
        /// Mirror. Строго после всех, кого дёргает из своих колбэков: <c>OnServerReady</c>
        /// зовёт <see cref="PlayersManager" /> и <see cref="AvatarManager" />, а прилететь
        /// он может в том же кадре, что и старт сервера.
        /// </summary>
        public const int GameNetworkManager = -1800;

        /// <summary>Дирижёр отладки. Последний из постоянных: он потребитель, а не поставщик.</summary>
        public const int DebugOrchestrator = -1700;

        // ── Сетевые менеджеры: спавнятся сервером в рантайме ────────────────────
        //
        // Порядок между ними задаётся не только атрибутом, но и порядком спавна:
        // объект появляется на клиенте тогда, когда его прислал сервер. Атрибут
        // разводит их Awake внутри одного кадра.

        /// <summary>Выбор карты и режима. Спавнится в <c>GameNetworkManager.OnStartServer</c>, живёт до остановки сервера.</summary>
        public const int SessionManager = -1600;

        /// <summary>Канал состояния UltimateXR. Тот же объект <c>SessionContext</c>, что и <see cref="SessionManager" />.</summary>
        public const int NetworkStateRelay = -1590;

        /// <summary>
        /// Оркестратор матча. Живёт в сценах карт, а не в <c>PersistentRoot</c>: режим
        /// существует только на карте. Значит <c>Instance</c> равен null всё время,
        /// пока игрок в лобби, — это норма, а не сбой.
        /// </summary>
        public const int GameplayManager = -1500;
    }
}
