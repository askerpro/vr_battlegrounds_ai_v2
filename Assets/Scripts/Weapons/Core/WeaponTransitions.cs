using System;
using System.Collections.Generic;
using static VrBattlegrounds.Weapons.Core.MechanismSet;
using static VrBattlegrounds.Weapons.Core.WeaponEventKind;

namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>Для каких ролей строка таблицы. Роль шага — <see cref="WeaponContext.ActsAsAuthor"/>.</summary>
    [Flags]
    public enum RoleSet { None = 0, Author = 1, Observer = 2, Both = Author | Observer }

    /// <summary>
    /// Строка таблицы переходов: из каких механических состояний, по какому событию, для каких ролей,
    /// при каком условии и что делает. <see cref="Apply"/> = null — явное «игнорировать» с причиной.
    /// <see cref="MayCommand"/> — строке разрешено выдавать команды учёту; такая строка только для автора (И9).
    /// </summary>
    public sealed class TransitionRow
    {
        public readonly string Id;
        public readonly MechanismSet From;
        public readonly WeaponEventKind On;
        public readonly RoleSet Roles;
        public readonly Func<WeaponStateMachine, bool> Guard;
        public readonly Action<WeaponStateMachine> Apply;
        public readonly bool MayCommand;
        public readonly string Doc;

        internal TransitionRow(string id, MechanismSet from, WeaponEventKind on, RoleSet roles,
            Func<WeaponStateMachine, bool> guard, Action<WeaponStateMachine> apply, bool mayCommand, string doc)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(doc) || from == MechanismSet.None || roles == RoleSet.None)
                throw new ArgumentException($"Строка {id}: нужны Id, причина, состояния и роли.");
            if (mayCommand && roles != RoleSet.Author)
                throw new ArgumentException($"Строка {id}: команды учёту выдаёт только автор (И9).");
            Id = id; From = from; On = on; Roles = roles; Guard = guard; Apply = apply; MayCommand = mayCommand; Doc = doc;
        }

        /// <summary>Строка без условия закрывает свою клетку (состояние × событие × роль).</summary>
        public bool IsTerminal => Guard == null;
        public bool IsIgnore => Apply == null;

        public bool Covers(MechanismState state, WeaponEventKind on, WeaponRole role) =>
            On == on && (From & WeaponMechanism.Bit(state)) != 0 &&
            (Roles & (role == WeaponRole.Author ? RoleSet.Author : RoleSet.Observer)) != 0;

        public override string ToString() => $"{Id} [{From} × {On} × {Roles}] {Doc}";
    }

    /// <summary>
    /// Таблица переходов оружия — единственное место, где описано, что машина делает в ответ на событие
    /// (план п. 3.5; образец — <c>RoundPhases.Transitions</c>). Читается сверху вниз по событиям.
    /// Внутри одного события побеждает первая подошедшая строка; поэтому строки с условием стоят выше
    /// строки-«иначе» своей клетки. Для каждой клетки (состояние × событие × роль) есть строка без условия —
    /// это проверяет тест полноты (И8). Новое механическое состояние таблица не пишет: его выводит Derive
    /// из следующей фиксации учёта.
    ///
    /// Номера T01–T82 — номера плана. Строки, которых в плане нет (уточнения этапа B), помечены в Doc «+B».
    /// </summary>
    public static class WeaponTransitions
    {
        public static readonly TransitionRow[] Table =
        {
            // ── Инициализация, снимок, выключение ─────────────────────────────────────────────
            Cmd("T01", Uninitialized, Configured,
                null, m => { m.ResetSession(TriggerEpisode.Armed); m.Initialize(); },
                "Однократная миграция: Initialize; SDK сам решает prepared = M>0 ∧ Action в покое."),
            Local("T05", Any, Configured, RoleSet.Both,
                null, m => m.ResetSession(TriggerEpisode.Armed),
                "Учёт уже инициализирован или шаг наблюдателя: миграции нет, локальные регионы с нуля (T02)."),
            Local("T03", Any, SnapshotLoaded, RoleSet.Both,
                null, m => m.ResetSession(TriggerEpisode.Consumed),
                "Снимок: жест сброшен, нажатие израсходовано, подсказка выключена, показ выводится заново (И11)."),
            Cmd("T04", Cycle, Disabled,
                null, m => { m.Cancel(); m.ResetSession(TriggerEpisode.Consumed); },
                "Выключение посреди цикла: Cancel, локальные регионы с нуля."),
            Local("T04b", Any, Disabled, RoleSet.Both,
                null, m => m.ResetSession(TriggerEpisode.Consumed),
                "Выключение: локальные регионы с нуля, показ выводится заново (И11)."),

            // ── Магазин ───────────────────────────────────────────────────────────────────────
            Cmd("T11", EmptyLike, MagazineChanged,
                m => m.InsertChambers && m.Ctx && m.CanBegin, m => m.BeginInsert(),
                "AutoOnMagazineInsert, M>0: BeginAction(Insert); Action в покое — CompleteChamber в том же шаге."),
            Cmd("T14", OpenIdle, MagazineChanged,
                m => m.InsertChambers && !m.L.Chamber && m.Ctx && m.CanBegin, m => m.BeginInsertFromOpen(),
                "Открытый Action после отменённого цикла: новый BeginAction(Insert), а не оживление старого."),
            Local("T15", EmptyLike | OpenIdle, MagazineChanged, RoleSet.Both,
                m => m.InsertChambers && !(m.Role == WeaponRole.Author && m.Ctx), m => m.DeferInsert(),
                "+B. Вставка без контекста автора (оружие не в руке, шаг наблюдателя) не теряется: досылание отложено до T15s."),
            Local("T13", OpenIdle | Cycle, MagazineChanged, RoleSet.Author,
                null, m => m.ResetGesture(),
                "Цикл начат с другим магазином — Pending недействителен (C2, вывод без фиксации): жест цикла сгорает."),
            Ignore("T10", Ready, MagazineChanged, RoleSet.Author,
                "Патрон в патроннике сохраняется; фиксации нет — магазин учёт читает из гнезда (C2)."),
            Ignore("T12", EmptyLike, MagazineChanged, RoleSet.Author,
                "Иная политика или M=0: причина отказа пересчитывается из учёта, подсказку снимает PostStep."),
            Ignore("T16", Uninitialized | Faulted, MagazineChanged, RoleSet.Author,
                "До инициализации и в сбое команд нет."),
            Ignore("T17", Any, MagazineChanged, RoleSet.Observer,
                "Наблюдатель получит фиксацию автора."),

            // ── Замер хода: цикл (Pending) ────────────────────────────────────────────────────
            Ignore("T09", Uninitialized | Faulted, ActionSampled, RoleSet.Author,
                "До инициализации и в сбое команды запрещены (T80)."),
            Ignore("T34", Live, ActionSampled, RoleSet.Author,
                m => m.L.AdmissionPending,
                "Барьер приёма патрона: учёт не принимает команд — ждём, жест не сгорает (Н6). Досылание — T71 или следующий замер."),
            Cmd("T15s", Live, ActionSampled,
                m => m.DeferredInsertNow != 0 && m.Ctx, m => m.ConsumeDeferredInsert(),
                "+B. Контекст появился: отложенная вставка того же магазина досылает (T11/T14)."),
            Cmd("T32s", Cycle, ActionSampled,
                m => !m.Ctx || !m.OwnsCurrentCycle, m => m.CancelForeignCycle(),
                "+B (T32/T33 на замере). Цикл не наш (снимок, другой автор, чужая фиксация) или нет контекста: Cancel."),
            Cmd("T42", Cycle, ActionSampled,
                m => m.AutoOrigin && m.S.AllAtRest, m => m.Complete(),
                "Подготовка (Insert/Assist/Automation): Action вернулся в покой — CompleteChamber."),
            Ignore("T41", Cycle, ActionSampled, RoleSet.Author,
                m => m.AutoOrigin,
                "Подготовка: Action возвращается (цель позы ReturnToRest с AutoReturnSpeed; в руке — FollowHand, T43)."),
            Cmd("T25", Cycle, ActionSampled,
                m => m.GestureNow == GestureState.Pulling && m.PastExtractionGate, m => m.PassGate(),
                "Порог извлечения пройден после хода назад: Extract (один на цикл, И6), жест PastGate, вибрация зада."),
            Cmd("T27", Cycle, ActionSampled,
                m => m.GestureNow == GestureState.Pulling && m.S.AllAtRest, m => m.CancelAndClose(),
                "Action вернулся в покой до порога: Cancel + CloseOnly, патрон в патроннике сохранён."),
            Cmd("T26", Cycle, ActionSampled,
                m => m.GestureNow == GestureState.PastGate && m.S.AllAtRest, m => m.Complete(),
                "Action в покое после порога: CompleteChamber (подача, если M>0)."),
            Cmd("T29", Cycle, ActionSampled,
                m => m.GestureNow == GestureState.Released && m.S.AllAtRest, m => m.FinishReleased(),
                "Ручку отпустили, пружина вернула Action: как T26, если порог пройден, иначе как T27 (В7)."),
            Ignore("T24c", Cycle, ActionSampled, RoleSet.Author,
                "Ход продолжается."),

            // ── Замер хода: вне цикла ────────────────────────────────────────────────────────
            Cmd("T30", OpenIdle, ActionSampled,
                m => m.Ctx && m.S.AllAtRest, m => m.CloseOnly(),
                "Отменённый цикл, Action физически закрыт: CloseOnly."),
            Cmd("T31", EmptyAwaitRest, ActionSampled,
                m => m.Ctx && m.S.AllAtRest && m.ClipNow != ClipKind.Empty, m => m.AckEmptyRest(),
                "Empty-показ закончен, Action в покое: AckEmptyRest."),
            Local("T18", Ready | EmptyLike | OpenIdle, ActionSampled, RoleSet.Author,
                m => m.GestureNow == GestureState.Contact && !(m.S.HeldByAuthorMainContext && m.Ctx), m => m.ResetGesture(),
                "+B. Рука больше не держит ручку (отпускание без события): контакт снят."),
            Local("T19", Ready | EmptyLike | OpenIdle, ActionSampled, RoleSet.Author,
                m => m.GestureNow == GestureState.None && m.Ctx && m.S.HeldByAuthorMainContext, m => m.Contact(),
                "+B. Ручку держат, а жеста нет (после отмены, T27, снимка, смены автора): новый контакт, Baseline = текущий ход."),
            Cmd("T22", HoldOpen, ActionSampled,
                m => m.Contacting && m.ReturnedFromValidatedRear && m.CanBegin, m => m.BeginFromValidatedRear(),
                "Толчок вперёд из проверенного зада HoldOpen: BeginAction, жест PastGate без извлечения."),
            Cmd("T21", Ready | EmptyLike | OpenIdle, ActionSampled,
                m => m.Contacting && m.MovedRear && m.CanBegin, m => m.BeginManual(true),
                "Ход назад больше ε: BeginAction(seq+1), жест Pulling; тот же замер за порогом — сразу T25. +B: и из HoldOpen не с зада, и из OpenIdle."),
            Cmd("T23", Ready, ActionSampled,
                m => m.Contacting && m.MovedForwardFromRetained && m.CanBegin, m => m.BeginManual(false),
                "Ready, ручка не в покое после Fire-клипа, ход вперёд: BeginAction без хода назад (закроется CloseOnly)."),
            Ignore("T24", Ready | EmptyLike | OpenIdle, ActionSampled, RoleSet.Author,
                "Сам хват не цикл; ручку не держат."),
            Ignore("T99", Any, ActionSampled, RoleSet.Observer,
                "Наблюдатель: поза выводится из замера (ComputePose)."),

            // ── Ручка: хват и отпускание ─────────────────────────────────────────────────────
            Local("T20", Ready | EmptyLike | OpenIdle, HandleGrabbed, RoleSet.Author,
                m => m.Ctx && m.S.HeldByAuthorMainContext, m => m.Contact(),
                "Хват ручки в контексте автора: Contact, Baseline = ход, RearEvidence = HoldOpen ∧ проверенный зад."),
            Local("T28r", Cycle, HandleGrabbed, RoleSet.Author,
                m => m.GestureNow == GestureState.Released && m.Ctx && m.S.HeldByAuthorMainContext, m => m.Regrab(),
                "+B. Ручку подхватили посреди возврата: жест продолжается (Pulling или PastGate)."),
            Ignore("T43", Any, HandleGrabbed, RoleSet.Author,
                "Прочее (в т. ч. подготовка): цель позы FollowHand, возврат продолжится после отпускания."),
            Ignore("T65", Any, HandleGrabbed, RoleSet.Observer,
                "Поза следует руке; Action отпущен из клипа в PreStep, нерычажные детали доигрывают клип."),
            Local("T28", Cycle, HandleReleased, RoleSet.Author,
                m => m.GestureNow == GestureState.Pulling || m.GestureNow == GestureState.PastGate, m => m.Release(),
                "Ручку отпустили посреди цикла: Released (порог, достигнутый к отпусканию, засчитан), цель позы ReturnToRest со скоростью пружины."),
            Local("T28c", Any, HandleReleased, RoleSet.Author,
                m => m.GestureNow == GestureState.Contact, m => m.DropGesture(),
                "+B. Отпустили без цикла: контакт снят."),
            Ignore("T28i", Any, HandleReleased, RoleSet.Both,
                "Прочее: цель позы выводится."),

            // ── Фиксации учёта (обе роли) ────────────────────────────────────────────────────
            Local("T60", Ready, LedgerCommitted, RoleSet.Both,
                m => m.E.Op == LedgerOp.Shot, m => m.StartClip(ClipKind.Fire),
                "Выстрел, после него C=1: FireClip(0)."),
            Local("T61", Empty, LedgerCommitted, RoleSet.Both,
                m => m.E.Op == LedgerOp.Shot, m => m.StartClip(ClipKind.Fire),
                "Manual: после выстрела C=0, M>0 — FireClip(0), дальше ручной цикл."),
            Local("T62", HoldOpen | EmptyAwaitRest, LedgerCommitted, RoleSet.Both,
                m => m.E.Op == LedgerOp.Shot, m => m.StartClip(ClipKind.Empty),
                "Последний патрон: EmptyClip(0); без клипа — сразу HoldRear/ReturnToRest."),
            Cmd("T33", Any, LedgerCommitted,
                m => m.OriginNow != ChamberOrigin.None && !m.OwnsCurrentCycle, m => m.CancelForeignCycle(),
                "Чужая фиксация сменила цикл или магазин: жест сброшен, Cancel, если Pending."),
            Ignore("T66", Any, LedgerCommitted, RoleSet.Both,
                "Звук фиксации уже выдан в PreStep (план п. 4.2); состояние — Derive."),
            Local("T80", Any, LedgerFaulted, RoleSet.Author,
                null, m => m.ReportFault(true),
                "Сбой учёта: команды запрещены (строки Faulted без команд), лог ошибки, запрос resync (В8). T81 — Derive после снятия сбоя."),
            Local("T80o", Any, LedgerFaulted, RoleSet.Observer,
                null, m => m.ReportFault(false),
                "Сбой учёта у наблюдателя: только лог."),
            Cmd("T35", Any, CommandRejected,
                null, m => m.OnCommandRejected(),
                "Учёт отклонил команду: жест сгорает, предупреждение в лог (C2: сверки магазина нет — копии нет)."),
            Ignore("T35o", Any, CommandRejected, RoleSet.Observer,
                "Наблюдатель команд не выдаёт."),

            // ── Время: клипы ─────────────────────────────────────────────────────────────────
            Local("T63", Any, Tick, RoleSet.Both,
                m => m.ClipNow == ClipKind.Empty && m.ClipTimeNow >= m.ClipEndNow, m => m.EndEmptyClip(),
                "Empty-клип дошёл до конца (HoldOpen — до проверенного зада, со щелчком SlideLockCatch)."),
            Local("T64", Any, Tick, RoleSet.Both,
                m => m.ClipNow == ClipKind.Fire && m.ClipTimeNow >= m.ClipEndNow, m => m.EndClip(),
                "Fire-клип дошёл до конца: покой."),
            Ignore("T41t", Any, Tick, RoleSet.Both,
                "Цель позы — функция состояния; возврат ведёт исполнитель."),

            // ── Спуск ────────────────────────────────────────────────────────────────────────
            Local("T55", Any, TriggerPressed, RoleSet.Author,
                m => m.C.Blocked == WeaponBlockReason.Obstructed, m => m.Deny(WeaponNotReadyReason.Obstructed, WeaponHapticCue.Obstructed),
                "Препятствие у ствола: DryFire(Obstructed) и вибрация препятствия; нажатие израсходовано."),
            Local("T58", Any, TriggerPressed, RoleSet.Author,
                m => m.C.Blocked == WeaponBlockReason.Other, m => m.ConsumeSilently(),
                "+B. Иная блокировка (CanUse=false): OtherDenied без отклика."),
            Local("T82", Faulted, TriggerPressed, RoleSet.Author,
                null, m => m.Deny(WeaponNotReadyReason.Faulted, WeaponHapticCue.Faulted),
                "В8: в сбое — сухой щелчок с отдельной вибрацией до восстановления (заменяет Faulted в T54)."),
            Local("T54a", Live, TriggerPressed, RoleSet.Author,
                m => m.L.AdmissionPending, m => m.ConsumeSilently(),
                "Барьер приёма патрона: OtherDenied без отклика о патронах."),
            Cmd("T50", Ready, TriggerPressed,
                m => m.C.RofTimer <= 0f, m => m.Shoot(),
                "Новое нажатие, готово: Shoot, эпизод Firing (хост делает повторный замер датчика до события)."),
            Local("T57", Ready, TriggerPressed, RoleSet.Author,
                null, m => m.SetEpisode(TriggerEpisode.Firing),
                "Таймер темпа не истёк: эпизод Firing без выстрела (Semi/Manual — нажатие пропадает; Auto продолжит T51)."),
            Cmd("T40", EmptyLike, TriggerPressed,
                m => m.AssistChambers && m.Ctx && m.CanBegin, m => m.AssistPrepare(),
                "TriggerAssist, ChamberingRequired: NotReady-отклик, эпизод Consumed (И5), BeginAction(Assist), в покое — Complete. +B: и из HoldOpen/EmptyAwaitRest."),
            Local("T53", EmptyLike, TriggerPressed, RoleSet.Author,
                null, m => m.DenyNotReady(),
                "NotReady(причина) один раз: DryFire и вибрация; при ChamberingRequired — защёлка подсказки."),
            Local("T54", Uninitialized | Cycle | OpenIdle, TriggerPressed, RoleSet.Author,
                null, m => m.ConsumeSilently(),
                "Цикл/открыт/не инициализирован: OtherDenied без отклика о патронах."),
            Ignore("T59", Any, TriggerPressed, RoleSet.Observer,
                "Спуск — только у автора."),
            Cmd("T51", Ready, TriggerHeld,
                m => m.EpisodeNow == TriggerEpisode.Firing && m.AxesRef.FireMode == WeaponFireMode.Auto && m.C.RofTimer <= 0f &&
                     m.C.Blocked == WeaponBlockReason.None && !m.L.AdmissionPending,
                m => m.Shoot(),
                "Auto, нажатие не отпускали, темп позволяет: Shoot."),
            Local("T52a", Ready, TriggerHeld, RoleSet.Author,
                m => m.EpisodeNow == TriggerEpisode.Firing && (m.C.Blocked != WeaponBlockReason.None || m.L.AdmissionPending),
                m => m.ConsumeSilently(),
                "+B. Блокировка или барьер посреди очереди: очередь останавливается."),
            Ignore("T51w", Ready, TriggerHeld, RoleSet.Author,
                "Semi/Manual ждут отпускания; Auto ждёт темпа."),
            Local("T52", Any & ~Ready, TriggerHeld, RoleSet.Author,
                m => m.EpisodeNow == TriggerEpisode.Firing, m => m.ConsumeSilently(),
                "Не готово посреди очереди: эпизод Consumed, без серии откликов."),
            Ignore("T52i", Any, TriggerHeld, RoleSet.Both,
                "Эпизод не Firing или наблюдатель."),
            Local("T56", Any, TriggerReleased, RoleSet.Author,
                null, m => m.SetEpisode(TriggerEpisode.Armed),
                "Отпускание: эпизод Armed."),
            Ignore("T56o", Any, TriggerReleased, RoleSet.Observer,
                "Спуск — только у автора."),

            // ── Контекст и автор ────────────────────────────────────────────────────────────
            Cmd("T32", Cycle, ContextLost,
                null, m => { m.Cancel(); m.LoseContext(); },
                "Потеря основной руки/CanUse посреди цикла: Cancel, жест и нажатие сброшены."),
            Local("T32b", Any, ContextLost, RoleSet.Author,
                null, m => m.LoseContext(),
                "Потеря контекста: жест и нажатие сброшены, подсказка выключена."),
            Ignore("T32o", Any, ContextLost, RoleSet.Observer,
                "У наблюдателя жеста нет."),
            Cmd("T37", Cycle, AuthorityChanged,
                null, m => { m.Cancel(); m.LoseContext(); },
                "Новый автор не наследует чужой цикл: Cancel (только если машина теперь автор)."),
            Local("T37b", Any, AuthorityChanged, RoleSet.Both,
                null, m => m.LoseContext(),
                "Смена автора: жест и нажатие не наследуются."),

            // ── Приём патрона ────────────────────────────────────────────────────────────────
            Cmd("T70", Live, CartridgeOffered,
                m => m.CanOfferCartridge, m => m.RequestAdmission(),
                "FixedStore, держатель-автор, есть место, нет барьера: RequestAdmission(token)."),
            Ignore("T72", Any, CartridgeOffered, RoleSet.Both,
                "Не FixedStore, нет места, барьер, сбой или наблюдатель."),
            Cmd("T71", Cycle, AdmissionResolved,
                m => m.GatePassedGesture && m.S.AllAtRest && !m.L.AdmissionPending && m.Ctx && m.OwnsCurrentCycle,
                m => m.Complete(),
                "Барьер снят: отложенное T34 досылание."),
            Ignore("T71i", Any, AdmissionResolved, RoleSet.Both,
                "Нечего досылать: Derive (M+1 или без изменений)."),

            // ── Бот ──────────────────────────────────────────────────────────────────────────
            Cmd("T44", EmptyAwaitRest, AutomationPrepareRequested,
                m => m.AutomationContext && m.S.AllAtRest && m.ClipNow != ClipKind.Empty,
                m => { m.AckEmptyRest(); m.Refill(); },
                "Бот (мир): Empty-показ закончен, Action в покое — AckEmptyRest, затем RefillForAutomation."),
            Cmd("T45", Empty, AutomationPrepareRequested,
                m => m.AutomationContext && m.L.MagazinePresent && m.L.Capacity > 0, m => m.Refill(),
                "Бот (мир): RefillForAutomation."),
            Cmd("T46", HoldOpen, AutomationPrepareRequested,
                m => m.AutomationContext && m.CanBegin, m => m.BeginAutomation(),
                "+B. Бот с HoldOpen: BeginAction(Automation) спускает затвор циклом подготовки (И10 сохраняется)."),
            Ignore("T47", Any, AutomationPrepareRequested, RoleSet.Both,
                "Готово, Action ещё возвращается, идёт цикл, нет мира/контекста или наблюдатель."),
        };

        /// <summary>Строки по событию в порядке таблицы. Без аллокаций на шаге.</summary>
        private static readonly TransitionRow[][] ByEvent = BuildIndex();

        /// <summary>Первая подошедшая строка шага; null — дыра в таблице (тест И8 держит невозможным).</summary>
        internal static TransitionRow Find(WeaponStateMachine m)
        {
            TransitionRow[] rows = ByEvent[(int)m.E.Kind];
            for (int index = 0; index < rows.Length; index++)
            {
                TransitionRow row = rows[index];
                if (!row.Covers(m.M, m.E.Kind, m.Role)) continue;
                if (row.Guard != null && !row.Guard(m)) continue;
                return row;
            }
            return null;
        }

        private static TransitionRow[][] BuildIndex()
        {
            var kinds = (WeaponEventKind[])Enum.GetValues(typeof(WeaponEventKind));
            var index = new TransitionRow[kinds.Length][];
            var ids = new HashSet<string>();
            foreach (TransitionRow row in Table)
                if (!ids.Add(row.Id)) throw new InvalidOperationException($"Повторный номер строки {row.Id}.");
            foreach (WeaponEventKind kind in kinds)
                index[(int)kind] = Array.FindAll(Table, row => row.On == kind);
            return index;
        }

        private static TransitionRow Cmd(string id, MechanismSet from, WeaponEventKind on,
            Func<WeaponStateMachine, bool> guard, Action<WeaponStateMachine> apply, string doc) =>
            new TransitionRow(id, from, on, RoleSet.Author, guard, apply, true, doc);

        private static TransitionRow Local(string id, MechanismSet from, WeaponEventKind on, RoleSet roles,
            Func<WeaponStateMachine, bool> guard, Action<WeaponStateMachine> apply, string doc) =>
            new TransitionRow(id, from, on, roles, guard, apply, false, doc);

        private static TransitionRow Ignore(string id, MechanismSet from, WeaponEventKind on, RoleSet roles, string doc) =>
            new TransitionRow(id, from, on, roles, null, null, false, doc);

        private static TransitionRow Ignore(string id, MechanismSet from, WeaponEventKind on, RoleSet roles,
            Func<WeaponStateMachine, bool> guard, string doc) =>
            new TransitionRow(id, from, on, roles, guard, null, false, doc);
    }
}
