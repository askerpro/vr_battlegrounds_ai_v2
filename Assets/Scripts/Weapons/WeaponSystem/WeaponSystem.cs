using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Sensors;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Хост оружия (этап D, план п. 2): единственный компонент, через который машина <see cref="WS.WeaponStateMachine"/>
    /// управляет стволом. Тот же скрипт и GUID, что прежний <c>WeaponReadinessController</c>: префабы переходят без новой
    /// миграции компонентов, данные пишет <c>WeaponSystemAuthoring</c>.
    ///
    /// <para>
    /// <b>Устройство.</b> Датчики (ход, контекст, спуск, магазин, окно приёма) → шаг машины → порт учёта
    /// (<see cref="UxrReadinessLedgerPort"/>, команды только у автора вне replay) → исполнители по одному на канал:
    /// поза (<see cref="WeaponPoseExecutor"/>), звук (<see cref="WeaponAudioExecutor"/>), вибрация и подсказка
    /// (<see cref="WeaponFeedbackExecutor"/>). Исполнители — обычные C#-объекты внутри хоста: второго писателя канала
    /// на префабе быть не может. Учёт патронов не копируется: снимок <see cref="WS.LedgerView"/> читается заново.
    /// </para>
    /// <para>
    /// <b>События SDK и сеть.</b> Фиксации учёта, фронты спуска и хват ручки приходят внутри синхронизации SDK, где команда
    /// учёта была бы отклонена как вложенная (класс Б). Поэтому хост ставит событие в очередь вместе со снимком учёта,
    /// контекста и хода на момент события и разбирает очередь с верхнего уровня (<c>LateUpdate</c>, замер SDK перед
    /// выстрелом, запрос бота). Replay-фиксация остаётся шагом наблюдателя (контекст снят внутри replay): команд нет (И9).
    /// </para>
    /// <para>
    /// <b>До этапа E</b> выстрел решает спуск SDK, барьер приёма — <see cref="CartridgeIntake"/>; упёртый ствол озвучивает
    /// <see cref="BarrelObstruction"/>. Вибрация — через <see cref="WeaponHapticOutput"/> до влития сервиса haptics-system.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrFirearmWeapon)), DefaultExecutionOrder(210)]
    public sealed class WeaponSystem : WeaponReadinessController
    {
        private const int DrainLimit = 64;
        private const float LedgerRefreshSeconds = 1f;

        [SerializeField] private int _triggerIndex;
        [SerializeField] private WeaponReadinessProfile _profile;
        [SerializeField] private WeaponMechanismRig _rig = new WeaponMechanismRig();
        [SerializeField] private WeaponAudioSet _audio = new WeaponAudioSet();
        [SerializeField] private WeaponHapticSet _haptics = new WeaponHapticSet();
        [Tooltip("Дефолты звука и вибрации категории ствола. Пустые поля Audio/Haptics ствола берутся отсюда.")]
        [SerializeField] private WeaponFeedbackDefaults _feedbackDefaults;
        [Tooltip("Подсветка Action «дошли патрон» (видна по защёлке машины или когда рука рядом).")]
        [SerializeField] private GameObject _hintVisual;
        [Tooltip("Сигнал близости руки (аффорданс SDK) для подсветки Action.")]
        [SerializeField] private GameObject _hintProximity;

        private struct Pending
        {
            public WS.WeaponEvent Event;
            public WS.LedgerView Ledger;
            public WS.WeaponContext Context;
            public WS.ActionSample Sample;
            public UxrGrabber MainHand;
            public bool Recapture;
        }

        /// <summary>Вывод одного шага: собирается во время <c>Step</c>, исполняется после (повторного входа нет).</summary>
        private sealed class Output : WS.IWeaponOutput
        {
            public readonly List<WS.LedgerCommand> Commands = new List<WS.LedgerCommand>(4);
            public readonly List<WS.WeaponCue> Cues = new List<WS.WeaponCue>(4);
            public readonly List<WS.WeaponNotReadyReason> Reasons = new List<WS.WeaponNotReadyReason>(4);
            public readonly List<WS.WeaponHapticCue> Haptics = new List<WS.WeaponHapticCue>(2);
            public readonly List<WS.WeaponReport> Reports = new List<WS.WeaponReport>(2);
            public bool HintSet, HintOn, HasPose;
            public WS.PoseTarget PoseValue;

            public void Clear()
            {
                Commands.Clear(); Cues.Clear(); Reasons.Clear(); Haptics.Clear(); Reports.Clear();
                HintSet = HasPose = false;
            }

            public void Command(in WS.LedgerCommand command) => Commands.Add(command);
            public void Pose(in WS.PoseTarget target) { PoseValue = target; HasPose = true; }
            public void Cue(WS.WeaponCue cue, WS.WeaponNotReadyReason reason) { Cues.Add(cue); Reasons.Add(reason); }
            public void Haptic(WS.WeaponHapticCue cue) => Haptics.Add(cue);
            public void Hint(bool on) { HintSet = true; HintOn = on; }
            public void Report(in WS.WeaponReport report) => Reports.Add(report);
        }

        private UxrFirearmWeapon _weapon;
        private WS.WeaponProfileAxes _axes;
        private WS.WeaponStateMachine _machine;
        private readonly Output _out = new Output();
        private readonly Queue<Pending> _queue = new Queue<Pending>(8);

        private WeaponLedgerReader _ledger;
        private UxrReadinessLedgerPort _port;
        private WeaponActionSensor _action;
        private WeaponContextSensor _context;
        private WeaponTriggerSensor _trigger;
        private WeaponMagazineSensor _magazine;
        private WeaponIntakeSensor _intake;
        private WeaponPoseExecutor _pose;
        private WeaponAudioExecutor _sound;
        private WeaponFeedbackExecutor _feedback;
        private WeaponLedgerIntegrity _integrity;

        private bool _configured, _started, _stepping, _draining, _dispatching, _subscribed;
        private bool _haveContext, _lastCtx;
        private WS.WeaponRole _lastRole;
        private WS.LedgerView _view;
        private bool _viewDirty = true;
        private float _viewReadAt;
        private WS.PoseTarget _poseTarget;

        public override bool IsConfigured => _configured;
        public override WeaponReadinessProfile Profile => _profile;
        public override int TriggerIndex => _triggerIndex;
        public WeaponMechanismRig Rig => _rig;
        public WeaponAudioSet Audio => _audio;
        public WeaponHapticSet Haptics => _haptics;
        public WeaponFeedbackDefaults FeedbackDefaults => _feedbackDefaults;
        public GameObject HintVisual => _hintVisual;
        public GameObject HintProximity => _hintProximity;

        // ── Диагностика (панель, пробы) ─────────────────────────────────────────────────────

        /// <summary>Механическое состояние по учёту (план п. 3.3).</summary>
        public WS.MechanismState Mechanism => _configured ? WS.WeaponMechanism.Derive(ReadView(), _axes) : WS.MechanismState.Uninitialized;
        public WS.WeaponMachineState MachineState => _machine != null ? _machine.State : default;
        public string LastRowId => _machine?.LastRowId;
        public WS.PoseTarget LastPose => _poseTarget;
        public int TableViolations => _machine?.Violations ?? 0;
        public int CommandsRejected { get; private set; }
        public int SoundsPlayed => _sound?.Played ?? 0;
        public int HapticsSent => _feedback?.HapticsSent ?? 0;
        public bool HintOn => _feedback != null && _feedback.HintOn;
        public float ActionProgress => _rig.IsPrepared ? _rig.HandleProgress : 0f;
        public bool IsActionAtRest => _rig.IsPrepared && _rig.IsAtRest();
        public bool IsActionAtValidatedRear => _rig.IsPrepared && _rig.IsAtValidatedRear();

        // ── Жизненный цикл ──────────────────────────────────────────────────────────────────

        private void Start()
        {
            _started = true;
            Configure(out _);
        }

        private void OnEnable()
        {
            UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;
            UxrStateSaveImplementer.StateSerialized += OnStateSerialized;
            if (_started) Configure(out _);
        }

        private void OnDisable()
        {
            UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;
            if (_configured)
            {
                _port.TearingDown = true;
                try { StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.Disabled)); }
                finally { _port.TearingDown = false; }
            }
            Teardown();
        }

        // Статическое событие переживает объект, уничтоженный без OnDisable (DestroyImmediate в EditMode).
        private void OnDestroy() => UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;

        /// <summary>Настроить хост по профилю и rig (Start/OnEnable). false — ствол не управляется, причина в логе.</summary>
        public bool Configure(out string error)
        {
            if (_configured) Teardown();
            _weapon = GetComponent<UxrFirearmWeapon>();
            if (!TryValidate(out error))
            {
                GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: не настроен — {error}", this);
                return false;
            }
            if (_port == null)
            {
                _ledger = new WeaponLedgerReader(_weapon, _triggerIndex);
                _port = new UxrReadinessLedgerPort(this, _weapon, _triggerIndex, _rig, _ledger);
                _context = new WeaponContextSensor(_weapon, this, _triggerIndex);
                _trigger = new WeaponTriggerSensor(_triggerIndex);
                _magazine = new WeaponMagazineSensor(_ledger);
                _intake = new WeaponIntakeSensor(_weapon, _triggerIndex);
                _pose = new WeaponPoseExecutor(_rig, _axes.HoldsOpen);
                _sound = new WeaponAudioExecutor(_weapon, _triggerIndex, _audio, _feedbackDefaults, _rig);
                _feedback = new WeaponFeedbackExecutor(_haptics, _feedbackDefaults, _hintVisual, _hintProximity);
                _integrity = new WeaponLedgerIntegrity(_weapon, this);
            }
            if (!_port.TryInstall(out error))
            {
                _port.Uninstall();
                GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: не настроен — {error}", this);
                return false;
            }
            _action = new WeaponActionSensor(_rig);
            _action.HandleGrabbed += OnHandleGrabbed;
            _action.HandleReleased += OnHandleReleased;
            _weapon.StateChanged += OnWeaponStateChanged;
            _subscribed = true;
            _integrity.Attach();
            _pose.Attach();
            _trigger.Reset();
            _magazine.Rebase();
            _ledger.Remember();
            _queue.Clear();
            _haveContext = false;
            _viewDirty = true;
            _machine = new WS.WeaponStateMachine(_axes);
            _configured = true;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.Configured)); // T01: однократная миграция учёта у автора
            Drain();
            return true;
        }

        private void Teardown()
        {
            if (_subscribed)
            {
                if (_weapon != null) _weapon.StateChanged -= OnWeaponStateChanged;
                if (_action != null) { _action.HandleGrabbed -= OnHandleGrabbed; _action.HandleReleased -= OnHandleReleased; _action.Dispose(); }
                _subscribed = false;
            }
            _port?.Uninstall();
            _integrity?.Detach();
            _pose?.Detach();
            _feedback?.Reset();
            _trigger?.Reset();
            _queue.Clear();
            _configured = false;
        }

        /// <summary>
        /// Проверка данных хоста без запуска (сборщик, preflight волн): профиль, спуск с гнездом, механизм и оси машины —
        /// те же правила, что при настройке в игре. Учёт SDK не подключается, состояние не меняется.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (_configured) { error = null; return true; }
            _weapon = GetComponent<UxrFirearmWeapon>();
            return TryValidate(out error);
        }

        private bool TryValidate(out string error)
        {
            error = null;
            if (_weapon == null) { error = "нет UxrFirearmWeapon"; return false; }
            if (_profile == null || !_profile.TryValidate(out error)) { error = error ?? "нет профиля готовности"; return false; }
            if (_profile.AmmoCapability == WeaponAmmoCapability.LegacyAmmo) { error = "LegacyAmmo не на учёте готовности"; return false; }
            if (_triggerIndex < 0 || _triggerIndex >= _weapon.TriggerCount || !_weapon.TryGetTriggerMagazineAnchor(_triggerIndex, out _))
            { error = "нет спуска с гнездом магазина"; return false; }
            bool travel = _profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel;
            if (travel && _triggerIndex != 0) { error = "ручной ход связан только со спуском 0"; return false; }
            if (!_rig.TryPrepare(travel, travel && _profile.EmptyPose == WeaponEmptyPose.HoldOpen, out error)) return false;
            bool fixedStore = _profile.AmmoCapability == WeaponAmmoCapability.FixedStoreChamber;
            if (fixedStore && (GetComponent<CartridgeIntake>() == null ||
                Array.Find(_weapon.GetComponentsInChildren<UxrFirearmMag>(true),
                    store => store.IsFixedAmmoStore && store.FixedStoreWeapon == _weapon && store.FixedStoreTrigger == _triggerIndex) == null))
            { error = "FixedStoreChamber требует встроенного запаса и окна приёма патрона"; return false; }
            return TryBuildAxes(travel, fixedStore, out _axes, out error);
        }

        private bool TryBuildAxes(bool travel, bool fixedStore, out WS.WeaponProfileAxes axes, out string error)
        {
            WS.WeaponFireMode fireMode = WS.WeaponFireMode.Semi;
            if (FirearmIntrospection.TryGetTriggerCycle(_weapon, _triggerIndex, out UxrShotCycle cycle, out _))
                fireMode = cycle == UxrShotCycle.ManualReload ? WS.WeaponFireMode.Manual :
                    cycle == UxrShotCycle.FullyAutomatic ? WS.WeaponFireMode.Auto : WS.WeaponFireMode.Semi;
            else GameLog.WeaponSystem.Warning($"[WeaponSystem] {name}: режим огня SDK не прочитан — считаю Semi.", this);
            WS.WeaponAmmoCapability ammo = fixedStore ? WS.WeaponAmmoCapability.FixedStoreChamber : WS.WeaponAmmoCapability.DetachableMagazineChamber;
            WS.WeaponChamberPolicy policy = _profile.ChamberPolicy == WeaponChamberPolicy.ManualReturn ? WS.WeaponChamberPolicy.ManualReturn :
                _profile.ChamberPolicy == WeaponChamberPolicy.AutoOnMagazineInsert ? WS.WeaponChamberPolicy.AutoOnMagazineInsert :
                WS.WeaponChamberPolicy.TriggerAssistPrepareOnly;
            WS.WeaponEmptyPose emptyPose = _profile.EmptyPose == WeaponEmptyPose.HoldOpen ? WS.WeaponEmptyPose.HoldOpen : WS.WeaponEmptyPose.ReturnToRest;
            WeaponMechanismMotion motion = _rig.Motion;
            WeaponMechanismMotion.Cycle fire = motion != null ? motion.Fire : null, empty = motion != null ? motion.Empty : null;
            bool hasFire = fire != null && fire.Duration > 0f, hasEmpty = empty != null && empty.Duration > 0f;
            axes = new WS.WeaponProfileAxes(fireMode, ammo,
                travel ? WS.WeaponPhysicalCapability.ActionTravel : WS.WeaponPhysicalCapability.NoAction, policy, emptyPose,
                travel ? _rig.ExtractionGate : 1f, travel ? _rig.Epsilon / _rig.TravelLength : 0.01f,
                travel ? _rig.SpringReturnSpeed : 1f, travel ? _rig.AutoReturnSpeed : 1f,
                hasFire, hasFire ? fire.Duration : 0f, hasEmpty, hasEmpty ? empty.Duration : 0f, travel ? _rig.EmptyRearTime : -1f,
                fixedStore, travel ? _rig.ReleasedAction : WS.WeaponReleasedAction.Spring);
            return WS.WeaponProfileAxes.TryValidate(axes, out error);
        }

        // ── Кадр ────────────────────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!_configured) return;
            // Поправка сервера после форка ревизии — вне replay, с верхнего уровня (иначе не уйдёт в сеть).
            _integrity.PublishPendingCorrection();
            Drain();

            WS.WeaponContext context = _context.Read(out _);
            if (_haveContext)
            {
                if (context.Role != _lastRole) { _trigger.Reset(); StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.AuthorityChanged)); }
                else if (_lastCtx && !context.MainGripLocal) { _trigger.Reset(); StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.ContextLost)); }
            }
            _haveContext = true; _lastRole = context.Role; _lastCtx = context.MainGripLocal;

            if (_magazine.Poll()) { _viewDirty = true; StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.MagazineChanged)); }
            if (_intake.HasIntake)
            {
                _intake.Poll(null, out bool raised, out bool lowered, out _);
                if (raised || lowered) _viewDirty = true;
                if (lowered) StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.AdmissionResolved)); // T71: отложенное досылание (Н6)
            }

            bool idle = IsIdle();
            if (!idle)
            {
                StepNow(WS.WeaponEvent.Tick(Time.deltaTime));
                StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.ActionSampled));
            }
            // Уровень спуска — каждый кадр: таймер темпа (отражение, выделение памяти) не читается. До этапа E очередь
            // ведёт спуск SDK, Shoot машины не исполняется; нужен только эпизод (T52/T52a).
            if (_trigger.Pressed) StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.TriggerHeld));
            Drain();

            if (!idle) _pose.Apply(_poseTarget, Time.deltaTime);
            _feedback.Refresh();
        }

        /// <summary>
        /// Ствол в покое: никто не держит, нет жеста, клипа и событий, Action уже в своей позе. Шаг пропускается — на Quest
        /// у лежащих стволов нет работы машины и записи трансформов (риск «перф» плана п. 7).
        /// </summary>
        private bool IsIdle()
        {
            if (_queue.Count > 0 || _trigger.Pressed || _rig.IsHandleHeld || _context.MainHand() != null) return false;
            WS.WeaponMachineState state = _machine.State;
            if (state.Gesture != WS.GestureState.None || state.Origin != WS.ChamberOrigin.None || state.Clip != WS.ClipKind.None ||
                state.HintLatched) return false;
            // Цель уже применена хотя бы раз и не требует движения.
            if (_pose.LastAction != _poseTarget.Action ||
                (_poseTarget.Action != WS.PosePresentation.Rest && _poseTarget.Action != WS.PosePresentation.HoldRear)) return false;
            WS.MechanismState mechanism = WS.WeaponMechanism.Derive(ReadView(), _axes);
            return mechanism == WS.MechanismState.Ready || mechanism == WS.MechanismState.Empty || mechanism == WS.MechanismState.HoldOpen;
        }

        /// <summary>Повторный замер перед выстрелом SDK (порт <c>RefreshPhysicalActionState</c>, план п. 3.4).</summary>
        internal void SampleBeforeShot()
        {
            if (!_configured || _stepping || _draining || _dispatching || !CanCommandNow()) return;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.ActionSampled));
            Drain();
        }

        /// <summary>Подготовка оружия бота (контракт <c>BotGunner</c>): событие машины T44–T46, true — готово стрелять.</summary>
        public override bool RequestAutomationPreparation()
        {
            if (!_configured || _stepping || _draining || _dispatching) return false;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.AutomationPrepareRequested));
            Drain();
            return _weapon.IsReadyToFire(_triggerIndex);
        }

        // ── Очередь и шаг ───────────────────────────────────────────────────────────────────

        private static bool CanCommandNow() =>
            UxrStateSyncImplementer.SyncCallDepth == 0 && !(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync);

        /// <summary>Событие с верхнего уровня: сначала разобрать очередь, затем шаг на свежем снимке.</summary>
        private void StepNow(in WS.WeaponEvent e, bool rateOfFire = false)
        {
            Drain();
            Pending pending = Capture(e, rateOfFire);
            Step(pending);
        }

        /// <summary>Событие изнутри синхронизации SDK: в очередь со снимком на момент события.</summary>
        private void Enqueue(in WS.WeaponEvent e, bool rateOfFire = false, bool recapture = false)
        {
            if (!_configured) return;
            _viewDirty = true;
            Pending pending = recapture ? new Pending { Event = e, Recapture = true } : Capture(e, rateOfFire);
            _queue.Enqueue(pending);
        }

        private Pending Capture(in WS.WeaponEvent e, bool rateOfFire)
        {
            WS.WeaponContext context = _context.Read(out UxrGrabber hand, rateOfFire);
            return new Pending
            {
                Event = e, Ledger = ReadView(), Context = context, MainHand = hand,
                Sample = _action.Sample(hand != null ? hand.Avatar : null)
            };
        }

        private void Drain()
        {
            if (_draining || _stepping || _dispatching || !_configured || !CanCommandNow()) return;
            _draining = true;
            try
            {
                for (int guard = 0; _queue.Count > 0 && guard < DrainLimit; guard++)
                {
                    Pending pending = _queue.Dequeue();
                    if (pending.Recapture)
                    {
                        _viewDirty = true;
                        pending = Capture(pending.Event, false);
                        _ledger.Remember(); // снимок загружен: «до фиксации» для следующих событий — он
                    }
                    Step(pending);
                }
            }
            finally { _draining = false; }
        }

        private void Step(in Pending pending)
        {
            if (_stepping || _machine == null) return;
            _stepping = true;
            _out.Clear();
            try { _machine.Step(pending.Event, pending.Ledger, pending.Sample, pending.Context, _out); }
            catch (Exception exception) { GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: шаг {pending.Event.Kind} — {exception}", this); }
            finally { _stepping = false; }
            Dispatch(pending);
        }

        private void Dispatch(in Pending pending)
        {
            _dispatching = true;
            try
            {
                for (int index = 0; index < _out.Commands.Count; index++)
                {
                    WS.LedgerCommand command = _out.Commands[index];
                    bool accepted;
                    try { accepted = _port.Execute(command); }
                    catch (Exception exception)
                    {
                        accepted = false;
                        GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: команда {command.Kind} — {exception}", this);
                    }
                    _viewDirty = true;
                    if (accepted) continue;
                    // Остальные команды шага рассчитаны на ревизию после отклонённой — учёт отклонил бы и их.
                    CommandsRejected++;
                    _queue.Enqueue(Capture(WS.WeaponEvent.RejectedCommand(command.Kind), false));
                    break;
                }
                UxrGrabber mainHand = pending.MainHand != null ? pending.MainHand : _context.MainHand();
                for (int index = 0; index < _out.Cues.Count; index++) _sound.Play(_out.Cues[index], _out.Reasons[index], mainHand);
                for (int index = 0; index < _out.Haptics.Count; index++) _feedback.Haptic(_out.Haptics[index], mainHand, _action.HandleHand());
                if (_out.HintSet) _feedback.Hint(_out.HintOn);
                foreach (WS.WeaponReport report in _out.Reports) Log(report);
                if (_out.HasPose) _poseTarget = _out.PoseValue;
            }
            finally { _dispatching = false; }
        }

        private void Log(in WS.WeaponReport report)
        {
            switch (report.Kind)
            {
                case WS.WeaponReportKind.CommandRejected:
                    GameLog.WeaponSystem.Warning($"[WeaponSystem] {name}: учёт отклонил команду {report.Command} (строка {report.RowId}); жест сброшен.", this);
                    break;
                case WS.WeaponReportKind.TableViolation:
                case WS.WeaponReportKind.Unhandled:
                    GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: ошибка таблицы машины {report.Kind} (строка {report.RowId ?? "—"}).", this);
                    break;
                default:
                    GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: {report.Kind} (строка {report.RowId ?? "—"}).", this);
                    break;
            }
        }

        private WS.LedgerView ReadView()
        {
            if (_viewDirty || Time.unscaledTime - _viewReadAt >= LedgerRefreshSeconds)
            {
                _view = _ledger.Read();
                _viewDirty = false;
                _viewReadAt = Time.unscaledTime;
            }
            return _view;
        }

        // ── События SDK ─────────────────────────────────────────────────────────────────────

        private void OnWeaponStateChanged(object sender, UxrSyncEventArgs args)
        {
            if (!_configured || !ReferenceEquals(sender, _weapon)) return;
            _viewDirty = true;
            if (_trigger.TryParse(args))
            {
                while (_trigger.TryDequeue(out WeaponTriggerSensor.Edge edge))
                    Enqueue(WS.WeaponEvent.Of(edge == WeaponTriggerSensor.Edge.Pressed ? WS.WeaponEventKind.TriggerPressed : WS.WeaponEventKind.TriggerReleased), true);
                return;
            }
            if (_ledger.TryParseCommit(args, out LedgerCommitInfo info))
                Enqueue(WS.WeaponEvent.Committed(info.Op, info.ChamberBefore, info.ChamberAfter));
            _ledger.Remember();
        }

        private void OnStateSerialized(object sender, UxrStateSaveEventArgs args)
        {
            if (this == null) { UxrStateSaveImplementer.StateSerialized -= OnStateSerialized; return; }
            if (!_configured || !ReferenceEquals(sender, _weapon) || args?.Serializer == null || !args.Serializer.IsReading) return;
            // Событие приходит до загрузки остальных компонентов: снимок для шага берётся при разборе очереди.
            _trigger.Reset();
            _magazine.Rebase();
            Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.SnapshotLoaded), recapture: true);
        }

        private void OnHandleGrabbed() => Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleGrabbed));
        private void OnHandleReleased() => Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleReleased));
    }
}
