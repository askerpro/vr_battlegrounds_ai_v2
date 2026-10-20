using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Input;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Источник ввода бота с мозгом Blaze (контракт tasks/bots-input/contracts/bot-input-design.md): решения мозга →
    /// кадр устройства через общий пилот (<see cref="AvatarPilot"/>), как его дал бы человек со шлемом. Мозг прежний:
    /// директор (приказы, закупка), <see cref="BotNavigator"/> и прокси Blaze (<see cref="BotCombatDriver"/>) задают ноги
    /// и курс в <see cref="BotBody"/>, <see cref="BotGunner"/> выбирает оружие, цель, точку прицела и решает «жать курок».
    /// Ниже ввода — код человека: хват по Grip, выстрел по курку, IK, ноги от головы. Источник в SDK ничего не вызывает.
    ///
    /// <para>
    /// <b>Один тик на кадр</b> — внутри <see cref="TryRead"/> (стадия Update устройства): ноги/курс тела, ходьба головой
    /// (≤ <see cref="AvatarPilot.MaxWalkSpeed"/>; отстала больше <see cref="ResyncDistance"/> — ноги мозга и прокси
    /// переносятся к голове: правда — тело), кисти и кнопки.
    /// </para>
    ///
    /// <para>
    /// <b>Оружие.</b> Не в руке — правая кисть к снапу основной рукояти выбранного оружия (кобура, покупка у кисти) и Grip.
    /// В руке — Grip держится; у винтовки левая — к поддержке и Grip. Есть цель — наведение
    /// (правая — замкнутым циклом по дулу, левая — за поддержкой); нет — ствол опущен. Курок — когда <see cref="BotGunner.DecideTriggerPress"/> разрешил: отпущен ≥ 2 кадров,
    /// нажат 3, отпущен. Пистолет под покупку и чужой предмет — отпустить Grip (размещение и падение решает SDK).
    /// </para>
    /// </summary>
    public sealed class BlazeInputSource : IAvatarBoundInputSource
    {
        /// <summary>Голова отстала от ног мозга дальше — ноги и прокси переносятся к голове, метры.</summary>
        public const float ResyncDistance = 0.6f;

        private const float ReachTolerance = 0.02f;
        /// <summary>Рукоять уже в ладони (покупка у кисти) — Grip сразу, не ждать, пока предмет упадёт, м.</summary>
        private const float InHandTolerance = 0.05f;
        /// <summary>Предмет дальше этого по горизонтали от головы — сначала подойти, м.</summary>
        private const float ApproachDistance = 0.45f;
        /// <summary>Глаза над предметом при наклоне к нему, м (рука тянется вниз).</summary>
        private const float CrouchAboveItem = 0.55f;
        private const int ReachStableFrames = 3;
        private const int TriggerReleaseFrames = 2;
        private const int TriggerPressFrames = 3;
        private const int SeesIntervalFrames = BotPlayerFlowSettings.SeesIntervalFrames;
        private const float HoldForward = 0.40f, HoldDown = 0.12f, HoldSide = 0.12f;
        private const float AimGain = 0.7f;
        private static readonly Vector3 LowReadyFromEye = new Vector3(0.1f, -1.2f, 3f);

        private readonly AvatarPilot _pilot = new AvatarPilot();
        private UxrAvatar _avatar;
        private PlayerController _player;
        private BotBody _body;
        private BotGunner _gunner;
        private BotCombatDriver _combat;
        private int _tickFrame = -1;
        private int _seesPhase;
        private int _rightAtTarget, _leftAtTarget;
        private int _triggerFrames;   // > 0 — курок нажат ещё столько кадров
        private int _releasedFrames;  // сколько кадров курок отпущен
        private bool _aiming;
        private Quaternion _aimRotation;
        private bool _approach;       // идём к предмету: ходьба головой к нему, а не к ногам мозга
        private Vector3 _approachFloor;
        private bool _crouch;
        private float _crouchEye;

        /// <summary>Общий пилот (только чтение: стенд, диагностика).</summary>
        public AvatarPilot Pilot => _pilot;

        /// <summary>Сколько раз источник нажал курок.</summary>
        public int TriggerPresses { get; private set; }

        /// <summary>Исход последнего решения «жать курок» (диагностика).</summary>
        public BotShotOutcome LastDecision { get; private set; }

        /// <summary>Диагностика наведения (только чтение): цель правой кисти и желаемая поза оружия последнего кадра.</summary>
        public Pose LastRightTarget { get; private set; }
        public Vector3 LastAimPoint { get; private set; }

        public void Bind(UxrAvatar avatar, bool keepHead)
        {
            _avatar = avatar;
            _pilot.Bind(avatar, keepHead);
            _player = avatar != null ? avatar.GetComponent<PlayerController>() : null;
            _body = avatar != null ? avatar.GetComponent<BotBody>() : null;
            _gunner = avatar != null ? avatar.GetComponent<BotGunner>() : null;
            _combat = avatar != null ? avatar.GetComponent<BotCombatDriver>() : null;
            _rightAtTarget = _leftAtTarget = 0;
            _triggerFrames = 0;
            _releasedFrames = 0;
            _seesPhase = avatar != null ? (avatar.GetHashCode() & 0x7fffffff) % SeesIntervalFrames : 0;
            // Ноги мозга — там, где голова (новое тело продолжает с позы головы).
            if (_body != null && avatar != null) _body.Place(_pilot.FloorPosition, _pilot.Yaw);
        }

        public bool TryRead(ref DeviceFrame frame)
        {
            if (_avatar == null) return false;
            if (_tickFrame != Time.frameCount)
            {
                _tickFrame = Time.frameCount;
                Tick(Time.deltaTime);
            }
            return _pilot.TryRead(ref frame);
        }

        // ── Кадр ─────────────────────────────────────────────────────────────

        private void Tick(float dt)
        {
            if (_body == null) return;
            // Призрак ожил на месте (BotKit.Upgrade) — Blaze добавлен телу после привязки.
            if (_combat == null && _player != null && _player.IsAlive) _avatar.TryGetComponent(out _combat);
            _body.PrepareFrame();
            bool alive = _player != null && _player.IsAlive;
            if (_gunner != null && alive && (Time.frameCount + _seesPhase) % SeesIntervalFrames == 0) _gunner.RefreshSees();
            _approach = false;
            _crouch = false;
            Hands(alive);
            // Ходьба после рук: подход к предмету заменяет шаг к ногам мозга (одна ходьба на кадр).
            if (_approach) Approach(dt);
            else Walk(dt);
            if (_crouch) _pilot.CrouchTowards(_crouchEye, dt);
            else _pilot.StandUp(dt);
            Trigger(alive);
        }

        /// <summary>
        /// Подход к предмету, который дальше вытянутой руки: голова идёт к нему (≤ предела ходьбы), ноги мозга и прокси
        /// Blaze — за головой. Как человек, который хочет взять своё оружие.
        /// </summary>
        private void Approach(float dt)
        {
            _pilot.StepTowards(_approachFloor, AvatarPilot.MaxWalkSpeed, dt, ApproachDistance * 0.5f);
            _body.Place(_pilot.FloorPosition, _pilot.Yaw);
            if (_combat != null) _combat.ResyncLegs(_pilot.FloorPosition);
        }

        private void Walk(float dt)
        {
            Vector3 feet = _body.Feet;
            Vector3 floor = _pilot.FloorPosition;
            Vector3 lag = feet - floor;
            lag.y = 0f;
            if (lag.magnitude > ResyncDistance)
            {
                // Голова упёрлась или мозг убежал быстрее шага: правда — тело.
                _body.Place(floor, _pilot.Yaw);
                if (_combat != null) _combat.ResyncLegs(floor);
                feet = floor;
            }
            else if (lag.sqrMagnitude > 1e-6f)
            {
                // Курс задаёт взгляд мозга ниже (стрейф в укрытиях, взгляд на цель), не направление шага.
                _pilot.StepTowards(feet, AvatarPilot.MaxWalkSpeed, dt, 0.01f, turn: false);
            }

            // Взгляд и курс головы — от мозга (цель, по ходу, осмотр).
            Vector3 eye = _pilot.HeadPosition;
            Vector3 forward = _body.HeadRotation(eye) * Vector3.forward;
            if (_gunner != null && _gunner.HasAimPoint) forward = _gunner.AimPoint - eye;
            _pilot.TurnTowards(forward, dt, 0.5f);
            _pilot.LookAt(eye + forward.normalized * 10f, dt);
        }

        private void Hands(bool alive)
        {
            UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
            UxrFirearmWeapon firearm = alive && _gunner != null ? _gunner.SelectedFirearm : null;
            bool resolved = BotHands.TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out int support);

            // Чужой предмет в руке (не выбранное оружие) — отпустить, как человек разжал бы руку.
            ReleaseForeign(UxrHandSide.Right, right, resolved ? grip : null);
            ReleaseForeign(UxrHandSide.Left, left, resolved ? grip : null);

            if (!resolved || right == null)
            {
                Rest();
                return;
            }

            bool holdsPrimary = right.GrabbedObject == grip;
            if (holdsPrimary && _gunner.SwapPending)
            {
                // Пистолет под покупку: отпустить (размещение в кобуру или падение решает SDK).
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, false);
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, false);
                Rest();
                return;
            }

            if (!holdsPrimary)
            {
                _aiming = false;
                _pilot.Rest(UxrHandSide.Left);
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, false);
                if (!UxrGrabManager.HasInstance || UxrGrabManager.Instance.IsBeingGrabbed(grip) || _gunner.SwapPending)
                {
                    _pilot.Rest(UxrHandSide.Right);
                    _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, false);
                    return;
                }

                // Кисть к снапу рукояти (кобура едет с корпусом — цель каждый кадр), Grip по прибытии.
                if (!grip.ComputeRequiredGrabberTransform(right, primary, out Vector3 snap, out Quaternion snapRotation, false))
                {
                    Rest();
                    return;
                }

                bool inHand = (right.transform.position - snap).magnitude <= InHandTolerance;
                Vector3 toSnap = snap - _pilot.HeadPosition;
                toSnap.y = 0f;
                if (!inHand && toSnap.magnitude > ApproachDistance)
                {
                    // Дальше вытянутой руки — сначала подойти (руки опущены).
                    _approach = true;
                    _approachFloor = new Vector3(snap.x, _pilot.FloorPosition.y, snap.z);
                    _rightAtTarget = 0;
                    _pilot.Rest(UxrHandSide.Right);
                    _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, false);
                    return;
                }

                // Низкий предмет (лежит на полу) — присесть так, чтобы рука дотянулась.
                if (snap.y < _pilot.FloorPosition.y + _pilot.EyeHeight - AvatarPilot.MaxReach * 0.8f)
                {
                    _crouch = true;
                    _crouchEye = snap.y - _pilot.FloorPosition.y + CrouchAboveItem;
                }

                _pilot.ReachGrabber(UxrHandSide.Right, snap, snapRotation);
                _rightAtTarget = inHand || _pilot.GrabberAtTarget(UxrHandSide.Right, ReachTolerance) ? _rightAtTarget + 1 : 0;
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, inHand || _rightAtTarget >= ReachStableFrames);
                return;
            }

            // В руке: Grip держится, ствол — на цель или опущен.
            _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, true);
            Transform muzzle = _gunner.Muzzle;
            Vector3 eye = _pilot.HeadPosition;
            Quaternion yaw = Quaternion.AngleAxis(_pilot.Yaw, Vector3.up);
            Vector3 aimPoint = _gunner.HasAimPoint ? _gunner.AimPoint : eye + yaw * LowReadyFromEye;
            bool rifle = _gunner.WeaponCategory == WeaponCategory.Rifle;
            bool useSupport = rifle && support >= 0 && left != null;
            bool holdsSupport = useSupport && left.GrabbedObject == grip;

            if (muzzle == null)
            {
                Rest();
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, true);
                return;
            }
            Aim(right, muzzle, aimPoint, eye, yaw);

            if (useSupport)
            {
                // Левая — к снапу поддержки на стволе в его текущей позе; Grip по прибытии, дальше держится.
                if (grip.ComputeRequiredGrabberTransform(left, support, out Vector3 supportSnap, out Quaternion supportRotation, false))
                {
                    _pilot.ReachGrabber(UxrHandSide.Left, supportSnap, supportRotation);
                    bool atSupport = (left.transform.position - supportSnap).magnitude <= ReachTolerance;
                    _leftAtTarget = holdsSupport || atSupport ? _leftAtTarget + 1 : 0;
                }
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, holdsSupport || _leftAtTarget >= ReachStableFrames);
            }
            else
            {
                _pilot.Rest(UxrHandSide.Left);
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, false);
            }
        }

        /// <summary>
        /// Наведение замкнутым циклом по фактическому дулу (как стрелок скриптового клиента): точка удержания перед
        /// головой, поворот правого грабера, при котором дуло смотрит в точку; дальше — поправка по фактическому дулу
        /// (ограничения хвата SDK — AlignToController, KeepGripInPlace, вторая рука — и отдача накапливаются в команде,
        /// коэффициент &lt; 1). Прямое решение по позе оружия здесь неверно: прогон iter2-6 — поворот кисти против цели 97°.
        /// </summary>
        private void Aim(UxrGrabber grabber, Transform muzzle, Vector3 target, Vector3 eye, Quaternion yaw)
        {
            Transform hand = grabber.transform;
            Vector3 hold = eye + yaw * new Vector3(HoldSide, -HoldDown, HoldForward);
            if (!_aiming)
            {
                Quaternion handInverse = Quaternion.Inverse(hand.rotation);
                Vector3 muzzleInHand = handInverse * (muzzle.position - hand.position);
                Quaternion muzzleInHandRotation = handInverse * muzzle.rotation;
                _aimRotation = hand.rotation;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 muzzlePosition = hold + _aimRotation * muzzleInHand;
                    Vector3 muzzleForward = _aimRotation * muzzleInHandRotation * Vector3.forward;
                    _aimRotation = Quaternion.FromToRotation(muzzleForward, target - muzzlePosition) * _aimRotation;
                }
                _aiming = true;
            }
            else
            {
                Quaternion correction = Quaternion.FromToRotation(muzzle.forward, target - muzzle.position);
                _aimRotation = Quaternion.Slerp(Quaternion.identity, correction, AimGain) * _aimRotation;
            }

            _pilot.ReachGrabber(UxrHandSide.Right, hold, _aimRotation);
            LastRightTarget = new Pose(hold, _aimRotation);
            LastAimPoint = target;
        }

        private void Trigger(bool alive)
        {
            if (_triggerFrames > 0)
            {
                _triggerFrames--;
                if (_triggerFrames == 0) _pilot.Press(UxrHandSide.Right, UxrInputButtons.Trigger, false);
                return;
            }

            _pilot.Press(UxrHandSide.Right, UxrInputButtons.Trigger, false);
            _releasedFrames++;
            if (!alive || _gunner == null || !_gunner.ShotRequested) return;

            // Как человек: курок отпущен ≥ 2 кадров перед новым нажатием; решение мозга — жать или нет.
            if (_releasedFrames < TriggerReleaseFrames) return;
            LastDecision = _gunner.DecideTriggerPress();
            if (LastDecision != BotShotOutcome.Fired) return;

            _pilot.Press(UxrHandSide.Right, UxrInputButtons.Trigger, true);
            _triggerFrames = TriggerPressFrames;
            _releasedFrames = 0;
            TriggerPresses++;
        }

        private void Rest()
        {
            _aiming = false;
            _rightAtTarget = _leftAtTarget = 0;
            _pilot.Rest(UxrHandSide.Right);
            _pilot.Rest(UxrHandSide.Left);
        }

        private void ReleaseForeign(UxrHandSide side, UxrGrabber grabber, UxrGrabbableObject keep)
        {
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            if (held != null && held != keep) _pilot.Press(side, UxrInputButtons.Grip, false);
        }
    }
}
