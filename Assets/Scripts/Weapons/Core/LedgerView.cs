using System;

namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>
    /// Операция фиксации учёта SDK. Зеркало <c>UxrFirearmReadinessOperation</c>; переводит порт хоста.
    /// MagazineChanged и Reconcile удалены на этапе C2: магазин учёт не хранит, сверять нечего.
    /// </summary>
    public enum LedgerOp
    {
        None, Initialize, BeginAction, Extract, Complete, Cancel, Shot, Automation,
        CloseOnly, EmptyRestAcknowledged, AmmoAdmission
    }

    /// <summary>
    /// Снимок учёта SDK (<c>UxrFirearmWeapon.Readiness</c>) на момент события. Машина его не хранит:
    /// хост читает учёт заново перед каждым шагом. Поэтому машина и учёт не могут разойтись (план п. 0, 2.4).
    ///
    /// Магазины сравниваются по жетону (<c>0</c> — магазина нет). Магазин один — тот, что стоит в гнезде
    /// (<see cref="MagazineToken"/>): учёт его не хранит (этап C2). <see cref="CycleMagazineToken"/> — магазин,
    /// с которым начат цикл. <see cref="CyclePending"/> — уже действительный Pending (SDK <c>IsCyclePendingFor</c>:
    /// цикл начат с магазином, который сейчас в гнезде), поэтому смена магазина посреди цикла даёт OpenIdle без фиксации.
    /// <see cref="MagazineRounds"/> и <see cref="MagazinePresent"/> относятся к магазину в гнезде,
    /// как <c>GetMagazineRounds</c> и <c>QueryReadinessDecision</c> SDK.
    /// </summary>
    public readonly struct LedgerView
    {
        public readonly bool Initialized, Chamber, ActionOpen, CyclePending, SlideLocked, Faulted, AdmissionPending, MagazinePresent;
        public readonly int MagazineRounds, Capacity;
        public readonly uint Revision, CycleSequence, ExtractedCycle, ShotSequence;
        public readonly int MagazineToken, CycleMagazineToken;

        /// <param name="slideLocked">Бывший <c>PostShotEmptyAction</c>: последний выстрел оставил оружие пустым.</param>
        public LedgerView(bool initialized = false, bool chamber = false, bool actionOpen = false, bool cyclePending = false,
            bool slideLocked = false, bool faulted = false, bool admissionPending = false, bool magazinePresent = false,
            int magazineRounds = 0, int capacity = 0, uint revision = 0, uint cycleSequence = 0, uint extractedCycle = 0,
            uint shotSequence = 0, int magazineToken = 0, int cycleMagazineToken = 0)
        {
            Initialized = initialized; Chamber = chamber; ActionOpen = actionOpen; CyclePending = cyclePending;
            SlideLocked = slideLocked; Faulted = faulted; AdmissionPending = admissionPending; MagazinePresent = magazinePresent;
            MagazineRounds = magazineRounds; Capacity = capacity; Revision = revision; CycleSequence = cycleSequence;
            ExtractedCycle = extractedCycle; ShotSequence = shotSequence; MagazineToken = magazineToken;
            CycleMagazineToken = cycleMagazineToken;
        }

        /// <summary>Всего патронов M + C.</summary>
        public int Total => MagazineRounds + (Chamber ? 1 : 0);
    }

    /// <summary>
    /// Механическое состояние оружия (план п. 3.3). Не хранится нигде: каждый раз выводится из
    /// <see cref="LedgerView"/> функцией <see cref="WeaponMechanism.Derive"/>.
    /// </summary>
    public enum MechanismState { Uninitialized, Ready, Empty, HoldOpen, EmptyAwaitRest, CycleLoaded, CycleCleared, OpenIdle, Faulted }

    /// <summary>Множество механических состояний — столбец «Из» таблицы переходов.</summary>
    [Flags]
    public enum MechanismSet
    {
        None = 0,
        Uninitialized = 1 << MechanismState.Uninitialized,
        Ready = 1 << MechanismState.Ready,
        Empty = 1 << MechanismState.Empty,
        HoldOpen = 1 << MechanismState.HoldOpen,
        EmptyAwaitRest = 1 << MechanismState.EmptyAwaitRest,
        CycleLoaded = 1 << MechanismState.CycleLoaded,
        CycleCleared = 1 << MechanismState.CycleCleared,
        OpenIdle = 1 << MechanismState.OpenIdle,
        Faulted = 1 << MechanismState.Faulted,

        /// <summary>Нет патрона в патроннике, Action закрыт или на задержке.</summary>
        EmptyLike = Empty | HoldOpen | EmptyAwaitRest,
        /// <summary>Идёт цикл досылания (Pending).</summary>
        Cycle = CycleLoaded | CycleCleared,
        /// <summary>Учёт инициализирован и не в сбое.</summary>
        Live = Ready | EmptyLike | Cycle | OpenIdle,
        Any = Uninitialized | Live | Faulted
    }

    /// <summary>Чистые функции учёта: механическое состояние, причина отказа, готовность к выстрелу.</summary>
    public static class WeaponMechanism
    {
        public static MechanismSet Bit(MechanismState state) => (MechanismSet)(1 << (int)state);

        /// <summary>
        /// Механическое состояние по учёту (план п. 3.3). Порядок проверок значим: сбой важнее всего,
        /// затем инициализация, цикл, открытый Action, патронник, задержка.
        ///
        /// Уточнения к таблице плана. Pending без ActionOpen и Pending∧C после извлечения того же цикла
        /// SDK не порождает (BeginAction ставит оба флага, Extract снимает C); такие снимки относятся к циклу
        /// по Pending и C, поэтому Derive тотальна. У <see cref="WeaponAmmoCapability.MagazineOnly"/>
        /// патронника нет: готовность — наличие патронов в магазине.
        /// </summary>
        public static MechanismState Derive(in LedgerView l, in WeaponProfileAxes a)
        {
            if (l.Faulted) return MechanismState.Faulted;
            if (!l.Initialized) return MechanismState.Uninitialized;
            if (a.Ammo == WeaponAmmoCapability.MagazineOnly) return l.MagazineRounds > 0 ? MechanismState.Ready : MechanismState.Empty;
            if (l.CyclePending) return l.Chamber ? MechanismState.CycleLoaded : MechanismState.CycleCleared;
            if (l.ActionOpen) return MechanismState.OpenIdle;
            if (l.Chamber) return MechanismState.Ready;
            if (l.SlideLocked) return a.HoldsOpen ? MechanismState.HoldOpen : MechanismState.EmptyAwaitRest;
            return MechanismState.Empty;
        }

        /// <summary>Причина отказа спуска без патрона в патроннике (как <c>QueryReadinessDecision</c> SDK).</summary>
        public static WeaponNotReadyReason ReasonOf(in LedgerView l) =>
            !l.MagazinePresent ? WeaponNotReadyReason.NoMagazine :
            l.MagazineRounds == 0 ? WeaponNotReadyReason.EmptyMagazine : WeaponNotReadyReason.ChamberingRequired;

        /// <summary>
        /// Инвариант И3: оружие готово стрелять ⇔ C ∧ ¬Open ∧ ¬Pending ∧ Initialized ∧ ¬Faulted ∧ ¬AdmissionPending.
        /// Барьер приёма патрона — надстройка над механическим состоянием, поэтому проверяется здесь, а не в Derive.
        /// </summary>
        public static bool CanFire(in LedgerView l, in WeaponProfileAxes a) =>
            Derive(l, a) == MechanismState.Ready && !l.AdmissionPending;
    }
}
