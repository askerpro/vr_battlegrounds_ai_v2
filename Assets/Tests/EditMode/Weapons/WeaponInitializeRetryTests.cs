using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Повтор Initialize (этап ejection, принят пользователем в шлеме 2026-10-09). Причина из лога: в начале раунда
    /// <c>UxrWeaponManager.WeaponSystemEnabled=false</c> → <c>CanUse=false</c> → учёт отклоняет Initialize у стволов, выданных
    /// в кобуру, и ствол навсегда оставался Uninitialized (спуск гасила T54u). Теперь у автора повтор, пока ствол не
    /// инициализирован: магазин (T16i), Tick в основной руке — сразу после хвата и затем не чаще <see cref="WeaponStateMachine.InitRetrySeconds"/>
    /// (T01t), смена автора (T37i), запрос бота (T44i), нажатие (T54i — <see cref="WeaponStateMachineDriveTests"/>).
    ///
    /// Классы ошибок: отказ при старте необратим; повтор спамит учёт каждый кадр; повтор после успешной инициализации;
    /// команда у наблюдателя (И9). Лог порта/хоста (одно Warning, далее Verbose, Info «со N-й попытки») здесь не проверяется:
    /// нужен настроенный <c>UxrFirearmWeapon</c> в Play.
    /// </summary>
    public class WeaponInitializeRetryTests
    {
        private const int Mag = 1;

        private static WeaponProfileAxes Pistol() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.7f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.2f);

        private static LedgerView Uninitialized() => new LedgerView(magazinePresent: true, magazineRounds: 7, capacity: 7, magazineToken: Mag);
        private static LedgerView Faulted() => new LedgerView(initialized: true, faulted: true, magazinePresent: true, magazineRounds: 7,
            capacity: 7, magazineToken: Mag);
        private static LedgerView Ready() => new LedgerView(initialized: true, chamber: true, magazinePresent: true, magazineRounds: 6,
            capacity: 7, magazineToken: Mag);

        private static WeaponContext Author(bool inHand, bool world = false) => new WeaponContext(WeaponRole.Author, inHand, isWorldAuthority: world);
        private static WeaponContext Observer() => new WeaponContext(WeaponRole.Observer, false);

        private static int Initializes(Recorder output) => output.Commands.Count(command => command == LedgerCommandKind.Initialize);

        [Test]
        public void Магазин_у_не_инициализированного_повторяет_Initialize_а_в_сбое_молчит()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.MagazineChanged), Uninitialized(), Author(false));
            Assert.That(machine.LastRowId, Is.EqualTo("T16i"));
            Assert.That(output.Commands, Is.EqualTo(new[] { LedgerCommandKind.Initialize }), "Магазин в гнезде — повод повторить Initialize.");

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.MagazineChanged), Faulted(), Author(true));
            Assert.That(machine.LastRowId, Is.EqualTo("T16"));
            Assert.That(output.Commands, Is.Empty, "В сбое команд нет.");
        }

        [Test]
        public void Tick_повторяет_сразу_после_хвата_и_затем_не_чаще_паузы()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            // Старт (T01) отклонён учётом: ствол в кобуре, CanUse=false.
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), Author(false));
            Assert.That(output.Commands, Is.EqualTo(new[] { LedgerCommandKind.Initialize }), "Контроль: T01 при настройке.");
            Step(machine, output, WeaponEvent.RejectedCommand(LedgerCommandKind.Initialize), Uninitialized(), Author(false));

            for (int frame = 0; frame < 10; frame++)
            {
                Step(machine, output, WeaponEvent.Tick(0.125f), Uninitialized(), Author(false));
                Assert.That(output.Commands, Is.Empty, "Вне руки повтора нет: учёт не спамится каждый кадр.");
            }

            Step(machine, output, WeaponEvent.Tick(0.016f), Uninitialized(), Author(true));
            Assert.That(machine.LastRowId, Is.EqualTo("T01t"));
            Assert.That(Initializes(output), Is.EqualTo(1), "Первый кадр в основной руке — сразу повтор.");

            for (int window = 0; window < 3; window++)
            {
                int sent = 0;
                for (int frame = 0; frame < 4; frame++) // 4 × 0,125 = InitRetrySeconds
                {
                    Step(machine, output, WeaponEvent.Tick(0.125f), Uninitialized(), Author(true));
                    sent += Initializes(output);
                }
                Assert.That(sent, Is.EqualTo(1), $"Окно {window}: ровно одна попытка за {WeaponStateMachine.InitRetrySeconds} с.");
            }
        }

        [Test]
        public void Смена_автора_повторяет_Initialize()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AuthorityChanged), Uninitialized(), Author(false));
            Assert.That(machine.LastRowId, Is.EqualTo("T37i"));
            Assert.That(output.Commands, Is.EqualTo(new[] { LedgerCommandKind.Initialize }));
            Assert.That(machine.State.Episode, Is.EqualTo(TriggerEpisode.Consumed), "Нажатие не наследуется (как T37b).");
        }

        [Test]
        public void Запрос_бота_повторяет_Initialize_только_в_контексте_мира()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), Uninitialized(), Author(true, world: true));
            Assert.That(machine.LastRowId, Is.EqualTo("T44i"));
            Assert.That(output.Commands, Is.EqualTo(new[] { LedgerCommandKind.Initialize }));

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested), Uninitialized(), Author(true, world: false));
            Assert.That(output.Commands, Is.Empty, "Без мира (не бот) — T47, без команд.");
        }

        [Test]
        public void После_инициализации_повторов_нет()
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.Configured), Uninitialized(), Author(true));
            Step(machine, output, WeaponEvent.Committed(LedgerOp.Initialize, false, true), Ready(), Author(true));

            var events = new List<WeaponEvent> { WeaponEvent.Of(WeaponEventKind.MagazineChanged), WeaponEvent.Of(WeaponEventKind.AuthorityChanged),
                WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested) };
            for (int frame = 0; frame < 12; frame++) events.Add(WeaponEvent.Tick(0.125f));
            foreach (WeaponEvent e in events)
            {
                Step(machine, output, e, Ready(), Author(true, world: true));
                Assert.That(Initializes(output), Is.Zero, $"{e.Kind}: учёт инициализирован — Initialize не повторяется.");
            }
        }

        private static IEnumerable<TestCaseData> ObserverEvents()
        {
            yield return new TestCaseData(WeaponEvent.Of(WeaponEventKind.MagazineChanged)).SetName("{m}(MagazineChanged)");
            yield return new TestCaseData(WeaponEvent.Tick(1f)).SetName("{m}(Tick)");
            yield return new TestCaseData(WeaponEvent.Of(WeaponEventKind.AuthorityChanged)).SetName("{m}(AuthorityChanged)");
            yield return new TestCaseData(WeaponEvent.Of(WeaponEventKind.AutomationPrepareRequested)).SetName("{m}(AutomationPrepareRequested)");
            yield return new TestCaseData(WeaponEvent.Of(WeaponEventKind.TriggerPressed)).SetName("{m}(TriggerPressed)");
        }

        [TestCaseSource(nameof(ObserverEvents))]
        public void Наблюдатель_не_повторяет_Initialize(WeaponEvent e)
        {
            var machine = new WeaponStateMachine(Pistol());
            var output = new Recorder();
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Step(machine, output, e, Uninitialized(), Observer());
                Assert.That(output.Commands, Is.Empty, "И9: наблюдатель команд не выдаёт.");
            }
        }

        // ── Инфраструктура ──────────────────────────────────────────────────────────────────

        private static void Step(WeaponStateMachine machine, Recorder output, WeaponEvent e, LedgerView l, WeaponContext c)
        {
            output.Reset();
            machine.Step(e, l, ActionSample.AtRest, c, output);
            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
        }

        private sealed class Recorder : IWeaponOutput
        {
            public readonly List<LedgerCommandKind> Commands = new List<LedgerCommandKind>();
            public int Violations;

            public void Reset() { Commands.Clear(); Violations = 0; }

            public void Command(in LedgerCommand command) => Commands.Add(command.Kind);
            public void Pose(in PoseTarget target) { }
            public void Cue(WeaponCue cue, WeaponNotReadyReason reason) { }
            public void Haptic(WeaponHapticCue cue) { }
            public void Hint(bool on) { }

            public void Report(in WeaponReport report)
            {
                if (report.Kind == WeaponReportKind.TableViolation || report.Kind == WeaponReportKind.Unhandled) Violations++;
            }
        }
    }
}
