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
    /// В руке — Grip держится; у винтовки левая — к поддержке и Grip. Есть цель — наведение жёсткого ствола двумя руками
    /// (обе кисти — по желаемой позе оружия); нет — ствол опущен. Курок — когда <see cref="BotGunner.DecideTriggerPress"/> разрешил: отпущен ≥ 2 кадров,
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
                _pilot.StepTowards(feet, AvatarPilot.MaxWalkSpeed, dt, 0.01f);
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

            if (muzzle == null || !AimRigid(grip, primary, useSupport ? support : -1, right, left, muzzle, aimPoint, eye, yaw,
                                            out Pose rightTarget, out Pose leftTarget))
            {
                Rest();
                _pilot.Press(UxrHandSide.Right, UxrInputButtons.Grip, true);
                return;
            }

            _pilot.ReachGrabber(UxrHandSide.Right, rightTarget.position, rightTarget.rotation);
            if (useSupport)
            {
                // Левая — к поддержке на стволе в заданной позе; Grip по прибытии, дальше держится.
                _pilot.ReachGrabber(UxrHandSide.Left, leftTarget.position, leftTarget.rotation);
                bool atSupport = grip.ComputeRequiredGrabberTransform(left, support, out Vector3 supportNow, out _, false) &&
                                 (left.transform.position - supportNow).magnitude <= ReachTolerance;
                _leftAtTarget = holdsSupport || atSupport ? _leftAtTarget + 1 : 0;
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, holdsSupport || _leftAtTarget >= ReachStableFrames);
            }
            else
            {
                _pilot.Rest(UxrHandSide.Left);
                _pilot.Press(UxrHandSide.Left, UxrInputButtons.Grip, false);
            }
        }

        /// <summary>
        /// Наведение жёсткого ствола (как человек держит оружие двумя руками): желаемая поза оружия — рукоять у точки
        /// удержания перед головой, дуло на <paramref name="target"/> (крен прежний); цели кистей — позы граберов для
        /// рукояти и поддержки в этой позе оружия (<c>ComputeRequiredGrabberTransform</c>, только чтение SDK). Без
        /// интегратора: ограничения хвата SDK держат ствол жёстко между руками.
        /// </summary>
        private bool AimRigid(UxrGrabbableObject grip, int primary, int support, UxrGrabber right, UxrGrabber left, Transform muzzle,
                              Vector3 target, Vector3 eye, Quaternion yaw, out Pose rightTarget, out Pose leftTarget)
        {
            rightTarget = leftTarget = default;
            Transform weapon = grip.transform;
            // Смещение «ствол ↔ правый грабер» — фактическое, пока ствол в руке: SDK при хвате выравнивает оружие по
            // контроллеру (AlignToController), и оно отличается от снапа рукояти (прогон iter2-4: систематические 31°).
            Vector3 rp = right.transform.position;
            Quaternion rr = right.transform.rotation;
            if (right.GrabbedObject != grip && !grip.ComputeRequiredGrabberTransform(right, primary, out rp, out rr, false)) return false;
            Quaternion inverse = Quaternion.Inverse(weapon.rotation);
            Vector3 rightInWeapon = inverse * (rp - weapon.position);
            Quaternion rightInWeaponRotation = inverse * rr;
            Vector3 muzzleInWeapon = inverse * (muzzle.position - weapon.position);
            Vector3 muzzleForwardInWeapon = inverse * muzzle.forward;
            Vector3 leftInWeapon = Vector3.zero;
            Quaternion leftInWeaponRotation = Quaternion.identity;
            bool hasLeft = false;
            if (support >= 0 && left != null && grip.ComputeRequiredGrabberTransform(left, support, out Vector3 lp, out Quaternion lr, false))
            {
                hasLeft = true;
                leftInWeapon = inverse * (lp - weapon.position);
                leftInWeaponRotation = inverse * lr;
            }

            Vector3 hold = eye + yaw * new Vector3(HoldSide, -HoldDown, HoldForward);
            Quaternion rotation = weapon.rotation;
            Vector3 position = hold - rotation * rightInWeapon;
            for (int i = 0; i < 4; i++)
            {
                position = hold - rotation * rightInWeapon;
                Vector3 muzzlePosition = position + rotation * muzzleInWeapon;
                Vector3 wanted = target - muzzlePosition;
                if (wanted.sqrMagnitude < 1e-6f) break;
                rotation = Quaternion.FromToRotation(rotation * muzzleForwardInWeapon, wanted) * rotation;
            }
            position = hold - rotation * rightInWeapon;

            rightTarget = new Pose(position + rotation * rightInWeapon, rotation * rightInWeaponRotation);
            if (hasLeft) leftTarget = new Pose(position + rotation * leftInWeapon, rotation * leftInWeaponRotation);
            return true;
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
