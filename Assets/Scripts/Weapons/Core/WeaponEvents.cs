namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>
    /// События машины (план п. 3.4). Источник каждого — датчик или порт хоста; машина событий не придумывает.
    /// <see cref="LedgerFaulted"/> — событие <c>ReadinessFaulted</c> SDK (в списке плана упомянуто в T80).
    /// </summary>
    public enum WeaponEventKind
    {
        Configured, SnapshotLoaded, LedgerCommitted, LedgerFaulted, CommandRejected, Tick, ActionSampled,
        HandleGrabbed, HandleReleased, MagazineChanged, TriggerPressed, TriggerHeld, TriggerReleased,
        ContextLost, AuthorityChanged, CartridgeOffered, AdmissionResolved, AutomationPrepareRequested, Disabled
    }

    /// <summary>
    /// Одно событие. Поля, кроме <see cref="Kind"/>, осмысленны только для своих видов:
    /// <see cref="Op"/>/<see cref="OpChamberBefore"/>/<see cref="OpChamberAfter"/> — для LedgerCommitted,
    /// <see cref="Dt"/> — для Tick, <see cref="Token"/> — для CartridgeOffered, <see cref="Rejected"/> — для CommandRejected.
    ///
    /// <see cref="TriggerPressed"/> — фронт нажатия: каждое такое событие — новое нажатие.
    /// <see cref="TriggerHeld"/> — уровень, раз в кадр, пока спуск нажат.
    /// </summary>
    public readonly struct WeaponEvent
    {
        public readonly WeaponEventKind Kind;
        public readonly LedgerOp Op;
        public readonly bool OpChamberBefore, OpChamberAfter;
        public readonly float Dt;
        public readonly ulong Token;
        public readonly LedgerCommandKind Rejected;

        public WeaponEvent(WeaponEventKind kind, LedgerOp op = LedgerOp.None, bool opChamberBefore = false, bool opChamberAfter = false,
            float dt = 0f, ulong token = 0, LedgerCommandKind rejected = default)
        {
            Kind = kind; Op = op; OpChamberBefore = opChamberBefore; OpChamberAfter = opChamberAfter;
            Dt = dt; Token = token; Rejected = rejected;
        }

        public static WeaponEvent Of(WeaponEventKind kind) => new WeaponEvent(kind);
        public static WeaponEvent Tick(float dt) => new WeaponEvent(WeaponEventKind.Tick, dt: dt);
        public static WeaponEvent Committed(LedgerOp op, bool chamberBefore, bool chamberAfter) =>
            new WeaponEvent(WeaponEventKind.LedgerCommitted, op, chamberBefore, chamberAfter);
        public static WeaponEvent Offered(ulong token) => new WeaponEvent(WeaponEventKind.CartridgeOffered, token: token);
        public static WeaponEvent RejectedCommand(LedgerCommandKind kind) => new WeaponEvent(WeaponEventKind.CommandRejected, rejected: kind);
    }

    /// <summary>
    /// Замер ручного хода (датчик Action). Прогрессы — в долях хода покой→зад.
    /// <see cref="Held"/> — ручку держит любая рука (поза следует руке на каждой машине);
    /// <see cref="HeldByAuthorMainContext"/> — держит локальная рука того, кто держит основную рукоять (жест автора).
    /// </summary>
    public readonly struct ActionSample
    {
        public readonly bool Held, HeldByAuthorMainContext;
        public readonly float HandleProgress, MinRequiredProgress;
        public readonly bool AllAtRest, AtValidatedRear;

        public ActionSample(bool held, bool heldByAuthorMainContext, float handleProgress, float minRequiredProgress,
            bool allAtRest, bool atValidatedRear)
        {
            Held = held; HeldByAuthorMainContext = heldByAuthorMainContext; HandleProgress = handleProgress;
            MinRequiredProgress = minRequiredProgress; AllAtRest = allAtRest; AtValidatedRear = atValidatedRear;
        }

        /// <summary>Ручка в покое и не в руке. Единственный замер оружия без хода (NoAction).</summary>
        public static ActionSample AtRest => new ActionSample(false, false, 0f, 0f, true, false);
    }

    public enum WeaponRole { Author, Observer }

    /// <summary>Почему SDK запрещает использование (<c>CanUse=false</c>).</summary>
    public enum WeaponBlockReason { None, Obstructed, Other }

    /// <summary>
    /// Контекст шага. <see cref="Role"/> — по <c>StateEventAuthority.IsAuthorOfItem</c>;
    /// <see cref="MainGripLocal"/> — основную рукоять держит локальная рука автора («контекст автора»);
    /// <see cref="InsideReplay"/> — шаг внутри replay state sync (машина ведёт себя как наблюдатель);
    /// <see cref="RofTimer"/> — остаток таймера темпа SDK; <see cref="IsWorldAuthority"/> — сервер/офлайн (бот).
    /// </summary>
    public readonly struct WeaponContext
    {
        public readonly WeaponRole Role;
        public readonly bool MainGripLocal, InsideReplay, IsWorldAuthority;
        public readonly WeaponBlockReason Blocked;
        public readonly float RofTimer;

        public WeaponContext(WeaponRole role, bool mainGripLocal, bool insideReplay = false,
            WeaponBlockReason blocked = WeaponBlockReason.None, float rofTimer = 0f, bool isWorldAuthority = false)
        {
            Role = role; MainGripLocal = mainGripLocal; InsideReplay = insideReplay; Blocked = blocked;
            RofTimer = rofTimer; IsWorldAuthority = isWorldAuthority;
        }

        /// <summary>Шаг идёт как у автора: только тогда таблица допускает команды учёту (И9).</summary>
        public bool ActsAsAuthor => Role == WeaponRole.Author && !InsideReplay;
    }
}
