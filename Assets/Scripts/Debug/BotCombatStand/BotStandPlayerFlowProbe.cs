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
    /// Замеры этапа pf-prototype на стенде (оба режима тела — гибрид даёт базу). Только чтение: ничего не пишет в
    /// трансформы, хват, оружие и учёт. Покадрово — после всех стадий UXR (<c>UxrManager.AvatarsUpdated</c>):
    /// ошибка дула к точке прицеливания ИИ, зазоры граббер↔снап (фактический и заданный драйвером), скольжение стоп,
    /// время маркеров бот-кода и GC кадра (<see cref="ProfilerRecorder"/>); по событиям — исходы запросов выстрела
    /// (<see cref="BotRuntimeController.ShotSampled"/>), попадания в контрольную цель (<c>UxrActor.DamageReceived</c>),
    /// исходящие байты Mirror на аватар и сессию бота (<c>NetworkDiagnostics.OutMessageEvent</c>), ошибки консоли.
    /// </summary>
    public sealed class BotStandPlayerFlowProbe : IDisposable
    {
        private static readonly string[] MarkerNames =
        {
            "BotDirector.Think", "BotRuntimeController.Update", "BotRuntimeController.LateUpdate",
            "BotAvatarDriver.Apply", "BotHands.Tick", "BotWeaponAim.Solve", "BotGunner.Sees",
            // Кадр выстрела: весь блок выстрела, гейт, automation, тракт SDK, подписчики стенда.
            "BotRuntimeController.Shot", "BotGunner.ShotGate", "BotGunner.Automation", "BotGunner.TryToShootRound",
            "BotRuntimeController.ShotSampled"
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

        private readonly BotStandSession _session;
        private readonly bool _playerFlow;
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
        private uint _avatarNetId, _sessionNetId;
        private UxrActor _subjectActor, _targetActor;
        private bool _disposed;

        public BotStandPlayerFlowProbe(BotStandSession session, bool playerFlow)
        {
            _session = session;
            _playerFlow = playerFlow;
            _start = Time.time;
            for (int i = 0; i < MarkerNames.Length; i++)
            {
                _markerMs[i] = new List<float>();
                _recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerNames[i]);
            }
            _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            UxrManager.AvatarsUpdated += Sample;
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

                for (int i = 0; i < _recorders.Length; i++)
                    if (_recorders[i].Valid) _markerMs[i].Add(_recorders[i].LastValue * 1e-6f);
                if (_gc.Valid) _gcBytes.Add(_gc.LastValue);

                bool settling = UpdateSettle();
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
            _player = player;
            _avatar = player.GetComponent<UxrAvatar>();
            _gunner = player.GetComponent<BotGunner>();
            _driver = player.GetComponent<BotAvatarDriver>();
            _runtime = player.GetComponent<BotRuntimeController>();
            _aim = player.GetComponent<BotWeaponAim>();
            if (_runtime != null) _runtime.ShotSampled += OnShot;
            _avatarNetId = player.netId;
            _sessionNetId = player.Session != null ? player.Session.netId : 0;
            _subjectActor = player._actor;
            _footReady = false;
            _rootKnown = false;
            _rightEpoch = _leftEpoch = 0;
        }

        private void BindTarget(PlayerController target)
        {
            UxrActor actor = target != null ? target._actor : null;
            if (_targetActor == actor) return;
            if (_targetActor != null) _targetActor.DamageReceived -= OnDamage;
            _targetActor = actor;
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
        /// Граббер↔снап после всех стадий и (режим P) заданная драйвером кисть↔снап, миллиметры. Переходные кадры
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

        private void OnDamage(object sender, UxrDamageEventArgs e)
        {
            if (e == null) return;
            // Каждое попадание по контрольной цели должно быть засчитано актору бота (в стенде стреляет только он).
            if (_subjectActor != null && e.ActorSource == _subjectActor) _hits++;
            else _foreignDamage++;
        }

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            uint netId;
            if (info.message is RpcMessage rpc) netId = rpc.netId;
            else if (info.message is EntityStateMessage state) netId = state.netId;
            else return;
            if (netId == 0 || (netId != _avatarNetId && netId != _sessionNetId)) return;
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
            int shots = _gunner != null ? _gunner.ShotsFired : 0;
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
                ["mode"] = _playerFlow ? "PlayerFlow" : "Hybrid",
                ["frames"] = _frames,
                ["seconds"] = seconds,
                ["bots"] = bots,
                ["shots"] = shots,
                ["hitsOnTarget"] = _hits,
                ["hitRate"] = shots > 0 ? _hits / (float)shots : 0f,
                ["foreignDamageEventsOnTarget"] = _foreignDamage,
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
                               "CommandedToSnap — кисть, заданная драйвером до Manipulation, против снапа SDK после кадра (только режим P)."
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
                               "скольжение — горизонтальный путь стопы между двумя такими кадрами подряд; база — тот же сценарий в Hybrid."
                },
                ["serverMs"] = markers,
                ["gcAllocBytesPerFrame"] = Stats(_gcBytes),
                ["network"] = new Dictionary<string, object>
                {
                    ["bytesAllClients"] = _bytes, ["messages"] = _messages, ["connections"] = connections,
                    ["bytesPerSecondAllClients"] = _bytes / seconds,
                    ["bytesPerSecondPerClient"] = connections > 0 ? _bytes / seconds / connections : 0f,
                    ["note"] = connections > 0 ? "RpcMessage/EntityStateMessage с netId аватара и сессии бота." :
                        "Клиентов нет — Mirror ничего не рассылает; байт/с на бота этим стендом не измерен (нужен клиент)."
                },
                ["weaponUseContext"] = new Dictionary<string, object>
                {
                    ["framesHolding"] = _useFrames, ["kindVrHand"] = _useVrHand, ["authoredHere"] = _useAuthored, ["epochPositive"] = _useEpoch
                },
                ["consoleErrors"] = _consoleErrors,
                ["consoleErrorSamples"] = _consoleSamples,
                ["criteria"] = new Dictionary<string, object>
                {
                    ["muzzleAtShotMedianLe1_5deg"] = _muzzleShot.Count > 0 && (float)shotMuzzle["median"] <= 1.5f,
                    ["handToSnapLt10mmBothHands"] = _primaryCommandMm.Count > 0 && _supportCommandMm.Count > 0 &&
                        (float)primaryCommand["p95"] < 10f && (float)supportCommand["p95"] < 10f,
                    ["weaponToGrabberLt1mm"] = _primaryGrabberMm.Count > 0 && (float)primaryGrabber["p95"] < 1f,
                    ["consoleErrorsZero"] = _consoleErrors == 0,
                    ["allHitsCreditedToBot"] = _foreignDamage == 0,
                    ["note"] = "Флаги для оценки главным агентом; статус сценария не меняют. Скольжение стоп и время — сравнить с Hybrid."
                }
            };
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
            NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
            Application.logMessageReceived -= OnLog;
            if (_runtime != null) _runtime.ShotSampled -= OnShot;
            if (_targetActor != null) _targetActor.DamageReceived -= OnDamage;
            for (int i = 0; i < _recorders.Length; i++) _recorders[i].Dispose();
            _gc.Dispose();
        }
    }
}
