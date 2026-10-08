namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>Команды учёту SDK. Порт хоста переводит их в <c>Try*</c>-методы <c>UxrFirearmWeapon</c>.</summary>
    public enum LedgerCommandKind
    {
        Initialize, BeginAction, Extract, CompleteChamber, CloseOnly, AckEmptyRest, Cancel, Shoot,
        RefillForAutomation, RequestAdmission
    }

    /// <summary>Кто начал цикл досылания. Хранит только машина автора; не реплицируется.</summary>
    public enum ChamberOrigin { None, Manual, Insert, Assist, Automation }

    /// <summary>
    /// Команда учёту. <see cref="ExpectedRevision"/> — revision, на которую рассчитана команда: SDK
    /// отклоняет команду, если учёт успел измениться. Вторая команда того же шага рассчитана на
    /// revision после первой; если первая отклонена, SDK отклонит и вторую.
    /// </summary>
    public readonly struct LedgerCommand
    {
        public readonly LedgerCommandKind Kind;
        public readonly uint ExpectedRevision, CycleSequence;
        public readonly int ExpectedMagazineToken;
        public readonly ulong Token;
        public readonly ChamberOrigin Origin;

        public LedgerCommand(LedgerCommandKind kind, uint expectedRevision, uint cycleSequence = 0, int expectedMagazineToken = 0,
            ulong token = 0, ChamberOrigin origin = ChamberOrigin.None)
        {
            Kind = kind; ExpectedRevision = expectedRevision; CycleSequence = cycleSequence;
            ExpectedMagazineToken = expectedMagazineToken; Token = token; Origin = origin;
        }
    }

    /// <summary>
    /// Причина отказа спуска. Порт переводит NoMagazine, EmptyMagazine и ChamberingRequired в <c>UxrFirearmNotReadyReason</c>.
    /// <see cref="RateOfFire"/> — нажатие до конца таймера темпа (решение S2): не про патроны, отдельный отказ
    /// (<see cref="WeaponCue.Refusal"/>), без подсказки.
    /// </summary>
    public enum WeaponNotReadyReason { None, NoMagazine, EmptyMagazine, ChamberingRequired, Obstructed, Faulted, RateOfFire }

    /// <summary>
    /// Звуки механизма (план п. 4.2). Вставки и выстрела здесь нет и быть не может: у них другие
    /// владельцы (<c>AnchorSound</c>, SDK) — класс ошибок 2.
    /// <see cref="DryFire"/> — щелчок отказа по патронам/препятствию/сбою; <see cref="Refusal"/> — искусственный
    /// звук отказа, не щелчок (S2: таймер темпа; звук <c>UI_Error_Subtle_Deep</c> подключается на этапе D).
    /// </summary>
    public enum WeaponCue { ActionBack, ActionForwardChambered, ActionForwardEmpty, ChamberEjected, SlideLockCatch, DryFire, Refusal }

    /// <summary>
    /// Вибрации оружия. Решение пользователя: все вибрации оружия, включая отдачу выстрела, ведёт WeaponSystem;
    /// исполняет их сервис вибрации <c>VrBattlegrounds.Haptics.HapticService</c> (worktree haptics). Сигнал
    /// выстрела добавляется на этапе D, до тех пор вибрация выстрела остаётся у SDK.
    /// <see cref="RateOfFire"/> — отрицательный класс (отказ без отклика о патронах, S2).
    /// </summary>
    public enum WeaponHapticCue { ActionRear, NotReady, Obstructed, Faulted, RateOfFire }

    /// <summary>
    /// Цель позы ручки (когда её не держат) и связанных Action-деталей (план п. 3.6).
    /// <see cref="Stay"/> — Action остаётся там, где его отпустила рука: исполнитель его не двигает
    /// (ось <see cref="WeaponReleasedAction.Stay"/>, решение S4).
    /// </summary>
    public enum PosePresentation { Rest, FollowHand, FireClip, EmptyClip, HoldRear, ReturnToRest, Stay }

    /// <summary>Цель позы нерычажных деталей (курок, барабан и т. п.).</summary>
    public enum AuxiliaryPose { Rest, FireClip, EmptyClip }

    /// <summary>
    /// Цель позы на кадр — функция состояния машины, учёта и замера (<see cref="WeaponStateMachine.ComputePose"/>).
    /// Исполнитель не хранит своей фазы и не решает, куда двигать детали.
    /// <see cref="ClipTime"/> — время текущего клипа (для FireClip/EmptyClip у Action и нерычажных деталей);
    /// HoldRear берёт проверенную заднюю позу rig. <see cref="ReturnSpeed"/> — только для ReturnToRest.
    /// </summary>
    public readonly struct PoseTarget
    {
        public readonly PosePresentation Action;
        public readonly AuxiliaryPose Auxiliary;
        public readonly float ClipTime;
        public readonly float ReturnSpeed;

        public PoseTarget(PosePresentation action, AuxiliaryPose auxiliary, float clipTime, float returnSpeed)
        {
            Action = action; Auxiliary = auxiliary; ClipTime = clipTime; ReturnSpeed = returnSpeed;
        }
    }

    /// <summary>Диагностика для лога хоста (у машины нет <c>GameLog</c>: сборка без Unity).</summary>
    public enum WeaponReportKind
    {
        /// <summary>Учёт отклонил команду (T35) — хост пишет <c>GameLog.WeaponSystem.Warning</c>.</summary>
        CommandRejected,
        /// <summary>Сбой учёта (T80) — <c>GameLog.WeaponSystem.Error</c>.</summary>
        LedgerFaulted,
        /// <summary>Автор просит восстановить учёт после сбоя (В8).</summary>
        ResyncRequested,
        /// <summary>Ошибка таблицы: строка без права выдала команду или вывод автора. Тесты держат счётчик нулевым.</summary>
        TableViolation,
        /// <summary>Ни одна строка не подошла (дыра в таблице). Тест полноты держит это невозможным.</summary>
        Unhandled
    }

    public readonly struct WeaponReport
    {
        public readonly WeaponReportKind Kind;
        public readonly string RowId;
        public readonly LedgerCommandKind Command;

        public WeaponReport(WeaponReportKind kind, string rowId, LedgerCommandKind command = default)
        {
            Kind = kind; RowId = rowId; Command = command;
        }
    }

    /// <summary>
    /// Выход машины. Реализует хост. Вызовы приходят внутри <see cref="WeaponStateMachine.Step"/>;
    /// хост не вызывает <c>Step</c> из этих методов (повторный вход запрещён) — события, порождённые
    /// выполнением команды (фиксация, отказ), он ставит в очередь и подаёт после шага.
    /// </summary>
    public interface IWeaponOutput
    {
        /// <summary>Команда учёту. Только автор вне replay (И9).</summary>
        void Command(in LedgerCommand command);

        /// <summary>Ровно один раз за шаг (И13).</summary>
        void Pose(in PoseTarget target);

        /// <summary>Звук механизма. DryFire и Refusal — только автор.</summary>
        void Cue(WeaponCue cue, WeaponNotReadyReason reason);

        /// <summary>Вибрация. Только автор.</summary>
        void Haptic(WeaponHapticCue cue);

        /// <summary>Подсказка «дошли патрон» (подсветка Action). Включение — только автор.</summary>
        void Hint(bool on);

        void Report(in WeaponReport report);
    }
}
