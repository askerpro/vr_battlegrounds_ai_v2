using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Поведение машины оружия, принятое пользователем в шлеме на этапе drive (Herrington и FABARM, 2026-10-09):
    /// S1 — показ последнего выстрела без Empty-клипа; S2/T57/T57a — нажатие до конца таймера темпа;
    /// S4 — отпущенный Action при оси Stay; T53/T54 — открытый или недовозвращённый Action как недосланный патрон,
    /// подсказка держится, пока причина ChamberingRequired; T54i/T54u — без отклика до инициализации, с попыткой Initialize.
    ///
    /// Каждый тест прогоняет машину по событиям, как хост, и проверяет конкретный вывод: команды учёту, звук с
    /// причиной, вибрацию, подсказку, цель позы. Откат любого правила меняет этот вывод.
    /// Структурные свойства таблицы — <see cref="WeaponStateMachineStructureTests"/>.
    /// </summary>
    public class WeaponStateMachineDriveTests
    {
        private const int Mag = 1;

        // ── Профили ─────────────────────────────────────────────────────────────────────────

        /// <summary>Ствол без Empty-клипа, затвор с пружиной (ось Spring).</summary>
        private static WeaponProfileAxes SemiSpringNoEmptyClip() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.FixedStoreChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasFixedStoreIntake: true, releasedAction: WeaponReleasedAction.Spring);

        /// <summary>Помпа без пружины и без Empty-клипа — как FABARM.</summary>
        private static WeaponProfileAxes ManualStayPump() => new WeaponProfileAxes(
            WeaponFireMode.Manual, WeaponAmmoCapability.FixedStoreChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.9f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            hasFixedStoreIntake: true, releasedAction: WeaponReleasedAction.Stay);

        private static WeaponProfileAxes AutoRifle() => new WeaponProfileAxes(
            WeaponFireMode.Auto, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.ManualReturn, WeaponEmptyPose.ReturnToRest, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.1f);

        /// <summary>Досылание первым нажатием: без правила S2 нажатие до конца темпа начало бы подготовку (T40).</summary>
        private static WeaponProfileAxes AssistPistol() => new WeaponProfileAxes(
            WeaponFireMode.Semi, WeaponAmmoCapability.DetachableMagazineChamber, WeaponPhysicalCapability.ActionTravel,
            WeaponChamberPolicy.TriggerAssistPrepareOnly, WeaponEmptyPose.ReturnToRest, extractionGate: 0.7f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f);

        private static IEnumerable<TestCaseData> PilotProfiles()
        {
            yield return new TestCaseData(SemiSpringNoEmptyClip()).SetName("{m}(SemiSpring)");
            yield return new TestCaseData(ManualStayPump()).SetName("{m}(ManualStayPump)");
        }

        // ── Снимки учёта ────────────────────────────────────────────────────────────────────

        private static LedgerView Uninitialized() => new LedgerView();

        private static LedgerView Ready(uint shot = 0) => new LedgerView(initialized: true, chamber: true, magazinePresent: true,
            magazineRounds: 3, capacity: 5, shotSequence: shot, magazineToken: Mag);

        /// <summary>Патронник пуст, в запасе есть патроны — причина ChamberingRequired.</summary>
        private static LedgerView EmptyChambering() => new LedgerView(initialized: true, magazinePresent: true, magazineRounds: 3,
            capacity: 5, magazineToken: Mag);

        private static LedgerView EmptyNoMagazine() => new LedgerView(initialized: true);

        /// <summary>Последний выстрел оставил оружие пустым, ось EmptyPose = ReturnToRest → EmptyAwaitRest.</summary>
        private static LedgerView EmptyAwaitRest(uint shot = 1) => new LedgerView(initialized: true, slideLocked: true,
            magazinePresent: true, magazineRounds: 0, capacity: 5, shotSequence: shot, magazineToken: Mag);

        private static LedgerView OpenIdle(bool magazine = true) => magazine
            ? new LedgerView(initialized: true, actionOpen: true, magazinePresent: true, magazineRounds: 3, capacity: 5, magazineToken: Mag)
            : new LedgerView(initialized: true, actionOpen: true);

        private static LedgerView Cycle(bool chamber, uint sequence = 1) => new LedgerView(initialized: true, chamber: chamber,
            actionOpen: true, cyclePending: true, magazinePresent: true, magazineRounds: 3, capacity: 5, cycleSequence: sequence,
            magazineToken: Mag, cycleMagazineToken: Mag);

        // ── Контексты и замеры ──────────────────────────────────────────────────────────────

        private static WeaponContext Author(float rofTimer = 0f) => new WeaponContext(WeaponRole.Author, true, rofTimer: rofTimer);

        private static readonly ActionSample HeldAtRest = new ActionSample(true, true, 0f, 0f, true, false);
        private static ActionSample Held(float progress) => new ActionSample(true, true, progress, progress, false, false);
        private static ActionSample Released(float progress) => new ActionSample(false, false, progress, progress, false, false);

        // ── S1: последний выстрел без Empty-клипа ───────────────────────────────────────────

        [TestCaseSource(nameof(PilotProfiles))]
        public void S1_ПоследнийВыстрелБезEmptyКлипа_ИграетFireКлип_AckEmptyRestПослеКонца(WeaponProfileAxes axes)
        {
            var machine = new WeaponStateMachine(axes);
            var output = new Recorder();

            Step(machine, output, WeaponEvent.Committed(LedgerOp.Shot, true, false), EmptyAwaitRest(), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T62"));
            Assert.That(machine.State.Clip, Is.EqualTo(ClipKind.Fire), "S1: у ствола без Empty-клипа последний выстрел показывает Fire-клип.");
            Assert.That(machine.LastPose.Action, Is.EqualTo(PosePresentation.FireClip));

            // Пока клип идёт, Action в покое и в руке контекст — подтверждать покой рано.
            Step(machine, output, WeaponEvent.Tick(0.1f), EmptyAwaitRest(), ActionSample.AtRest, Author());
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), EmptyAwaitRest(), ActionSample.AtRest, Author());
            Assert.That(output.Commands, Is.Empty, "S1: AckEmptyRest до конца Fire-клипа обрывает показ выстрела.");
            Assert.That(machine.LastPose.Action, Is.EqualTo(PosePresentation.FireClip));

            Step(machine, output, WeaponEvent.Tick(axes.FireClipDuration), EmptyAwaitRest(), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T64"));
            Assert.That(machine.State.Clip, Is.EqualTo(ClipKind.None));

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), EmptyAwaitRest(), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T31"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.AckEmptyRest }),
                "S1: после конца клипа и в покое — ровно AckEmptyRest.");
        }

        // ── S2 / T57: нажатие до конца таймера темпа ────────────────────────────────────────

        private static IEnumerable<TestCaseData> RateOfFireRefusals()
        {
            yield return new TestCaseData(SemiSpringNoEmptyClip(), Ready(), "Ready Semi").SetName("{m}(ReadySemi)");
            yield return new TestCaseData(ManualStayPump(), Ready(), "Ready Manual").SetName("{m}(ReadyManual)");
            yield return new TestCaseData(SemiSpringNoEmptyClip(), EmptyChambering(), "пусто, ChamberingRequired").SetName("{m}(EmptyChambering)");
            yield return new TestCaseData(ManualStayPump(), EmptyNoMagazine(), "пусто, без запаса").SetName("{m}(EmptyNoMagazine)");
            yield return new TestCaseData(ManualStayPump(), EmptyAwaitRest(), "EmptyAwaitRest").SetName("{m}(EmptyAwaitRest)");
            yield return new TestCaseData(ManualStayPump(), OpenIdle(), "открытый Action").SetName("{m}(OpenIdle)");
            yield return new TestCaseData(SemiSpringNoEmptyClip(), Cycle(false), "недовозвращённый Action").SetName("{m}(CycleCleared)");
            yield return new TestCaseData(AssistPistol(), EmptyChambering(), "TriggerAssist, ChamberingRequired").SetName("{m}(AssistNoPrepare)");
        }

        [TestCaseSource(nameof(RateOfFireRefusals))]
        public void S2_НажатиеДоКонцаТемпа_ОтказRateOfFire_БезЩелчкаКомандИПодсказки(WeaponProfileAxes axes, LedgerView view, string what)
        {
            var machine = new WeaponStateMachine(axes);
            var output = new Recorder();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), view, ActionSample.AtRest, Author(rofTimer: 0.1f));

            Assert.That(machine.LastRowId, Is.EqualTo("T57"), what);
            Assert.That(output.Cues, Is.EqualTo(new[] { (WeaponCue.Refusal, WeaponNotReadyReason.RateOfFire) }),
                $"S2 ({what}): только звук отказа Refusal — не DryFire и не звук хода.");
            Assert.That(output.Haptics, Is.EqualTo(new[] { WeaponHapticCue.RateOfFire }), $"S2 ({what}): вибрация RateOfFire.");
            Assert.That(output.Commands, Is.Empty, $"S2 ({what}): ни выстрела, ни подготовки (T40).");
            Assert.That(output.HintsOn, Is.Zero, $"S2 ({what}): без подсказки.");
            Assert.That(machine.State.Episode, Is.EqualTo(TriggerEpisode.Consumed), $"S2 ({what}): нажатие израсходовано.");
        }

        [Test]
        public void S2_ТемпИстёк_ТоЖеНажатиеСтреляет()
        {
            // Контроль к S2: отказ даёт именно таймер, а не состояние.
            var machine = new WeaponStateMachine(SemiSpringNoEmptyClip());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), Ready(), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T50"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.Shoot }));
            Assert.That(output.Cues, Is.Empty);
        }

        [Test]
        public void T57a_Auto_НажатиеДоКонцаТемпа_НеОтказ_ВыстрелЖдётТемпа()
        {
            var machine = new WeaponStateMachine(AutoRifle());
            var output = new Recorder();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), Ready(), ActionSample.AtRest, Author(rofTimer: 0.05f));
            Assert.That(machine.LastRowId, Is.EqualTo("T57a"));
            Assert.That(output.Cues, Is.Empty, "T57a: у Auto это не отказ — без звука.");
            Assert.That(output.Haptics, Is.Empty, "T57a: без вибрации отказа.");
            Assert.That(output.Commands, Is.Empty, "T57a: выстрела до конца темпа нет.");
            Assert.That(machine.State.Episode, Is.EqualTo(TriggerEpisode.Firing), "T57a: нажатие не израсходовано.");

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerHeld), Ready(), ActionSample.AtRest, Author(rofTimer: 0.01f));
            Assert.That(output.Commands, Is.Empty, "Таймер ещё идёт — ждём.");

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerHeld), Ready(), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T51"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.Shoot }),
                "T57a: темп истёк при удержании — очередь стреляет.");
        }

        // ── S4: отпущенный Action при оси Stay ──────────────────────────────────────────────

        [TestCase(WeaponReleasedAction.Stay)]
        [TestCase(WeaponReleasedAction.Spring)]
        public void S4_ОтпущенныйПосредиЦиклаAction_ПоОсиReleasedAction(WeaponReleasedAction released)
        {
            WeaponProfileAxes axes = released == WeaponReleasedAction.Stay ? ManualStayPump() : SemiSpringNoEmptyClip();
            var machine = new WeaponStateMachine(axes);
            var output = new Recorder();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Ready(), HeldAtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T20"));
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Ready(), Held(0.5f), Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T21"));
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(new[] { LedgerCommandKind.BeginAction }));

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.HandleReleased), Cycle(true), Released(0.5f), Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T28"));
            Assert.That(machine.State.Gesture, Is.EqualTo(GestureState.Released));

            // Несколько замеров без руки посреди хода: цель позы не меняется, цикл не отменяется и не закрывается.
            for (int frame = 0; frame < 3; frame++)
            {
                Step(machine, output, WeaponEvent.Of(WeaponEventKind.ActionSampled), Cycle(true), Released(0.5f), Author());
                Assert.That(output.Commands, Is.Empty, "Отпущенный посреди хода Action не закрывает цикл.");
                if (released == WeaponReleasedAction.Stay)
                    Assert.That(machine.LastPose.Action, Is.EqualTo(PosePresentation.Stay), "S4: при Stay Action остаётся, где отпустили.");
                else
                {
                    Assert.That(machine.LastPose.Action, Is.EqualTo(PosePresentation.ReturnToRest), "Spring: пружина возвращает Action.");
                    Assert.That(machine.LastPose.ReturnSpeed, Is.EqualTo(axes.SpringReturnSpeed));
                }
            }

            if (released == WeaponReleasedAction.Stay)
            {
                // Новый хват продолжает тот же цикл (T28r), а не начинает другой.
                Step(machine, output, WeaponEvent.Of(WeaponEventKind.HandleGrabbed), Cycle(true), Held(0.5f), Author());
                Assert.That(machine.LastRowId, Is.EqualTo("T28r"));
                Assert.That(machine.State.Gesture, Is.EqualTo(GestureState.Pulling));
                Assert.That(machine.LastPose.Action, Is.EqualTo(PosePresentation.FollowHand));
            }
        }

        // ── T53 / T54: недосланный патрон, открытый или недовозвращённый Action ──────────────

        private static IEnumerable<TestCaseData> NotChamberedChamberingRequired()
        {
            yield return new TestCaseData(EmptyChambering(), "T53", "пусто").SetName("{m}(T53_Empty)");
            yield return new TestCaseData(OpenIdle(), "T54", "открытый Action").SetName("{m}(T54_OpenIdle)");
            yield return new TestCaseData(Cycle(false), "T54", "недовозвращённый Action, патронник пуст").SetName("{m}(T54_CycleCleared)");
            yield return new TestCaseData(Cycle(true), "T54", "недовозвращённый Action, патрон в патроннике").SetName("{m}(T54_CycleLoaded)");
        }

        [TestCaseSource(nameof(NotChamberedChamberingRequired))]
        public void T54_ОткрытыйИлиНедовозвращённыйAction_КакНедосланныйПатрон(LedgerView view, string row, string what)
        {
            foreach (WeaponProfileAxes axes in new[] { SemiSpringNoEmptyClip(), ManualStayPump() })
            {
                var machine = new WeaponStateMachine(axes);
                var output = new Recorder();

                Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), view, ActionSample.AtRest, Author());

                string where = $"{what} ({axes.FireMode}/{axes.ReleasedAction})";
                Assert.That(machine.LastRowId, Is.EqualTo(row), where);
                Assert.That(output.Cues, Is.EqualTo(new[] { (WeaponCue.DryFire, WeaponNotReadyReason.ChamberingRequired) }),
                    $"{where}: сухой щелчок с причиной ChamberingRequired.");
                Assert.That(output.Haptics, Is.EqualTo(new[] { WeaponHapticCue.NotReady }), $"{where}: вибрация NotReady.");
                Assert.That(output.HintEvents, Is.EqualTo(new[] { true }), $"{where}: подсказка (подсветка Action) включена.");
                Assert.That(output.Commands, Is.Empty, $"{where}: команд учёту нет.");
                Assert.That(machine.State.Episode, Is.EqualTo(TriggerEpisode.Consumed));
            }
        }

        [Test]
        public void T54_ОткрытыйActionБезЗапаса_ЩелчокИВибрацияБезПодсказки()
        {
            var machine = new WeaponStateMachine(ManualStayPump());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), OpenIdle(magazine: false), ActionSample.AtRest, Author());
            Assert.That(machine.LastRowId, Is.EqualTo("T54"));
            Assert.That(output.Cues, Is.EqualTo(new[] { (WeaponCue.DryFire, WeaponNotReadyReason.NoMagazine) }));
            Assert.That(output.Haptics, Is.EqualTo(new[] { WeaponHapticCue.NotReady }));
            Assert.That(output.HintsOn, Is.Zero, "Подсказка только при причине ChamberingRequired.");
        }

        [Test]
        public void T54_Подсказка_ДержитсяВOpenIdleCycleEmpty_ГаснетКогдаГотово()
        {
            var machine = new WeaponStateMachine(ManualStayPump());
            var output = new Recorder();

            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), OpenIdle(), ActionSample.AtRest, Author());
            Assert.That(output.HintEvents, Is.EqualTo(new[] { true }));

            // Причина остаётся ChamberingRequired, пока Action открыт, идёт цикл или патронник пуст.
            foreach (LedgerView stillNotChambered in new[] { OpenIdle(), Cycle(false), Cycle(true), EmptyChambering(), OpenIdle() })
            {
                Step(machine, output, WeaponEvent.Tick(0.016f), stillNotChambered, ActionSample.AtRest, Author());
                Assert.That(output.HintEvents, Is.Empty, $"Подсказка погасла в {WeaponMechanism.Derive(stillNotChambered, machine.Axes)}.");
                Assert.That(machine.State.HintLatched, Is.True);
            }

            Step(machine, output, WeaponEvent.Tick(0.016f), Ready(), ActionSample.AtRest, Author());
            Assert.That(output.HintEvents, Is.EqualTo(new[] { false }), "Оружие готово — подсказка гаснет.");
            Assert.That(machine.State.HintLatched, Is.False);
        }

        [Test]
        public void T54_Подсказка_ГаснетКогдаПричинаНеChamberingRequired()
        {
            var machine = new WeaponStateMachine(ManualStayPump());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), Cycle(false), ActionSample.AtRest, Author());
            Assert.That(output.HintEvents, Is.EqualTo(new[] { true }));

            Step(machine, output, WeaponEvent.Tick(0.016f), OpenIdle(magazine: false), ActionSample.AtRest, Author());
            Assert.That(output.HintEvents, Is.EqualTo(new[] { false }), "Запаса нет — досылать нечего, подсказка гаснет.");
        }

        // ── T54i/T54u: не инициализирован ───────────────────────────────────────────────────

        /// <summary>
        /// Принято в шлеме 2026-10-09 (повтор Initialize): нажатие на не инициализированном стволе израсходовано без отклика
        /// о патронах; при истёкшем таймере темпа — попытка Initialize (T54i), до его конца — без команд (T54u, S2).
        /// </summary>
        [TestCase(0f, "T54i", 1)]
        [TestCase(0.1f, "T54u", 0)]
        public void T54_НеИнициализирован_НажатиеБезОтклика_ИПопыткаИнициализации(float rof, string row, int initializes)
        {
            var machine = new WeaponStateMachine(SemiSpringNoEmptyClip());
            var output = new Recorder();
            Step(machine, output, WeaponEvent.Of(WeaponEventKind.TriggerPressed), Uninitialized(), ActionSample.AtRest, Author(rof));

            Assert.That(machine.LastRowId, Is.EqualTo(row), $"rof={rof}");
            Assert.That(output.Cues, Is.Empty, "Без звука (ни DryFire, ни Refusal).");
            Assert.That(output.Haptics, Is.Empty, "Без вибрации.");
            Assert.That(output.HintsOn, Is.Zero, "Без подсказки.");
            Assert.That(output.Commands.Select(c => c.Kind), Is.EqualTo(Enumerable.Repeat(LedgerCommandKind.Initialize, initializes)),
                rof > 0f ? "S2: до конца таймера темпа команд нет." : "Ровно одна попытка Initialize.");
            Assert.That(machine.State.Episode, Is.EqualTo(TriggerEpisode.Consumed), "Нажатие израсходовано: выстрел — следующим нажатием.");
        }

        // ── Инфраструктура ──────────────────────────────────────────────────────────────────

        /// <summary>Один шаг с чистым журналом: в журнале только вывод этого шага.</summary>
        private static void Step(WeaponStateMachine machine, Recorder output, WeaponEvent e, LedgerView l, ActionSample s, WeaponContext c)
        {
            output.Reset();
            machine.Step(e, l, s, c, output);
            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
            Assert.That(output.Poses, Is.EqualTo(1), "И13: ровно одна цель позы за шаг.");
        }

        private sealed class Recorder : IWeaponOutput
        {
            public readonly List<LedgerCommand> Commands = new List<LedgerCommand>();
            public readonly List<(WeaponCue, WeaponNotReadyReason)> Cues = new List<(WeaponCue, WeaponNotReadyReason)>();
            public readonly List<WeaponHapticCue> Haptics = new List<WeaponHapticCue>();
            public readonly List<bool> HintEvents = new List<bool>();
            public int Poses, Violations;

            public int HintsOn => HintEvents.Count(on => on);

            public void Reset()
            {
                Commands.Clear(); Cues.Clear(); Haptics.Clear(); HintEvents.Clear();
                Poses = Violations = 0;
            }

            public void Command(in LedgerCommand command) => Commands.Add(command);
            public void Pose(in PoseTarget target) => Poses++;
            public void Cue(WeaponCue cue, WeaponNotReadyReason reason) => Cues.Add((cue, reason));
            public void Haptic(WeaponHapticCue cue) => Haptics.Add(cue);
            public void Hint(bool on) => HintEvents.Add(on);

            public void Report(in WeaponReport report)
            {
                if (report.Kind == WeaponReportKind.TableViolation || report.Kind == WeaponReportKind.Unhandled) Violations++;
            }
        }
    }
}
