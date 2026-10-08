using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Структурные контракты машины оружия (план WeaponSystem, п. 3.5–3.7, решение В2): полнота таблицы (И8),
    /// наблюдатель без команд (И9), при HoldOpen без руки нет возврата в покой (И10, класс ошибок 1),
    /// клип всегда завершается и не переживает снимок/выключение (И11, класс 4), ровно одна цель позы за шаг (И13),
    /// отпущенный Action при оси Stay не возвращается пружиной (S4), нажатие до конца темпа не выдаёт команд (S2),
    /// Derive и недопустимые оси. Это архитектурные свойства, а не настройка механики: поведенческие сценарии
    /// S01–S33 пишутся после приёмки пилота в шлеме.
    ///
    /// Класс ошибок, который ловят тесты: решение «что делать» разъезжалось по флагам нескольких компонентов,
    /// и клетка состояние × событие оставалась без владельца (зависшая фаза показа, пружина против HoldOpen).
    /// Здесь у каждой клетки есть строка, а поза выводится только из состояния.
    /// </summary>
    public class WeaponStateMachineStructureTests
    {
        // ── Профили, на которых перебираются состояния ─────────────────────────────────────

        private static WeaponProfileAxes HoldOpenRifle() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.HoldOpen, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasEmptyClip: true, emptyClipDuration: 0.4f, emptyRearTime: 0.2f);

        private static WeaponProfileAxes FixedStorePump() => new WeaponProfileAxes(
            WeaponFireMode.Manual, WeaponAmmoCapability.FixedStoreChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.9f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasFixedStoreIntake: true);

        /// <summary>Помпа без пружины отпущенной ручки и без Empty-клипа — как FABARM (S1, S4).</summary>
        private static WeaponProfileAxes StayPump() => new WeaponProfileAxes(
            WeaponFireMode.Manual, WeaponAmmoCapability.FixedStoreChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.9f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasFixedStoreIntake: true, releasedAction: WeaponReleasedAction.Stay);

        private static WeaponProfileAxes AssistPistol() => new WeaponProfileAxes(
            WeaponFireMode.Auto, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.TriggerAssistPrepareOnly, WeaponEmptyPose.ReturnToRest, extractionGate: 0.7f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasEmptyClip: true, emptyClipDuration: 0.4f);

        private static WeaponProfileAxes InsertHoldOpen() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.HoldOpen, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasEmptyClip: true, emptyClipDuration: 0.4f, emptyRearTime: 0.4f);

        private static WeaponProfileAxes NoActionAuto() => new WeaponProfileAxes(
            WeaponFireMode.Auto, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.NoAction,
            WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.ReturnToRest, extractionGate: 0f, epsilon: 0f,
            springReturnSpeed: 0f, autoReturnSpeed: 0f, hasFireClip: true, fireClipDuration: 0.1f);

        private static WeaponProfileAxes MagazineOnlyRevolver() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.MagazineOnly, WeaponPhysicalCapability.NoAction,
            WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.ReturnToRest, extractionGate: 0f, epsilon: 0f,
            springReturnSpeed: 0f, autoReturnSpeed: 0f, hasFireClip: true, fireClipDuration: 0.2f);

        private static IEnumerable<TestCaseData> Profiles()
        {
            yield return new TestCaseData(HoldOpenRifle()).SetName("HoldOpenRifle");
            yield return new TestCaseData(FixedStorePump()).SetName("FixedStorePump");
            yield return new TestCaseData(StayPump()).SetName("StayPump");
            yield return new TestCaseData(AssistPistol()).SetName("AssistPistol");
            yield return new TestCaseData(InsertHoldOpen()).SetName("InsertHoldOpen");
            yield return new TestCaseData(NoActionAuto()).SetName("NoActionAuto");
            yield return new TestCaseData(MagazineOnlyRevolver()).SetName("MagazineOnlyRevolver");
        }

        // ── Таблица ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Table_EveryStateEventRoleCell_EndsWithUnconditionalRow()
        {
            var holes = new List<string>();
            foreach (MechanismState state in Enum.GetValues(typeof(MechanismState)))
            foreach (WeaponEventKind kind in Enum.GetValues(typeof(WeaponEventKind)))
            foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
                if (!WeaponTransitions.Table.Any(row => row.Covers(state, kind, role) && row.IsTerminal))
                    holes.Add($"{state} × {kind} × {role}");
            Assert.That(holes, Is.Empty, "И8: клетки без строки-«иначе» — машина молча пропустит событие:\n" + string.Join("\n", holes));
        }

        [Test]
        public void Table_NoRowIsShadowedByEarlierUnconditionalRows()
        {
            var dead = new List<string>();
            TransitionRow[] table = WeaponTransitions.Table;
            for (int index = 0; index < table.Length; index++)
            {
                TransitionRow row = table[index];
                bool reachable = false;
                foreach (MechanismState state in Enum.GetValues(typeof(MechanismState)))
                foreach (WeaponRole role in Enum.GetValues(typeof(WeaponRole)))
                {
                    if (!row.Covers(state, row.On, role)) continue;
                    bool shadowed = false;
                    for (int earlier = 0; earlier < index && !shadowed; earlier++)
                        shadowed = table[earlier].IsTerminal && table[earlier].Covers(state, row.On, role);
                    reachable |= !shadowed;
                }
                if (!reachable) dead.Add(row.ToString());
            }
            Assert.That(dead, Is.Empty, "Строки, которые никогда не применятся (их закрывают строки выше):\n" + string.Join("\n", dead));
        }

        [Test]
        public void Table_RowsThatMayCommand_AreAuthorOnly_AndIdsAreUnique()
        {
            foreach (TransitionRow row in WeaponTransitions.Table)
            {
                Assert.That(row.Doc, Is.Not.Empty, row.Id);
                if (row.MayCommand) Assert.That(row.Roles, Is.EqualTo(RoleSet.Author), $"И9: {row.Id} выдаёт команды не только автору.");
                if (row.IsIgnore) Assert.That(row.MayCommand, Is.False, row.Id);
            }
            var duplicates = WeaponTransitions.Table.GroupBy(row => row.Id).Where(group => group.Count() > 1).Select(group => group.Key);
            Assert.That(duplicates, Is.Empty);
        }

        // ── Перебор достижимых состояний ───────────────────────────────────────────────────

        [TestCaseSource(nameof(Profiles))]
        public void ReachableStates_KeepStructuralInvariants(WeaponProfileAxes axes)
        {
            var output = new RecordingOutput();
            var failures = new List<string>();
            int states = Explore(axes, (before, e, l, s, c, after) =>
            {
                MechanismState mechanism = WeaponMechanism.Derive(l, axes);
                void Fail(string what)
                {
                    if (failures.Count < 50)
                        failures.Add($"{what}: {e.Kind}/{e.Op} в {mechanism} [{before}] роль={c.Role} replay={c.InsideReplay} " +
                                     $"ctx={c.MainGripLocal} held={s.Held} p={s.HandleProgress} → {after.LastRowId}");
                }
                if (output.Poses != 1) Fail($"И13: целей позы {output.Poses}");
                if (output.Unhandled + output.TableViolations != 0 || after.Violations != 0) Fail("И8/И9: нарушение таблицы");
                if (!c.ActsAsAuthor && output.Commands.Count + output.Haptics + output.HintsOn + output.Denials != 0)
                    Fail("И9/И7: наблюдатель выдал команду или авторский отклик");
                if (output.Denials > 1) Fail("И7: больше одного отклика отказа за нажатие");
                if (e.Kind == WeaponEventKind.TriggerPressed && c.RofTimer > 0f && c.Blocked == WeaponBlockReason.None &&
                    (output.Commands.Count != 0 || output.HintsOn != 0))
                    Fail("S2: нажатие до конца таймера темпа выдало команду или подсказку");
                if (output.Commands.Count > 2) Fail("Больше двух команд за шаг");
                if (mechanism == MechanismState.HoldOpen && !s.Held &&
                    after.LastPose.Action != PosePresentation.EmptyClip && after.LastPose.Action != PosePresentation.HoldRear)
                    Fail($"И10: при HoldOpen без руки цель {after.LastPose.Action}");
                if ((e.Kind == WeaponEventKind.SnapshotLoaded || e.Kind == WeaponEventKind.Disabled ||
                     e.Kind == WeaponEventKind.Configured) && after.State.Clip != ClipKind.None)
                    Fail("И11: клип пережил снимок/выключение/настройку");
                if (e.Kind == WeaponEventKind.Tick && e.Dt >= LongTick && after.State.Clip != ClipKind.None)
                    Fail("И11: клип не завершился по времени");
                if (after.State.Gesture != GestureState.None && after.State.Gesture != GestureState.Contact &&
                    after.State.Origin != ChamberOrigin.Manual)
                    Fail("Жест цикла без своего ручного цикла");
            }, output);

            Assert.That(states, Is.GreaterThan(10), $"Обход не вышел за начальное состояние ({states}) — набор входов неверен.");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void ComputePose_ReleasedActionStay_NeverSpringsWithoutPreparation()
        {
            WeaponProfileAxes stay = StayPump();
            foreach (MechanismState state in Enum.GetValues(typeof(MechanismState)))
            foreach (ClipKind clip in Enum.GetValues(typeof(ClipKind)))
            foreach (bool atRest in new[] { false, true })
            foreach (ChamberOrigin origin in Enum.GetValues(typeof(ChamberOrigin)))
            {
                var sample = new ActionSample(false, false, atRest ? 0f : 0.5f, atRest ? 0f : 0.5f, atRest, false);
                PoseTarget pose = WeaponStateMachine.ComputePose(state, stay, sample, clip, 0.1f, false, origin);
                bool preparing = origin == ChamberOrigin.Insert || origin == ChamberOrigin.Assist || origin == ChamberOrigin.Automation;
                bool cycle = state == MechanismState.CycleLoaded || state == MechanismState.CycleCleared || state == MechanismState.OpenIdle;
                string where = $"S4: state={state} clip={clip} atRest={atRest} origin={origin}";
                if (preparing && cycle)
                    Assert.That(pose.Action, Is.EqualTo(PosePresentation.ReturnToRest), where + " — подготовку без руки возвращает машина");
                else
                    Assert.That(pose.Action, Is.Not.EqualTo(PosePresentation.ReturnToRest), where);
            }
        }

        [Test]
        public void ComputePose_HoldOpenWithoutHand_NeverRestsOrReturns()
        {
            WeaponProfileAxes axes = HoldOpenRifle();
            foreach (ClipKind clip in Enum.GetValues(typeof(ClipKind)))
            foreach (bool detached in new[] { false, true })
            foreach (bool atRest in new[] { false, true })
            foreach (ChamberOrigin origin in Enum.GetValues(typeof(ChamberOrigin)))
            {
                var sample = new ActionSample(false, false, atRest ? 0f : 1f, atRest ? 0f : 1f, atRest, !atRest);
                PoseTarget pose = WeaponStateMachine.ComputePose(MechanismState.HoldOpen, axes, sample, clip, 0.1f, detached, origin);
                Assert.That(pose.Action, Is.EqualTo(PosePresentation.EmptyClip).Or.EqualTo(PosePresentation.HoldRear),
                    $"И10: clip={clip} detached={detached} atRest={atRest} origin={origin}");
            }
        }

        // ── Derive, оси, повторный вход ────────────────────────────────────────────────────

        [Test]
        public void Derive_IsTotal_AndMatchesPlanTable()
        {
            WeaponProfileAxes holdOpen = HoldOpenRifle(), returnToRest = FixedStorePump(), noAction = NoActionAuto();
            for (int bits = 0; bits < 1 << 7; bits++)
            {
                bool initialized = (bits & 1) != 0, chamber = (bits & 2) != 0, open = (bits & 4) != 0, pending = (bits & 8) != 0,
                     locked = (bits & 16) != 0, faulted = (bits & 32) != 0, admission = (bits & 64) != 0;
                var view = new LedgerView(initialized, chamber, open, pending, locked, faulted, admission, true, 1, 5);
                foreach (WeaponProfileAxes axes in new[] { holdOpen, returnToRest, noAction })
                {
                    MechanismState state = WeaponMechanism.Derive(view, axes);
                    MechanismState expected =
                        faulted ? MechanismState.Faulted :
                        !initialized ? MechanismState.Uninitialized :
                        pending ? (chamber ? MechanismState.CycleLoaded : MechanismState.CycleCleared) :
                        open ? MechanismState.OpenIdle :
                        chamber ? MechanismState.Ready :
                        locked ? (axes.HoldsOpen ? MechanismState.HoldOpen : MechanismState.EmptyAwaitRest) :
                        MechanismState.Empty;
                    Assert.That(state, Is.EqualTo(expected), $"bits={bits} axes={axes.EmptyPose}/{axes.Physical}");

                    // И3: готовность к выстрелу ⇔ C ∧ ¬Open ∧ ¬Pending ∧ Initialized ∧ ¬Faulted ∧ ¬AdmissionPending.
                    bool ready = chamber && !open && !pending && initialized && !faulted && !admission;
                    Assert.That(WeaponMechanism.CanFire(view, axes), Is.EqualTo(ready), $"И3 bits={bits}");
                }
            }
        }

        [Test]
        public void Derive_MagazineOnly_IsReadyByMagazineRounds()
        {
            WeaponProfileAxes axes = MagazineOnlyRevolver();
            Assert.That(WeaponMechanism.Derive(new LedgerView(initialized: true, magazinePresent: true, magazineRounds: 3), axes),
                Is.EqualTo(MechanismState.Ready));
            Assert.That(WeaponMechanism.Derive(new LedgerView(initialized: true, magazinePresent: true, magazineRounds: 0), axes),
                Is.EqualTo(MechanismState.Empty));
        }

        [Test]
        public void TryValidate_RejectsPlanForbiddenCombinations()
        {
            Assert.That(WeaponProfileAxes.TryValidate(HoldOpenRifle(), out _), Is.True);
            Assert.That(WeaponProfileAxes.TryValidate(FixedStorePump(), out _), Is.True);
            Assert.That(WeaponProfileAxes.TryValidate(StayPump(), out _), Is.True);
            Assert.That(WeaponProfileAxes.TryValidate(NoActionAuto(), out _), Is.True);
            Assert.That(WeaponProfileAxes.TryValidate(MagazineOnlyRevolver(), out _), Is.True);

            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, 0f, 0f, 0f, 0f),
                "NoAction + ManualReturn");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.HoldOpen, 0f, 0f, 0f, 0f),
                "NoAction + HoldOpen");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.AutoOnMagazineInsert, WeaponEmptyPose.ReturnToRest, 0f, 0f, 0f, 0f,
                releasedAction: WeaponReleasedAction.Stay),
                "NoAction + Stay");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, 0.8f, 0.02f, 4f, 2f,
                releasedAction: (WeaponReleasedAction)7),
                "неизвестное значение оси отпущенного Action");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.MagazineOnly,
                WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.TriggerAssistPrepareOnly, WeaponEmptyPose.ReturnToRest, 0f, 0f, 0f, 0f),
                "MagazineOnly + политика досылания");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Manual, WeaponAmmoCapability.FixedStoreChamber,
                WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, 0.9f, 0.02f, 4f, 2f),
                "FixedStore без окна приёма");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.HoldOpen, 0.8f, 0.02f, 4f, 2f,
                hasEmptyClip: true, emptyClipDuration: 0.4f),
                "HoldOpen без проверенной задней позы");
            AssertInvalid(new WeaponProfileAxes(WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber,
                WeaponPhysicalCapability.ActionTravel, WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, 0f, 0.02f, 4f, 2f),
                "нулевой порог извлечения");
            Assert.Throws<ArgumentException>(() => new WeaponStateMachine(new WeaponProfileAxes(WeaponFireMode.Semi,
                WeaponAmmoCapability.MagazineOnly, WeaponPhysicalCapability.NoAction, WeaponChamberPolicy.ManualReturn,
                WeaponEmptyPose.ReturnToRest, 0f, 0f, 0f, 0f)));
        }

        [Test]
        public void Step_FromInsideOutput_IsRejected()
        {
            var machine = new WeaponStateMachine(HoldOpenRifle());
            var output = new RecordingOutput();
            output.OnPose = () => machine.Step(WeaponEvent.Tick(0.1f), default, ActionSample.AtRest,
                new WeaponContext(WeaponRole.Author, true), new RecordingOutput());
            Assert.Throws<InvalidOperationException>(() =>
                machine.Step(WeaponEvent.Tick(0.1f), default, ActionSample.AtRest, new WeaponContext(WeaponRole.Author, true), output));
        }

        // ── Перебор ─────────────────────────────────────────────────────────────────────────

        private const float LongTick = 10f;
        private const int WalkSteps = 150000;
        private const int WalkRestart = 300;

        private delegate void StepCheck(WeaponMachineState before, WeaponEvent e, LedgerView l, ActionSample s,
            WeaponContext c, WeaponStateMachine after);

        /// <summary>
        /// Обход достижимых состояний случайным блужданием с фиксированным зерном (воспроизводимо): из
        /// текущего состояния — случайное событие, снимок учёта, замер и контекст из наборов ниже; каждые
        /// <see cref="WalkRestart"/> шагов — новая машина. Полный обход в ширину не замыкается: регионы машины
        /// ортогональны (жест × эпизод × клип × подсказка × отложенная вставка), их произведение ~10⁴ состояний
        /// на ~3·10⁴ входов. Блуждание попадает в каждую клетку состояние × событие × роль сотни раз.
        /// Учёт подаётся независимо от команд, поэтому состояний не меньше, чем в игре.
        /// </summary>
        private static int Explore(WeaponProfileAxes axes, StepCheck check, RecordingOutput output)
        {
            WeaponEvent[] events = Events();
            LedgerView[] views = Views();
            ActionSample[] samples = Samples(axes);
            WeaponContext[] contexts = Contexts();
            var random = new Random(20261007);
            var seen = new HashSet<WeaponMachineState>();
            WeaponStateMachine machine = null;
            for (int step = 0; step < WalkSteps; step++)
            {
                if (step % WalkRestart == 0) machine = new WeaponStateMachine(axes);
                WeaponEvent e = events[random.Next(events.Length)];
                LedgerView l = views[random.Next(views.Length)];
                ActionSample s = samples[random.Next(samples.Length)];
                WeaponContext c = contexts[random.Next(contexts.Length)];
                WeaponMachineState before = machine.State;
                output.Reset();
                machine.Step(e, l, s, c, output);
                check(before, e, l, s, c, machine);
                seen.Add(machine.State);
            }
            return seen.Count;
        }

        private static WeaponEvent[] Events()
        {
            var list = new List<WeaponEvent>();
            foreach (WeaponEventKind kind in Enum.GetValues(typeof(WeaponEventKind)))
                if (kind != WeaponEventKind.LedgerCommitted && kind != WeaponEventKind.Tick &&
                    kind != WeaponEventKind.CartridgeOffered && kind != WeaponEventKind.CommandRejected)
                    list.Add(WeaponEvent.Of(kind));
            list.Add(WeaponEvent.Tick(0.15f));
            list.Add(WeaponEvent.Tick(LongTick));
            list.Add(WeaponEvent.Offered(7));
            list.Add(WeaponEvent.RejectedCommand(LedgerCommandKind.CompleteChamber));
            list.Add(WeaponEvent.RejectedCommand(LedgerCommandKind.Cancel));
            list.Add(WeaponEvent.Committed(LedgerOp.Shot, true, true));
            list.Add(WeaponEvent.Committed(LedgerOp.Shot, true, false));
            list.Add(WeaponEvent.Committed(LedgerOp.Extract, true, false));
            list.Add(WeaponEvent.Committed(LedgerOp.Complete, false, true));
            list.Add(WeaponEvent.Committed(LedgerOp.CloseOnly, true, true));
            list.Add(WeaponEvent.Committed(LedgerOp.BeginAction, false, false));
            list.Add(WeaponEvent.Committed(LedgerOp.Cancel, false, false));
            return list.ToArray();
        }

        /// <summary>Снимки учёта: каждое механическое состояние × магазин (нет / пуст / с патронами), свой и чужой цикл.</summary>
        private static LedgerView[] Views()
        {
            var list = new List<LedgerView>
            {
                new LedgerView(),
                new LedgerView(initialized: true, faulted: true, chamber: true, magazinePresent: true, magazineRounds: 2, capacity: 5,
                    magazineToken: 1),
            };
            foreach (int rounds in new[] { -1, 0, 2 })
            {
                bool present = rounds >= 0;
                int token = present ? 1 : 0, count = Math.Max(rounds, 0);
                // C2: pending — уже действительный Pending (хост выводит его из гнезда); магазин цикла по умолчанию —
                // магазин в гнезде при Pending и «нет» без него.
                LedgerView V(bool chamber, bool open, bool pending, bool locked, uint cycle = 1, uint extracted = 0, bool admission = false,
                    int cycleMagazine = -1) =>
                    new LedgerView(true, chamber, open, pending, locked, false, admission, present, count, 5, 10, cycle, extracted, 3,
                        token, cycleMagazine >= 0 ? cycleMagazine : pending ? token : 0);
                list.Add(V(true, false, false, false));                  // Ready
                list.Add(V(false, false, false, false));                 // Empty
                list.Add(V(false, false, false, true));                  // HoldOpen / EmptyAwaitRest
                list.Add(V(true, true, false, false));                   // OpenIdle, C сохранён
                list.Add(V(false, true, false, false));                  // OpenIdle без C
                list.Add(V(true, true, true, false, cycle: 2));          // CycleLoaded — свой цикл (seq+1)
                list.Add(V(false, true, true, false, cycle: 2, extracted: 2)); // CycleCleared — свой цикл после извлечения
                list.Add(V(false, true, true, false, cycle: 7));         // CycleCleared — чужой цикл
                if (present)
                {
                    list.Add(V(true, false, false, false, admission: true));   // барьер приёма
                    list.Add(V(false, true, true, false, cycle: 2, extracted: 2, admission: true));
                    list.Add(V(false, true, false, false, cycleMagazine: 2));  // цикл начат с другим магазином: Pending недействителен
                }
            }
            return list.ToArray();
        }

        private static ActionSample[] Samples(WeaponProfileAxes axes)
        {
            if (axes.Physical == WeaponPhysicalCapability.NoAction) return new[] { ActionSample.AtRest };
            return new[]
            {
                ActionSample.AtRest,
                new ActionSample(true, true, 0f, 0f, true, false),       // держат в покое
                new ActionSample(true, true, 0.5f, 0.5f, false, false),  // держат посередине
                new ActionSample(true, true, 1f, 1f, false, true),       // держат в проверенном заду
                new ActionSample(false, false, 0.5f, 0.5f, false, false),// отпущена посередине
                new ActionSample(true, false, 0.5f, 0.5f, false, false), // держит чужая рука
            };
        }

        private static WeaponContext[] Contexts() => new[]
        {
            new WeaponContext(WeaponRole.Author, true, isWorldAuthority: true),
            new WeaponContext(WeaponRole.Author, false),
            new WeaponContext(WeaponRole.Author, true, blocked: WeaponBlockReason.Obstructed, rofTimer: 0.1f),
            new WeaponContext(WeaponRole.Author, true, rofTimer: 0.1f),
            new WeaponContext(WeaponRole.Author, true, insideReplay: true),
            new WeaponContext(WeaponRole.Observer, false),
        };

        private static void AssertInvalid(in WeaponProfileAxes axes, string what) =>
            Assert.That(WeaponProfileAxes.TryValidate(axes, out _), Is.False, what);

        private sealed class RecordingOutput : IWeaponOutput
        {
            public readonly List<LedgerCommand> Commands = new List<LedgerCommand>();
            public int Poses, Haptics, HintsOn, Denials, Unhandled, TableViolations;
            public Action OnPose;

            public void Reset()
            {
                Commands.Clear();
                Poses = Haptics = HintsOn = Denials = Unhandled = TableViolations = 0;
            }

            public void Command(in LedgerCommand command) => Commands.Add(command);
            public void Pose(in PoseTarget target) { Poses++; OnPose?.Invoke(); }
            public void Cue(WeaponCue cue, WeaponNotReadyReason reason) { if (cue == WeaponCue.DryFire || cue == WeaponCue.Refusal) Denials++; }
            public void Haptic(WeaponHapticCue cue) => Haptics++;
            public void Hint(bool on) { if (on) HintsOn++; }

            public void Report(in WeaponReport report)
            {
                if (report.Kind == WeaponReportKind.Unhandled) Unhandled++;
                if (report.Kind == WeaponReportKind.TableViolation) TableViolations++;
            }
        }
    }
}
