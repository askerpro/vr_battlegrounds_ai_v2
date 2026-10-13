using System;
using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Порядок кадра бота в режиме <see cref="BotBodyMode.PlayerFlow"/> (вариант P «кукловод»). Только сервер/хост,
    /// только аватар без владельца. Владельцы: корень/камера/кисти — <see cref="BotAvatarDriver"/>; ноги, курс и взгляд —
    /// <see cref="BotBody"/> (от Blaze-рига/навигатора); оружие, цель и выстрел — <see cref="BotGunner"/>; хват —
    /// <see cref="BotHands"/>; поза оружия — <see cref="BotWeaponAim"/>. Этот компонент только вызывает их по порядку.
    ///
    /// <para>Кадр сервера:</para>
    /// <list type="number">
    /// <item>Update (порядок 11000, после Blaze): ноги/курс (<see cref="BotBody.PrepareFrame"/>), сборка
    /// <see cref="BotInputFrame"/> → <see cref="BotAvatarDriver.Submit"/>.</item>
    /// <item><c>UxrManager.PostUpdate</c> (LateUpdate, порядок 0): драйвер пишет позы перед Manipulation; дальше штатно
    /// Manipulation → Animation → PostProcess (IK).</item>
    /// <item>LateUpdate (порядок 11000, после всех стадий): видимость с каденцией <see cref="SeesIntervalFrames"/>,
    /// ошибка фактического дула (поправка следующего кадра), выстрел по запросу Blaze (гейт — луч дула), затем хват/сброс.
    /// Действия SDK — только отсюда, не из обработчиков событий UXR.</item>
    /// </list>
    ///
    /// <para>
    /// <b>Снаряжение (этап pf-equipment).</b> Кисти ведутся к цели (<see cref="BotHandGoal"/>) видимой траекторией
    /// (<see cref="BotHandTrajectory"/>, 0,25–0,4 с от прошлой команды), а не телепортом: покой без оружия, подвод к
    /// снапу оружия в кобуре/у кисти, подъём на прицел после хвата, поддержка, подвод к кобуре перед покупкой. Хват —
    /// только по прибытии кисти. Уборка: сначала отпускается поддержка, потом кисть у якоря кобуры отпускает оружие
    /// штатным путём человека (<see cref="BotHands"/>, SDK размещает). Погибшее тело не ведётся (ни поз, ни хвата):
    /// отпускание при смерти — сервер (AvatarTeardown, deferred-body). Компенсация отдачи учится только в устоявшемся прицеле.
    /// </para>
    ///
    /// <para>
    /// <b>Пассивный комплект</b> (<see cref="Passive"/>: призрак, цель стенда; без <see cref="BotHands"/> и <see cref="BotWeaponAim"/>): тот же
    /// единственный писатель — драйвер (корень, камера, кисти в позе покоя относительно тела, как Unarmed); ноги, курс и
    /// взгляд — <see cref="BotBody"/> (сам не пишет). Ни хвата, ни прицела, ни выстрелов; Blaze не создаётся (директор).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(11000)]
    [RequireComponent(typeof(BotBody), typeof(BotGunner), typeof(BotAvatarDriver))]
    public sealed class BotRuntimeController : MonoBehaviour
    {
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("BotRuntimeController.Update");
        private static readonly ProfilerMarker LateUpdateMarker = new ProfilerMarker("BotRuntimeController.LateUpdate");
        private static readonly ProfilerMarker ShotMarker = new ProfilerMarker("BotRuntimeController.Shot");
        private static readonly ProfilerMarker ShotSampledMarker = new ProfilerMarker("BotRuntimeController.ShotSampled");
        private static readonly ProfilerMarker EquipmentMarker = new ProfilerMarker("BotRuntimeController.Equipment");
        // Вложенные в Equipment (eq-round1: пики 2,5–2,8 мс): поза покоя (вкл. разовый снимок геометрии префаба),
        // подвод к снапу (снап SDK), уборка (TryStow + CanBePlacedOnAnchor SDK). Решение W* — BotWeaponAim.Solve.
        private static readonly ProfilerMarker RestMarker = new ProfilerMarker("BotRuntimeController.Equipment.Rest");
        private static readonly ProfilerMarker ReachMarker = new ProfilerMarker("BotRuntimeController.Equipment.Reach");
        private static readonly ProfilerMarker StowMarker = new ProfilerMarker("BotRuntimeController.Equipment.Stow");

        /// <summary>Подвод к снапу дольше этого без прибытия — событие таймаута (<see cref="ReachTimeouts"/>), хвата нет.</summary>
        public const float ReachTimeoutSeconds = 1.5f;

        /// <summary>
        /// Правило огня (eq-round2, решение эксперта): у оружия с точкой поддержки (и у пистолета) первый выстрел после хвата
        /// основной рукояти — только после хвата поддержки или через столько секунд (<see cref="SupportTimeouts"/>).
        /// </summary>
        public const float SupportTimeoutSeconds = 1f;

        /// <summary>
        /// Сколько ждать после прибытия кисти к кобуре, пока предмет войдёт в радиус якоря (<c>CanBePlacedOnAnchor</c>),
        /// секунды. Дальше отпускание всё равно идёт штатным путём: SDK положит в ближайший якорь в радиусе или уронит.
        /// </summary>
        public const float StowSettleSeconds = 0.6f;

        /// <summary>Опущенный ствол без цели: точка прицеливания относительно глаз в системе корпуса, метры при росте 1,65.</summary>
        private static readonly Vector3 LowReadyFromEye = new Vector3(0.1f, -1.2f, 3f);

        /// <summary>Каденция raycast-а видимости (<see cref="BotGunner.RefreshSees"/>), кадры. Гейт выстрела — каждый запрос.</summary>
        public int SeesIntervalFrames = BotPlayerFlowSettings.SeesIntervalFrames;

        /// <summary>Замер каждого запроса выстрела (поза кадра после всех стадий). Только чтение — стенд.</summary>
        public event Action<BotShotSample> ShotSampled;

        private BotBody _body;
        private BotGunner _gunner;
        private BotAvatarDriver _driver;
        private BotHands _hands;
        private BotWeaponAim _aim;
        private PlayerController _player;
        private UxrAvatar _avatar;
        private BotInputFrame _frame;
        private int _seesPhase;
        private readonly BotHandTrajectory _rightHand = new BotHandTrajectory();
        private readonly BotHandTrajectory _leftHand = new BotHandTrajectory();
        /// <summary>Геометрия плеч и рук префаба тела — для позы покоя опущенных рук (есть и у призрака).</summary>
        private readonly BotArmRestGeometry _rest = new BotArmRestGeometry();
        private bool _rightArrived, _leftArrived;
        private float _reachTimeoutFor = -1f;
        private bool _heldPrimaryLast, _supportGateOpen = true;
        private float _equipSince;
        private readonly int[] _outcomes = new int[(int)BotShotOutcome.Fired + 1];

        /// <summary>Последний собранный кадр намерений.</summary>
        public BotInputFrame LastFrame => _frame;

        /// <summary>Сколько запросов Blaze закончились исходом <paramref name="outcome"/>.</summary>
        public int OutcomeCount(BotShotOutcome outcome) => _outcomes[(int)outcome];

        /// <summary>Цели кистей и ход подвода — только чтение (стенд).</summary>
        public BotHandGoal RightGoal => _rightHand.Goal;
        public BotHandGoal LeftGoal => _leftHand.Goal;
        public float RightGoalSince => _rightHand.GoalSince;
        public float RightGoalSeconds => _rightHand.GoalSeconds;
        /// <summary>
        /// Кисть у цели. Для подвода к снапу (<see cref="BotHandGoal.ReachPrimary"/>, <see cref="BotHandGoal.Support"/>) — по
        /// факту: заданный граббер на снапе SDK этого кадра (<see cref="BotHands.AtSnap"/>), а не по концу траектории.
        /// </summary>
        public bool RightArrived => _rightArrived;
        public float LeftGoalSince => _leftHand.GoalSince;
        public bool LeftArrived => _leftArrived;

        /// <summary>Заданный граббер правой ↔ снап основной рукояти при подводе, мм (NaN — не подвод).</summary>
        public float RightReachGapMm { get; private set; } = float.NaN;

        /// <summary>Сколько подводов к снапу не дошли за <see cref="ReachTimeoutSeconds"/> (хвата издалека нет).</summary>
        public int ReachTimeouts { get; private set; }

        /// <summary>Огонь после экипировки разрешён (поддержка взята, её нет у оружия, или вышел таймаут).</summary>
        public bool SupportGateOpen => _supportGateOpen;

        /// <summary>Сколько раз огонь открыт по таймауту поддержки (событие supportTimeout стенда).</summary>
        public int SupportTimeouts { get; private set; }

        /// <summary>
        /// Пассивный комплект (<see cref="BotKitKind.Passive"/>: призрак выбывшего, контрольная цель стенда): тело ведётся
        /// без оружия — без хвата, прицела и выстрелов, и мёртвым тоже.
        /// </summary>
        public bool Passive { get; private set; }

        /// <summary>
        /// Ведётся ли тело: сервер, автор мира, драйвер может писать; живое — любой комплект, мёртвое — только пассивный
        /// (погибшее живое тело до смены на призрака не ведётся).
        /// </summary>
        private bool CanControl => NetworkServer.active && StateEventAuthority.IsWorldAuthority && !NetworkServer.isLoadingScene &&
                                   _player != null && _avatar != null && _driver != null && _driver.CanDrive && (_player.IsAlive || Passive);

        /// <summary>
        /// Комплект тела. Зовёт только <see cref="BotKit"/> сразу после добавления компонентов: пассивный — без
        /// <see cref="BotHands"/>/<see cref="BotWeaponAim"/> (драйвер ведёт и мёртвое тело), живой — с ними (при повышении
        /// пассивного <see cref="BotKit.Upgrade"/> добавляет их и переключает комплект).
        /// </summary>
        internal void SetKit(bool passive)
        {
            Passive = passive;
            _hands = GetComponent<BotHands>();
            _aim = GetComponent<BotWeaponAim>();
            if (_driver != null) _driver.DrivesDeadBody = passive;
            if (_aim != null) _aim.ResetCompensation();
        }

        private void Awake()
        {
            _body = GetComponent<BotBody>();
            _gunner = GetComponent<BotGunner>();
            _driver = GetComponent<BotAvatarDriver>();
            _hands = GetComponent<BotHands>();
            _aim = GetComponent<BotWeaponAim>();
            _player = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnEnable()
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
            {
                enabled = false;
                return;
            }
            _gunner.SetPlayerFlow(true);
            // Разнести raycast-ы видимости разных ботов по кадрам.
            _seesPhase = (GetHashCode() & 0x7fffffff) % Mathf.Max(1, SeesIntervalFrames);
        }

        private void OnDisable()
        {
            if (_gunner != null) _gunner.SetPlayerFlow(false);
            if (_aim != null) _aim.ResetCompensation();
        }

        private void Update()
        {
            using (UpdateMarker.Auto())
            {
                if (!CanControl) return;
                _body.PrepareFrame();
                BuildFrame();
                _driver.Submit(_frame);
            }
        }

        private void LateUpdate()
        {
            using (LateUpdateMarker.Auto())
            {
                // Призраку после всех стадий делать нечего: ни видимости, ни выстрела, ни хвата.
                if (!CanControl || Passive || _hands == null || _aim == null) return;

                int interval = Mathf.Max(1, SeesIntervalFrames);
                if (interval == 1 || (Time.frameCount + _seesPhase) % interval == 0) _gunner.RefreshSees();

                // Ошибка фактического дула этого кадра — поправка отдачи следующего. Учится только в устоявшемся прицеле:
                // во время подъёма ствола ошибка — траектория кисти, а не отдача.
                bool engaged = _gunner.HasAimPoint && _gunner.Firearm != null;
                bool settled = _rightHand.Goal == BotHandGoal.Aim && _rightHand.Arrived;
                if (engaged) _aim.ObserveMuzzle(transform, _gunner.Muzzle, _gunner.AimPoint, Time.deltaTime, settled);
                else _aim.ResetCompensation();

                if (_gunner.ShotRequested && !_supportGateOpen && _gunner.Firearm != null)
                {
                    // Первый выстрел после экипировки ждёт поддержку (или таймаут) — запрос снимается без выстрела.
                    _gunner.DeclineRequestedShot();
                    _outcomes[(int)BotShotOutcome.AwaitingSupport]++;
                    var waiting = new BotShotSample { Frame = Time.frameCount, Time = Time.time, Outcome = BotShotOutcome.AwaitingSupport,
                                                      GateAimErrorDeg = float.NaN };
                    using (ShotSampledMarker.Auto())
                    {
                        ShotSampled?.Invoke(waiting);
                    }
                }

                if (_gunner.ShotRequested)
                using (ShotMarker.Auto())
                {
                    var sample = new BotShotSample
                    {
                        Frame = Time.frameCount, Time = Time.time,
                        HasError = engaged && _aim.HasMuzzleError, Error = _aim.LastMuzzleError, Compensation = _aim.Compensation
                    };
                    sample.Outcome = _gunner.ExecuteRequestedShot();
                    sample.GateAimErrorDeg = _gunner.LastGateAimErrorDeg;
                    sample.Blocker = _gunner.LastBlocker;
                    _outcomes[(int)sample.Outcome]++;
                    // Подписчики — только стенд (проба): их стоимость отдельно от стоимости бота.
                    using (ShotSampledMarker.Auto())
                    {
                        ShotSampled?.Invoke(sample);
                    }
                }

                // Хват/сброс — после выстрела: поза кадра для гейта не меняется посреди LateUpdate.
                _hands.Tick(_frame, _gunner.SelectedFirearm);
            }
        }

        private void BuildFrame()
        {
            var frame = new BotInputFrame { Frame = Time.frameCount };
            Vector3 feet = _body.Feet;
            Quaternion body = _body.BodyRotation;
            frame.RootPosition = feet;
            frame.RootRotation = body;

            // Голова — как HMD в пространстве трекинга: локально к пивоту камеры, на высоте глаз калибровки.
            // Пивот пишет только AvatarCalibrationApplier; здесь его поза относительно корня только читается.
            Transform root = transform;
            Transform pivot = _avatar.CameraController;
            Quaternion rootInverse = Quaternion.Inverse(root.rotation);
            Vector3 pivotInRoot = pivot != null ? rootInverse * (pivot.position - root.position) : Vector3.zero;
            Quaternion pivotRotationInRoot = pivot != null ? rootInverse * pivot.rotation : Quaternion.identity;
            float scale = pivot != null ? pivot.lossyScale.y : root.lossyScale.y;
            Quaternion pivotRotation = body * pivotRotationInRoot;

            float eyeHeight = EyeHeight();
            float heightScale = eyeHeight / BotPlayerFlowSettings.EyeHeight;
            Vector3 headLocal = new Vector3(0f, eyeHeight, 0f);
            Vector3 eye = feet + body * pivotInRoot + pivotRotation * (headLocal * scale);
            frame.HeadPosition = headLocal;
            Quaternion head = _body.HeadRotation(eye);
            frame.HeadRotation = Quaternion.Inverse(pivotRotation) * head;

            using (EquipmentMarker.Auto())
            {
                BuildHands(ref frame, body, eye, head, heightScale);
            }

            frame.Trigger = _gunner.ShotRequested;
            _frame = frame;
        }

        /// <summary>
        /// Кисти кадра: цель каждой руки по состоянию снаряжения и шаг видимой траектории к ней; флаги хвата, отпускания
        /// поддержки, уборки и сброса. Геометрия снапов и якоря — у SDK в этом кадре (<see cref="BotWeaponAim"/>).
        /// </summary>
        private void BuildHands(ref BotInputFrame frame, Quaternion body, Vector3 eye, Quaternion head, float heightScale)
        {
            float now = Time.time;
            bool hasRest;
            Pose restLeft, restRight;
            using (RestMarker.Auto())
            {
                // Естественная поза опущенных рук от плеч префаба, а не исходная поза рига (T-поза, eq-round1).
                hasRest = _rest.TryRestHands(_avatar, this, eye, body, heightScale, out restLeft, out restRight);
            }
            if (hasRest)
            {
                // Первая цель нового тела ведётся от покоя, а не ставится сразу.
                _rightHand.Seed(restRight);
                _leftHand.Seed(restLeft);
            }
            BotHandGoal rightGoal = BotHandGoal.Rest, leftGoal = BotHandGoal.Rest;
            Pose rightTarget = restRight, leftTarget = restLeft;
            bool haveRight = hasRest, haveLeft = hasRest;
            bool reach = false, stowing = false, reachOnBody = false;

            // Призрак (или комплект без рук/прицела) — без оружия: обе кисти в позе покоя.
            UxrFirearmWeapon firearm = Passive || _hands == null || _aim == null ? null : _gunner.SelectedFirearm;
            bool resolved = BotHands.TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out int support);
            bool holdsPrimary = resolved && _hands.Holds(grip, primary, UxrHandSide.Right);
            bool holdsSupport = resolved && support >= 0 && _hands.Holds(grip, support, UxrHandSide.Left);
            UxrGrabbableObjectAnchor holster = null;

            // Правило огня: новая экипировка (хват основной) закрывает огонь до хвата поддержки или таймаута.
            if (holdsPrimary && !_heldPrimaryLast)
            {
                _equipSince = now;
                _supportGateOpen = support < 0;
            }
            _heldPrimaryLast = holdsPrimary;
            if (holdsPrimary && !_supportGateOpen)
            {
                if (holdsSupport) _supportGateOpen = true;
                else if (now - _equipSince >= SupportTimeoutSeconds)
                {
                    _supportGateOpen = true;
                    SupportTimeouts++;
                }
            }

            // Снап на собственном теле (кобура) прочитан у SDK по позе прошлого кадра, а корень в этом кадре драйвер ставит
            // в frame.RootPosition/RootRotation: на ходу снап уезжает вместе с телом (eq-round2: 1,3 м/с — 3 см за кадр,
            // хват ждал остановки 3,4 с). Цель переносится вместе с корнем (прошлый корень → корень кадра) — это перенос
            // по нашей же команде корня, не чтение скелета; остаток (покачивание бедра) покрывает допуск хвата BotHands.
            Transform root = _avatar.transform;
            Quaternion rootDelta = frame.RootRotation * Quaternion.Inverse(root.rotation);
            Vector3 rootFrom = root.position, rootTo = frame.RootPosition;

            if (holdsPrimary && _gunner.StowFirearm != null)
            {
                // Пистолет под покупку: поддержка отпускается, кисть с оружием — к якорю своей кобуры.
                stowing = true;
                holster = _gunner.Holster;
                rightGoal = BotHandGoal.Stow;
                bool stowSolved;
                Pose stowHand = default;
                using (StowMarker.Auto())
                {
                    stowSolved = holster != null && _aim.TryStow(_avatar, grip, primary, holster, out stowHand);
                }
                if (stowSolved) rightTarget = FollowRoot(stowHand, rootFrom, rootTo, rootDelta);
                else
                {
                    // Кобуры нет (или SDK не дал снап) — отпустить на месте, как человек разжал бы руку.
                    holster = null;
                    rightTarget = _rightHand.HasLast ? _rightHand.Last : restRight;
                }
                haveRight = true;
                frame.ReleaseSupport = holdsSupport;
            }
            else if (holdsPrimary)
            {
                bool aiming = _gunner.HasAimPoint;
                Vector3 aimPoint = aiming ? _gunner.AimPoint : eye + body * (LowReadyFromEye * heightScale);
                bool rifle = _gunner.WeaponCategory == WeaponCategory.Rifle;
                // Новый подъём на прицел — оценка отдачи с нуля.
                if (_rightHand.Goal != BotHandGoal.Aim) _aim.ResetCompensation();
                rightGoal = BotHandGoal.Aim;
                if (_aim.TrySolve(_avatar, _gunner.Muzzle, grip, primary, support, rifle, body, eye, head, heightScale, aimPoint, aiming,
                                  out Pose right, out Pose left, out bool hasLeft))
                {
                    rightTarget = right;
                    haveRight = true;
                    if (hasLeft)
                    {
                        leftGoal = BotHandGoal.Support;
                        leftTarget = left;
                        haveLeft = true;
                    }
                }
                else
                {
                    rightTarget = _rightHand.HasLast ? _rightHand.Last : restRight;
                    haveRight = _rightHand.HasLast || hasRest;
                }
            }
            else if (resolved && !_gunner.SwapPending && UxrGrabManager.HasInstance && !UxrGrabManager.Instance.IsBeingGrabbed(grip))
            {
                // К снапу основной рукояти (кобура, покупка у кисти; снап читается у SDK каждый кадр — если кисть после
                // выдачи сдвинулась, траектория доводит её); хват — BotHands после всех стадий, по факту прибытия.
                bool reached;
                Pose reachHand = default;
                using (ReachMarker.Auto())
                {
                    reached = _aim.TryReach(_avatar, grip, primary, UxrHandSide.Right, out reachHand);
                }
                if (reached)
                {
                    rightGoal = BotHandGoal.ReachPrimary;
                    // Предмет в якоре своего тела (кобура) едет с корнем; свободный предмет (покупка у кисти) — в мире.
                    reachOnBody = grip.CurrentAnchor != null && grip.CurrentAnchor.transform.IsChildOf(transform);
                    rightTarget = reachOnBody ? FollowRoot(reachHand, rootFrom, rootTo, rootDelta) : reachHand;
                    haveRight = true;
                    reach = true;
                }
            }

            if (haveRight)
            {
                frame.HasRightHand = true;
                if (rightGoal == _rightHand.Goal && (rightGoal == BotHandGoal.ReachPrimary || rightGoal == BotHandGoal.Stow))
                    _rightHand.Retarget(rightTarget, now);
                frame.RightHand = _rightHand.Step(rightGoal, rightTarget, now);
            }

            // Пока правая кисть поднимает оружие, цель левой — снап поддержки на оружии в заданной (промежуточной) позе:
            // обе кисти связаны оружием жёстко, поэтому цель левой сдвигается тем же преобразованием, что правая.
            if (leftGoal == BotHandGoal.Support && haveRight && !_rightHand.Arrived)
            {
                Quaternion delta = frame.RightHand.rotation * Quaternion.Inverse(rightTarget.rotation);
                leftTarget = new Pose(frame.RightHand.position + delta * (leftTarget.position - rightTarget.position), delta * leftTarget.rotation);
            }

            if (haveLeft)
            {
                frame.HasLeftHand = true;
                frame.LeftHand = _leftHand.Step(leftGoal, leftTarget, now);
            }

            // Прибытие к снапу — по факту (заданный граббер ↔ снап SDK ≤ 5 мм/5°), не по концу траектории.
            RightReachGapMm = float.NaN;
            bool rightAtSnap = false;
            if (reach && frame.HasRightHand)
            {
                using (ReachMarker.Auto())
                {
                    // Снап SDK — по позе прошлого кадра: команду для сравнения переносим обратно к прошлому корню.
                    Pose check = reachOnBody ? FollowRoot(frame.RightHand, rootTo, rootFrom, Quaternion.Inverse(rootDelta)) : frame.RightHand;
                    rightAtSnap = BotHands.AtSnap(_avatar, grip, primary, UxrHandSide.Right, check, out float gapMm, out _);
                    RightReachGapMm = gapMm;
                }
                if (!rightAtSnap && now - _rightHand.GoalSince > ReachTimeoutSeconds && _reachTimeoutFor != _rightHand.GoalSince)
                {
                    _reachTimeoutFor = _rightHand.GoalSince;
                    ReachTimeouts++;
                }
            }
            _rightArrived = reach ? rightAtSnap : _rightHand.Arrived;
            // Левая к поддержке: цель — снап поддержки по W* (обновляется каждый кадр), прибытие — конец траектории
            // (0,25–0,4 с от начала подъёма); фактический снап на оружии сверяет BotHands перед хватом (допуск хвата).
            _leftArrived = _leftHand.Arrived;

            frame.GrabPrimary = reach && rightAtSnap;
            frame.GrabSupport = leftGoal == BotHandGoal.Support && !holdsSupport && _leftHand.Arrived && _rightHand.Arrived;
            if (stowing && !holdsSupport && _rightHand.Arrived)
            {
                // Отпустить, когда предмет вошёл в радиус якоря; не вошёл за StowSettleSeconds — всё равно отпустить
                // штатно (SDK положит в ближайший якорь в радиусе или уронит), чтобы покупка не ждала вечно.
                bool settledTooLong = now - _rightHand.GoalSince >= _rightHand.GoalSeconds + StowSettleSeconds;
                using (StowMarker.Auto())
                {
                    frame.Stow = holster == null || settledTooLong || grip.CanBePlacedOnAnchor(holster);
                }
            }

            // Сброс — только чтобы освободить руки под выбранное оружие; без выбора ничего не отпускаем.
            frame.Release = resolved && (HoldsForeign(UxrHandSide.Right, grip) || HoldsForeign(UxrHandSide.Left, grip));
        }

        /// <summary>Поза, прочитанная при корне (<paramref name="from"/>), перенесённая к корню (<paramref name="to"/>, поворот <paramref name="delta"/>).</summary>
        private static Pose FollowRoot(in Pose pose, Vector3 from, Vector3 to, Quaternion delta) =>
            new Pose(to + delta * (pose.position - from), delta * pose.rotation);

        private bool HoldsForeign(UxrHandSide side, UxrGrabbableObject keep)
        {
            UxrGrabber grabber = _avatar.GetGrabber(side);
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            return held != null && held != keep;
        }

        private float EyeHeight()
        {
            var session = _player.Session;
            return session != null && session.Calibration.HasEyeHeight ? session.Calibration.EyeHeight : BotPlayerFlowSettings.EyeHeight;
        }
    }
}
