using System;
using System.Collections.Generic;
using UltimateXR.Core;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
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
    /// (<see cref="WeaponFeedbackExecutor"/>), вылет патрона и гильзы (<see cref="WeaponEjectionExecutor"/>). Исполнители — обычные C#-объекты внутри хоста: второго писателя канала
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
        [Tooltip("Вылет ствола (оверрайды): живой патрон и гильза. Пустая запись — дефолт категории.")]
        [SerializeField] private WeaponEjectionSet _ejection = new WeaponEjectionSet();
        [Tooltip("Окно выброса: точка и поворот (+X — наружу, +Y — вверх, +Z — к дулу) на детали ствола.")]
        [SerializeField] private WeaponEjectionPort _ejectionPort = new WeaponEjectionPort();
        [Tooltip("Дефолты звука, вибрации и вылета категории ствола. Пустые поля Audio/Haptics/Ejection ствола берутся отсюда.")]
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
            public WeaponUseContext Use;
            public ulong ContextGeneration;
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
        private WeaponEjectionExecutor _ejectionOut;
        private WeaponLedgerIntegrity _integrity;

        // _prepared — пассивная часть (порт и учёт SDK, подписки, исполнители): события SDK уже ставятся в очередь.
        // _configured — машина запущена шагом Configured (T01/T05): шаги, команды учёту, кадр.
        private bool _prepared, _configured, _started, _stepping, _draining, _dispatching, _subscribed;
        private bool _haveContext, _lastCtx;
        private int _initAttempts, _initRejects;
        private WS.WeaponRole _lastRole;
        private WeaponUseContext _lastUse, _automationUse;
        private bool _automationGranted, _contextResetPending, _reconciling;
        private ulong _contextGeneration;
        private WS.LedgerView _view;
        private bool _viewDirty = true;
        private float _viewReadAt;
        private WS.PoseTarget _poseTarget;

        public override bool IsConfigured => _configured;
        /// <summary>Учёт SDK подключён портом (пассивная часть, с OnEnable); машина может быть ещё не запущена.</summary>
        internal bool IsLedgerAttached => _prepared;
        public override WeaponReadinessProfile Profile => _profile;
        public override int TriggerIndex => _triggerIndex;
        public WeaponMechanismRig Rig => _rig;
        public WeaponAudioSet Audio => _audio;
        public WeaponHapticSet Haptics => _haptics;
        public WeaponEjectionSet Ejection => _ejection;
        public WeaponEjectionPort EjectionPort => _ejectionPort;
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
        public int EjectaSpawned => _ejectionOut?.Spawned ?? 0;
        public bool HintOn => _feedback != null && _feedback.HintOn;
        public float ActionProgress => _rig.IsPrepared ? _rig.HandleProgress : 0f;
        public bool IsActionAtRest => _rig.IsPrepared && _rig.IsAtRest();
        public bool IsActionAtValidatedRear => _rig.IsPrepared && _rig.IsAtValidatedRear();

        // ── Жизненный цикл ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Активная часть: шаг Configured (T01 — однократный Initialize у автора). Пассивная часть уже сделана в OnEnable;
        /// если она не удалась (порядок Awake, ошибка данных) — полная настройка с логом причины.
        /// </summary>
        private void Start()
        {
            _started = true;
            if (_configured) return;
            if (_prepared) Activate();
            else Configure(out _);
        }

        /// <summary>
        /// Пассивная часть при активации объекта — до Start (ledger-spawn-ready). Клиент получает spawn и фиксацию
        /// Initialize автора одной пачкой: объект уже активен, Start ещё не вызван. Учёт SDK должен быть включён к этому
        /// моменту, иначе replay отклоняется и ствол навсегда расходится по ревизии. Команд учёту и шагов машины здесь нет;
        /// события SDK до Start ставятся в очередь и разбираются шагом Configured. Повторный OnEnable после Start — полная
        /// настройка, как прежде. Отказ здесь молчит: Start повторит настройку и запишет причину.
        /// </summary>
        private void OnEnable()
        {
            UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;
            UxrStateSaveImplementer.StateSerialized += OnStateSerialized;
            if (_started) Configure(out _);
            else Prepare(false, out _);
        }

        private void OnDisable()
        {
            RevokeAutomation();
            UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;
            if (_configured)
            {
                _port.TearingDown = true;
                try { Step(Capture(WS.WeaponEvent.Of(WS.WeaponEventKind.Disabled), false)); }
                finally { _port.TearingDown = false; }
            }
            Teardown();
        }

        // Статическое событие переживает объект, уничтоженный без OnDisable (DestroyImmediate в EditMode).
        private void OnDestroy() => UxrStateSaveImplementer.StateSerialized -= OnStateSerialized;

        /// <summary>Настроить хост по профилю и rig (Start/OnEnable). false — ствол не управляется, причина в логе.</summary>
        public bool Configure(out string error)
        {
            if (!Prepare(true, out error)) return false;
            Activate();
            return true;
        }

        /// <summary>
        /// Пассивная часть настройки: проверка данных, порт и учёт SDK (<c>TryEnableReadiness</c>), подписки, исполнители,
        /// новая машина. Команд учёту и шагов машины нет. Идемпотентна: повторный вызов сначала снимает прежнее.
        /// </summary>
        private bool Prepare(bool log, out string error)
        {
            if (_prepared) Teardown();
            _weapon = GetComponent<UxrFirearmWeapon>();
            if (!TryValidate(out error))
            {
                if (log) GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: не настроен — {error}", this);
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
                _ejectionOut = new WeaponEjectionExecutor(_weapon, _ejectionPort, _ejection, _feedbackDefaults);
                _integrity = new WeaponLedgerIntegrity(_weapon, this);
            }
            // До порта: если учёт включить не удалось, отказ replay (SDK-патч 76, LedgerDisabled) всё равно виден в логе.
            _integrity.Attach();
            if (!_port.TryInstall(out error))
            {
                _port.Uninstall();
                if (log) GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: не настроен — {error}", this);
                return false;
            }
            _action = new WeaponActionSensor(_rig);
            _action.HandleGrabbed += OnHandleGrabbed;
            _action.HandleReleased += OnHandleReleased;
            _weapon.StateChanged += OnWeaponStateChanged;
            _subscribed = true;
            _pose.Attach();
            _trigger.Reset();
            _magazine.Rebase();
            _ledger.Remember();
            _queue.Clear();
            _haveContext = false;
            _viewDirty = true;
            _initAttempts = _initRejects = 0;
            _machine = new WS.WeaponStateMachine(_axes);
            _prepared = true;
            return true;
        }

        /// <summary>Запуск машины: события, накопленные с OnEnable, разбираются, затем шаг Configured.</summary>
        private void Activate()
        {
            _configured = true;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.Configured)); // T01: однократная миграция учёта у автора
            Drain();
        }

        private void Teardown()
        {
            RevokeAutomation();
            _haveContext = _contextResetPending = false;
            _lastUse = default;
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
            _prepared = _configured = false;
        }

        /// <summary>
        /// Проверка данных хоста без запуска (сборщик, preflight волн): профиль, спуск с гнездом, механизм и оси машины —
        /// те же правила, что при настройке в игре. Учёт SDK не подключается, состояние не меняется.
        /// </summary>
        public bool TryValidateConfiguration(out string error)
        {
            if (_prepared) { error = null; return true; }
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
            _ejectionOut.Track(idle);
            _feedback.Refresh();
        }

        /// <summary>
        /// Ствол в покое: никто не держит, нет жеста, клипа и событий, Action уже в своей позе. Шаг пропускается — на Quest
        /// у лежащих стволов нет работы машины и записи трансформов (риск «перф» плана п. 7).
        /// </summary>
        private bool IsIdle()
        {
            if (_queue.Count > 0 || _trigger.Pressed || _rig.IsHandleHeld || _lastCtx || _automationGranted || _context.MainHand() != null) return false;
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
            InvalidateLostAutomation();
            if (!_configured || _stepping || _draining || _dispatching || !CanCommandNow()) return;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.ActionSampled));
            Drain();
        }

        /// <summary>Подготовка оружия бота (контракт <c>BotGunner</c>): событие машины T44–T46, true — готово стрелять.</summary>
        public override bool RequestAutomationPreparation()
        {
            InvalidateLostAutomation();
            if (!_configured || _stepping || _draining || _dispatching || !CanCommandNow()) return false;
            WeaponUseContext use = ReconcileUse();
            if (!IsUseEligible(use) || !StateEventAuthority.IsWorldAuthority) return false;
            _automationUse = use;
            _automationGranted = true;
            StepNow(WS.WeaponEvent.Of(WS.WeaponEventKind.AutomationPrepareRequested));
            Drain();
            WeaponUseContext current = ReconcileUse();
            return use.SameKey(current) && IsUseEligible(current) && HasAutomationGrant(current) && _weapon.IsReadyToFire(_triggerIndex);
        }

        internal bool HasAutomationGrant(in WeaponUseContext use) =>
            _automationGranted && use.Kind != WeaponUseKind.None && use.SameKey(_automationUse);

        private bool IsUseEligible(in WeaponUseContext use) =>
            _configured && isActiveAndEnabled && _weapon != null && _weapon.isActiveAndEnabled && _weapon.CanUse &&
            use.Kind != WeaponUseKind.None && use.User != null && use.AuthoredHere && StateEventAuthority.IsAuthorOfItem(_weapon) &&
            !(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) &&
            (use.Kind == WeaponUseKind.Equipment ? StateEventAuthority.IsWorldAuthority :
                UxrGrabManager.HasInstance && UxrGrabManager.Instance.isActiveAndEnabled && use.Hand != null &&
                use.Hand.isActiveAndEnabled && use.Epoch > 0 && use.Hand.GrabLifetimeEpoch == use.Epoch);

        private void RevokeAutomation()
        {
            _automationGranted = false;
            _automationUse = default;
            _contextGeneration++;
            _trigger?.Reset();
        }

        /// <summary>Отзыв безопасен и внутри replay: авторский reset выполняется позже с верхнего уровня.</summary>
        private void InvalidateLostAutomation()
        {
            if (!_automationGranted) return;
            WeaponUseContext current = WeaponUseContext.Resolve(_weapon, _triggerIndex);
            if (IsUseEligible(current) && current.SameKey(_automationUse)) return;
            RevokeAutomation();
            _contextResetPending = true;
            // Один SyncCallDepth собственного SDK-вызова не означает replay и не отзывает разрешение.
        }

        /// <summary>Отзыв до очереди. Reset шагает напрямую: StepNow сначала исполнил бы старые события.</summary>
        private WeaponUseContext ReconcileUse()
        {
            WeaponUseContext use = WeaponUseContext.Resolve(_weapon, _triggerIndex);
            if (_reconciling || _stepping || _dispatching || !_configured) return use;
            _reconciling = true;
            try
            {
                WS.WeaponRole role = StateEventAuthority.IsAuthorOfItem(_weapon) ? WS.WeaponRole.Author : WS.WeaponRole.Observer;
                bool eligible = IsUseEligible(use);
                bool authorityChanged = _haveContext && role != _lastRole;
                bool changed = _haveContext && (!_lastUse.SameKey(use) || _lastUse.Hand != use.Hand || (_lastCtx && !eligible));
                bool reset = authorityChanged || _contextResetPending || (changed && (_lastCtx || _automationGranted));
                if (authorityChanged || changed || _contextResetPending || (_automationGranted && !eligible)) RevokeAutomation();
                _contextResetPending = false;
                _haveContext = true; _lastRole = role; _lastCtx = eligible; _lastUse = use;
                if (reset)
                    Step(Capture(WS.WeaponEvent.Of(authorityChanged ? WS.WeaponEventKind.AuthorityChanged : WS.WeaponEventKind.ContextLost), false));
                return use;
            }
            finally { _reconciling = false; }
        }

        // ── Очередь и шаг ───────────────────────────────────────────────────────────────────

        private static bool CanCommandNow() =>
            UxrStateSyncImplementer.SyncCallDepth == 0 && !(UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync);

        /// <summary>Событие с верхнего уровня: сначала разобрать очередь, затем шаг на свежем снимке.</summary>
        private void StepNow(in WS.WeaponEvent e, bool rateOfFire = false)
        {
            InvalidateLostAutomation();
            if (!_configured || _stepping || _dispatching || !CanCommandNow()) return;
            ReconcileUse();
            Drain();
            Pending pending = Capture(e, rateOfFire);
            Step(pending);
        }

        /// <summary>Событие изнутри синхронизации SDK: в очередь со снимком на момент события.</summary>
        private void Enqueue(in WS.WeaponEvent e, bool rateOfFire = false, bool recapture = false)
        {
            InvalidateLostAutomation();
            if (!_prepared) return; // до Start (пассивная часть) событие ждёт шага Configured
            _viewDirty = true;
            Pending pending = recapture ? new Pending { Event = e, Recapture = true } : Capture(e, rateOfFire);
            _queue.Enqueue(pending);
        }

        private Pending Capture(in WS.WeaponEvent e, bool rateOfFire)
        {
            WeaponUseContext use = WeaponUseContext.Resolve(_weapon, _triggerIndex);
            WS.WeaponContext context = _context.Read(use, out UxrGrabber hand, rateOfFire);
            return new Pending
            {
                Event = e, Ledger = ReadView(), Context = context, MainHand = hand,
                Use = use, ContextGeneration = _contextGeneration,
                Sample = _action.Sample(hand != null ? hand.Avatar : null)
            };
        }

        private void Drain()
        {
            InvalidateLostAutomation();
            if (_draining || _stepping || _dispatching || !_configured || !CanCommandNow()) return;
            ReconcileUse();
            _draining = true;
            try
            {
                for (int guard = 0; _queue.Count > 0 && guard < DrainLimit; guard++)
                {
                    ReconcileUse();
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
            Pending step = pending;
            if (pending.Context.ActsAsAuthor && !MayAuthor(pending))
            {
                // Фиксации сохраняют звук/вылет/позу наблюдателя, но старый ключ больше не выполняет авторские строки.
                step.Context = new WS.WeaponContext(WS.WeaponRole.Observer, false, true, pending.Context.Blocked,
                    pending.Context.RofTimer, pending.Context.IsWorldAuthority, false, false);
                WS.ActionSample sample = pending.Sample;
                step.Sample = new WS.ActionSample(sample.Held, false, sample.HandleProgress, sample.MinRequiredProgress,
                    sample.AllAtRest, sample.AtValidatedRear);
                step.MainHand = null;
            }
            _stepping = true;
            _out.Clear();
            try { _machine.Step(step.Event, step.Ledger, step.Sample, step.Context, _out); }
            catch (Exception exception) { GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: шаг {pending.Event.Kind} — {exception}", this); }
            finally { _stepping = false; }
            Dispatch(step);
        }

        private static bool IsControlEvent(WS.WeaponEventKind kind) => kind == WS.WeaponEventKind.Configured ||
            kind == WS.WeaponEventKind.SnapshotLoaded || kind == WS.WeaponEventKind.ContextLost ||
            kind == WS.WeaponEventKind.AuthorityChanged || kind == WS.WeaponEventKind.Disabled;

        private bool MayAuthor(in Pending pending, bool requireUsable = false)
        {
            InvalidateLostAutomation();
            if (!pending.Context.ActsAsAuthor || !CanCommandNow() || !StateEventAuthority.IsAuthorOfItem(_weapon)) return false;
            if (IsControlEvent(pending.Event.Kind)) return true;
            WeaponUseContext current = WeaponUseContext.Resolve(_weapon, _triggerIndex);
            bool liveEligible = IsUseEligible(current);
            // Свежий VR-шаг с CanUse=false сохраняет прежний авторский отклик об упоре; команды всё равно запрещены.
            bool stepEligible = liveEligible || (!requireUsable && current.Kind == WeaponUseKind.VrHand && current.AuthoredHere &&
                _configured && isActiveAndEnabled && _weapon.isActiveAndEnabled && current.Hand != null &&
                current.Hand.isActiveAndEnabled && current.Epoch > 0 && current.Hand.GrabLifetimeEpoch == current.Epoch &&
                UxrGrabManager.HasInstance && UxrGrabManager.Instance.isActiveAndEnabled);
            bool valid = pending.ContextGeneration == _contextGeneration && pending.Use.SameKey(current) &&
                         pending.Use.Hand == current.Hand && stepEligible &&
                         (current.Kind != WeaponUseKind.Equipment ||
                             (pending.Context.HasAuthorContext && pending.Context.AutomationAllowed && HasAutomationGrant(current)));
            // Старый pending не отзывает новое разрешение. Отзыв требует потери именно текущего live-ключа.
            if (_automationGranted && (!liveEligible || !current.SameKey(_automationUse)))
            {
                RevokeAutomation();
                _contextResetPending = true;
            }
            return valid;
        }

        private void Dispatch(in Pending pending)
        {
            _dispatching = true;
            try
            {
                for (int index = 0; index < _out.Commands.Count; index++)
                {
                    WS.LedgerCommand command = _out.Commands[index];
                    if (!MayAuthor(pending, requireUsable: true)) break;
                    bool accepted;
                    try { accepted = _port.Execute(command); }
                    catch (Exception exception)
                    {
                        accepted = false;
                        GameLog.WeaponSystem.Error($"[WeaponSystem] {name}: команда {command.Kind} — {exception}", this);
                    }
                    _viewDirty = true;
                    if (command.Kind == WS.LedgerCommandKind.Initialize) LogInitialize(accepted);
                    if (accepted) continue;
                    // Остальные команды шага рассчитаны на ревизию после отклонённой — учёт отклонил бы и их.
                    CommandsRejected++;
                    _queue.Enqueue(Capture(WS.WeaponEvent.RejectedCommand(command.Kind), false));
                    break;
                }
                bool vrFeedback = pending.Use.Kind == WeaponUseKind.VrHand;
                UxrGrabber mainHand = vrFeedback ? pending.MainHand ?? _context.MainHand() : null;
                for (int index = 0; index < _out.Cues.Count; index++)
                {
                    _sound.Play(_out.Cues[index], _out.Reasons[index], mainHand);
                    _ejectionOut.Play(_out.Cues[index]);
                }
                for (int index = 0; index < _out.Haptics.Count; index++) _feedback.Haptic(_out.Haptics[index], mainHand, vrFeedback ? _action.HandleHand() : null);
                if (_out.HintSet) _feedback.Hint(_out.HintOn);
                foreach (WS.WeaponReport report in _out.Reports) Log(report);
                if (_out.HasPose) _poseTarget = _out.PoseValue;
            }
            finally { _dispatching = false; }
        }

        /// <summary>
        /// Повтор Initialize без спама: первый отказ — предупреждение с причиной (видимые условия порта), следующие — Verbose;
        /// успех после отказов — Info с номером попытки.
        /// </summary>
        private void LogInitialize(bool accepted)
        {
            _initAttempts++;
            if (accepted)
            {
                if (_initRejects > 0)
                    GameLog.WeaponSystem.Info($"[WeaponSystem] {name}: учёт инициализирован со {_initAttempts}-й попытки.", this);
                _initAttempts = _initRejects = 0;
                return;
            }
            _initRejects++;
            string why = _port.LastInitializeRefusal ?? "причина не определена";
            if (_initRejects == 1)
                GameLog.WeaponSystem.Warning($"[WeaponSystem] {name}: учёт отклонил Initialize — {why}. Повтор: хват основной рукой, " +
                                             "магазин, нажатие спуска, смена автора, запрос бота; дальше отказы — Verbose.", this);
            else GameLog.WeaponSystem.Verbose($"[WeaponSystem] {name}: Initialize отклонён ({_initRejects}-й раз) — {why}.", this);
        }

        private void Log(in WS.WeaponReport report)
        {
            switch (report.Kind)
            {
                case WS.WeaponReportKind.CommandRejected when report.Command == WS.LedgerCommandKind.Initialize:
                    break; // отказ Initialize уже записан LogInitialize (без спама)
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
            if (!_prepared || !ReferenceEquals(sender, _weapon)) return;
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
            if (!_prepared || !ReferenceEquals(sender, _weapon) || args?.Serializer == null || !args.Serializer.IsReading) return;
            if (_automationGranted)
            {
                RevokeAutomation();
                _contextResetPending = true;
            }
            // Событие приходит до загрузки остальных компонентов: снимок для шага берётся при разборе очереди.
            _trigger.Reset();
            _magazine.Rebase();
            Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.SnapshotLoaded), recapture: true);
        }

        private void OnHandleGrabbed() => Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleGrabbed));
        private void OnHandleReleased() => Enqueue(WS.WeaponEvent.Of(WS.WeaponEventKind.HandleReleased));
    }
}
