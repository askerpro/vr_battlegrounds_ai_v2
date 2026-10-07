using System;
using System.Collections.Generic;
using System.Text;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Sensors;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Теневой режим WeaponSystem (этап C плана <c>Docs/tasks/weapon-system-refactor-plan.md</c>).
    /// Машина <see cref="WS.WeaponStateMachine"/> работает рядом со старым <see cref="WeaponReadinessController"/>
    /// и **ничего не исполняет**: команды учёту, цель позы, звуки, вибрации и подсказка только записываются
    /// и сравниваются с тем, что фактически сделал старый код. Расхождения — <c>GameLog.WeaponSystem.Warning</c>
    /// с ограничением частоты; сводка — при отключении.
    ///
    /// **Как сравнивается.** Замер датчиков — в <c>LateUpdate</c> с порядком 205, до контроллера (210): машина видит
    /// тот же учёт и ту же позу, что старый код перед решением, и её команды становятся ожиданиями. Фиксация
    /// учёта автора (событие <c>StateChanged</c>) погашает ожидание того же вида. Если ожидания нет (старый код
    /// решил вне своего <c>LateUpdate</c>: замер SDK перед выстрелом, подготовка бота, окно приёма), машина
    /// досчитывает решение тем же замером на учёте «до фиксации»; не совпало — расхождение. Ожидание машины,
    /// не погашенное за 3 кадра и 0,25 с, — расхождение. Сухой щелчок (<see cref="WeaponTriggerAttemptRouter"/>)
    /// и звук досылания (<see cref="AutomaticWeaponSlideFeedback.ManualCycleCompleted"/>) сравниваются окном в обе
    /// стороны. Поза — по устойчивому результату: машина держит HoldRear дольше 0,35 с, а Action не в проверенной
    /// задней позе; машина возвращает Action в покой дольше 1 с, а он не в покое.
    ///
    /// **Карантин.** После расхождения, снимка или подключения машина пересоздаётся, сравнение команд и позы
    /// приостанавливается до спокойного состояния (Ready/Empty/HoldOpen, ручку не держат, спуск отпущен, нет барьера):
    /// одно расхождение не порождает каскад. Расхождения только по сигналам (известное S2) и по позе карантин
    /// не включают: учёт и состояние машины в них не расходятся.
    ///
    /// Подключается во время игры на настроенный контроллер (<see cref="WeaponReadinessController.ConfiguredAny"/>),
    /// только при включённом <see cref="WeaponShadowSettings.Enabled"/>. Префабы не меняются.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(205)]
    public sealed class WeaponShadowComparer : MonoBehaviour
    {
        private const int ExpiryFrames = 3;
        private const float ExpirySeconds = 0.25f;
        private const float HoldRearSettle = 0.35f;
        private const float ReturnSettle = 1f;
        private const float LogInterval = 10f;
        private const int RecentLimit = 64;

        private static readonly List<WeaponShadowComparer> s_active = new List<WeaponShadowComparer>();

        /// <summary>Подключённые тени (для проб и сводки).</summary>
        public static IReadOnlyList<WeaponShadowComparer> Active => s_active;

        private enum Channel { Command, DryFire, ForwardSound }

        private struct Expectation
        {
            public Channel Channel;
            public WS.LedgerCommandKind Command;
            public WS.WeaponNotReadyReason Reason;
            public int Frame;
            public float Time;
            public string Row;
            public bool RofActive;

            public string Describe() =>
                Channel == Channel.Command ? Command.ToString() :
                Channel == Channel.DryFire ? "DryFire(" + Reason + ")" : "звук досылания";

            public bool SameAs(in Expectation other) =>
                Channel == other.Channel && (Channel != Channel.Command || Command == other.Command) &&
                (Channel != Channel.DryFire || Reason == other.Reason);
        }

        private enum OutputKind { Command, Cue, Haptic, Hint, Report }

        private struct StepOutput
        {
            public OutputKind Kind;
            public WS.LedgerCommand Command;
            public WS.WeaponCue Cue;
            public WS.WeaponNotReadyReason Reason;
            public WS.WeaponReport Report;
        }

        /// <summary>Вывод машины: только запись, ничего не исполняется.</summary>
        private sealed class RecordingOutput : WS.IWeaponOutput
        {
            public readonly List<StepOutput> Items = new List<StepOutput>(8);
            public WS.PoseTarget Current;

            public void Command(in WS.LedgerCommand command) => Items.Add(new StepOutput { Kind = OutputKind.Command, Command = command });
            public void Pose(in WS.PoseTarget target) => Current = target;
            public void Cue(WS.WeaponCue cue, WS.WeaponNotReadyReason reason) => Items.Add(new StepOutput { Kind = OutputKind.Cue, Cue = cue, Reason = reason });
            public void Haptic(WS.WeaponHapticCue cue) => Items.Add(new StepOutput { Kind = OutputKind.Haptic });
            public void Hint(bool on) => Items.Add(new StepOutput { Kind = OutputKind.Hint });
            public void Report(in WS.WeaponReport report) => Items.Add(new StepOutput { Kind = OutputKind.Report, Report = report });
        }

        private struct LogState { public float Last; public int Suppressed; }

        private WeaponReadinessController _controller;
        private UxrFirearmWeapon _weapon;
        private AutomaticWeaponSlideFeedback _feedback;
        private WeaponTriggerAttemptRouter _router;
        private int _trigger;
        private WS.WeaponProfileAxes _axes;
        private WS.WeaponStateMachine _machine;
        private readonly RecordingOutput _output = new RecordingOutput();
        private bool _stepActsAsAuthor;

        private WeaponLedgerReader _ledger;
        private WeaponActionSensor _action;
        private WeaponContextSensor _context;
        private WeaponTriggerSensor _triggerSensor;
        private WeaponMagazineSensor _magazine;
        private WeaponIntakeSensor _intake;

        private bool _attached, _stepping, _quarantine, _haveContext, _lastCtx;
        private WS.WeaponRole _lastRole;
        private float? _rofOverride;
        private float _stepRof, _barrierLoweredAt = -100f;
        private string _brief;
        private WS.GestureState _briefGesture;
        private WS.TriggerEpisode _briefEpisode;
        private WS.ClipKind _briefClip;
        private UxrAvatar _mainAvatar;
        private WS.ActionSample _lastSample;
        private float _holdRearTime, _returnTime;
        private bool _holdRearReported, _returnReported;
        private readonly List<Expectation> _machineSide = new List<Expectation>();
        private readonly List<Expectation> _oldSide = new List<Expectation>();
        private readonly Dictionary<string, LogState> _logs = new Dictionary<string, LogState>();

        private readonly Dictionary<string, int> _matched = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _divergences = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _byDesign = new Dictionary<string, int>();
        private readonly List<string> _recent = new List<string>();

        // ── Статистика для проб ─────────────────────────────────────────────────────────────

        public bool IsAttached => _attached;
        public bool InQuarantine => _quarantine;
        public int MatchedTotal { get; private set; }
        public int CaughtUp { get; private set; }
        public int DivergenceTotal { get; private set; }
        public int SuppressedInQuarantine { get; private set; }
        public int DirectShots { get; private set; }
        public int ReplayCommits { get; private set; }
        public IReadOnlyDictionary<string, int> Matched => _matched;
        public IReadOnlyDictionary<string, int> Divergences => _divergences;
        public IReadOnlyDictionary<string, int> ByDesign => _byDesign;
        public IReadOnlyList<string> Recent => _recent;
        public string MachineState => _machine != null ? _machine.State.ToString() : "—";

        /// <summary>Кратко для панели: жест руки, происхождение цикла, эпизод спуска, клип.</summary>
        public string MachineBrief
        {
            get
            {
                if (_machine == null) return "—";
                WS.WeaponMachineState state = _machine.State;
                if (_briefGesture == state.Gesture && _briefEpisode == state.Episode && _briefClip == state.Clip && _brief != null) return _brief;
                _briefGesture = state.Gesture; _briefEpisode = state.Episode; _briefClip = state.Clip;
                _brief = state.Gesture + " " + state.Episode + (state.Clip != WS.ClipKind.None ? " " + state.Clip : "");
                return _brief;
            }
        }

        /// <summary>Вид последнего расхождения (null — не было) и время по <c>Time.unscaledTime</c>.</summary>
        public string LastDivergenceKey { get; private set; }
        public float LastDivergenceTime { get; private set; }
        public string LastPose => _machine != null ? _machine.LastPose.Action + "/" + _machine.LastPose.Auxiliary : "—";

        /// <summary>Сбросить счётчики (проба между сценариями).</summary>
        public void ResetStatistics()
        {
            MatchedTotal = CaughtUp = DivergenceTotal = SuppressedInQuarantine = DirectShots = ReplayCommits = 0;
            LastDivergenceKey = null; LastDivergenceTime = 0f;
            _matched.Clear(); _divergences.Clear(); _byDesign.Clear(); _recent.Clear(); _logs.Clear();
        }

        public string Summary()
        {
            var text = new StringBuilder();
            text.Append($"совпадений {MatchedTotal} (досчитано {CaughtUp}), расхождений {DivergenceTotal}, в карантине пропущено {SuppressedInQuarantine}, " +
                        $"прямых выстрелов {DirectShots}, replay-фиксаций {ReplayCommits}");
            Append(text, " | совпало: ", _matched);
            Append(text, " | расхождения: ", _divergences);
            Append(text, " | новые выводы машины без сравнения: ", _byDesign);
            return text.ToString();
        }

        private static void Append(StringBuilder text, string title, Dictionary<string, int> counts)
        {
            if (counts.Count == 0) return;
            text.Append(title);
            bool first = true;
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (!first) text.Append(", ");
                text.Append(pair.Key).Append('×').Append(pair.Value);
                first = false;
            }
        }

        // ── Подключение ─────────────────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Bootstrap()
        {
            s_active.Clear();
            WeaponReadinessController.ConfiguredAny -= OnControllerConfigured;
            WeaponReadinessController.ConfiguredAny += OnControllerConfigured;
            WeaponShadowSettings.Changed -= OnSettingChanged;
            WeaponShadowSettings.Changed += OnSettingChanged;
        }

        private static void OnControllerConfigured(WeaponReadinessController controller)
        {
            if (Application.isPlaying && WeaponShadowSettings.Enabled) TryAttach(controller);
        }

        private static void OnSettingChanged(bool enabled)
        {
            if (!enabled || !Application.isPlaying) return;
            foreach (WeaponReadinessController controller in FindObjectsByType<WeaponReadinessController>(FindObjectsSortMode.None))
                if (controller.IsConfigured && controller.isActiveAndEnabled) TryAttach(controller);
        }

        /// <summary>Подключить тень к настроенному контроллеру (повторно — переподключить).</summary>
        public static WeaponShadowComparer TryAttach(WeaponReadinessController controller)
        {
            if (controller == null || !controller.IsConfigured) return null;
            WeaponShadowComparer shadow = controller.GetComponent<WeaponShadowComparer>();
            if (shadow != null)
            {
                if (shadow.isActiveAndEnabled) { shadow.Detach(false); shadow.Attach(); }
                return shadow;
            }
            shadow = controller.gameObject.AddComponent<WeaponShadowComparer>();
            shadow.hideFlags = HideFlags.DontSave;
            return shadow;
        }

        private void OnEnable() => Attach();
        private void OnDisable() => Detach(true);
        private void OnDestroy() => Detach(true);

        private void Attach()
        {
            if (_attached) return;
            _controller = GetComponent<WeaponReadinessController>();
            _weapon = GetComponent<UxrFirearmWeapon>();
            if (_controller == null || _weapon == null || !_controller.IsConfigured) return;
            if (!FirearmIntrospection.IsAvailable) { Refuse("поля SDK-спуска недоступны для чтения"); return; }
            _trigger = _controller.TriggerIndex;
            _feedback = GetComponent<AutomaticWeaponSlideFeedback>();
            _router = GetComponent<WeaponTriggerAttemptRouter>();
            if (!TryBuildAxes(out _axes, out string error)) { Refuse(error); return; }

            _ledger = new WeaponLedgerReader(_weapon, _trigger);
            _action = new WeaponActionSensor(_controller, _feedback);
            _context = new WeaponContextSensor(_weapon, _controller, _trigger);
            _triggerSensor = new WeaponTriggerSensor(_trigger);
            _magazine = new WeaponMagazineSensor(_ledger);
            _intake = new WeaponIntakeSensor(_weapon, _trigger);

            _weapon.StateChanged += OnWeaponStateChanged;
            _weapon.ReadinessFaulted += OnReadinessFaulted;
            UxrStateSaveImplementer.StateSerialized += OnStateSerialized;
            _action.HandleGrabbed += OnHandleGrabbed;
            _action.HandleReleased += OnHandleReleased;
            _controller.AutomationPreparationRequested += OnAutomationRequested;
            if (_router != null) _router.NotReadyAttempted += OnOldNotReady;
            if (_feedback != null) _feedback.ManualCycleCompleted += OnOldForwardSound;

            _attached = true;
            _haveContext = false;
            s_active.Add(this);
            EnterQuarantine();
            GameLog.WeaponSystem.Info($"[WeaponShadow] {name}: теневой режим включён — {_axes.FireMode}, {_axes.Ammo}, {_axes.Physical}, " +
                                      $"{_axes.Policy}, {_axes.EmptyPose}; порог {_axes.ExtractionGate:0.###}, Empty-клип {(_axes.HasEmptyClip ? _axes.EmptyClipDuration.ToString("0.###") : "нет")}.", this);
        }

        private void Refuse(string reason)
        {
            GameLog.WeaponSystem.Warning($"[WeaponShadow] {name}: теневой режим не включён — {reason}.", this);
            enabled = false;
        }

        private void Detach(bool summary)
        {
            if (!_attached) return;
            _attached = false;
            s_active.Remove(this);
            if (_weapon != null) { _weapon.StateChanged -= OnWeaponStateChanged; _weapon.ReadinessFaulted -= OnReadinessFaulted; }
            UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;
            if (_action != null) { _action.HandleGrabbed -= OnHandleGrabbed; _action.HandleReleased -= OnHandleReleased; _action.Dispose(); }
            if (_controller != null) _controller.AutomationPreparationRequested -= OnAutomationRequested;
            if (_router != null) _router.NotReadyAttempted -= OnOldNotReady;
            if (_feedback != null) _feedback.ManualCycleCompleted -= OnOldForwardSound;
            _machineSide.Clear(); _oldSide.Clear();
            if (summary && (MatchedTotal > 0 || DivergenceTotal > 0))
                GameLog.WeaponSystem.Info($"[WeaponShadow] {name}: сводка — {Summary()}.", this);
        }

        private bool TryBuildAxes(out WS.WeaponProfileAxes axes, out string error)
        {
            axes = default;
            WeaponReadinessProfile profile = _controller.Profile;
            if (profile == null) { error = "нет профиля готовности"; return false; }
            if (profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo) { error = "LegacyAmmo не на учёте"; return false; }
            if (!FirearmIntrospection.TryGetTriggerCycle(_weapon, _trigger, out UxrShotCycle cycle, out _)) { error = "нет спуска SDK"; return false; }
            WS.WeaponFireMode fireMode = cycle == UxrShotCycle.ManualReload ? WS.WeaponFireMode.Manual :
                cycle == UxrShotCycle.FullyAutomatic ? WS.WeaponFireMode.Auto : WS.WeaponFireMode.Semi;
            WS.WeaponAmmoCapability ammo = profile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber
                ? WS.WeaponAmmoCapability.FixedStoreChamber : WS.WeaponAmmoCapability.DetachableMagazineChamber;
            bool travel = profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel;
            WS.WeaponPhysicalCapability physical = travel ? WS.WeaponPhysicalCapability.ActionTravel : WS.WeaponPhysicalCapability.NoAction;
            WS.WeaponChamberPolicy policy = profile.ChamberPolicy == WeaponChamberPolicy.ManualReturn ? WS.WeaponChamberPolicy.ManualReturn :
                profile.ChamberPolicy == WeaponChamberPolicy.AutoOnMagazineInsert ? WS.WeaponChamberPolicy.AutoOnMagazineInsert :
                WS.WeaponChamberPolicy.TriggerAssistPrepareOnly;
            WS.WeaponEmptyPose emptyPose = profile.EmptyPose == WeaponEmptyPose.HoldOpen ? WS.WeaponEmptyPose.HoldOpen : WS.WeaponEmptyPose.ReturnToRest;
            float gate = 1f, epsilon = 0.01f, speed = 1f;
            if (travel)
            {
                if (_feedback == null || _feedback.Slide == null || _feedback.SlideTravelLength <= 0f) { error = "нет ручки Action"; return false; }
                gate = _feedback.SlideThreshold;
                epsilon = _feedback.PhysicalPositionEpsilon / _feedback.SlideTravelLength;
                speed = _feedback.AutoReturnSpeed;
            }
            WeaponMechanismVisuals visuals = GetComponent<WeaponMechanismVisuals>();
            WeaponMechanismMotion motion = visuals != null ? visuals.Motion : null;
            WeaponMechanismMotion.Cycle fire = motion != null ? motion.Fire : null;
            WeaponMechanismMotion.Cycle empty = motion != null ? motion.Empty : null;
            bool hasFire = fire != null && fire.Duration > 0f;
            bool hasEmpty = empty != null && empty.Duration > 0f;
            axes = new WS.WeaponProfileAxes(fireMode, ammo, physical, policy, emptyPose, gate, epsilon, speed, speed,
                hasFire, hasFire ? fire.Duration : 0f, hasEmpty, hasEmpty ? empty.Duration : 0f, _controller.EmptyRearTime,
                ammo == WS.WeaponAmmoCapability.FixedStoreChamber && GetComponent<CartridgeIntake>() != null);
            return WS.WeaponProfileAxes.TryValidate(axes, out error);
        }

        // ── Кадр ────────────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!_attached) return;
            if (!WeaponShadowSettings.Enabled) { Destroy(this); return; }
            if (_controller == null || !_controller.IsConfigured) return;

            WS.LedgerView l = _ledger.Read();
            WS.WeaponContext c = _context.Read(out UxrAvatar avatar);
            if (_haveContext)
            {
                if (c.Role != _lastRole) { _triggerSensor.Reset(); Step(WS.WeaponEvent.Of(WS.WeaponEventKind.AuthorityChanged), l); }
                else if (_lastCtx && !c.MainGripLocal) { _triggerSensor.Reset(); Step(WS.WeaponEvent.Of(WS.WeaponEventKind.ContextLost), l); }
            }
            _haveContext = true; _lastRole = c.Role; _lastCtx = c.MainGripLocal;

            if (_magazine.Poll()) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.MagazineChanged), l);
            if (_intake.HasIntake) PollIntake(l, avatar);

            Step(WS.WeaponEvent.Tick(Time.deltaTime), l);
            Step(WS.WeaponEvent.Of(WS.WeaponEventKind.ActionSampled), l);
            DrainTrigger(l, false);
            if (_triggerSensor.Pressed) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.TriggerHeld), l);
            _ledger.Remember();

            ComparePose(Time.deltaTime);
            Expire();
            UpdateQuarantine(l);
        }

        private void PollIntake(in WS.LedgerView l, UxrAvatar avatar)
        {
            _intake.Poll(avatar, out bool raised, out bool lowered, out bool offered);
            if (raised)
            {
                // Старый код (CartridgeIntake.Update) уже поднял барьер приёма — досчитать решение машины до барьера.
                if (_quarantine) SuppressedInQuarantine++;
                else if (!TryConsume(Channel.Command, WS.LedgerCommandKind.RequestAdmission))
                {
                    Step(WS.WeaponEvent.Offered(_intake.NextToken()), WithoutAdmission(l));
                    if (TryConsume(Channel.Command, WS.LedgerCommandKind.RequestAdmission)) CaughtUp++;
                    else Diverge("old:RequestAdmission", "старый код запросил приём патрона; машина — нет");
                }
                _intake.MarkOffered(avatar);
            }
            else if (offered) Step(WS.WeaponEvent.Offered(_intake.NextToken()), l);
            if (lowered) { _barrierLoweredAt = Time.unscaledTime; Step(WS.WeaponEvent.Of(WS.WeaponEventKind.AdmissionResolved), l); }
        }

        private void DrainTrigger(in WS.LedgerView l, bool stopAfterPress)
        {
            while (_triggerSensor.TryDequeue(out WeaponTriggerSensor.Edge edge))
            {
                bool press = edge == WeaponTriggerSensor.Edge.Pressed;
                Step(WS.WeaponEvent.Of(press ? WS.WeaponEventKind.TriggerPressed : WS.WeaponEventKind.TriggerReleased), l);
                if (press && stopAfterPress) return;
            }
        }

        private void Step(in WS.WeaponEvent e, in WS.LedgerView l)
        {
            if (_stepping || _machine == null) return;
            _stepping = true;
            try
            {
                WS.WeaponContext c = _context.Read(out _mainAvatar);
                if (_rofOverride.HasValue)
                    c = new WS.WeaponContext(c.Role, c.MainGripLocal, c.InsideReplay, c.Blocked, _rofOverride.Value, c.IsWorldAuthority);
                WS.ActionSample s = _action.Sample(_mainAvatar);
                _lastSample = s;
                _stepActsAsAuthor = c.ActsAsAuthor;
                _stepRof = c.RofTimer;
                _output.Items.Clear();
                _machine.Step(e, l, s, c, _output);
            }
            finally { _stepping = false; }
            Flush(_machine.LastRowId);
        }

        private void Flush(string row)
        {
            // Копия: расхождение внутри разбора пересоздаёт машину и делает новый шаг в тот же буфер.
            if (_output.Items.Count == 0) return;
            StepOutput[] items = _output.Items.ToArray(); // только редактор: аллокация допустима
            _output.Items.Clear();
            bool author = _stepActsAsAuthor;
            foreach (StepOutput item in items)
            {
                switch (item.Kind)
                {
                    case OutputKind.Command:
                        if (_quarantine) SuppressedInQuarantine++;
                        else AddMachine(new Expectation { Channel = Channel.Command, Command = item.Command.Kind, Row = row });
                        break;
                    case OutputKind.Cue:
                        if (item.Cue == WS.WeaponCue.DryFire && IsSdkReason(item.Reason))
                        {
                            if (_quarantine) SuppressedInQuarantine++;
                            else AddMachine(new Expectation { Channel = Channel.DryFire, Reason = item.Reason, Row = row, RofActive = _stepRof > 0f });
                        }
                        else if (item.Cue == WS.WeaponCue.ActionForwardChambered && author && !_quarantine)
                            AddMachine(new Expectation { Channel = Channel.ForwardSound, Row = row });
                        else Count(_byDesign, "звук " + item.Cue + (item.Cue == WS.WeaponCue.DryFire ? "(" + item.Reason + ")" : ""));
                        break;
                    case OutputKind.Haptic: Count(_byDesign, "вибрация"); break;
                    case OutputKind.Hint: Count(_byDesign, "подсказка"); break;
                    case OutputKind.Report:
                        if (item.Report.Kind == WS.WeaponReportKind.TableViolation || item.Report.Kind == WS.WeaponReportKind.Unhandled)
                            Diverge("machine-table:" + item.Report.Kind, $"ошибка таблицы машины {item.Report.Kind} в строке {item.Report.RowId ?? "—"}");
                        else Count(_byDesign, "отчёт " + item.Report.Kind);
                        break;
                }
            }
        }

        private static bool IsSdkReason(WS.WeaponNotReadyReason reason) =>
            reason == WS.WeaponNotReadyReason.NoMagazine || reason == WS.WeaponNotReadyReason.EmptyMagazine ||
            reason == WS.WeaponNotReadyReason.ChamberingRequired;

        // ── События старого кода и SDK ──────────────────────────────────────────────────────

        private void OnWeaponStateChanged(object sender, UxrSyncEventArgs args)
        {
            if (!_attached || !ReferenceEquals(sender, _weapon)) return;
            if (_triggerSensor.TryParse(args)) return;
            if (!_ledger.TryParseCommit(args, out LedgerCommitInfo info)) return;
            WS.LedgerView after = _ledger.Read();
            if (info.Replay) ReplayCommits++;
            else if (TryMapCommand(info.Op, out WS.LedgerCommandKind kind))
            {
                if (_quarantine) SuppressedInQuarantine++;
                else if (TryConsume(Channel.Command, kind)) { }
                else
                {
                    bool hadPress = _triggerSensor.Pressed || _triggerSensor.HasPendingPress;
                    CatchUp(info.Op);
                    if (TryConsume(Channel.Command, kind)) CaughtUp++;
                    else if (info.Op == WS.LedgerOp.Shot && !hadPress) DirectShots++; // бот/прямой TryToShootRound: решает не спуск
                    else Diverge("old:" + kind, $"старый код зафиксировал {kind}; машина этого не решила (строка {_machine.LastRowId ?? "—"})");
                }
            }
            Step(WS.WeaponEvent.Committed(info.Op, info.ChamberBefore, info.ChamberAfter), after);
            _ledger.Remember();
        }

        /// <summary>Машина досчитывает решение тем же замером на учёте до фиксации (решение старого кода вне его LateUpdate).</summary>
        private void CatchUp(WS.LedgerOp op)
        {
            WS.LedgerView before = _ledger.ReadBefore();
            if (op == WS.LedgerOp.Shot) _rofOverride = 0f; // SDK стреляет только при истёкшем таймере темпа
            try
            {
                Step(WS.WeaponEvent.Of(WS.WeaponEventKind.ActionSampled), before);
                if (op != WS.LedgerOp.Shot) return;
                bool pressed = _triggerSensor.HasPendingPress;
                DrainTrigger(before, true);
                if (!pressed && _triggerSensor.Pressed) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.TriggerHeld), before);
            }
            finally { _rofOverride = null; }
        }

        private static bool TryMapCommand(WS.LedgerOp op, out WS.LedgerCommandKind kind)
        {
            switch (op)
            {
                case WS.LedgerOp.Initialize: kind = WS.LedgerCommandKind.Initialize; return true;
                case WS.LedgerOp.BeginAction: kind = WS.LedgerCommandKind.BeginAction; return true;
                case WS.LedgerOp.Extract: kind = WS.LedgerCommandKind.Extract; return true;
                case WS.LedgerOp.Complete: kind = WS.LedgerCommandKind.CompleteChamber; return true;
                case WS.LedgerOp.Cancel: kind = WS.LedgerCommandKind.Cancel; return true;
                case WS.LedgerOp.CloseOnly: kind = WS.LedgerCommandKind.CloseOnly; return true;
                case WS.LedgerOp.EmptyRestAcknowledged: kind = WS.LedgerCommandKind.AckEmptyRest; return true;
                case WS.LedgerOp.Automation: kind = WS.LedgerCommandKind.RefillForAutomation; return true;
                case WS.LedgerOp.Shot: kind = WS.LedgerCommandKind.Shoot; return true;
                default: kind = default; return false; // AmmoAdmission фиксирует сервер
            }
        }

        private void OnReadinessFaulted(int trigger, UxrFirearmShotEmissionOutcome outcome, Exception failure)
        {
            if (!_attached || trigger != _trigger) return;
            Step(WS.WeaponEvent.Of(WS.WeaponEventKind.LedgerFaulted), _ledger.Read());
        }

        private void OnStateSerialized(object sender, UxrStateSaveEventArgs args)
        {
            if (!_attached || !ReferenceEquals(sender, _weapon) || args?.Serializer == null || !args.Serializer.IsReading) return;
            _triggerSensor.Reset();
            _magazine.Rebase();
            EnterQuarantine();
            Step(WS.WeaponEvent.Of(WS.WeaponEventKind.SnapshotLoaded), _ledger.Read());
        }

        private void OnHandleGrabbed()
        {
            if (_attached) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleGrabbed), _ledger.Read());
        }

        private void OnHandleReleased()
        {
            if (_attached) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleReleased), _ledger.Read());
        }

        private void OnAutomationRequested()
        {
            if (_attached) Step(WS.WeaponEvent.Of(WS.WeaponEventKind.AutomationPrepareRequested), _ledger.Read());
        }

        private void OnOldNotReady(WeaponTriggerAttemptContext context, UxrFirearmNotReadyReason reason)
        {
            if (!_attached || _quarantine) return;
            WS.WeaponNotReadyReason mapped = reason == UxrFirearmNotReadyReason.NoMagazine ? WS.WeaponNotReadyReason.NoMagazine :
                reason == UxrFirearmNotReadyReason.EmptyMagazine ? WS.WeaponNotReadyReason.EmptyMagazine : WS.WeaponNotReadyReason.ChamberingRequired;
            AddOld(new Expectation { Channel = Channel.DryFire, Reason = mapped });
        }

        private void OnOldForwardSound()
        {
            if (_attached && !_quarantine) AddOld(new Expectation { Channel = Channel.ForwardSound });
        }

        // ── Сопоставление ───────────────────────────────────────────────────────────────────

        private void AddMachine(Expectation expectation)
        {
            expectation.Frame = Time.frameCount; expectation.Time = Time.unscaledTime;
            for (int index = 0; index < _oldSide.Count; index++)
                if (_oldSide[index].SameAs(expectation)) { _oldSide.RemoveAt(index); Match(expectation); return; }
            // В тени команда не исполняется, учёт не меняется — следующий шаг того же кадра может решить то же
            // самое ещё раз (например, T71 и T29). Это одно решение, а не второе.
            for (int index = 0; index < _machineSide.Count; index++)
                if (_machineSide[index].SameAs(expectation)) { Count(_byDesign, "повтор неисполненного решения " + expectation.Describe()); return; }
            _machineSide.Add(expectation);
        }

        private void AddOld(Expectation expectation)
        {
            expectation.Frame = Time.frameCount; expectation.Time = Time.unscaledTime;
            for (int index = 0; index < _machineSide.Count; index++)
                if (_machineSide[index].SameAs(expectation)) { _machineSide.RemoveAt(index); Match(expectation); return; }
            _oldSide.Add(expectation);
        }

        private bool TryConsume(Channel channel, WS.LedgerCommandKind kind)
        {
            var probe = new Expectation { Channel = channel, Command = kind };
            for (int index = 0; index < _machineSide.Count; index++)
                if (_machineSide[index].SameAs(probe)) { Match(_machineSide[index]); _machineSide.RemoveAt(index); return true; }
            return false;
        }

        private void Match(in Expectation expectation)
        {
            MatchedTotal++;
            Count(_matched, expectation.Describe());
        }

        private void Expire()
        {
            for (int index = _machineSide.Count - 1; index >= 0; index--)
            {
                Expectation e = _machineSide[index];
                if (!Expired(e)) continue;
                _machineSide.RemoveAt(index);
                // S2 — только сигнал машины (щелчок), учёт и состояние машины не расходятся: карантин не нужен,
                // иначе одно раннее нажатие выключает сравнение ствола до конца сессии.
                bool knownS2 = e.Channel == Channel.DryFire && e.RofActive;
                Diverge("machine:" + e.Describe(), $"машина решила {e.Describe()} (строка {e.Row ?? "—"}); старый код — нет" +
                    (knownS2 ? KnownS2 : ""), !knownS2);
                if (_quarantine) return;
            }
            for (int index = _oldSide.Count - 1; index >= 0; index--)
            {
                Expectation e = _oldSide[index];
                if (!Expired(e)) continue;
                _oldSide.RemoveAt(index);
                Diverge("old:" + e.Describe(), $"старый код: {e.Describe()}; машина — нет");
                if (_quarantine) return;
            }
        }

        private static bool Expired(in Expectation e) =>
            Time.frameCount - e.Frame >= ExpiryFrames && Time.unscaledTime - e.Time >= ExpirySeconds;

        private void ComparePose(float dt)
        {
            if (_quarantine || !_action.HasAction || _lastSample.Held)
            {
                _holdRearTime = _returnTime = 0f; _holdRearReported = _returnReported = false;
                return;
            }
            WS.PosePresentation pose = _machine.LastPose.Action;
            if (pose == WS.PosePresentation.HoldRear && !_lastSample.AtValidatedRear)
            {
                _holdRearTime += dt;
                if (_holdRearTime >= HoldRearSettle && !_holdRearReported)
                {
                    _holdRearReported = true;
                    Diverge("pose:HoldRear", "машина держит Action в задней позе HoldOpen; фактически он не в проверенной задней позе", false);
                }
            }
            else { _holdRearTime = 0f; _holdRearReported = false; }
            if (pose == WS.PosePresentation.ReturnToRest && !_lastSample.AllAtRest)
            {
                _returnTime += dt;
                if (_returnTime >= ReturnSettle && !_returnReported)
                {
                    _returnReported = true;
                    Diverge("pose:ReturnToRest", "машина возвращает Action в покой; фактически он не в покое дольше 1 с", false);
                }
            }
            else { _returnTime = 0f; _returnReported = false; }
        }

        // ── Расхождения и карантин ──────────────────────────────────────────────────────────

        private void Diverge(string key, string message, bool quarantine = true)
        {
            DivergenceTotal++;
            Count(_divergences, key);
            LastDivergenceKey = key; LastDivergenceTime = Time.unscaledTime;
            WS.LedgerView l = _ledger.Read();
            string state = $"учёт {WS.WeaponMechanism.Derive(l, _axes)} C={(l.Chamber ? 1 : 0)} M={l.MagazineRounds} rev={l.Revision}" +
                           $"{(l.AdmissionPending ? " барьер" : "")}; машина {_machine?.State.ToString() ?? "—"}; поза {LastPose}";
            string line = $"{key}: {message}{KnownNote(key)}. {state}";
            _recent.Add($"[{Time.frameCount}] {line}");
            if (_recent.Count > RecentLimit) _recent.RemoveAt(0);

            float now = Time.unscaledTime;
            _logs.TryGetValue(key, out LogState log);
            if (log.Last <= 0f || now - log.Last >= LogInterval)
            {
                string repeats = log.Suppressed > 0 ? $" (+{log.Suppressed} повторов за {LogInterval:0} с)" : "";
                GameLog.WeaponSystem.Warning($"[WeaponShadow] {name}: расхождение {line}{repeats}", this);
                log.Last = now; log.Suppressed = 0;
            }
            else log.Suppressed++;
            _logs[key] = log;
            if (quarantine) EnterQuarantine();
        }

        // Известные расхождения спецификации машины со старым кодом (отчёт этапа C, план п. 5.2): нужен выбор, не подгонка.
        private const string KnownS1 = " [известное S1: у ствола нет Empty-клипа — старый код подтверждает покой после Fire-клипа последнего выстрела, машина сразу]";
        private const string KnownS2 = " [известное S2: нажатие во время таймера темпа — SDK молчит, машина T53 даёт сухой щелчок]";
        private const string KnownS4 = " [известное S4: у ствола нет пружины отпущенной ручки (_autoReturnOnRelease=0) — старый код прав, машина вернёт пружиной]";
        private const string KnownH6 = " [известное Н6: досылание пришлось на барьер приёма патрона — старый код отменяет цикл, машина дошлёт (T34/T71)]";

        private string KnownNote(string key)
        {
            if (key == "machine:AckEmptyRest" && !_axes.HasEmptyClip) return KnownS1;
            if (key == "pose:ReturnToRest" && _feedback != null && !_feedback.AutoReturnOnRelease) return KnownS4;
            if ((key == "old:Cancel" || key == "machine:CompleteChamber") && Time.unscaledTime - _barrierLoweredAt < 1.5f) return KnownH6;
            return "";
        }

        /// <summary>Машина с нуля, сравнение приостановлено до спокойного состояния (см. класс).</summary>
        private void EnterQuarantine()
        {
            _quarantine = true;
            _machineSide.Clear(); _oldSide.Clear();
            _holdRearTime = _returnTime = 0f;
            _machine = new WS.WeaponStateMachine(_axes);
            Step(WS.WeaponEvent.Of(WS.WeaponEventKind.Configured), _ledger.Read());
        }

        private void UpdateQuarantine(in WS.LedgerView l)
        {
            if (!_quarantine) return;
            WS.MechanismState m = WS.WeaponMechanism.Derive(l, _axes);
            if ((m == WS.MechanismState.Ready || m == WS.MechanismState.Empty || m == WS.MechanismState.HoldOpen) &&
                !_lastSample.Held && !_triggerSensor.Pressed && _triggerSensor.PendingEdges == 0 && !l.AdmissionPending)
                _quarantine = false;
        }

        private static WS.LedgerView WithoutAdmission(in WS.LedgerView l) =>
            new WS.LedgerView(l.Initialized, l.Chamber, l.ActionOpen, l.CyclePending, l.SlideLocked, l.Faulted, false, l.MagazinePresent,
                l.MagazineRounds, l.Capacity, l.Revision, l.CycleSequence, l.ExtractedCycle, l.ShotSequence, l.MagazineToken, l.CycleMagazineToken);

        private static void Count(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int value);
            counts[key] = value + 1;
        }
    }
}
