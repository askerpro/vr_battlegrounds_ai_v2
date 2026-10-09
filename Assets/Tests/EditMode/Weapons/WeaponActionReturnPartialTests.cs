using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Частичный ход Action (этап waves-f, принят пользователем в шлеме 2026-10-09). Фиксация <c>CloseOnly</c> — Action
    /// закрылся без извлечения и подачи (T27/T30/T23) — даёт отдельный сигнал <see cref="WeaponCue.ActionReturnPartial"/>,
    /// а не звук досылания или возврата полного цикла. Полный цикл по-прежнему даёт <c>ActionBack</c> и
    /// <c>ActionForwardChambered</c>/<c>ActionForwardEmpty</c>. Звук частичного хода — тот же, что у возврата без подачи;
    /// это правило данных проверяет <see cref="WeaponFeedbackResolveTests"/>.
    ///
    /// Класс ошибки: частичная оттяжка звучит как досылание (игрок слышит «дослал», а патрон не дослан) или сигнал
    /// частичного хода сливается с полным циклом и пропадает возможность дать ему свой отклик.
    /// </summary>
    public class WeaponActionReturnPartialTests
    {
        private const int Mag = 1;

        private static WeaponProfileAxes SemiSpring() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            releasedAction: WeaponReleasedAction.Spring);

        private static LedgerView Ready() => new LedgerView(initialized: true, chamber: true, magazinePresent: true,
            magazineRounds: 3, capacity: 5, magazineToken: Mag);

        private static LedgerView EmptyChambering(int rounds = 3) => new LedgerView(initialized: true, magazinePresent: true,
            magazineRounds: rounds, capacity: 5, magazineToken: Mag);

        private static LedgerView Cycle(bool chamber) => new LedgerView(initialized: true, chamber: chamber, actionOpen: true,
            cyclePending: true, magazinePresent: true, magazineRounds: 3, capacity: 5, cycleSequence: 1,
            magazineToken: Mag, cycleMagazineToken: Mag);

        private static WeaponContext Author() => new WeaponContext(WeaponRole.Author, true);
        private static WeaponContext Observer() => new WeaponContext(WeaponRole.Observer, false);

        private static readonly ActionSample HeldAtRest = new ActionSample(true, true, 0f, 0f, true, false);
        private static ActionSample Held(float progress) => new ActionSample(true, true, progress, progress, false, false);
        private static readonly ActionSample IdleAtRest = new ActionSample(false, false, 0f, 0f, true, false);

        private static readonly WeaponCue[] FullCycleForward = { WeaponCue.ActionForwardChambered, WeaponCue.ActionForwardEmpty };

        // ── Сигнал фиксации: одинаково у автора и наблюдателя ──────────────────────────────

        private static IEnumerable<TestCaseData> Commits()
        {
            foreach (bool observer in new[] { false, true })
            {
                string role = observer ? "Observer" : "Author";
                yield return new TestCaseData(observer, LedgerOp.CloseOnly, true, true, new[] { WeaponCue.ActionReturnPartial })
                    .SetName($"{{m}}({role}_CloseOnly_ПатронСохранён)");
                yield return new TestCaseData(observer, LedgerOp.CloseOnly, false, false, new[] { WeaponCue.ActionReturnPartial })
                    .SetName($"{{m}}({role}_CloseOnly_Пусто)");
                yield return new TestCaseData(observer, LedgerOp.Complete, false, true, new[] { WeaponCue.ActionForwardChambered })
                    .SetName($"{{m}}({role}_Complete_Дослан)");
                yield return new TestCaseData(observer, LedgerOp.Complete, false, false, new[] { WeaponCue.ActionForwardEmpty })
                    .SetName($"{{m}}({role}_Complete_БезПодачи)");
                yield return new TestCaseData(observer, LedgerOp.Extract, true, false, new[] { WeaponCue.ActionBack, WeaponCue.ChamberEjected })
                    .SetName($"{{m}}({role}_Extract_ЖивойПатрон)");
            }
        }

        [TestCaseSource(nameof(Commits))]
        public void Фиксация_даёт_ровно_свой_сигнал(bool observer, LedgerOp op, bool chamberBefore, bool chamberAfter, WeaponCue[] expected)
        {
            var machine = new WeaponStateMachine(SemiSpring());
            var output = new Recorder();
            LedgerView view = chamberAfter ? Ready() : EmptyChambering();
            Step(machine, output, WeaponEvent.Committed(op, chamberBefore, chamberAfter), view, IdleAtRest, observer ? Observer() : Author());

            Assert.That(output.Cues.Select(c => c.Item1), Is.EqualTo(expected),
                $"{op}: сигналы фиксации. CloseOnly — только ActionReturnPartial, полный цикл — ActionForward*.");
            Assert.That(output.Cues.Select(c => c.Item2), Is.All.EqualTo(WeaponNotReadyReason.None), "Сигналы хода не несут причины отказа.");
        }

        // ── Жест: частичная оттяжка против полного цикла ────────────────────────────────────

        [Test]
        public void Частичная_оттяжка_CloseOnly_звучит_ActionReturnPartial_а_не_досыланием()
        {
            var machine = new WeaponStateMachine(SemiSpring());
            var output = new Recorder();
            var cues = new List<WeaponCue>();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest, Author(), cues);
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f), Author(), cues);
            Assert.That(machine.LastRowId, Is.EqualTo("T21"), "Контроль: ход назад начал цикл.");

            // Вернули в покой, не дойдя до порога извлечения 0.8: T27 — Cancel + CloseOnly, без Extract/CompleteChamber.
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Cycle(true), HeldAtRest, Author(), cues);
            Assert.That(machine.LastRowId, Is.EqualTo("T27"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.Cancel, LedgerCommandKind.CloseOnly }));

            Step(machine, output, WeaponEvent.Committed(LedgerOp.CloseOnly, true, true), Ready(), HeldAtRest, Author(), cues);

            Assert.That(cues, Is.EqualTo(new[] { WeaponCue.ActionReturnPartial }),
                "Частичный ход: ровно один сигнал ActionReturnPartial, без ActionBack и звука досылания.");
        }

        [TestCase(true, WeaponCue.ActionForwardChambered)]
        [TestCase(false, WeaponCue.ActionForwardEmpty)]
        public void Полный_цикл_звучит_ActionBack_и_ActionForward(bool feeds, WeaponCue forward)
        {
            var machine = new WeaponStateMachine(SemiSpring());
            var output = new Recorder();
            var cues = new List<WeaponCue>();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest, Author(), cues);
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f), Author(), cues);
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Cycle(true), Held(0.9f), Author(), cues);
            Assert.That(machine.LastRowId, Is.EqualTo("T25"), "Контроль: порог извлечения пройден.");
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.Extract }));
            Step(machine, output, WeaponEvent.Committed(LedgerOp.Extract, true, false), Cycle(false), Held(0.9f), Author(), cues);

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Cycle(false), HeldAtRest, Author(), cues);
            Assert.That(machine.LastRowId, Is.EqualTo("T26"), "Контроль: Action в покое после порога — досылание.");
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.CompleteChamber }));
            Step(machine, output, WeaponEvent.Committed(LedgerOp.Complete, false, feeds), feeds ? Ready() : EmptyChambering(0),
                HeldAtRest, Author(), cues);

            Assert.That(cues, Is.EqualTo(new[] { WeaponCue.ActionBack, WeaponCue.ChamberEjected, forward }),
                "Полный цикл: оттяжка, извлечение и звук закрытия по подаче — без ActionReturnPartial.");
            Assert.That(cues.Where(c => FullCycleForward.Contains(c)).Count(), Is.EqualTo(1));
        }

        // ── Инфраструктура ──────────────────────────────────────────────────────────────────

        private static void Step(WeaponStateMachine machine, Recorder output, WeaponEvent e, LedgerView l, ActionSample s, WeaponContext c,
            List<WeaponCue> journal = null)
        {
            output.Reset();
            machine.Step(e, l, s, c, output);
            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
            journal?.AddRange(output.Cues.Select(cue => cue.Item1));
        }

        private sealed class Recorder : IWeaponOutput
        {
            public readonly List<LedgerCommand> Commands = new List<LedgerCommand>();
            public readonly List<(WeaponCue, WeaponNotReadyReason)> Cues = new List<(WeaponCue, WeaponNotReadyReason)>();
            public int Violations;

            public void Reset() { Commands.Clear(); Cues.Clear(); Violations = 0; }

            public void Command(in LedgerCommand command) => Commands.Add(command);
            public void Pose(in PoseTarget target) { }
            public void Cue(WeaponCue cue, WeaponNotReadyReason reason) => Cues.Add((cue, reason));
            public void Haptic(WeaponHapticCue cue) { }
            public void Hint(bool on) { }

            public void Report(in WeaponReport report)
            {
                if (report.Kind == WeaponReportKind.TableViolation || report.Kind == WeaponReportKind.Unhandled) Violations++;
            }
        }
    }
}
