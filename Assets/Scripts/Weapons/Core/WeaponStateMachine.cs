using System;

namespace VrBattlegrounds.Weapons.Core
{
    /// <summary>Жест руки на ручке Action (только автор, не реплицируется).</summary>
    public enum GestureState { None, Contact, Pulling, PastGate, Released }

    /// <summary>Эпизод спуска: ждёт нажатия, ведёт принятое нажатие или нажатие уже израсходовано.</summary>
    public enum TriggerEpisode { Armed, Firing, Consumed }

    /// <summary>Какой клип выстрела играет (у всех ролей).</summary>
    public enum ClipKind { None, Fire, Empty }

    /// <summary>
    /// Локальные регионы машины (план п. 3.2) — всё, что машина хранит. Учёта здесь нет:
    /// механическое состояние выводится из <see cref="LedgerView"/> на каждом шаге.
    /// </summary>
    public readonly struct WeaponMachineState : IEquatable<WeaponMachineState>
    {
        public readonly GestureState Gesture;
        public readonly ChamberOrigin Origin;
        public readonly TriggerEpisode Episode;
        public readonly ClipKind Clip;
        public readonly float ClipTime, ClipEnd;
        public readonly uint ClipShot;
        public readonly bool ClipActionDetached, HintLatched, GatePassed, OpenedRear, RearEvidence;
        public readonly uint RearShot, CycleSeqOwned;
        public readonly float Baseline, LastProgress;
        public readonly int ExpectedMagazineToken, DeferredInsertMagazine;

        internal WeaponMachineState(WeaponStateMachine m)
        {
            Gesture = m.GestureNow; Origin = m.OriginNow; Episode = m.EpisodeNow; Clip = m.ClipNow;
            ClipTime = m.ClipTimeNow; ClipEnd = m.ClipEndNow; ClipShot = m.ClipShotNow;
            ClipActionDetached = m.ClipActionDetachedNow; HintLatched = m.HintLatchedNow;
            GatePassed = m.GatePassedNow; OpenedRear = m.OpenedRearNow; RearEvidence = m.RearEvidenceNow;
            RearShot = m.RearShotNow; CycleSeqOwned = m.CycleSeqOwnedNow;
            Baseline = m.BaselineNow; LastProgress = m.LastProgressNow;
            ExpectedMagazineToken = m.ExpectedMagazineNow; DeferredInsertMagazine = m.DeferredInsertNow;
        }

        public bool Equals(WeaponMachineState o) =>
            Gesture == o.Gesture && Origin == o.Origin && Episode == o.Episode && Clip == o.Clip &&
            ClipTime.Equals(o.ClipTime) && ClipEnd.Equals(o.ClipEnd) && ClipShot == o.ClipShot &&
            ClipActionDetached == o.ClipActionDetached && HintLatched == o.HintLatched && GatePassed == o.GatePassed &&
            OpenedRear == o.OpenedRear && RearEvidence == o.RearEvidence && RearShot == o.RearShot &&
            CycleSeqOwned == o.CycleSeqOwned && Baseline.Equals(o.Baseline) && LastProgress.Equals(o.LastProgress) &&
            ExpectedMagazineToken == o.ExpectedMagazineToken && DeferredInsertMagazine == o.DeferredInsertMagazine;

        public override bool Equals(object obj) => obj is WeaponMachineState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Gesture;
                h = h * 31 + (int)Origin; h = h * 31 + (int)Episode; h = h * 31 + (int)Clip;
                h = h * 31 + ClipTime.GetHashCode(); h = h * 31 + ClipEnd.GetHashCode(); h = h * 31 + (int)ClipShot;
                h = h * 31 + (ClipActionDetached ? 1 : 0) + (HintLatched ? 2 : 0) + (GatePassed ? 4 : 0) + (OpenedRear ? 8 : 0) + (RearEvidence ? 16 : 0);
                h = h * 31 + (int)RearShot; h = h * 31 + (int)CycleSeqOwned;
                h = h * 31 + Baseline.GetHashCode(); h = h * 31 + LastProgress.GetHashCode();
                h = h * 31 + ExpectedMagazineToken; h = h * 31 + DeferredInsertMagazine;
                return h;
            }
        }

        public override string ToString() =>
            $"{Gesture}/{Origin}/{Episode}/{Clip}@{ClipTime:0.###}{(ClipActionDetached ? "(detached)" : "")}" +
            $"{(HintLatched ? " hint" : "")}{(GatePassed ? " gate" : "")}{(OpenedRear ? " rear" : "")} seq={CycleSeqOwned} mag={ExpectedMagazineToken}";
    }

    /// <summary>
    /// Единственная машина решений оружия (план п. 2, 3). Чистый C#: сборка собрана с
    /// <c>noEngineReferences</c>, поэтому Unity, SDK и сеть машине недоступны по построению.
    ///
    /// **Что хранит.** Только локальные регионы (<see cref="WeaponMachineState"/>): жест руки, происхождение
    /// цикла, эпизод спуска, клип выстрела и подсказку. Учёт патронов (M, C, флаги, revision) живёт в SDK и
    /// приходит снимком <see cref="LedgerView"/> в каждый шаг; механическое состояние —
    /// <see cref="Derive"/> от этого снимка. Второго источника истины нет.
    ///
    /// **Кто меняет состояние.** Только <see cref="Step"/> и только по таблице <see cref="WeaponTransitions.Table"/>:
    /// первая строка, подошедшая по механическому состоянию, событию, роли и условию. Перед таблицей —
    /// <c>PreStep</c> (время клипа, наблюдение за ходом, звук фиксации); после — <c>PostStep</c>
    /// (снятие подсказки и ровно одна цель позы). Оба — фиксированные побочные эффекты, как пересчёт
    /// готовности в <c>RoundPhases.Tick</c>; условия строк остаются чистыми.
    ///
    /// **Ключ строки.** Столбец «Из» — <c>Derive(L)</c> на момент события, где L уже содержит фиксацию,
    /// вызвавшую событие. Поэтому строки результата выстрела (T60–T62) различаются состоянием после выстрела.
    ///
    /// **Сеть.** Команды учёту выдают только строки с правом команды, а их таблица разрешает только автору вне
    /// replay (И9). Наблюдатель получает те же фиксации и выводит из них позу и звук.
    /// </summary>
    public sealed class WeaponStateMachine
    {
        private readonly WeaponProfileAxes _axes;

        // Локальные регионы.
        private GestureState _gesture;
        private ChamberOrigin _origin;
        private TriggerEpisode _episode;
        private ClipKind _clip;
        private float _clipTime, _clipEnd;
        private uint _clipShot;
        private bool _clipActionDetached, _hintLatched, _gatePassed, _openedRear, _rearEvidence;
        private uint _rearShot, _cycleSeqOwned;
        private float _baseline, _lastProgress;
        private int _expectedMagazine, _deferredInsert;

        // Входы текущего шага (копии in-параметров). Читаются условиями строк таблицы.
        internal WeaponEvent E;
        internal LedgerView L;
        internal ActionSample S;
        internal WeaponContext C;
        internal MechanismState M;
        internal WeaponRole Role;

        private IWeaponOutput _out;
        private TransitionRow _row;
        private uint _revision;
        private bool _stepping;

        public WeaponStateMachine(in WeaponProfileAxes axes)
        {
            if (!WeaponProfileAxes.TryValidate(axes, out string error)) throw new ArgumentException(error, nameof(axes));
            _axes = axes;
        }

        public WeaponProfileAxes Axes => _axes;
        public WeaponMachineState State => new WeaponMachineState(this);

        /// <summary>Цель позы последнего шага.</summary>
        public PoseTarget LastPose { get; private set; }

        /// <summary>Строка таблицы, применённая последним шагом.</summary>
        public string LastRowId { get; private set; }

        /// <summary>Нарушения таблицы (команда или авторский вывод из строки без права). Должно быть 0.</summary>
        public int Violations { get; private set; }

        /// <summary>Механическое состояние по учёту (план п. 3.3).</summary>
        public static MechanismState Derive(in LedgerView l, in WeaponProfileAxes a) => WeaponMechanism.Derive(l, a);

        /// <summary>
        /// Один шаг машины. Без аллокаций. Повторный вход из методов <paramref name="o"/> запрещён:
        /// события, порождённые выполнением команд, хост подаёт следующими шагами.
        /// </summary>
        public void Step(in WeaponEvent e, in LedgerView l, in ActionSample s, in WeaponContext c, IWeaponOutput o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (_stepping) throw new InvalidOperationException("WeaponStateMachine.Step: повторный вход из вывода машины.");
            _stepping = true;
            try
            {
                E = e; L = l; S = s; C = c; _out = o;
                Role = c.ActsAsAuthor ? WeaponRole.Author : WeaponRole.Observer;
                M = WeaponMechanism.Derive(l, _axes);
                _revision = l.Revision;

                PreStep();

                _row = WeaponTransitions.Find(this);
                if (_row == null)
                {
                    LastRowId = null;
                    o.Report(new WeaponReport(WeaponReportKind.Unhandled, null));
                }
                else
                {
                    LastRowId = _row.Id;
                    _row.Apply?.Invoke(this);
                }

                PostStep();
            }
            finally
            {
                _row = null; _out = null; _stepping = false;
            }
        }

        /// <summary>
        /// Цель позы — функция состояния (план п. 3.6, инварианты И10, И11). Исполнитель своей фазы не хранит.
        /// Порядок: нет хода → покой; ручку держат → рука; HoldOpen → клип до задней позы или задержка;
        /// цикл/открыт → возврат; клип, если Action ещё его; Empty-ожидание или не в покое → возврат; иначе покой.
        /// </summary>
        public static PoseTarget ComputePose(MechanismState m, in WeaponProfileAxes a, in ActionSample s,
            ClipKind clip, float clipTime, bool clipActionDetached, ChamberOrigin origin)
        {
            AuxiliaryPose aux = clip == ClipKind.Fire ? AuxiliaryPose.FireClip :
                clip == ClipKind.Empty ? AuxiliaryPose.EmptyClip : AuxiliaryPose.Rest;
            if (a.Physical == WeaponPhysicalCapability.NoAction) return new PoseTarget(PosePresentation.Rest, aux, clipTime, 0f);
            if (s.Held) return new PoseTarget(PosePresentation.FollowHand, aux, clipTime, 0f);
            bool clipOwnsAction = clip != ClipKind.None && !clipActionDetached;
            switch (m)
            {
                case MechanismState.HoldOpen:
                    return clipOwnsAction && clip == ClipKind.Empty
                        ? new PoseTarget(PosePresentation.EmptyClip, aux, clipTime, 0f)
                        : new PoseTarget(PosePresentation.HoldRear, aux, clipTime, 0f);
                case MechanismState.CycleLoaded:
                case MechanismState.CycleCleared:
                case MechanismState.OpenIdle:
                    return new PoseTarget(PosePresentation.ReturnToRest, aux, clipTime, IsAutomatic(origin) ? a.AutoReturnSpeed : a.SpringReturnSpeed);
                default:
                    if (clipOwnsAction)
                        return new PoseTarget(clip == ClipKind.Fire ? PosePresentation.FireClip : PosePresentation.EmptyClip, aux, clipTime, 0f);
                    if (m == MechanismState.EmptyAwaitRest || !s.AllAtRest)
                        return new PoseTarget(PosePresentation.ReturnToRest, aux, clipTime, a.SpringReturnSpeed);
                    return new PoseTarget(PosePresentation.Rest, aux, clipTime, 0f);
            }
        }

        // ---------- Шаг: до и после таблицы ----------

        private void PreStep()
        {
            switch (E.Kind)
            {
                case WeaponEventKind.Tick:
                    // И11: клип стареет только временем; конец — строки T63/T64.
                    if (_clip != ClipKind.None && E.Dt > 0f && !float.IsInfinity(E.Dt)) _clipTime += E.Dt;
                    break;
                case WeaponEventKind.ActionSampled:
                    // Ход назад после вперёд (T23) открывает путь к порогу; наблюдается до разбора таблицы.
                    if (_gesture == GestureState.Pulling && S.HeldByAuthorMainContext && S.HandleProgress > _lastProgress + _axes.Epsilon)
                        _openedRear = true;
                    if (GestureInCycle) _lastProgress = S.HandleProgress;
                    break;
                case WeaponEventKind.HandleGrabbed:
                    // T65: рука забирает Action у клипа; нерычажные детали доигрывают его (у всех ролей).
                    if (_clip != ClipKind.None) _clipActionDetached = true;
                    break;
                case WeaponEventKind.LedgerCommitted:
                    // T66, план п. 4.2: звук по операции фиксации — одинаково у автора и наблюдателя.
                    CueForCommit();
                    break;
            }
        }

        private void PostStep()
        {
            if (_hintLatched && !(Role == WeaponRole.Author && (WeaponMechanism.Bit(M) & MechanismSet.EmptyLike) != 0 &&
                                  WeaponMechanism.ReasonOf(L) == WeaponNotReadyReason.ChamberingRequired))
                HintOff();
            PoseTarget pose = ComputePose(M, _axes, S, _clip, _clipTime, _clipActionDetached, _origin);
            LastPose = pose;
            _out.Pose(pose); // И13: ровно одна цель позы за шаг.
        }

        private void CueForCommit()
        {
            switch (E.Op)
            {
                case LedgerOp.Extract:
                    _out.Cue(WeaponCue.ActionBack, WeaponNotReadyReason.None);
                    if (E.OpChamberBefore) _out.Cue(WeaponCue.ChamberEjected, WeaponNotReadyReason.None);
                    break;
                case LedgerOp.Complete:
                    _out.Cue(!E.OpChamberBefore && E.OpChamberAfter ? WeaponCue.ActionForwardChambered : WeaponCue.ActionForwardEmpty,
                        WeaponNotReadyReason.None);
                    break;
                case LedgerOp.CloseOnly:
                    _out.Cue(WeaponCue.ActionForwardEmpty, WeaponNotReadyReason.None);
                    break;
            }
        }

        // ---------- Условия строк ----------

        internal WeaponProfileAxes AxesRef => _axes;
        internal GestureState GestureNow => _gesture;
        internal ChamberOrigin OriginNow => _origin;
        internal TriggerEpisode EpisodeNow => _episode;
        internal ClipKind ClipNow => _clip;
        internal float ClipTimeNow => _clipTime;
        internal float ClipEndNow => _clipEnd;
        internal uint ClipShotNow => _clipShot;
        internal bool ClipActionDetachedNow => _clipActionDetached;
        internal bool HintLatchedNow => _hintLatched;
        internal bool GatePassedNow => _gatePassed;
        internal bool OpenedRearNow => _openedRear;
        internal bool RearEvidenceNow => _rearEvidence;
        internal uint RearShotNow => _rearShot;
        internal uint CycleSeqOwnedNow => _cycleSeqOwned;
        internal float BaselineNow => _baseline;
        internal float LastProgressNow => _lastProgress;
        internal int ExpectedMagazineNow => _expectedMagazine;
        internal int DeferredInsertNow => _deferredInsert;

        /// <summary>Контекст автора: основную рукоять держит локальная рука.</summary>
        internal bool Ctx => C.MainGripLocal;

        /// <summary>Жест автора на ручке с контекстом.</summary>
        internal bool Contacting => _gesture == GestureState.Contact && Ctx && S.HeldByAuthorMainContext;

        internal bool GestureInCycle => _gesture == GestureState.Pulling || _gesture == GestureState.PastGate || _gesture == GestureState.Released;
        internal bool GatePassedGesture => _gesture == GestureState.PastGate || (_gesture == GestureState.Released && _gatePassed);
        internal bool AutoOrigin => IsAutomatic(_origin);
        internal bool CanBegin => L.CycleSequence != uint.MaxValue;

        /// <summary>Цикл в учёте — тот, который начала эта машина, на том же магазине (И12).</summary>
        internal bool OwnsCurrentCycle => _origin != ChamberOrigin.None && L.CyclePending &&
                                          L.CycleSequence == _cycleSeqOwned && L.MagazineToken == _expectedMagazine;

        internal bool MovedRear => S.HandleProgress > _baseline + _axes.Epsilon && S.HandleProgress > _axes.Epsilon;
        internal bool MovedForwardFromRetained => S.HandleProgress < _baseline - _axes.Epsilon &&
                                                  Math.Max(S.HandleProgress, _baseline) > _axes.Epsilon;
        internal bool ReturnedFromValidatedRear => _rearEvidence && _rearShot == L.ShotSequence &&
                                                   S.HandleProgress < _baseline - _axes.Epsilon;
        internal bool PastExtractionGate => _openedRear && S.MinRequiredProgress >= _axes.ExtractionGate;

        /// <summary>Вставка магазина досылает по политике AutoOnMagazineInsert.</summary>
        internal bool InsertChambers => _axes.Policy == WeaponChamberPolicy.AutoOnMagazineInsert &&
                                        _axes.Ammo != WeaponAmmoCapability.MagazineOnly &&
                                        L.MagazinePresent && L.MagazineRounds > 0;

        internal bool AssistChambers => _axes.Policy == WeaponChamberPolicy.TriggerAssistPrepareOnly &&
                                        WeaponMechanism.ReasonOf(L) == WeaponNotReadyReason.ChamberingRequired;

        internal bool AutomationContext => C.IsWorldAuthority && Ctx;

        internal bool CanOfferCartridge => _axes.Ammo == WeaponAmmoCapability.FixedStoreChamber && Ctx && E.Token != 0 &&
                                           !L.AdmissionPending && L.Total < L.Capacity;

        private static bool IsAutomatic(ChamberOrigin origin) =>
            origin == ChamberOrigin.Insert || origin == ChamberOrigin.Assist || origin == ChamberOrigin.Automation;

        // ---------- Действия строк: учёт ----------

        private void Emit(LedgerCommandKind kind, uint cycleSequence = 0, int magazine = 0, ulong token = 0)
        {
            if (_row == null || !_row.MayCommand || Role != WeaponRole.Author)
            {
                Violations++;
                _out.Report(new WeaponReport(WeaponReportKind.TableViolation, _row?.Id, kind));
                return;
            }
            _out.Command(new LedgerCommand(kind, _revision, cycleSequence, magazine, token, _origin));
            // Следующая команда этого шага рассчитана на revision после этой (см. LedgerCommand).
            if (kind != LedgerCommandKind.RequestAdmission) _revision++;
        }

        internal void Initialize() => Emit(LedgerCommandKind.Initialize, magazine: L.AnchorMagazineToken);
        internal void Reconcile() => Emit(LedgerCommandKind.Reconcile);
        internal void Cancel() => Emit(LedgerCommandKind.Cancel);
        internal void CloseOnly() => Emit(LedgerCommandKind.CloseOnly, magazine: L.AnchorMagazineToken);
        internal void AckEmptyRest() => Emit(LedgerCommandKind.AckEmptyRest, magazine: L.AnchorMagazineToken);
        internal void Refill() => Emit(LedgerCommandKind.RefillForAutomation, magazine: L.AnchorMagazineToken);
        internal void RequestAdmission() => Emit(LedgerCommandKind.RequestAdmission, token: E.Token);

        internal void Shoot()
        {
            Emit(LedgerCommandKind.Shoot, magazine: L.AnchorMagazineToken);
            _episode = TriggerEpisode.Firing;
        }

        /// <summary>BeginAction(seq+1). Запоминает свой цикл и магазин — по ним T33/T32s узнают чужой цикл.</summary>
        private void Begin(ChamberOrigin origin)
        {
            uint seq = L.CycleSequence + 1;
            _origin = origin; _cycleSeqOwned = seq; _expectedMagazine = L.AnchorMagazineToken;
            _gatePassed = false; _openedRear = false; _lastProgress = S.HandleProgress;
            Emit(LedgerCommandKind.BeginAction, seq, _expectedMagazine);
        }

        /// <summary>
        /// T21/T23: ручной цикл. Если тот же замер уже за порогом (быстрый рывок за кадр), порог проходится
        /// в этом же шаге — иначе следующий замер мог бы прийти уже после отпускания и порог потерялся бы.
        /// </summary>
        internal void BeginManual(bool openedRear)
        {
            Begin(ChamberOrigin.Manual);
            _gesture = GestureState.Pulling;
            _openedRear = openedRear;
            if (PastExtractionGate) PassGate();
        }

        /// <summary>T22: толчок вперёд из проверенной задней позы HoldOpen — порог уже пройден, извлекать нечего.</summary>
        internal void BeginFromValidatedRear()
        {
            Begin(ChamberOrigin.Manual);
            _gesture = GestureState.PastGate;
            _gatePassed = true;
        }

        /// <summary>T11/T15s: вставка досылает; если Action уже в покое — в том же шаге.</summary>
        internal void BeginInsert()
        {
            ResetGesture();
            Begin(ChamberOrigin.Insert);
            if (S.AllAtRest) Complete();
        }

        /// <summary>T14: новый запрос из открытого Action, а не оживление отменённого цикла.</summary>
        internal void BeginInsertFromOpen()
        {
            ResetGesture();
            Begin(ChamberOrigin.Insert);
        }

        /// <summary>T40: нажатие с TriggerAssist готовит патрон и израсходовано (И5).</summary>
        internal void AssistPrepare()
        {
            Deny(WeaponMechanism.ReasonOf(L), WeaponHapticCue.NotReady);
            ResetGesture();
            Begin(ChamberOrigin.Assist);
            if (S.AllAtRest) Complete();
        }

        /// <summary>T46: бот с HoldOpen — спускает затвор циклом подготовки; И10 не нарушается.</summary>
        internal void BeginAutomation()
        {
            ResetGesture();
            Begin(ChamberOrigin.Automation);
        }

        /// <summary>T25: порог извлечения пройден — одно извлечение на цикл (И6), вибрация зада.</summary>
        internal void PassGate()
        {
            if (L.ExtractedCycle != _cycleSeqOwned) Emit(LedgerCommandKind.Extract, _cycleSeqOwned);
            _gesture = GestureState.PastGate;
            _gatePassed = true;
            AuthorHaptic(WeaponHapticCue.ActionRear);
        }

        /// <summary>T26/T29/T42/T71: Action в покое после порога — досылание.</summary>
        internal void Complete()
        {
            Emit(LedgerCommandKind.CompleteChamber, _cycleSeqOwned, _expectedMagazine);
            AfterCycleClosed();
        }

        /// <summary>T27: порог не пройден — цикл отменяется, Action закрыт без подачи (C сохранён).</summary>
        internal void CancelAndClose()
        {
            Emit(LedgerCommandKind.Cancel);
            Emit(LedgerCommandKind.CloseOnly, magazine: L.AnchorMagazineToken);
            AfterCycleClosed();
        }

        /// <summary>T29: ручку отпустили посреди цикла, пружина вернула Action (В7: досылает, если порог пройден).</summary>
        internal void FinishReleased()
        {
            if (_gatePassed) Complete();
            else CancelAndClose();
        }

        /// <summary>T33/T32s/T32/T37: цикл больше не наш — отмена, жест сброшен.</summary>
        internal void CancelForeignCycle()
        {
            if (L.CyclePending) Cancel();
            ResetGesture();
        }

        /// <summary>T35: учёт отклонил команду. Жест сгорает (повтора нет); магазин учёта сверяется.</summary>
        internal void OnCommandRejected()
        {
            _out.Report(new WeaponReport(WeaponReportKind.CommandRejected, _row.Id, E.Rejected));
            ResetGesture();
            if (E.Rejected != LedgerCommandKind.Reconcile && L.Initialized && !L.Faulted && !L.AdmissionPending && L.MagazineMismatch)
                Reconcile();
        }

        /// <summary>T15s: отложенная вставка (не было контекста) применяется, если магазин тот же.</summary>
        internal void ConsumeDeferredInsert()
        {
            int magazine = _deferredInsert;
            _deferredInsert = 0;
            if (magazine != L.AnchorMagazineToken || !InsertChambers || !CanBegin) return;
            if ((WeaponMechanism.Bit(M) & MechanismSet.EmptyLike) != 0) BeginInsert();
            else if (M == MechanismState.OpenIdle && !L.Chamber) BeginInsertFromOpen();
        }

        // ---------- Действия строк: локальные регионы ----------

        internal void DeferInsert() => _deferredInsert = L.AnchorMagazineToken;

        internal void Contact()
        {
            _gesture = GestureState.Contact;
            _baseline = S.HandleProgress;
            _rearEvidence = M == MechanismState.HoldOpen && S.AtValidatedRear;
            _rearShot = L.ShotSequence;
        }

        /// <summary>T28: отпускание; порог, достигнутый к моменту отпускания, засчитывается до перехода в Released.</summary>
        internal void Release()
        {
            if (_gesture == GestureState.Pulling && PastExtractionGate) PassGate();
            _gesture = GestureState.Released;
        }

        /// <summary>Повторный хват отпущенной посреди цикла ручки: жест продолжается с того же места.</summary>
        internal void Regrab()
        {
            _gesture = _gatePassed ? GestureState.PastGate : GestureState.Pulling;
            _lastProgress = S.HandleProgress;
        }

        internal void DropGesture()
        {
            _gesture = GestureState.None;
            _baseline = 0f; _lastProgress = 0f;
            _rearEvidence = false; _rearShot = 0;
        }

        internal void ResetGesture()
        {
            DropGesture();
            _origin = ChamberOrigin.None;
            _gatePassed = false; _openedRear = false;
            _cycleSeqOwned = 0; _expectedMagazine = 0;
        }

        private void AfterCycleClosed()
        {
            bool held = S.HeldByAuthorMainContext && Ctx;
            ResetGesture();
            if (held) Contact();
        }

        /// <summary>Снимок/настройка/выключение: жест, эпизод, клип и подсказка с нуля (T03, T04, T05).</summary>
        internal void ResetSession(TriggerEpisode episode)
        {
            ResetGesture();
            _episode = episode;
            EndClip();
            _deferredInsert = 0;
            HintOff();
        }

        /// <summary>Потеря контекста или смена автора: жест и нажатие не наследуются (T32, T37).</summary>
        internal void LoseContext()
        {
            ResetGesture();
            _episode = TriggerEpisode.Consumed;
            HintOff();
        }

        internal void SetEpisode(TriggerEpisode episode) => _episode = episode;

        internal void StartClip(ClipKind kind)
        {
            bool has = kind == ClipKind.Fire ? _axes.HasFireClip : _axes.HasEmptyClip;
            if (!has) { EndClip(); return; }
            _clip = kind;
            _clipTime = 0f;
            _clipEnd = kind == ClipKind.Fire ? _axes.FireClipDuration : _axes.HoldsOpen ? _axes.EmptyRearTime : _axes.EmptyClipDuration;
            _clipShot = L.ShotSequence;
            _clipActionDetached = S.Held;
        }

        internal void EndClip()
        {
            _clip = ClipKind.None;
            _clipTime = 0f; _clipEnd = 0f; _clipShot = 0;
            _clipActionDetached = false;
        }

        /// <summary>T63: конец Empty-клипа; у HoldOpen Action встаёт на задержку со щелчком.</summary>
        internal void EndEmptyClip()
        {
            if (M == MechanismState.HoldOpen && !_clipActionDetached && !S.Held)
                _out.Cue(WeaponCue.SlideLockCatch, WeaponNotReadyReason.None);
            EndClip();
        }

        // ---------- Действия строк: отклик автора ----------

        /// <summary>Отказ спуска с откликом: сухой щелчок и вибрация (И7: один раз на нажатие).</summary>
        internal void Deny(WeaponNotReadyReason reason, WeaponHapticCue haptic)
        {
            _episode = TriggerEpisode.Consumed;
            if (!AuthorOnly()) return;
            _out.Cue(WeaponCue.DryFire, reason);
            _out.Haptic(haptic);
        }

        /// <summary>T53: NotReady с причиной; при ChamberingRequired — защёлка подсказки.</summary>
        internal void DenyNotReady()
        {
            WeaponNotReadyReason reason = WeaponMechanism.ReasonOf(L);
            Deny(reason, WeaponHapticCue.NotReady);
            if (reason == WeaponNotReadyReason.ChamberingRequired && !_hintLatched && Role == WeaponRole.Author)
            {
                _hintLatched = true;
                _out.Hint(true);
            }
        }

        /// <summary>OtherDenied: нажатие израсходовано без отклика о патронах.</summary>
        internal void ConsumeSilently() => _episode = TriggerEpisode.Consumed;

        internal void ReportFault(bool requestResync)
        {
            ResetGesture();
            _out.Report(new WeaponReport(WeaponReportKind.LedgerFaulted, _row.Id));
            if (requestResync) _out.Report(new WeaponReport(WeaponReportKind.ResyncRequested, _row.Id));
        }

        private void AuthorHaptic(WeaponHapticCue cue)
        {
            if (AuthorOnly()) _out.Haptic(cue);
        }

        private bool AuthorOnly()
        {
            if (Role == WeaponRole.Author) return true;
            Violations++;
            _out.Report(new WeaponReport(WeaponReportKind.TableViolation, _row?.Id));
            return false;
        }

        private void HintOff()
        {
            if (!_hintLatched) return;
            _hintLatched = false;
            _out?.Hint(false);
        }
    }
}
