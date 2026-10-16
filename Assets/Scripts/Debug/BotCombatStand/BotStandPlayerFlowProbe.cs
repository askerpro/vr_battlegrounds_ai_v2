using System;
using System.Collections.Generic;
using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Замеры player-flow ботов на стенде (база гибрида — отчёты cc-0 / pf-round). Только чтение: ничего не пишет в
    /// трансформы, хват, оружие и учёт. Покадрово — после всех стадий UXR (<c>UxrManager.AvatarsUpdated</c>):
    /// ошибка дула к точке прицеливания ИИ, зазоры граббер↔снап (фактический и заданный драйвером), скольжение стоп,
    /// время маркеров бот-кода и GC кадра (<see cref="ProfilerRecorder"/>); по событиям — исходы запросов выстрела
    /// (<see cref="BotRuntimeController.ShotSampled"/>), попадания в контрольную цель (<c>UxrActor.DamageReceived</c>),
    /// исходящие байты Mirror на аватар и сессию бота (<c>NetworkDiagnostics.OutMessageEvent</c>), ошибки консоли.
    /// Этап pf-equipment: хронология снаряжения (цели и прибытие кистей, хваты, уборка в кобуру, выбор оружия, выстрелы,
    /// смерть, призрак, новое тело), время подвода кисти, зазор кисть–снап в момент хвата, состав бот-компонентов каждого
    /// тела, писатели и действия после смерти, Unarmed (Blaze/выстрелы/поза покоя).
    /// </summary>
    public sealed class BotStandPlayerFlowProbe : IDisposable
    {
        private static readonly string[] MarkerNames =
        {
            "BotDirector.Think", "BotRuntimeController.Update", "BotRuntimeController.LateUpdate",
            "BotAvatarDriver.Apply", "BotHands.Tick", "BotWeaponAim.Solve", "BotGunner.Sees",
            // Кадр выстрела: весь блок выстрела, гейт, automation, тракт SDK, подписчики стенда.
            "BotRuntimeController.Shot", "BotGunner.ShotGate", "BotGunner.Automation", "BotGunner.TryToShootRound",
            "BotRuntimeController.ShotSampled",
            // Этап pf-equipment: цели и траектории кистей, уборка; вложенные — поза покоя, подвод к снапу, уборка;
            // вызовы SDK хвата/отпускания из BotHands.
            "BotRuntimeController.Equipment", "BotRuntimeController.Equipment.Rest", "BotRuntimeController.Equipment.Reach",
            "BotRuntimeController.Equipment.Stow", "BotHands.GrabObject", "BotHands.Release",
            // Разложение пиков BotHands.Tick (ревью manipulation 5005).
            "BotHands.CanGrab", "BotHands.SnapCheck", "BotHands.GrabQuery", "BotHands.ResolveGrips"
        };
        private static readonly string[] OutcomeNames = Enum.GetNames(typeof(BotShotOutcome));
        private const int MaxTimelineEvents = 24;

        /// <summary>Стопа «стоит»: не выше нижней из двух стоп кадра больше чем на столько, метры.</summary>
        private const float PlantedAboveLowest = 0.03f;
        /// <summary>…и её вертикальная скорость в мире меньше этой, м/с.</summary>
        private const float PlantedVerticalSpeed = 0.25f;
        /// <summary>
        /// Опора начинается, только если горизонтальная скорость стопы в мире ниже этой, м/с: низко проходящая маховая стопа
        /// (ниже 3 см над опорной в середине шага) иначе засчитывалась «стоящей» и раздувала скольжение (pf-round2).
        /// Дальше опора длится по вертикальным признакам — её скольжение и мерим.
        /// </summary>
        private const float PlantedOnsetHorizontalSpeed = 0.15f;
        /// <summary>Окно после выстрела для замера смещения оружия от граббера (корневая отдача SDK), секунды.</summary>
        private const float PostShotSeconds = 0.3f;
        private const int MaxOccludedExamples = 10;
        /// <summary>Переходный интервал после смены хвата (эпоха граббера) или телепорта корня, секунды.</summary>
        private const float SettleSeconds = 0.5f;
        /// <summary>Скачок корня за кадр, после которого кадры считаются переходными (Place/телепорт), метры.</summary>
        private const float TeleportJump = 0.5f;
        /// <summary>Зазор, выше которого кадр разбирается по причине (досягаемость/прочее), мм.</summary>
        private const float GapAttributionMm = 10f;
        private const int MaxConsoleSamples = 8;
        private const int MaxEquipmentEvents = 96;
        /// <summary>Сколько первых выстрелов каждого выбранного оружия попадает в хронологию снаряжения.</summary>
        private const int ShotsPerWeaponInTimeline = 3;

        private readonly BotStandSession _session;
        private readonly float _start;
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[MarkerNames.Length];
        private readonly List<float>[] _markerMs = new List<float>[MarkerNames.Length];
        private ProfilerRecorder _gc;
        private readonly List<float> _gcBytes = new List<float>();

        private readonly List<float> _muzzleEngaged = new List<float>();
        private readonly List<float> _muzzleShot = new List<float>();
        private readonly Dictionary<string, int> _outcomes = new Dictionary<string, int>();
        private readonly List<float> _primaryGrabberMm = new List<float>();
        private readonly List<float> _supportGrabberMm = new List<float>();
        private readonly List<float> _primaryCommandMm = new List<float>();
        private readonly List<float> _supportCommandMm = new List<float>();
        private readonly List<float> _primaryGrabberRawMm = new List<float>();
        private readonly List<float> _supportGrabberRawMm = new List<float>();
        private readonly List<float> _primaryCommandRawMm = new List<float>();
        private readonly List<float> _supportCommandRawMm = new List<float>();
        private readonly List<float> _rightReachExcessMm = new List<float>();
        private readonly List<float> _leftReachExcessMm = new List<float>();
        private readonly List<float> _pullbackMm = new List<float>();
        private readonly List<float> _muzzleToCenter = new List<float>();
        private int _settleAfterGrab, _settleAfterTeleport, _pullbackLimited;
        private int _gapFramesReach, _gapFramesOther;
        private float _settleUntil = -1f;
        private int _rightEpoch, _leftEpoch;
        private Vector3 _rootPrevious;
        private bool _rootKnown;
        private readonly List<float> _footAboveRoot = new List<float>();
        private int _footSamples, _footPlantedSamples;

        private readonly Vector3[] _footPrevious = new Vector3[2];
        private readonly bool[] _footPlanted = new bool[2];
        private bool _footReady;
        private float _plantedSlide, _plantedSeconds, _bodyTravel;
        private Vector3 _bodyPrevious;

        private long _bytes;
        private int _messages;
        // Этап pf-network: та же выборка для контрольной цели (кукла с сервера) — сравнение «бот не дороже».
        private long _targetBytes;
        private int _targetMessages;
        private uint _targetAvatarNetId, _targetSessionNetId;
        // Этап pf-network: поза каждого выстрела на сервере для сверки с клиентом на той же метке времени.
        private const int MaxShotPoses = 400;
        private readonly List<object> _shotPoses = new List<object>();
        private readonly List<object> _networkInventory = new List<object>();
        private float[] _preDriverWristL, _preDriverWristR;
        private int _preDriverFrame = -1;
        private Pose _preDriverPoseL, _preDriverPoseR;
        // Периодическая выборка поз на сетке серверного времени (0,25 с) — сверка с клиентом без выстрелов.
        private readonly BotStandPoseGrid _poseGrid = new BotStandPoseGrid(800);
        private int _hits, _foreignDamage;
        private readonly List<string> _occludedExamples = new List<string>();
        private readonly List<float> _notAimedDeg = new List<float>();
        private readonly List<float> _postShotGrabberMm = new List<float>();
        private float _lastShotTime = float.NegativeInfinity;
        private readonly List<float> _lagMm = new List<float>(), _lagDeg = new List<float>(), _lagPreviousMm = new List<float>();
        private readonly List<float> _commandedSpeed = new List<float>(), _commandedAngularSpeed = new List<float>(), _gapForLag = new List<float>();
        private Pose _previousCommanded;
        private int _previousCommandedFrame = -1;
        private float _previousCommandedTime;
        private int _lagGapFrames, _lagGapCloserToPrevious;
        private int _footEpisodes;
        private readonly List<object> _timeline = new List<object>();
        private bool _timelinePrimary, _timelineSupport, _timelineGap;
        private int _timelineShots;
        private int _consoleErrors;
        private readonly List<string> _consoleSamples = new List<string>();
        private int _frames;
        private int _useFrames, _useVrHand, _useAuthored, _useEpoch;

        private PlayerController _player;
        private UxrAvatar _avatar;
        private BotGunner _gunner;
        private BotAvatarDriver _driver;
        private BotRuntimeController _runtime;
        private BotWeaponAim _aim;
        private BotHands _hands;
        private BotCombatDriver _combat;
        private uint _avatarNetId, _sessionNetId;

        // ── Этап pf-equipment ──
        private readonly List<object> _equipment = new List<object>();
        private readonly List<object> _bodies = new List<object>();
        private readonly List<float> _reachPrimarySeconds = new List<float>(), _stowSeconds = new List<float>();
        private readonly List<float> _raiseSeconds = new List<float>(), _supportReachSeconds = new List<float>();
        private readonly List<float> _grabGrabberMm = new List<float>(), _grabCommandedMm = new List<float>();
        private readonly List<float> _supportGrabGrabberMm = new List<float>(), _supportGrabCommandedMm = new List<float>();
        private int _reachTimeoutsSeen, _supportTimeoutsSeen, _shotsGateClosed;
        /// <summary>Уборки, где SDK не положил ствол в якорь, а уронил (Stows − StowsPlaced, по всем телам; ревью 5005).</summary>
        private int _stowDropped;
        // Хват после прибытия кисти (eq-round2: должно быть ≤ 0,15 с, в т.ч. на ходу) и первый выстрел после экипировки.
        private readonly List<float> _primaryGrabAfterArrival = new List<float>(), _supportGrabAfterArrival = new List<float>();
        private readonly List<float> _firstShotAfterEquip = new List<float>();
        private float _primaryArrivedAt = -1f, _supportArrivedAt = -1f, _equippedAt = -1f;
        private BotHandGoal _rightGoal, _leftGoal;
        private bool _rightArrivedSeen, _leftArrivedSeen;
        private int _primaryGrabsSeen, _supportGrabsSeen, _supportReleasesSeen, _stowsSeen, _stowsPlacedSeen;
        private UxrFirearmWeapon _selectedSeen;
        private bool _selectedAnchoredSeen;
        private int _selectedShots;
        private UxrGrabbableObject _stowedGrip;
        private float _stowedAt;
        /// <summary>Оружие прежнего тела при смене тела (смерть → призрак): где оно через секунду — в руке, якоре, на полу.</summary>
        private UxrFirearmWeapon _previousWeapon;
        private Vector3 _previousFeet;
        private float _previousWeaponAt;
        /// <summary>Секунды после смены тела до замера оружия прежнего тела.</summary>
        private const float PreviousWeaponSeconds = 1f;
        private bool _aliveSeen, _bodyBornDead;
        private int _shotsSeen, _shotsPreviousBodies, _bodyChanges;
        private int _driverWritesDead, _grabsDead, _shotsDead, _ghostHandsAimFrames, _deadFrames;
        // Призрак: его ведёт тот же драйвер (кисти в покое); куда он дошёл по приказу директора.
        private BotBody _botBody;
        private int _ghostFrames, _ghostDriverWrites, _ghostRestFrames, _ghostBodies;
        private float _ghostTravel, _ghostStart = -1f, _ghostReachedZoneAfter = -1f;
        private Vector3 _ghostPrevious, _ghostFirstFeet, _ghostLastFeet;
        private bool _ghostKnown;
        private int _ghostTeam;
        private int _unarmedFrames, _unarmedBlazeFrames, _unarmedRestFrames, _unarmedShotRequests;
        private UxrActor _subjectActor, _targetActor;
        private readonly DamageTrace _damageTrace;
        private bool _disposed;

        /// <summary>Пассивный журнал публичного урона: ограничивает память, не меняет классификацию попаданий.</summary>
        private sealed class DamageTrace
        {
            private const int MaxEvents = 512;
            private const int MaxBindings = 32;
            private sealed class Binding
            {
                public UxrActor Actor;
                public Dictionary<string, object> Row;
            }

            private readonly List<object> _events = new List<object>();
            private readonly List<Binding> _bindings = new List<Binding>();
            private int _observed, _dropped, _bindingsDropped, _errors;
            private string _firstError;

            private static Dictionary<string, object> Clock() => new Dictionary<string, object>
            {
                ["frame"] = Time.frameCount, ["gameTime"] = Time.time,
                ["realtime"] = Time.realtimeSinceStartup, ["networkTime"] = NetworkTime.localTime
            };

            public void Bind(string role, PlayerController player, UxrActor actor)
            {
                try
                {
                    if (_bindings.Count >= MaxBindings) { _bindingsDropped++; return; }
                    var row = Clock();
                    row["role"] = role;
                    row["actorInstanceId"] = actor != null ? (object)actor.GetInstanceID() : null;
                    row["managedNull"] = ReferenceEquals(actor, null);
                    row["unityNull"] = actor == null;
                    row["destroyedNull"] = !ReferenceEquals(actor, null) && actor == null;
                    row["bodyInstanceId"] = player != null ? (object)player.GetInstanceID() : null;
                    row["avatarNetId"] = player != null ? (object)player.netId : null;
                    row["sessionNetId"] = player != null ? (object)player.SessionNetId : null;
                    row["alive"] = player != null ? (object)player.IsAlive : null;
                    _bindings.Add(new Binding { Actor = actor, Row = row });
                }
                catch (Exception e) { Error(e); }
            }

            private Dictionary<string, object> Actor(UxrActor actor)
            {
                bool managedNull = ReferenceEquals(actor, null);
                bool unityNull = actor == null;
                Binding known = null;
                if (!managedNull)
                    for (int i = _bindings.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(_bindings[i].Actor, actor)) { known = _bindings[i]; break; }
                // Managed-ссылка позволяет найти сохранённые IDs после Unity Destroy; к мёртвому объекту не обращаемся.
                PlayerController player = !unityNull ? actor.GetComponentInParent<PlayerController>() : null;
                return new Dictionary<string, object>
                {
                    ["managedNull"] = managedNull, ["unityNull"] = unityNull,
                    ["destroyedNull"] = !managedNull && unityNull,
                    ["actorInstanceId"] = known != null ? known.Row["actorInstanceId"] :
                        !unityNull ? (object)actor.GetInstanceID() : null,
                    ["bodyInstanceId"] = known != null ? known.Row["bodyInstanceId"] :
                        player != null ? (object)player.GetInstanceID() : null,
                    ["avatarNetId"] = known != null ? known.Row["avatarNetId"] :
                        player != null ? (object)player.netId : null,
                    ["sessionNetId"] = known != null ? known.Row["sessionNetId"] :
                        player != null ? (object)player.SessionNetId : null,
                    ["bindingRole"] = known != null ? known.Row["role"] : null,
                    ["bindingFrame"] = known != null ? known.Row["frame"] : null
                };
            }

            public void Damage(UxrDamageEventArgs e, UxrActor currentSubject, UxrActor boundTarget, int hits, int foreign)
            {
                _observed++;
                if (_events.Count >= MaxEvents) { _dropped++; return; }
                try
                {
                    var row = Clock();
                    row["source"] = Actor(e.ActorSource);
                    row["currentSubject"] = Actor(currentSubject);
                    row["eventTarget"] = Actor(e.ActorTarget);
                    row["boundTarget"] = Actor(boundTarget);
                    row["sourceReferenceEqualsCurrent"] = ReferenceEquals(e.ActorSource, currentSubject);
                    row["creditedToCurrentSubject"] = currentSubject != null && e.ActorSource == currentSubject;
                    row["hitsAfter"] = hits;
                    row["foreignAfter"] = foreign;
                    row["damageType"] = e.DamageType.ToString();
                    row["damage"] = e.Damage;
                    row["dies"] = e.Dies;
                    row["raycastValid"] = e.DamageType == UxrDamageType.ProjectileHit;
                    if (e.DamageType == UxrDamageType.ProjectileHit)
                    {
                        Collider collider = e.RaycastHit.collider;
                        row["colliderInstanceId"] = collider != null ? (object)collider.GetInstanceID() : null;
                        row["colliderName"] = collider != null ? collider.name : null;
                        Vector3 point = e.RaycastHit.point;
                        row["hitPoint"] = new[] { point.x, point.y, point.z };
                    }
                    _events.Add(row);
                }
                catch (Exception error) { _dropped++; Error(error); }
            }

            private void Error(Exception error)
            {
                _errors++;
                if (_firstError != null) return;
                string message = error.GetType().Name + ": " + error.Message;
                _firstError = message.Length <= 240 ? message : message.Substring(0, 240);
            }

            public Dictionary<string, object> Write()
            {
                var bindings = new List<object>();
                foreach (Binding binding in _bindings) bindings.Add(binding.Row);
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["eventLimit"] = MaxEvents, ["bindingLimit"] = MaxBindings,
                    ["observedEvents"] = _observed, ["dropped"] = _dropped,
                    ["bindingsDropped"] = _bindingsDropped, ["errors"] = _errors, ["firstError"] = _firstError,
                    ["incomplete"] = _dropped != 0 || _bindingsDropped != 0 || _errors != 0,
                    ["events"] = _events, ["bindings"] = bindings,
                    ["scope"] = "Только публичный DamageReceived подписанной контрольной цели и Bind тел. " +
                                "До подписки события неизвестны; точный projectile ID/terminal не доступен. " +
                                "Actor IDs уничтоженного объекта известны только при сохранённой managed-ссылке из Bind. " +
                                "Классификация hits/foreignDamage не изменена."
                };
            }
        }

        public BotStandPlayerFlowProbe(BotStandSession session)
        {
            _session = session;
            _start = Time.time;
            // Только сценарий со штатной смертью: обычные/perf case не выделяют журнал и его строки.
            BotStandRunOptions options = BotCombatStand.Instance != null ? BotCombatStand.Instance.ActiveOptions : null;
            _damageTrace = options != null && options.KillAtSeconds >= 0f ? new DamageTrace() : null;
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                _markerMs[i] = new List<float>();
                _recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerNames[i]);
            }
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            UxrManager.AvatarsUpdated += Sample;
            UxrManager.StageUpdating += OnStageUpdating;
            NetworkDiagnostics.OutMessageEvent += OnOutMessage;
            Application.logMessageReceived += OnLog;
        }

        private void Sample()
        {
            if (_disposed) return;
            try
            {
                PlayerController player = _session.Subject != null ? _session.Subject.ActiveAvatar : null;
                if (player == null) return;
                Bind(player);
                BindTarget(_session.Target != null ? _session.Target.ActiveAvatar : null);
                _frames++;
                SamplePoseGrid();

                for (int i = 0; i < _recorders.Length; i++)
                    if (_recorders[i].Valid) _markerMs[i].Add(_recorders[i].LastValue * 1e-6f);
                if (_gc.Valid) _gcBytes.Add(_gc.LastValue);

                bool settling = UpdateSettle();
                SampleEquipment();
                SampleFeet();
                SampleMuzzle();
                SampleGrips(settling);
            }
            catch (Exception e)
            {
                GameLog.Debug.Warning("[BotStandPlayerFlowProbe] Замер кадра: " + e.Message);
            }
        }

        private void Bind(PlayerController player)
        {
            if (_player == player) return;
            if (_runtime != null) _runtime.ShotSampled -= OnShot;
            if (_gunner != null) _shotsPreviousBodies += _gunner.ShotsFired;
            if (_player != null)
            {
                _bodyChanges++;
                _previousWeapon = _gunner != null ? _gunner.SelectedFirearm : null;
                _previousFeet = _player.transform.position;
                _previousWeaponAt = Time.time;
            }
            _player = player;
            _avatar = player.GetComponent<UxrAvatar>();
            _gunner = player.GetComponent<BotGunner>();
            _driver = player.GetComponent<BotAvatarDriver>();
            _runtime = player.GetComponent<BotRuntimeController>();
            _aim = player.GetComponent<BotWeaponAim>();
            if (_runtime != null) _runtime.ShotSampled += OnShot;
            _avatarNetId = player.netId;
            _poseGrid.Reset();
            _sessionNetId = player.Session != null ? player.Session.netId : 0;
            _subjectActor = player._actor;
            _damageTrace?.Bind("subject", player, _subjectActor);
            _footReady = false;
            _rootKnown = false;
            _rightEpoch = _leftEpoch = 0;

            _hands = player.GetComponent<BotHands>();
            _combat = player.GetComponent<BotCombatDriver>();
            _botBody = player.GetComponent<BotBody>();
            _ghostKnown = false;
            _rightGoal = _leftGoal = BotHandGoal.None;
            _rightArrivedSeen = _leftArrivedSeen = false;
            _primaryGrabsSeen = _hands != null ? _hands.PrimaryGrabs : 0;
            _supportGrabsSeen = _hands != null ? _hands.SupportGrabs : 0;
            _supportReleasesSeen = _hands != null ? _hands.SupportReleases : 0;
            _stowsSeen = _hands != null ? _hands.Stows : 0;
            _stowsPlacedSeen = _hands != null ? _hands.StowsPlaced : 0;
            _reachTimeoutsSeen = _runtime != null ? _runtime.ReachTimeouts : 0;
            _supportTimeoutsSeen = _runtime != null ? _runtime.SupportTimeouts : 0;
            _primaryArrivedAt = _supportArrivedAt = _equippedAt = -1f;
            _selectedSeen = null;
            _selectedAnchoredSeen = false;
            _shotsSeen = _gunner != null ? _gunner.ShotsFired : 0;
            _aliveSeen = player.IsAlive;
            _bodyBornDead = !player.IsAlive;
            _bodies.Add(DescribeBody(player, "bind"));
            if (_networkInventory.Count < 8) _networkInventory.Add(BotStandNetPoses.Inventory(_avatar));
            Equipment(_bodyChanges == 0 ? "firstBody" : _bodyBornDead ? "ghostOrDeadBody" : "newBody",
                player.SourcePrefab != null ? player.SourcePrefab.name : player.name, null);
        }

        /// <summary>
        /// Состав тела: сколько каких бот-компонентов (повторные — признак двойного добавления), жив ли, какой комплект
        /// (призрак — пассивный: без BotHands/BotWeaponAim/BotCombatDriver). Аллокации — только при смене тела.
        /// </summary>
        private static Dictionary<string, object> DescribeBody(PlayerController player, string when)
        {
            return new Dictionary<string, object>
            {
                ["when"] = when, ["time"] = Time.time, ["frame"] = Time.frameCount, ["body"] = player.GetInstanceID(),
                ["prefab"] = player.SourcePrefab != null ? player.SourcePrefab.name : null, ["alive"] = player.IsAlive,
                ["botAvatarDriver"] = player.GetComponents<BotAvatarDriver>().Length,
                ["botHands"] = player.GetComponents<BotHands>().Length,
                ["botWeaponAim"] = player.GetComponents<BotWeaponAim>().Length,
                ["botRuntimeController"] = player.GetComponents<BotRuntimeController>().Length,
                ["botGunner"] = player.GetComponents<BotGunner>().Length,
                ["botBody"] = player.GetComponents<BotBody>().Length,
                ["botNavigator"] = player.GetComponents<BotNavigator>().Length,
                ["botCombatDriver"] = player.GetComponents<BotCombatDriver>().Length,
                ["runtimeGhostKit"] = player.GetComponent<BotRuntimeController>() is BotRuntimeController runtime && runtime.Passive
            };
        }

        /// <summary>
        /// Хронология снаряжения после всех стадий кадра: цели и прибытие кистей (время подвода), хваты (источник —
        /// якорь/свободный предмет, зазор граббер↔снап и заданная кисть↔снап в кадре хвата), отпускание поддержки,
        /// уборка (лёг ли в якорь), выбор оружия, первые выстрелы, смерть; после смерти — запись драйвера, хваты и
        /// выстрелы (ожидается 0); Unarmed — Blaze, запросы выстрела, поза покоя. Только чтение.
        /// </summary>
        private void SampleEquipment()
        {
            if (_player == null) return;
            float now = Time.time;
            bool alive = _player.IsAlive;
            if (_aliveSeen && !alive) Equipment("death", null, null);
            else if (!_aliveSeen && alive) Equipment("revivedInPlace", null, null);
            _aliveSeen = alive;

            int shots = _gunner != null ? _gunner.ShotsFired : 0;
            int grabs = _hands != null ? _hands.PrimaryGrabs + _hands.SupportGrabs : 0;
            if (!alive)
            {
                _deadFrames++;
                bool written = _driver != null && _driver.AppliedFrame == Time.frameCount;
                // Погибшее живое тело (до смены на призрака) не ведётся; призрака ведёт драйвер — это допустимо.
                if (written && !_bodyBornDead) _driverWritesDead++;
                if (grabs > _primaryGrabsSeen + _supportGrabsSeen) _grabsDead += grabs - _primaryGrabsSeen - _supportGrabsSeen;
                if (shots > _shotsSeen) _shotsDead += shots - _shotsSeen;
                if (_bodyBornDead) SampleGhost(written);
            }

            if (_runtime != null)
            {
                if (_runtime.RightGoal != _rightGoal)
                {
                    _rightGoal = _runtime.RightGoal;
                    _rightArrivedSeen = false;
                    Equipment("right:" + _rightGoal, null, _runtime.RightGoalSeconds);
                }
                if (!_rightArrivedSeen && _runtime.RightArrived)
                {
                    _rightArrivedSeen = true;
                    float took = now - _runtime.RightGoalSince;
                    if (_rightGoal == BotHandGoal.ReachPrimary) _reachPrimarySeconds.Add(took);
                    else if (_rightGoal == BotHandGoal.Stow) _stowSeconds.Add(took);
                    else if (_rightGoal == BotHandGoal.Aim) _raiseSeconds.Add(took);
                    if (_rightGoal == BotHandGoal.ReachPrimary) _primaryArrivedAt = now;
                    Equipment("rightArrived:" + _rightGoal, null, took);
                }
                if (_runtime.LeftGoal != _leftGoal)
                {
                    _leftGoal = _runtime.LeftGoal;
                    _leftArrivedSeen = false;
                    Equipment("left:" + _leftGoal, null, null);
                }
                if (_runtime.SupportTimeouts > _supportTimeoutsSeen)
                {
                    _supportTimeoutsSeen = _runtime.SupportTimeouts;
                    Equipment("supportTimeout", null, RuntimeSupportTimeout());
                }
                if (_runtime.ReachTimeouts > _reachTimeoutsSeen)
                {
                    _reachTimeoutsSeen = _runtime.ReachTimeouts;
                    Equipment("reachTimeout", null, Nullable(_runtime.RightReachGapMm));
                }
                if (!_leftArrivedSeen && _runtime.LeftArrived)
                {
                    _leftArrivedSeen = true;
                    float took = now - _runtime.LeftGoalSince;
                    if (_leftGoal == BotHandGoal.Support)
                    {
                        _supportReachSeconds.Add(took);
                        _supportArrivedAt = now;
                    }
                    Equipment("leftArrived:" + _leftGoal, null, took);
                }
            }

            UxrFirearmWeapon selected = _gunner != null ? _gunner.SelectedFirearm : null;
            BotHands.TryResolveGrips(selected, out UxrGrabbableObject grip, out int primary, out _);
            if (_hands != null)
            {
                if (_hands.PrimaryGrabs > _primaryGrabsSeen)
                {
                    GrabEvent("primaryGrab", _hands.LastPrimaryGrab, _grabGrabberMm, _grabCommandedMm);
                    if (_primaryArrivedAt >= 0f) _primaryGrabAfterArrival.Add(now - _primaryArrivedAt);
                    _primaryArrivedAt = -1f;
                    _equippedAt = now;
                }
                if (_hands.SupportGrabs > _supportGrabsSeen)
                {
                    GrabEvent("supportGrab", _hands.LastSupportGrab, _supportGrabGrabberMm, _supportGrabCommandedMm);
                    if (_supportArrivedAt >= 0f) _supportGrabAfterArrival.Add(now - _supportArrivedAt);
                    _supportArrivedAt = -1f;
                }
                if (_hands.SupportReleases > _supportReleasesSeen) Equipment("supportRelease", Name(selected), null);
                if (_hands.Stows > _stowsSeen)
                {
                    // Имя — убранного предмета (BotHands), не выбранного: к этому кадру стрелок уже выбрал покупку.
                    bool placed = _hands.StowsPlaced > _stowsPlacedSeen;
                    UxrGrabbableObject stowed = _hands.LastStowedItem;
                    Equipment(placed ? "stowPlacedByRelease" : "stowReleasedNotPlaced", stowed != null ? stowed.name : null,
                        _hands.LastStowAnchor);
                    // Уронил: отпускание без якоря (в т.ч. по StowSettleSeconds, когда предмет не вошёл в радиус якоря).
                    int dropped = (_hands.Stows - _stowsSeen) - (_hands.StowsPlaced - _stowsPlacedSeen);
                    if (dropped > 0)
                    {
                        _stowDropped += dropped;
                        Equipment("stowDropped", stowed != null ? stowed.name : null, dropped);
                    }
                    _stowedGrip = stowed;
                    _stowedAt = now;
                }
                _primaryGrabsSeen = _hands.PrimaryGrabs;
                _supportGrabsSeen = _hands.SupportGrabs;
                _supportReleasesSeen = _hands.SupportReleases;
                _stowsSeen = _hands.Stows;
                _stowsPlacedSeen = _hands.StowsPlaced;
            }

            // Убранный пистолет: в якоре ли он после отпускания (плавное размещение SDK) — одно событие.
            if (_stowedGrip != null && now - _stowedAt > 0.05f)
            {
                Equipment(_stowedGrip.CurrentAnchor != null ? "stowedInAnchor:" + _stowedGrip.CurrentAnchor.name : "stowedNotInAnchor",
                    _stowedGrip.name, now - _stowedAt);
                _stowedGrip = null;
            }

            if (_previousWeapon != null && now - _previousWeaponAt >= PreviousWeaponSeconds) PreviousWeaponEvent();
            else if (!ReferenceEquals(_previousWeapon, null) && _previousWeapon == null)
            {
                Equipment("previousBodyWeapon", null, "destroyed");
                _previousWeapon = null;
            }

            if (selected != _selectedSeen)
            {
                Equipment("selected", Name(selected), grip != null && grip.CurrentAnchor != null ? "inAnchor" : "free");
                _selectedSeen = selected;
                _selectedShots = 0;
            }
            _selectedAnchoredSeen = grip != null && grip.CurrentAnchor != null;

            if (alive && shots > _shotsSeen)
            {
                if (_runtime != null && !_runtime.SupportGateOpen) _shotsGateClosed += shots - _shotsSeen;
                if (_equippedAt >= 0f)
                {
                    _firstShotAfterEquip.Add(now - _equippedAt);
                    _equippedAt = -1f;
                }
                _selectedShots += shots - _shotsSeen;
                if (_selectedShots <= ShotsPerWeaponInTimeline) Equipment("shot", Name(selected), shots);
            }
            _shotsSeen = shots;

            if (alive && _gunner != null && !_gunner.HasWeapon)
            {
                _unarmedFrames++;
                if (_combat != null && _combat.CombatActive) _unarmedBlazeFrames++;
                if (_gunner.ShotRequested) _unarmedShotRequests++;
                if (_runtime != null && _runtime.RightGoal == BotHandGoal.Rest && _runtime.LeftGoal == BotHandGoal.Rest) _unarmedRestFrames++;
            }
        }

        /// <summary>
        /// Кадр призрака: комплект без рук/прицела (компоненты BotHands/BotWeaponAim — ожидается 0), запись драйвера
        /// (допустима: он единственный писатель), кисти в покое, путь ног и вход в зону спавна своей команды
        /// (приказ директора — GoHome). Только чтение.
        /// </summary>
        private void SampleGhost(bool written)
        {
            _ghostFrames++;
            if (written) _ghostDriverWrites++;
            if (_hands != null || _aim != null) _ghostHandsAimFrames++;
            if (_runtime != null && _runtime.RightGoal == BotHandGoal.Rest && _runtime.LeftGoal == BotHandGoal.Rest) _ghostRestFrames++;
            if (_botBody == null) return;
            Vector3 feet = _botBody.Feet;
            if (!_ghostKnown)
            {
                _ghostKnown = true;
                _ghostBodies++;
                _ghostStart = Time.time;
                _ghostFirstFeet = feet;
                _ghostTeam = _player.Session != null ? _player.Session.TeamIndex : 0;
                Equipment("ghostDriven", _runtime != null && _runtime.Passive ? "ghostKit" : "noGhostKit", written);
            }
            else _ghostTravel += Planar(feet - _ghostPrevious);
            _ghostPrevious = feet;
            _ghostLastFeet = feet;
            if (_ghostReachedZoneAfter < 0f && _player.Session != null && _player.Session.IsInSpawnZone)
            {
                _ghostReachedZoneAfter = Time.time - _ghostStart;
                Equipment("ghostInOwnSpawnZone", null, _ghostReachedZoneAfter);
            }
        }

        /// <summary>Расстояние по полу от последней точки призрака до точки спавна его команды (конец case), метры; -1 — нет.</summary>
        private float GhostDistanceToOwnZone()
        {
            if (_ghostBodies == 0) return -1f;
            foreach (VrBattlegrounds.Maps.TeamSpawnZone zone in UnityEngine.Object.FindObjectsByType<VrBattlegrounds.Maps.TeamSpawnZone>(FindObjectsSortMode.None))
                if (zone != null && zone.Team != null && zone.Team.teamIndex == _ghostTeam && zone.SpawnPoint != null)
                    return Planar(zone.SpawnPoint.position - _ghostLastFeet);
            return -1f;
        }

        /// <summary>
        /// Хват (замер BotHands в кадре хвата, до GrabObject): предмет, откуда (якорь/свободный — по прошлому кадру), зазор
        /// фактического граббера и заданного драйвером граббера до снапа SDK.
        /// </summary>
        private void GrabEvent(string kind, BotGrabMoment moment, List<float> grabber, List<float> commanded)
        {
            if (!float.IsNaN(moment.GrabberToSnapMm)) grabber.Add(moment.GrabberToSnapMm);
            if (!float.IsNaN(moment.CommandedToSnapMm) && !float.IsInfinity(moment.CommandedToSnapMm)) commanded.Add(moment.CommandedToSnapMm);
            Equipment(kind, moment.Item != null ? moment.Item.name : null, new Dictionary<string, object>
            {
                ["source"] = kind == "primaryGrab" ? (_selectedAnchoredSeen ? "anchor" : "free") : "weapon",
                ["grabFrame"] = moment.Frame,
                ["grabberToSnapMm"] = Nullable(moment.GrabberToSnapMm),
                ["commandedToSnapMm"] = Nullable(moment.CommandedToSnapMm),
                ["commandedToSnapDeg"] = Nullable(moment.CommandedToSnapDegrees)
            });
        }

        /// <summary>
        /// Оружие прежнего тела через <see cref="PreviousWeaponSeconds"/> после смены тела: держит ли его кто-то, в якоре ли
        /// оно, высота над ногами прежнего тела (упало ли), есть ли Rigidbody с физикой. Сравнить с человеком — тот же путь
        /// gameplay (AvatarTeardown/ServerDropEquipment). Только чтение.
        /// </summary>
        private void PreviousWeaponEvent()
        {
            UxrFirearmWeapon weapon = _previousWeapon;
            _previousWeapon = null;
            BotHands.TryResolveGrips(weapon, out UxrGrabbableObject grip, out _, out _);
            Rigidbody body = grip != null ? grip.GetComponent<Rigidbody>() : null;
            Equipment("previousBodyWeapon", weapon.name, new Dictionary<string, object>
            {
                ["grabbed"] = grip != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(grip),
                ["anchor"] = grip != null && grip.CurrentAnchor != null ? grip.CurrentAnchor.name : null,
                ["heightAbovePreviousRootM"] = weapon.transform.position.y - _previousFeet.y,
                ["rigidbodyDynamic"] = body != null && !body.isKinematic
            });
        }

        private static float RuntimeSupportTimeout() => BotRuntimeController.SupportTimeoutSeconds;

        private static string Name(UxrFirearmWeapon firearm) => firearm != null ? firearm.name : null;

        private void Equipment(string kind, string what, object value)
        {
            if (_equipment.Count >= MaxEquipmentEvents) return;
            _equipment.Add(new Dictionary<string, object>
            {
                ["event"] = kind, ["what"] = what, ["value"] = value, ["time"] = Time.time - _start, ["frame"] = Time.frameCount,
                ["body"] = _player != null ? _player.GetInstanceID() : 0
            });
        }

        private void BindTarget(PlayerController target)
        {
            UxrActor actor = target != null ? target._actor : null;
            if (_targetActor == actor) return;
            _targetAvatarNetId = target != null ? target.netId : 0;
            _targetSessionNetId = target != null && target.Session != null ? target.Session.netId : 0;
            if (_targetActor != null) _targetActor.DamageReceived -= OnDamage;
            _targetActor = actor;
            _damageTrace?.Bind("target", target, _targetActor);
            if (_targetActor != null) _targetActor.DamageReceived += OnDamage;
        }

        private void SampleFeet()
        {
            if (_avatar == null || _avatar.AvatarRig == null) return;
            Transform root = _avatar.transform;
            Transform left = _avatar.AvatarRig.LeftLeg != null ? _avatar.AvatarRig.LeftLeg.Foot : null;
            Transform right = _avatar.AvatarRig.RightLeg != null ? _avatar.AvatarRig.RightLeg.Foot : null;
            Vector3 body = _avatar.CameraTransform != null ? _avatar.CameraFloorPosition : root.position;
            float dt = Time.deltaTime;
            if (_footReady && left != null && right != null && dt > 1e-5f)
            {
                _bodyTravel += Planar(body - _bodyPrevious);
                // Опора — нижняя из двух стоп кадра (при ходьбе хотя бы одна на земле), а не высота над корнем:
                // кость Foot у ботинок MEF выше 0,12 м над корнем, и прежний порог не срабатывал ни разу (pf-round1).
                float lowest = Mathf.Min(left.position.y, right.position.y);
                Foot(0, left, lowest, dt);
                Foot(1, right, lowest, dt);
                _footAboveRoot.Add(lowest - root.position.y);
            }
            else if (left != null && right != null)
            {
                _footPrevious[0] = left.position;
                _footPrevious[1] = right.position;
                _footPlanted[0] = _footPlanted[1] = false;
                _footReady = true;
            }
            _bodyPrevious = body;
        }

        private void Foot(int index, Transform foot, float lowest, float dt)
        {
            Vector3 now = foot.position;
            Vector3 before = _footPrevious[index];
            bool support = now.y - lowest < PlantedAboveLowest && Mathf.Abs(now.y - before.y) / dt < PlantedVerticalSpeed;
            bool planted = support && (_footPlanted[index] || Planar(now - before) / dt < PlantedOnsetHorizontalSpeed);
            if (planted && !_footPlanted[index]) _footEpisodes++;
            _footSamples++;
            if (planted) _footPlantedSamples++;
            if (planted && _footPlanted[index])
            {
                _plantedSlide += Planar(now - before);
                _plantedSeconds += dt;
            }
            _footPlanted[index] = planted;
            _footPrevious[index] = now;
        }

        private void SampleMuzzle()
        {
            if (_gunner == null || !_gunner.HasAimPoint || _gunner.Firearm == null || _gunner.Muzzle == null) return;
            Transform muzzle = _gunner.Muzzle;
            // Фактический луч дула против точки прицеливания ИИ: точка — из габаритов цели и разброса ИИ, от позы бота не
            // зависит. Ноль без выстрелов ожидаем: отдачи нет, оружие следует грабберу (GrabberToSnap < 1 мм).
            if (BotAimMath.TryGetMuzzleError(_avatar.transform, muzzle.position, muzzle.forward, _gunner.AimPoint,
                                             out BotAimMath.MuzzleError error))
                _muzzleEngaged.Add(error.Angle);
            // Для контекста: против центра тела цели (включает намеренный разброс ИИ).
            Vector3 toCenter = _gunner.AimCenter - muzzle.position;
            if (toCenter.sqrMagnitude > 1e-6f) _muzzleToCenter.Add(Vector3.Angle(muzzle.forward, toCenter));
        }

        /// <summary>
        /// Переходные кадры: смена эпохи хвата любой руки (хват/перехват/отпускание — SDK ведёт оружие к снапу) или
        /// скачок корня больше <see cref="TeleportJump"/> (Place стенда/телепорт). Окно <see cref="SettleSeconds"/>.
        /// Такие кадры идут только в сырые ряды и считаются; дефекты вне окна остаются в основных рядах.
        /// </summary>
        private bool UpdateSettle()
        {
            if (_avatar == null) return false;
            UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
            int rightEpoch = right != null && right.GrabbedObject != null ? right.GrabLifetimeEpoch : 0;
            int leftEpoch = left != null && left.GrabbedObject != null ? left.GrabLifetimeEpoch : 0;
            if (rightEpoch != _rightEpoch || leftEpoch != _leftEpoch)
            {
                _settleUntil = Mathf.Max(_settleUntil, Time.time + SettleSeconds);
                _rightEpoch = rightEpoch;
                _leftEpoch = leftEpoch;
                _settleAfterGrab++;
            }
            Vector3 root = _avatar.transform.position;
            if (_rootKnown && Planar(root - _rootPrevious) > TeleportJump)
            {
                _settleUntil = Mathf.Max(_settleUntil, Time.time + SettleSeconds);
                _settleAfterTeleport++;
            }
            _rootPrevious = root;
            _rootKnown = true;
            return Time.time < _settleUntil;
        }

        private void SampleGrips(bool settling)
        {
            if (_gunner == null || _avatar == null || !UxrGrabManager.HasInstance) return;
            UxrFirearmWeapon firearm = _gunner.Firearm;
            if (!BotHands.TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out int support)) return;

            // Условие automation (weapon-system): контекст VrHand, автор здесь, SDK-хват с GrabLifetimeEpoch > 0.
            WeaponUseContext use = WeaponUseContext.Resolve(firearm, 0);
            _useFrames++;
            if (use.Kind == WeaponUseKind.VrHand) _useVrHand++;
            if (use.AuthoredHere) _useAuthored++;
            if (use.Epoch > 0) _useEpoch++;

            bool applied = _driver != null && _driver.AppliedFrame == Time.frameCount;
            float rightExcess = float.NaN, leftExcess = float.NaN;
            if (applied && _driver.AppliedRight) rightExcess = ReachExcessMm(UxrHandSide.Right, _driver.AppliedRightHand);
            if (applied && _driver.AppliedLeft) leftExcess = ReachExcessMm(UxrHandSide.Left, _driver.AppliedLeftHand);

            float primaryCommand = Gap(grip, primary, UxrHandSide.Right, applied && _driver.AppliedRight,
                applied ? _driver.AppliedRightHand : default, settling, _primaryGrabberMm, _primaryGrabberRawMm, _primaryCommandMm, _primaryCommandRawMm);
            float supportCommand = support >= 0
                ? Gap(grip, support, UxrHandSide.Left, applied && _driver.AppliedLeft, applied ? _driver.AppliedLeftHand : default,
                      settling, _supportGrabberMm, _supportGrabberRawMm, _supportCommandMm, _supportCommandRawMm)
                : float.NaN;
            Timeline(grip, primary, support, primaryCommand, supportCommand, settling);
            if (Time.time - _lastShotTime < PostShotSeconds) PostShotGap(grip, primary);
            if (settling) return;

            SampleWeaponLag(grip, Max(primaryCommand, supportCommand));
            if (!float.IsNaN(rightExcess)) _rightReachExcessMm.Add(rightExcess);
            if (!float.IsNaN(leftExcess)) _leftReachExcessMm.Add(leftExcess);
            if (_aim != null)
            {
                _pullbackMm.Add(_aim.LastReachPullback * 1000f);
                if (_aim.LastReachLimited) _pullbackLimited++;
            }

            // Разбор зазора: заданное запястье дальше длины руки (LimitHandReach UXR укорачивает кисть) или иная причина.
            bool bigGap = primaryCommand > GapAttributionMm || supportCommand > GapAttributionMm;
            if (!bigGap) return;
            bool reach = (!float.IsNaN(rightExcess) && rightExcess > -5f) || (!float.IsNaN(leftExcess) && leftExcess > -5f);
            if (reach) _gapFramesReach++;
            else _gapFramesOther++;
        }

        /// <summary>
        /// Насколько заданное драйвером запястье дальше длины руки от плеча после IK этого кадра, мм (+ — недостижимо).
        /// Только чтение рига (UpperArm/Forearm и исходная позиция запястья), для разбора причины зазора.
        /// </summary>
        private float ReachExcessMm(UxrHandSide side, Pose hand)
        {
            // Замер стенда (только чтение после IK); адаптер эти кости не читает.
            UltimateXR.Avatar.Rig.UxrAvatarArm arm = _avatar.GetArm(side);
            Transform wrist = arm != null && arm.Hand != null ? arm.Hand.Wrist : null;
            Transform wristParent = wrist != null ? wrist.parent : null;
            var armInfo = _avatar.AvatarRigInfo != null ? _avatar.AvatarRigInfo.GetArmInfo(side) : null;
            if (arm == null || arm.UpperArm == null || arm.Forearm == null || wristParent == null || armInfo == null ||
                armInfo.HandUniversalLocalAxes == null) return float.NaN;
            // Длина руки — как в UxrArmIKSolver: плечевая кость + предплечье до исходной позиции запястья.
            Vector3 restWrist = wristParent.TransformPoint(armInfo.HandUniversalLocalAxes.InitialLocalPosition);
            float length = Vector3.Distance(arm.UpperArm.position, arm.Forearm.position) + Vector3.Distance(arm.Forearm.position, restWrist);
            return (Vector3.Distance(hand.position, arm.UpperArm.position) - length) * 1000f;
        }

        /// <summary>
        /// Граббер↔снап после всех стадий и заданная драйвером кисть↔снап, миллиметры. Переходные кадры
        /// (<paramref name="settling"/>) — только в сырые ряды. Возвращает заданный зазор (NaN — не измерен).
        /// </summary>
        private float Gap(UxrGrabbableObject grip, int point, UxrHandSide side, bool commanded, Pose hand, bool settling,
                          List<float> grabberGaps, List<float> grabberRaw, List<float> commandGaps, List<float> commandRaw)
        {
            UxrGrabber grabber = _avatar.GetGrabber(side);
            Transform bone = _avatar.GetHandBone(side);
            if (grabber == null || bone == null) return float.NaN;
            if (!UxrGrabManager.Instance.GetGrabbingHand(grip, point, out UxrGrabber holder) || holder != grabber) return float.NaN;
            if (!grip.ComputeRequiredGrabberTransform(grabber, point, out Vector3 snap, out Quaternion _, false)) return float.NaN;
            float grabberGap = (grabber.transform.position - snap).magnitude * 1000f;
            grabberRaw.Add(grabberGap);
            if (!settling) grabberGaps.Add(grabberGap);
            if (!commanded) return float.NaN;
            Vector3 offset = Quaternion.Inverse(bone.rotation) * (grabber.transform.position - bone.position);
            float commandGap = (hand.position + hand.rotation * offset - snap).magnitude * 1000f;
            commandRaw.Add(commandGap);
            if (!settling) commandGaps.Add(commandGap);
            return commandGap;
        }

        /// <summary>
        /// Поза оружия после кадра против W* этого кадра (<see cref="BotWeaponAim.LastWeaponPose"/>) и прошлого: отставание
        /// предмета от команды (held-motion/сглаживание SDK) отличается от прочих причин тем, что растёт со скоростью W* и
        /// ближе к W* прошлого кадра. Только чтение.
        /// </summary>
        private void SampleWeaponLag(UxrGrabbableObject grip, float gap)
        {
            if (_aim == null || _aim.LastSolveFrame != Time.frameCount) return;
            Pose commanded = _aim.LastWeaponPose;
            Transform weapon = grip.transform;
            float lag = (weapon.position - commanded.position).magnitude * 1000f;
            _lagMm.Add(lag);
            _lagDeg.Add(Quaternion.Angle(weapon.rotation, commanded.rotation));
            if (_previousCommandedFrame == Time.frameCount - 1 && Time.time > _previousCommandedTime)
            {
                float dt = Time.time - _previousCommandedTime;
                float lagPrevious = (weapon.position - _previousCommanded.position).magnitude * 1000f;
                _lagPreviousMm.Add(lagPrevious);
                _commandedSpeed.Add((commanded.position - _previousCommanded.position).magnitude / dt);
                _commandedAngularSpeed.Add(Quaternion.Angle(commanded.rotation, _previousCommanded.rotation) / dt);
                _gapForLag.Add(gap);
                if (gap > GapAttributionMm)
                {
                    _lagGapFrames++;
                    if (lagPrevious < lag) _lagGapCloserToPrevious++;
                }
            }
            _previousCommanded = commanded;
            _previousCommandedFrame = Time.frameCount;
            _previousCommandedTime = Time.time;
        }

        /// <summary>Основная рукоять ↔ граббер в окне после выстрела: признак корневой отдачи SDK (с NoRecoil ≈ 0).</summary>
        private void PostShotGap(UxrGrabbableObject grip, int primary)
        {
            UxrGrabber grabber = _avatar.GetGrabber(UxrHandSide.Right);
            if (grabber == null || grabber.GrabbedObject != grip) return;
            if (!grip.ComputeRequiredGrabberTransform(grabber, primary, out Vector3 snap, out Quaternion _, false)) return;
            _postShotGrabberMm.Add((grabber.transform.position - snap).magnitude * 1000f);
        }

        /// <summary>
        /// Хронология хвата и выстрелов (до <see cref="MaxTimelineEvents"/> событий): основной хват, поддержка, первые
        /// выстрелы, первый кадр с заданным зазором > 10 мм. Нужна, чтобы видеть, когда возникает постоянное смещение
        /// (до выстрелов, при хвате поддержки или после отдачи). Только чтение.
        /// </summary>
        private void Timeline(UxrGrabbableObject grip, int primary, int support, float primaryGap, float supportGap, bool settling)
        {
            if (_timeline.Count >= MaxTimelineEvents) return;
            UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
            bool primaryHeld = right != null && right.GrabbedObject == grip;
            bool supportHeld = support >= 0 && left != null && left.GrabbedObject == grip;
            int shots = _gunner != null ? _gunner.ShotsFired : 0;
            string reason = null;
            if (primaryHeld && !_timelinePrimary) { _timelinePrimary = true; reason = "primaryGrab"; }
            else if (supportHeld && !_timelineSupport) { _timelineSupport = true; reason = "supportGrab"; }
            else if (shots != _timelineShots && _timelineShots < 3) reason = "shot" + shots;
            else if (!settling && !_timelineGap && Max(primaryGap, supportGap) > GapAttributionMm) { _timelineGap = true; reason = "firstGapOver10mm"; }
            _timelineShots = shots;
            if (reason == null) return;

            float lagMm = float.NaN, lagDeg = float.NaN;
            if (_aim != null && _aim.LastSolveFrame == Time.frameCount)
            {
                lagMm = (grip.transform.position - _aim.LastWeaponPose.position).magnitude * 1000f;
                lagDeg = Quaternion.Angle(grip.transform.rotation, _aim.LastWeaponPose.rotation);
            }
            _timeline.Add(new Dictionary<string, object>
            {
                ["event"] = reason, ["frame"] = Time.frameCount, ["time"] = Time.time, ["shots"] = shots, ["settling"] = settling,
                ["primaryCommandedToSnapMm"] = Nullable(primaryGap), ["supportCommandedToSnapMm"] = Nullable(supportGap),
                ["weaponLagMm"] = Nullable(lagMm), ["weaponLagDeg"] = Nullable(lagDeg),
                ["inSmoothTransition"] = grip.IsInSmoothTransition
            });
        }

        private static object Nullable(float value) => float.IsNaN(value) ? null : (object)value;

        private static float Max(float a, float b) => float.IsNaN(a) ? (float.IsNaN(b) ? 0f : b) : float.IsNaN(b) ? a : Mathf.Max(a, b);

        private void OnShot(BotShotSample sample)
        {
            int index = (int)sample.Outcome;
            string key = index >= 0 && index < OutcomeNames.Length ? OutcomeNames[index] : sample.Outcome.ToString();
            _outcomes.TryGetValue(key, out int count);
            _outcomes[key] = count + 1;
            if (sample.Outcome == BotShotOutcome.Fired)
            {
                _lastShotTime = Time.time;
                if (sample.HasError) _muzzleShot.Add(sample.Error.Angle);
                RecordShotPose();
            }
            else if (sample.Outcome == BotShotOutcome.NotAimed && !float.IsNaN(sample.GateAimErrorDeg))
            {
                _notAimedDeg.Add(sample.GateAimErrorDeg);
            }
            else if (sample.Outcome == BotShotOutcome.Occluded && sample.Blocker != null && _occludedExamples.Count < MaxOccludedExamples)
            {
                GameObject blocker = sample.Blocker.gameObject;
                _occludedExamples.Add(blocker.name + " (слой " + LayerMask.LayerToName(blocker.layer) + ")");
            }
        }

        /// <summary>
        /// Начало PostUpdate UXR (до записи драйвера): запястья, какими их видит NetworkTransform, если его LateUpdate
        /// (рассылка ServerToClient, порядок 0) отработал раньше UxrManager.LateUpdate (тоже порядок 0).
        /// </summary>
        private void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.AvatarUsingTracking || _avatar == null) return;
            _preDriverWristL = BotStandNetPoses.Pose(_avatar.GetHandBone(UxrHandSide.Left));
            _preDriverWristR = BotStandNetPoses.Pose(_avatar.GetHandBone(UxrHandSide.Right));
            Transform left = _avatar.GetHandBone(UxrHandSide.Left), right = _avatar.GetHandBone(UxrHandSide.Right);
            if (left != null) _preDriverPoseL = new Pose(left.position, left.rotation);
            if (right != null) _preDriverPoseR = new Pose(right.position, right.rotation);
            _preDriverFrame = Time.frameCount;
        }

        /// <summary>
        /// Кадр сервера после всех стадий UXR: позы на сетке серверного времени (<c>NetworkTime.localTime</c> — та же шкала,
        /// что метки снимков NT у клиента). Запястья до записи драйвера — рядом: NT мог разослать любую из двух поз.
        /// </summary>
        private void SamplePoseGrid()
        {
            if (_avatar == null) return;
            BotStandPoseSet set = BotStandPoseSet.Capture(_avatar);
            if (_preDriverFrame == Time.frameCount)
            {
                set.HasPreDriver = true;
                set.PreWristL = _preDriverPoseL;
                set.PreWristR = _preDriverPoseR;
            }
            _poseGrid.Feed(NetworkTime.localTime, set, _avatarNetId);
        }

        /// <summary>
        /// Выстрел на сервере (LateUpdate 11000, после всех стадий UXR этого кадра): метка <c>NetworkTime.localTime</c>
        /// (= метка батча Mirror, с которой событие выстрела придёт клиенту), мировая поза ShotSource, корень, камера,
        /// запястья (после IK и до записи драйвера), идентификаторы оружия. Клиент пишет то же на этой метке.
        /// </summary>
        private void RecordShotPose()
        {
            if (_shotPoses.Count >= MaxShotPoses || _avatar == null || _gunner == null) return;
            UxrFirearmWeapon firearm = _gunner.Firearm;
            NetworkIdentity identity = firearm != null ? firearm.GetComponentInParent<NetworkIdentity>() : null;
            _shotPoses.Add(new Dictionary<string, object>
            {
                ["t"] = NetworkTime.localTime, ["frame"] = Time.frameCount, ["muzzle"] = BotStandNetPoses.Pose(_gunner.Muzzle),
                ["rig"] = BotStandNetPoses.Rig(_avatar),
                ["preDriverWristL"] = _preDriverFrame == Time.frameCount ? _preDriverWristL : null,
                ["preDriverWristR"] = _preDriverFrame == Time.frameCount ? _preDriverWristR : null,
                ["weaponUniqueId"] = firearm != null ? firearm.UniqueId.ToString() : null,
                ["weaponNetId"] = identity != null ? identity.netId : 0u, ["avatarNetId"] = _avatarNetId
            });
        }

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            if (e == null) return;
            // Каждое попадание по контрольной цели должно быть засчитано актору бота (в стенде стреляет только он).
            if (_subjectActor != null && e.ActorSource == _subjectActor) _hits++;
            else _foreignDamage++;
            _damageTrace?.Damage(e, _subjectActor, _targetActor, _hits, _foreignDamage);
        }

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            uint netId;
            if (info.message is RpcMessage rpc) netId = rpc.netId;
            else if (info.message is EntityStateMessage state) netId = state.netId;
            else return;
            if (netId == 0) return;
            if (netId == _targetAvatarNetId || netId == _targetSessionNetId)
            {
                _targetBytes += (long)info.bytes * info.count;
                _targetMessages += info.count;
                return;
            }
            if (netId != _avatarNetId && netId != _sessionNetId) return;
            _bytes += (long)info.bytes * info.count;
            _messages += info.count;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _consoleErrors++;
            if (_consoleSamples.Count < MaxConsoleSamples) _consoleSamples.Add(type + ": " + condition);
        }

        /// <summary>Сводка в <c>result.Metrics["playerFlow"]</c>. Критерии этапа — флаги для оценки, статус не меняют.</summary>
        public void WriteMetrics(BotStandCaseResult result)
        {
            float seconds = Mathf.Max(1e-3f, Time.time - _start);
            int bots = Mathf.Max(1, _session.OwnedBots.Count);
            // Выстрелы всех тел бота в case (смерть/возрождение меняют стрелка).
            int shots = _shotsPreviousBodies + (_gunner != null ? _gunner.ShotsFired : 0);
            int connections = NetworkServer.connections.Count;

            var markers = new Dictionary<string, object>();
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                var stats = Stats(_markerMs[i]);
                stats["meanPerBotMs"] = _markerMs[i].Count > 0 ? Mean(_markerMs[i]) / bots : 0f;
                // Кадры, где маркер вообще работал (> 0): стоимость события отдельно от «пустых» кадров.
                var active = new List<float>();
                foreach (float value in _markerMs[i]) if (value > 0f) active.Add(value);
                stats["activeFrames"] = Stats(active);
                stats["recorderValid"] = _recorders[i].Valid;
                markers[MarkerNames[i]] = stats;
            }

            var shotMuzzle = Stats(_muzzleShot);
            var supportGrabber = Stats(_supportGrabberMm);
            var primaryGrabber = Stats(_primaryGrabberMm);
            var supportCommand = Stats(_supportCommandMm);
            var primaryCommand = Stats(_primaryCommandMm);

            result.Metrics["playerFlow"] = new Dictionary<string, object>
            {
                ["mode"] = "PlayerFlow",
                ["frames"] = _frames,
                ["seconds"] = seconds,
                ["bots"] = bots,
                ["shots"] = shots,
                ["hitsOnTarget"] = _hits,
                ["hitRate"] = shots > 0 ? _hits / (float)shots : 0f,
                ["foreignDamageEventsOnTarget"] = _foreignDamage,
                ["damageTrace"] = _damageTrace?.Write() ?? new Dictionary<string, object>
                {
                    ["enabled"] = false, ["coverage"] = "unknown", ["incomplete"] = true,
                    ["reason"] = "Журнал выключен: KillAtSeconds < 0 либо ActiveOptions недоступны."
                },
                ["gate"] = new Dictionary<string, object>
                {
                    ["maxAimErrorDeg"] = BotPlayerFlowSettings.MaxShotAimErrorDegrees,
                    ["notAimedAimErrorDeg"] = Stats(_notAimedDeg),
                    ["occludedFirstColliderExamples"] = _occludedExamples,
                    ["note"] = "NotAimed — луч дула дальше допуска от точки ИИ; Occluded — путь пули до точки ИИ перекрыт не целью. " +
                               "Промах по силуэту при неточности ИИ — обычный выстрел, поэтому hitRate справочный, не критерий."
                },
                ["gripTimeline"] = _timeline,
                ["weaponLagToCommanded"] = new Dictionary<string, object>
                {
                    ["lagMm"] = Stats(_lagMm), ["lagDeg"] = Stats(_lagDeg), ["lagToPreviousCommandedMm"] = Stats(_lagPreviousMm),
                    ["commandedSpeedMps"] = Stats(_commandedSpeed), ["commandedAngularSpeedDegps"] = Stats(_commandedAngularSpeed),
                    ["corrGapVsCommandedAngularSpeed"] = Correlation(_gapForLag, _commandedAngularSpeed),
                    ["corrGapVsCommandedSpeed"] = Correlation(_gapForLag, _commandedSpeed),
                    ["corrGapVsLagToPrevious"] = Correlation(_gapForLag, _lagPreviousMm),
                    ["gapFramesOver10mm"] = _lagGapFrames, ["gapFramesCloserToPreviousCommanded"] = _lagGapCloserToPrevious,
                    ["note"] = "Поза трансформа хвата после всех стадий против W* (BotWeaponAim) этого и прошлого кадра; скорости W* — по " +
                               "соседним кадрам. Хвост зазора объясняется отставанием предмета, если он растёт со скоростью W* и оружие ближе к " +
                               "W* прошлого кадра."
                },
                ["recoilEvidence"] = new Dictionary<string, object>
                {
                    ["postShotPrimaryGrabberToSnapMm"] = Stats(_postShotGrabberMm), ["windowSeconds"] = PostShotSeconds,
                    ["note"] = "Корневая отдача SDK сдвигает оружие от граббера после выстрела; с NoRecoil должно оставаться < 1 мм."
                },
                ["shotOutcomes"] = _outcomes,
                ["muzzleErrorDeg"] = new Dictionary<string, object>
                {
                    ["atShot"] = shotMuzzle, ["engagedFrames"] = Stats(_muzzleEngaged), ["engagedToTargetCenter"] = Stats(_muzzleToCenter),
                    ["note"] = "atShot/engagedFrames — фактический луч ShotSource против точки прицеливания ИИ (габариты цели + разброс ИИ, " +
                               "от позы бота не зависят); без выстрелов отдачи нет и ~0 ожидаем. engagedToTargetCenter — против центра тела цели."
                },
                ["gripGapMm"] = new Dictionary<string, object>
                {
                    ["primaryGrabberToSnap"] = primaryGrabber, ["supportGrabberToSnap"] = supportGrabber,
                    ["primaryCommandedToSnap"] = primaryCommand, ["supportCommandedToSnap"] = supportCommand,
                    ["raw"] = new Dictionary<string, object>
                    {
                        ["primaryGrabberToSnap"] = Stats(_primaryGrabberRawMm), ["supportGrabberToSnap"] = Stats(_supportGrabberRawMm),
                        ["primaryCommandedToSnap"] = Stats(_primaryCommandRawMm), ["supportCommandedToSnap"] = Stats(_supportCommandRawMm)
                    },
                    ["excludedTransient"] = new Dictionary<string, object>
                    {
                        ["grabEpochChanges"] = _settleAfterGrab, ["rootJumps"] = _settleAfterTeleport,
                        ["framesExcludedPrimary"] = _primaryGrabberRawMm.Count - _primaryGrabberMm.Count,
                        ["framesExcludedSupport"] = _supportGrabberRawMm.Count - _supportGrabberMm.Count,
                        ["windowSeconds"] = SettleSeconds, ["rootJumpMeters"] = TeleportJump
                    },
                    ["reach"] = new Dictionary<string, object>
                    {
                        ["rightWristExcessMm"] = Stats(_rightReachExcessMm), ["leftWristExcessMm"] = Stats(_leftReachExcessMm),
                        ["weaponPullbackMm"] = Stats(_pullbackMm), ["pullbackLimitedFrames"] = _pullbackLimited,
                        ["gapFramesOver10mmReach"] = _gapFramesReach, ["gapFramesOver10mmOther"] = _gapFramesOther,
                        ["note"] = "Excess — заданное запястье минус длина руки от UpperArm после IK (+ — недостижимо, UXR LimitHandReach). " +
                                   "gapFrames: кадры вне переходных с заданным зазором > 10 мм, с недостижимым (excess > −5 мм) запястьем или без."
                    },
                    ["note"] = "GrabberToSnap — фактический граббер после всех стадий против снапа SDK (оружие следует граббер в том же кадре); " +
                               "CommandedToSnap — кисть, заданная драйвером до Manipulation, против снапа SDK после кадра."
                },
                ["footSlide"] = new Dictionary<string, object>
                {
                    ["plantedSlideMeters"] = _plantedSlide, ["plantedFootSeconds"] = _plantedSeconds,
                    ["metersPerPlantedSecond"] = _plantedSeconds > 0f ? _plantedSlide / _plantedSeconds : 0f,
                    ["bodyTravelMeters"] = _bodyTravel,
                    ["plantedSampleFraction"] = _footSamples > 0 ? _footPlantedSamples / (float)_footSamples : 0f,
                    ["plantedEpisodes"] = _footEpisodes,
                    ["onsetHorizontalSpeedMps"] = PlantedOnsetHorizontalSpeed,
                    ["lowestFootAboveRootMeters"] = Stats(_footAboveRoot),
                    ["slidePerTravelMeter"] = _bodyTravel > 0.01f ? _plantedSlide / _bodyTravel : 0f,
                    ["note"] = $"Стопа «стоит»: не выше нижней стопы кадра на {PlantedAboveLowest} м и |vy| < {PlantedVerticalSpeed} м/с в мире; " +
                               "скольжение — горизонтальный путь стопы между двумя такими кадрами подряд; база — тот же сценарий с базой cc-0 / pf-round."
                },
                ["serverMs"] = markers,
                ["gcAllocBytesPerFrame"] = Stats(_gcBytes),
                ["network"] = new Dictionary<string, object>
                {
                    ["bytesAllClients"] = _bytes, ["messages"] = _messages, ["connections"] = connections,
                    ["bytesPerSecondAllClients"] = _bytes / seconds,
                    ["bytesPerSecondPerClient"] = connections > 0 ? _bytes / seconds / connections : 0f,
                    ["target"] = new Dictionary<string, object>
                    {
                        ["bytesAllClients"] = _targetBytes, ["messages"] = _targetMessages,
                        ["bytesPerSecondPerClient"] = connections > 0 ? _targetBytes / seconds / connections : 0f,
                        ["note"] = "Контрольная цель стенда — аватар без владельца, позу пишет сервер (как кукла): база сравнения байт/с."
                    },
                    ["note"] = connections > 0 ? "RpcMessage/EntityStateMessage с netId аватара и сессии бота." :
                        "Клиентов нет — Mirror ничего не рассылает; байт/с на бота этим стендом не измерен (нужен клиент)."
                },
                ["netShotPoses"] = new Dictionary<string, object>
                {
                    ["shots"] = _shotPoses, ["bodies"] = _networkInventory, ["samples"] = _poseGrid.Samples,
                    ["sampleIntervalSeconds"] = BotStandPoseGrid.Interval,
                    ["note"] = "Сверка с клиентом (BotStandNetObserver.shotPoses) по метке t = NetworkTime.localTime сервера = метка " +
                               "батча Mirror. rig — после всех стадий UXR кадра выстрела; preDriverWrist — в начале PostUpdate UXR этого " +
                               "кадра (что увидел бы NetworkTransform, если его LateUpdate раньше UxrManager)."
                },
                ["weaponUseContext"] = new Dictionary<string, object>
                {
                    ["framesHolding"] = _useFrames, ["kindVrHand"] = _useVrHand, ["authoredHere"] = _useAuthored, ["epochPositive"] = _useEpoch
                },
                ["consoleErrors"] = _consoleErrors,
                ["consoleErrorSamples"] = _consoleSamples,
                ["equipment"] = EquipmentMetrics(),
                ["criteria"] = new Dictionary<string, object>
                {
                    ["muzzleAtShotMedianLe1_5deg"] = _muzzleShot.Count > 0 && (float)shotMuzzle["median"] <= 1.5f,
                    ["handToSnapLt10mmBothHands"] = _primaryCommandMm.Count > 0 && _supportCommandMm.Count > 0 &&
                        (float)primaryCommand["p95"] < 10f && (float)supportCommand["p95"] < 10f,
                    ["weaponToGrabberLt1mm"] = _primaryGrabberMm.Count > 0 && (float)primaryGrabber["p95"] < 1f,
                    ["consoleErrorsZero"] = _consoleErrors == 0,
                    ["allHitsCreditedToBot"] = _foreignDamage == 0,
                    ["note"] = "Флаги для оценки главным агентом; статус сценария не меняют. Скольжение стоп и время — сравнить с базой cc-0 / pf-round."
                }
            };
        }

        /// <summary>Сводка этапа pf-equipment в <c>playerFlow.equipment</c>.</summary>
        private Dictionary<string, object> EquipmentMetrics()
        {
            var bodies = new List<object>(_bodies);
            if (_player != null) bodies.Add(DescribeBody(_player, "end"));
            bool duplicates = false;
            foreach (object entry in bodies)
            {
                foreach (var pair in (Dictionary<string, object>)entry)
                    if (pair.Value is int count && count > 1 && pair.Key.StartsWith("bot", StringComparison.Ordinal)) duplicates = true;
            }
            var script = new List<object>();
            if (_session.Equipment != null) foreach (var entry in _session.Equipment.Log) script.Add(entry);
            return new Dictionary<string, object>
            {
                ["timeline"] = _equipment,
                ["script"] = script,
                ["handReachSeconds"] = new Dictionary<string, object>
                {
                    ["reachPrimary"] = Stats(_reachPrimarySeconds), ["raiseToAim"] = Stats(_raiseSeconds), ["stowToHolster"] = Stats(_stowSeconds),
                    ["leftToSupport"] = Stats(_supportReachSeconds),
                    ["plannedRangeSeconds"] = new[] { BotHandTrajectory.MinSeconds, BotHandTrajectory.MaxSeconds },
                    ["note"] = "От смены цели кисти до прибытия (BotHandTrajectory): подвод к снапу (кобура/покупка), подъём на прицел, " +
                               "к кобуре, левая к поддержке. 0 — кисть уже была на месте (короче 1 см/3°)."
                },
                ["grabMoment"] = new Dictionary<string, object>
                {
                    ["grabberToSnapMm"] = Stats(_grabGrabberMm), ["commandedToSnapMm"] = Stats(_grabCommandedMm),
                    ["support"] = new Dictionary<string, object>
                    {
                        ["grabberToSnapMm"] = Stats(_supportGrabGrabberMm), ["commandedToSnapMm"] = Stats(_supportGrabCommandedMm)
                    },
                    ["arriveMm"] = BotHands.ArriveMm, ["arriveDeg"] = BotHands.ArriveDegrees,
                    ["grabToleranceMm"] = BotHands.GrabMm, ["grabToleranceDeg"] = BotHands.GrabDegrees,
                    ["primaryGrabAfterArrivalSeconds"] = Stats(_primaryGrabAfterArrival),
                    ["supportGrabAfterArrivalSeconds"] = Stats(_supportGrabAfterArrival),
                    ["reachTimeouts"] = _runtime != null ? _runtime.ReachTimeouts : 0,
                    ["note"] = "Замер BotHands в кадре хвата (LateUpdate, после всех стадий, до GrabObject): фактический граббер и заданный " +
                               "драйвером граббер против снапа SDK этого кадра. Хват только при заданном ≤ arriveMm/arriveDeg."
                },
                ["bodies"] = bodies,
                ["bodyChanges"] = _bodyChanges,
                ["afterDeath"] = new Dictionary<string, object>
                {
                    ["deadFrames"] = _deadFrames, ["dyingBodyDriverWrites"] = _driverWritesDead, ["grabs"] = _grabsDead, ["shots"] = _shotsDead,
                    ["note"] = "Мёртвые кадры (погибшее тело до смены и призрак): хваты и выстрелы — ожидается 0; запись драйвера в погибшее " +
                               "живое тело — 0 (отпускание при смерти — сервер, AvatarTeardown). Запись в призрака — в ghost."
                },
                ["ghost"] = new Dictionary<string, object>
                {
                    ["bodies"] = _ghostBodies, ["frames"] = _ghostFrames, ["driverWrites"] = _ghostDriverWrites,
                    ["adapterHandsAimComponentFrames"] = _ghostHandsAimFrames, ["restPoseFrames"] = _ghostRestFrames,
                    ["travelMeters"] = _ghostTravel,
                    ["firstFeet"] = _ghostBodies > 0 ? new[] { _ghostFirstFeet.x, _ghostFirstFeet.y, _ghostFirstFeet.z } : null,
                    ["lastFeet"] = _ghostBodies > 0 ? new[] { _ghostLastFeet.x, _ghostLastFeet.y, _ghostLastFeet.z } : null,
                    ["reachedOwnSpawnZoneAfterSeconds"] = _ghostReachedZoneAfter,
                    ["distanceToOwnSpawnPointMeters"] = GhostDistanceToOwnZone(),
                    ["note"] = "Призрака ведёт тот же BotAvatarDriver (комплект призрака: без BotHands/BotWeaponAim, кисти в покое), " +
                               "BotBody только источник ног; директор велит GoHome — путь и вход в зону своей команды. -1 — не дошёл/нет."
                },
                ["unarmed"] = new Dictionary<string, object>
                {
                    ["frames"] = _unarmedFrames, ["blazeActiveFrames"] = _unarmedBlazeFrames, ["shotRequestFrames"] = _unarmedShotRequests,
                    ["restPoseFrames"] = _unarmedRestFrames,
                    ["note"] = "Живое тело без оружия: Blaze не должен быть активен, запросов выстрела нет, обе кисти в позе покоя."
                },
                ["hands"] = _hands == null ? null : (object)new Dictionary<string, object>
                {
                    ["primaryGrabs"] = _hands.PrimaryGrabs, ["supportGrabs"] = _hands.SupportGrabs,
                    ["supportReleases"] = _hands.SupportReleases, ["stows"] = _hands.Stows, ["stowsPlaced"] = _hands.StowsPlaced
                },
                ["stowDropped"] = _stowDropped,
                ["criteria"] = new Dictionary<string, object>
                {
                    ["noDuplicateBotComponents"] = !duplicates,
                    ["noActionsAfterDeath"] = _driverWritesDead == 0 && _grabsDead == 0 && _shotsDead == 0,
                    ["ghostDrivenWithoutHandsAim"] = _ghostBodies == 0 || (_ghostDriverWrites > 0 && _ghostHandsAimFrames == 0),
                    ["unarmedNoBlazeNoShots"] = _unarmedBlazeFrames == 0 && _unarmedShotRequests == 0,
                    ["grabGapLt10mm"] = (_grabGrabberMm.Count + _supportGrabGrabberMm.Count) > 0 &&
                                        MaxOf(_grabGrabberMm) < 10f && MaxOf(_grabCommandedMm) < 10f &&
                                        MaxOf(_supportGrabGrabberMm) < 10f && MaxOf(_supportGrabCommandedMm) < 10f,
                    ["handReachWithinPlanned"] = MaxOf(_reachPrimarySeconds) <= BotHandTrajectory.CeilingSeconds &&
                                                 MaxOf(_stowSeconds) <= BotHandTrajectory.CeilingSeconds &&
                                                 MaxOf(_supportReachSeconds) <= BotHandTrajectory.CeilingSeconds,
                    ["grabWithin150msOfArrival"] = MaxOf(_primaryGrabAfterArrival) <= 0.15f && MaxOf(_supportGrabAfterArrival) <= 0.15f,
                    ["noShotBeforeSupportOrTimeout"] = _shotsGateClosed == 0,
                    ["stowDroppedZero"] = _stowDropped == 0,
                    ["note"] = "Флаги для оценки главным агентом; статус сценария не меняют. Время подвода — по кадрам пробы; потолок " +
                               "прибытия BotHandTrajectory.CeilingSeconds (длинный кадр редактора может его превысить — смотреть timeline)."
                },
                ["firstShotAfterEquipSeconds"] = Stats(_firstShotAfterEquip),
                ["supportTimeouts"] = _runtime != null ? _runtime.SupportTimeouts : 0
            };
        }

        private static float MaxOf(List<float> values)
        {
            float max = 0f;
            for (int i = 0; i < values.Count; i++) max = Mathf.Max(max, values[i]);
            return max;
        }

        private static Dictionary<string, object> Stats(List<float> values)
        {
            var result = new Dictionary<string, object> { ["count"] = values.Count };
            if (values.Count == 0)
            {
                result["median"] = result["p95"] = result["max"] = result["mean"] = 0f;
                return result;
            }
            var sorted = new List<float>(values);
            sorted.Sort();
            result["median"] = sorted[sorted.Count / 2];
            result["p95"] = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.95f))];
            result["max"] = sorted[sorted.Count - 1];
            float mean = Mean(values);
            result["mean"] = mean;
            // Проверка невырожденности ряда: минимум, разброс и число различных значений (до 0,001).
            result["min"] = sorted[0];
            double variance = 0;
            int distinct = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                variance += (sorted[i] - mean) * (double)(sorted[i] - mean);
                if (i > 0 && sorted[i] - sorted[i - 1] > 1e-3f) distinct++;
            }
            result["std"] = (float)System.Math.Sqrt(variance / sorted.Count);
            result["distinct"] = distinct;
            return result;
        }

        /// <summary>Коэффициент корреляции Пирсона двух рядов одной длины; 0 — нет данных/разброса.</summary>
        private static float Correlation(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n < 3) return 0f;
            double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - ma, db = b[i] - mb;
                sab += da * db; saa += da * da; sbb += db * db;
            }
            return saa > 0 && sbb > 0 ? (float)(sab / System.Math.Sqrt(saa * sbb)) : 0f;
        }

        private static float Mean(List<float> values)
        {
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return values.Count > 0 ? (float)(sum / values.Count) : 0f;
        }

        private static float Planar(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UxrManager.AvatarsUpdated -= Sample;
            UxrManager.StageUpdating -= OnStageUpdating;
            NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
            Application.logMessageReceived -= OnLog;
            if (_runtime != null) _runtime.ShotSampled -= OnShot;
            if (_targetActor != null) _targetActor.DamageReceived -= OnDamage;
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
            _gc.Dispose();
        }
    }
}
