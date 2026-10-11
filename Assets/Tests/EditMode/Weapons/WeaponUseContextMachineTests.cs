using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Контекст использования в чистой машине (этап use-context, принят пользователем в шлеме 2026-10-11).
    /// <see cref="WeaponContext.HasAuthorContext"/> отделён от <see cref="WeaponContext.MainGripLocal"/>: equipment-контекст
    /// (NPC держит ствол сокетом, без SDK-хвата) повторяет Initialize и получает подготовку, но не жест ручки и не приём
    /// патрона — им нужна локальная рука. Подготовка (бот) — только при <c>IsWorldAuthority ∧ AutomationAllowed</c>.
    ///
    /// Классы ошибок: equipment-ствол навсегда остаётся Uninitialized; подготовка без разрешения хоста; жест/патрон
    /// без руки; 6-параметровый конструктор (VR-рука) меняет прежнее поведение. Проверяется только наблюдаемый вывод:
    /// строка таблицы, команды учёту, состояние машины.
    /// </summary>
    public class WeaponUseContextMachineTests
    {
        private const int Mag = 1;
        private const ulong Cartridge = 7;

        // ── Профили ─────────────────────────────────────────────────────────────────────────

        private static WeaponProfileAxes Pistol() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.7f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.2f);

        /// <summary>Ствол с приёмом патрона по одному (FixedStore), затвор с пружиной.</summary>
        private static WeaponProfileAxes FixedStore() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.FixedStoreChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasFixedStoreIntake: true, releasedAction: WeaponReleasedAction.Spring);

        // ── Снимки учёта ────────────────────────────────────────────────────────────────────

        private static LedgerView Uninitialized() => new LedgerView(magazinePresent: true, magazineRounds: 7, capacity: 7, magazineToken: Mag);

        /// <summary>Патрон в патроннике, в запасе есть место (Total 4 &lt; Capacity 5).</summary>
        private static LedgerView Ready() => new LedgerView(initialized: true, chamber: true, magazinePresent: true, magazineRounds: 3,
            capacity: 5, magazineToken: Mag);

        private static LedgerView Empty() => new LedgerView(initialized: true, magazinePresent: true, magazineRounds: 0, capacity: 5,
            magazineToken: Mag);

        private static LedgerView EmptyAwaitRest() => new LedgerView(initialized: true, slideLocked: true, magazinePresent: true,
            magazineRounds: 0, capacity: 5, shotSequence: 1, magazineToken: Mag);

        private static LedgerView Cycle() => new LedgerView(initialized: true, chamber: true, actionOpen: true, cyclePending: true,
            magazinePresent: true, magazineRounds: 3, capacity: 5, cycleSequence: 1, magazineToken: Mag, cycleMagazineToken: Mag);

        // ── Контексты и замеры ──────────────────────────────────────────────────────────────

        /// <summary>VR-рука автора: прежний 6-параметровый конструктор.</summary>
        private static WeaponContext Hand(bool world = false) => new WeaponContext(WeaponRole.Author, true, isWorldAuthority: world);

        /// <summary>Equipment: контекст автора есть, локальной руки на основной рукояти нет.</summary>
        private static WeaponContext Equipment(bool world, bool automation) => Full(mainGripLocal: false, authorContext: true, world, automation);

        private static WeaponContext Full(bool mainGripLocal, bool authorContext, bool world, bool automation) =>
            new WeaponContext(WeaponRole.Author, mainGripLocal, false, WeaponBlockReason.None, 0f, world, authorContext, automation);

        private static readonly ActionSample HeldAtRest = new ActionSample(true, true, 0f, 0f, true, false);
        private static ActionSample Held(float progress) => new ActionSample(true, true, progress, progress, false, false);
        private static ActionSample Released(float progress) => new ActionSample(false, false, progress, progress, false, false);

        private static int Initializes(Recorder output) => output.Commands.Count(command => command.Kind == LedgerCommandKind.Initialize);

        // ── Повтор Initialize ───────────────────────────────────────────────────────────────

        [Test]
        public void Equipment_контекст_повторяет_Initialize_не_чаще_паузы()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            // Старт (T01) отклонён учётом: например, NPC-владелец ещё не заспавнен.
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), ActionSample.AtRest, Equipment(true, true));
            Assert.That(Initializes(output), Is.EqualTo(1), "Контроль: T01 при настройке.");
            Step(machine, output, WeaponEvent.RejectedCommand(LedgerCommandKind.Initialize), Uninitialized(), ActionSample.AtRest,
                Equipment(true, true));

            // Ствол закреплён сокетом: руки на рукояти нет, но контекст автора есть — повтор, как у VR-руки.
            for (int window = 0; window < 3; window++)
            {
                int sent = 0;
                for (int frame = 0; frame < 4; frame++) // 4 × InitRetryStep = InitRetrySeconds
                {
                    Step(machine, output, WeaponEvent.Tick(InitRetryStep), Uninitialized(), ActionSample.AtRest, Equipment(true, true));
                    if (Initializes(output) > 0) Assert.That(machine.LastRowId, Is.EqualTo("T01t"));
                    sent += Initializes(output);
                }
                Assert.That(sent, Is.EqualTo(1), $"Окно {window}: ровно одна попытка за {WeaponStateMachine.InitRetrySeconds} с без MainGripLocal.");
            }
        }

        [Test]
        public void Equipment_контекст_повторяет_Initialize_первым_же_кадром()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), ActionSample.AtRest, Full(false, false, true, true));
            Step(machine, output, WeaponEvent.RejectedCommand(LedgerCommandKind.Initialize), Uninitialized(), ActionSample.AtRest,
                Full(false, false, true, true));
            for (int frame = 0; frame < 10; frame++)
            {
                // Мир и разрешение подготовки есть, контекста автора нет: Tick повтор не даёт (его гейт — HasAuthorContext).
                Step(machine, output, WeaponEvent.Tick(InitRetryStep), Uninitialized(), ActionSample.AtRest, Full(false, false, true, true));
                Assert.That(output.Commands, Is.Empty, "Без HasAuthorContext повтора нет: учёт не спамится каждый кадр.");
            }

            Step(machine, output, WeaponEvent.Tick(0.016f), Uninitialized(), ActionSample.AtRest, Equipment(true, false));
            Assert.That(machine.LastRowId, Is.EqualTo("T01t"));
            Assert.That(Initializes(output), Is.EqualTo(1), "Первый кадр с equipment-контекстом — сразу повтор (без руки и без автоматики).");
        }

        [Test]
        public void Без_контекста_автора_Tick_не_повторяет_Initialize()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), ActionSample.AtRest, Full(false, false, false, false));
            Step(machine, output, WeaponEvent.RejectedCommand(LedgerCommandKind.Initialize), Uninitialized(), ActionSample.AtRest,
                Full(false, false, false, false));
            for (int frame = 0; frame < 12; frame++)
            {
                Step(machine, output, WeaponEvent.Tick(InitRetryStep), Uninitialized(), ActionSample.AtRest, Full(false, false, false, false));
                Assert.That(output.Commands, Is.Empty, $"Кадр {frame}: ни руки, ни equipment — команд нет.");
            }
        }

        // ── Подготовка (бот) ────────────────────────────────────────────────────────────────

        private static IEnumerable<TestCaseData> AutomationCases()
        {
            yield return new TestCaseData(Uninitialized(), "T44i", new[] { LedgerCommandKind.Initialize }).SetName("{m}(T44i_Uninitialized)");
            yield return new TestCaseData(Empty(), "T45", new[] { LedgerCommandKind.RefillForAutomation }).SetName("{m}(T45_Empty)");
            yield return new TestCaseData(EmptyAwaitRest(), "T44",
                new[] { LedgerCommandKind.AckEmptyRest, LedgerCommandKind.RefillForAutomation }).SetName("{m}(T44_EmptyAwaitRest)");
        }

        [TestCaseSource(nameof(AutomationCases))]
        public void Подготовка_в_equipment_только_при_мире_и_разрешении(LedgerView view, string row, LedgerCommandKind[] expected)
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), view, ActionSample.AtRest, Equipment(true, true));
            Assert.That(machine.LastRowId, Is.EqualTo(row), "Мир и AutomationAllowed: подготовка без руки на рукояти.");
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(expected));

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), view, ActionSample.AtRest, Equipment(true, false));
            Assert.That(machine.LastRowId, Is.EqualTo("T47"), "HasAuthorContext без AutomationAllowed: хост не разрешил подготовку.");
            Assert.That(output.Commands, Is.Empty);

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), view, ActionSample.AtRest, Equipment(false, true));
            Assert.That(machine.LastRowId, Is.EqualTo("T47"), "AutomationAllowed без мира (не сервер/офлайн): подготовки нет.");
            Assert.That(output.Commands, Is.Empty);

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), view, ActionSample.AtRest, Hand(world: false));
            Assert.That(machine.LastRowId, Is.EqualTo("T47"), "VR-рука клиента (не мир): подготовки нет, как до этапа.");
            Assert.That(output.Commands, Is.Empty);
        }

        // ── Жест ручки и приём патрона требуют локальной руки ───────────────────────────────

        [Test]
        public void Equipment_без_руки_не_начинает_ход_Action_а_VR_рука_начинает()
        {
            var equipment = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(equipment, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest, Equipment(true, true));
            Step(equipment, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f), Equipment(true, true));
            Assert.That(equipment.LastRowId, Is.Not.EqualTo("T21"), "Contacting требует MainGripLocal.");
            Assert.That(output.Commands, Is.Empty, "Equipment-контекст без руки не выдаёт BeginAction по ходу ручки.");

            // Контроль: то же для VR-руки (6-параметровый конструктор) начинает цикл.
            var hand = new WeaponStateMachine(Pistol());
            Step(hand, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest, Hand());
            Assert.That(hand.LastRowId, Is.EqualTo("T20"));
            Step(hand, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f), Hand());
            Assert.That(hand.LastRowId, Is.EqualTo("T21"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.BeginAction }));
        }

        [Test]
        public void Equipment_без_руки_не_предлагает_патрон_а_VR_рука_предлагает()
        {
            var machine = new WeaponStateMachine(FixedStore());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Offered(Cartridge), Ready(), ActionSample.AtRest, Equipment(true, true));
            Assert.That(machine.LastRowId, Is.EqualTo("T72"), "CanOfferCartridge требует MainGripLocal.");
            Assert.That(output.Commands, Is.Empty, "Equipment-контекст без руки не запрашивает приём патрона.");

            Step(machine, output, WeaponEvent.Offered(Cartridge), Ready(), ActionSample.AtRest, Hand());
            Assert.That(machine.LastRowId, Is.EqualTo("T70"));
            Assert.That(output.Commands.Select(c => (c.Kind, c.Token)), Is.EqualTo(new[] { (LedgerCommandKind.RequestAdmission, Cartridge) }));
        }

        // ── 6-параметровый конструктор: прежнее поведение VR-руки ───────────────────────────

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void Шестипараметровый_конструктор_выводит_контекст_из_руки(bool mainGripLocal, bool world)
        {
            var legacy = new WeaponContext(WeaponRole.Author, mainGripLocal, isWorldAuthority: world);
            Assert.That(legacy.HasAuthorContext, Is.EqualTo(mainGripLocal), "HasAuthorContext = MainGripLocal.");
            Assert.That(legacy.AutomationAllowed, Is.EqualTo(world && mainGripLocal), "AutomationAllowed = мир ∧ рука.");
            Assert.That(legacy.MainGripLocal, Is.EqualTo(mainGripLocal));
            Assert.That(legacy.IsWorldAuthority, Is.EqualTo(world));
        }

        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public void Шестипараметровый_конструктор_ведёт_машину_как_явный_контекст_руки(bool mainGripLocal, bool world)
        {
            // Один сценарий на двух машинах: прежний конструктор и 8-параметровый с теми же выведенными значениями.
            var legacyContext = new WeaponContext(WeaponRole.Author, mainGripLocal, isWorldAuthority: world);
            WeaponContext explicitContext = Full(mainGripLocal, mainGripLocal, world, world && mainGripLocal);
            var legacy = new WeaponStateMachine(FixedStore());
            var explicitMachine = new WeaponStateMachine(FixedStore());
            var legacyOutput = new Recorder();
            var explicitOutput = new Recorder();

            int index = 0;
            foreach ((WeaponEvent e, LedgerView l, ActionSample s) in Scenario())
            {
                legacyOutput.Reset(); explicitOutput.Reset();
                legacy.Step(e, l, s, legacyContext, legacyOutput);
                explicitMachine.Step(e, l, s, explicitContext, explicitOutput);
                string where = $"шаг {index++} ({e.Kind})";
                Assert.That(explicitMachine.LastRowId, Is.EqualTo(legacy.LastRowId), $"{where}: строка таблицы.");
                Assert.That(explicitOutput.Log, Is.EqualTo(legacyOutput.Log), $"{where}: вывод машины.");
                Assert.That(explicitMachine.State, Is.EqualTo(legacy.State), $"{where}: состояние машины.");
            }
        }

        /// <summary>Сценарий, проходящий через все строки с контекстом: повтор, жест, цикл, патрон, подготовка, спуск.</summary>
        private static IEnumerable<(WeaponEvent, LedgerView, ActionSample)> Scenario()
        {
            yield return (WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), ActionSample.AtRest);
            yield return (WeaponEvent.RejectedCommand(LedgerCommandKind.Initialize), Uninitialized(), ActionSample.AtRest);
            for (int frame = 0; frame < 6; frame++) yield return (WeaponEvent.Tick(InitRetryStep), Uninitialized(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.MagazineChanged), Uninitialized(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), Uninitialized(), ActionSample.AtRest);
            yield return (WeaponEvent.Committed(LedgerOp.Initialize, false, true), Ready(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f));
            yield return (WeaponEvent.Of(WeaponEventKind.HandleReleased), Cycle(), Released(0.5f));
            yield return (WeaponEvent.Of(WeaponEventKind.ActionSampled), Cycle(), ActionSample.AtRest);
            yield return (WeaponEvent.Offered(Cartridge), Ready(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), EmptyAwaitRest(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), Empty(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.TriggerPressed), Ready(), ActionSample.AtRest);
            yield return (WeaponEvent.Of(WeaponEventKind.TriggerReleased), Ready(), ActionSample.AtRest);
        }

        // ── Инфраструктура ──────────────────────────────────────────────────────────────────

        private const float InitRetryStep = WeaponStateMachine.InitRetrySeconds / 4f;

        private static void Step(WeaponStateMachine machine, Recorder output, WeaponEvent e, LedgerView l, ActionSample s, WeaponContext c)
        {
            output.Reset();
            machine.Step(e, l, s, c, output);
            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
        }

        private sealed class Recorder : IWeaponOutput
        {
            public readonly List<LedgerCommand> Commands = new List<LedgerCommand>();
            /// <summary>Весь вывод шага в порядке выдачи — для сравнения двух машин.</summary>
            public readonly List<string> Log = new List<string>();
            public int Violations;

            public void Reset() { Commands.Clear(); Log.Clear(); Violations = 0; }

            public void Command(in LedgerCommand command)
            {
                Commands.Add(command);
                Log.Add($"Command {command.Kind} rev={command.ExpectedRevision} seq={command.CycleSequence} " +
                        $"mag={command.ExpectedMagazineToken} token={command.Token} origin={command.Origin}");
            }

            public void Pose(in PoseTarget target) =>
                Log.Add($"Pose {target.Action} {target.Auxiliary} clip={target.ClipTime} speed={target.ReturnSpeed}");

            public void Cue(WeaponCue cue, WeaponNotReadyReason reason) => Log.Add($"Cue {cue} {reason}");
            public void Haptic(WeaponHapticCue cue) => Log.Add($"Haptic {cue}");
            public void Hint(bool on) => Log.Add($"Hint {on}");

            public void Report(in WeaponReport report)
            {
                Log.Add($"Report {report.Kind}");
                if (report.Kind == WeaponReportKind.TableViolation || report.Kind == WeaponReportKind.Unhandled) Violations++;
            }
        }
    }
}
