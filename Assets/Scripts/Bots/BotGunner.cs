using System.Collections.Generic;
using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Стрелок бота. Только сервер, вешается рядом с <see cref="BotBody"/>.
    ///
    /// <para>
    /// <b>Оружие настоящее.</b> Что взять, решает директор (<see cref="Arm"/>, T-48): купленный ствол
    /// (<see cref="BotShopper"/>) заспавнен как у стены арсенала (<see cref="NetworkUxrIdentity"/>, магазин вложен в
    /// префаб), без покупки — стартовый пистолет из своей кобуры (его выдаёт <c>StartingSidearmPolicy</c>, как всем).
    /// В руку — штатным <c>UxrGrabManager.GrabObject</c>. Захват синхронизируемый, а автор аватара без владельца —
    /// сервер (<c>StateEventAuthority</c>), поэтому клиенты видят бота с оружием в руке. Захват же делает бота
    /// владельцем оружия (<c>UxrWeapon.Owner</c>), и убийство засчитывается ему.
    /// </para>
    ///
    /// <para>
    /// <b>Выстрел настоящий и один (Issue 23).</b> <c>UxrFirearmWeapon.TryToShootRound</c> — весь тракт:
    /// патроны, темп, <c>CanUse</c> (оружие выключено вне фазы боя), снаряд <c>UxrWeaponManager</c>, урон по
    /// коллайдеру, в который попал луч. Спуск жмёт этот компонент, а не контроллер: у аватара в режиме
    /// <c>UpdateExternally</c> ввода нет. Жмёт только автор предмета в руке
    /// (<see cref="StateEventAuthority.IsAuthorOfItem"/>; у бота — сервер): событие выстрела рассылает автор,
    /// пересчитай его другая машина — второй выстрел и двойной урон.
    /// </para>
    ///
    /// <para>
    /// Моменты атаки и паузы выбирает Blaze Cover Shooter. Каждая пуля направлена
    /// в случайную точку тела цели, часть уходит мимо края. Стреляет, только пока цель видна
    /// из ствола (луч в центр её тела первым упирается в неё) — сквозь укрытия не палит.
    /// Проверка видимости идёт по центру, а не по точке разброса: иначе разброс, ушедший
    /// за край тела, навсегда запрещал бы выстрел.
    /// </para>
    ///
    /// <para>
    /// <b>Режим P</b> (<see cref="BotBodyMode.PlayerFlow"/>, <see cref="SetPlayerFlow"/>): стрелок остаётся владельцем
    /// выбора оружия (покупка/кобура), цели и точки прицеливания и исполнителем выстрела, но не хватает сам
    /// (<see cref="BotHands"/>), не ставит кисти (<see cref="BotWeaponAim"/> → <see cref="BotAvatarDriver"/>) и не стреляет
    /// из обработчика стадии UXR: выстрел — <see cref="ExecuteRequestedShot"/> из LateUpdate <see cref="BotRuntimeController"/>.
    /// Гейт: фактический луч дула смотрит в точку прицеливания ИИ (допуск <see cref="BotPlayerFlowSettings.MaxShotAimErrorDegrees"/>)
    /// и путь пули до неё не перекрыт ничем, кроме цели; промах по силуэту при неточности ИИ — нормальный выстрел.
    /// Выстрел — только после true от <c>RequestAutomationPreparation</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody))]
    public sealed class BotGunner : MonoBehaviour
    {
        /// <summary>
        /// Разброс в долях габаритов тела цели (общий бокс тела, шлема, рук — шире силуэта).
        /// 0.5 — попадает примерно половина пуль.
        /// </summary>
        private const float AimSpread = 0.5f;
        /// <summary>Дальность стрельбы и восприятия Blaze, метры.</summary>
        public const float MaxRange = 40f;
        /// <summary>Не удалось взять оружие — следующая попытка не раньше, чем через столько секунд.</summary>
        private const float ArmRetrySeconds = 2f;

        private BotBody _body;
        private PlayerController _player;
        private UxrAvatar _avatar;

        private GameObject _weapon;
        private UxrFirearmWeapon _firearm;
        private UxrGrabbableObject _grabbable;
        /// <summary>Оружие заспавнил сам бот (покупка) — убирается вместе с живым телом.</summary>
        private bool _spawnedByBot;

        private bool _armRequested;
        private WeaponInfo _armWith;
        private float _nextArmAttempt;

        /// <summary>Ствол относительно кисти — снимается после стадии захвата, см. <see cref="OnStageUpdated"/>.</summary>
        private bool _gripKnown;
        private Vector3 _gripPosition;
        private Quaternion _gripRotation;

        private PlayerController _target;
        private bool _shotRequested;
        private Vector3 _aimOffset;

        private static readonly ProfilerMarker SeesMarker = new ProfilerMarker("BotGunner.Sees");
        private static readonly ProfilerMarker GateMarker = new ProfilerMarker("BotGunner.ShotGate");
        private static readonly ProfilerMarker AutomationMarker = new ProfilerMarker("BotGunner.Automation");
        private static readonly ProfilerMarker ShootRoundMarker = new ProfilerMarker("BotGunner.TryToShootRound");
        private bool _playerFlow;
        private bool _hasAimPoint;
        private Vector3 _aimPoint;
        private Vector3 _aimCenter;
        private PlayerController _cachedTarget;
        private readonly List<Collider> _targetColliders = new List<Collider>();
        private UxrShotDescriptor _boundShot;
        private float _lastGateAimError = float.NaN;
        private Collider _lastBlocker;
        private UxrGrabbableObject _triggerGrip;
        private BotCombatDriver _combat;

        /// <summary>Режим P: хват/кисти/выстрел ведут компоненты <see cref="BotBodyMode.PlayerFlow"/>.</summary>
        public bool PlayerFlow => _playerFlow;

        /// <summary>Включает режим P. Зовёт только <see cref="BotRuntimeController"/>.</summary>
        internal void SetPlayerFlow(bool enabled)
        {
            _playerFlow = enabled;
            _shotRequested = false;
            _hasAimPoint = false;
        }

        /// <summary>Выбранное оружие (купленное/из кобуры), даже если ещё не в руке; null — нет.</summary>
        public UxrFirearmWeapon SelectedFirearm => _weapon != null ? _firearm : null;

        /// <summary>Режим P: есть цель и точка прицеливания ИИ (центр тела + разброс).</summary>
        public bool HasAimPoint => _hasAimPoint;
        public Vector3 AimPoint => _aimPoint;

        /// <summary>Режим P: центр тела цели этого кадра (куда смотрит голова и идёт луч видимости).</summary>
        public Vector3 AimCenter => _aimCenter;

        /// <summary>Blaze попросил выстрел в этом кадре (ещё не исполнен).</summary>
        public bool ShotRequested => _shotRequested;

        /// <summary>
        /// Только стенд (эталонный замер): точка прицеливания ИИ — ровно центр тела цели, без разброса ИИ. По умолчанию false.
        /// </summary>
        public bool ZeroAimSpread { get; set; }

        /// <summary>Угол луча дула к точке прицеливания в последнем гейте, градусы (NaN — гейт до угла не дошёл).</summary>
        public float LastGateAimErrorDeg => _lastGateAimError;

        /// <summary>Первый коллайдер на пути пули в последнем гейте с исходом Occluded; иначе null.</summary>
        public Collider LastBlocker => _lastBlocker;

        /// <summary>Дуло выбранного оружия (снято при выборе, без поиска компонентов в кадре); null — нет.</summary>
        public Transform Muzzle => _weapon != null && _boundShot != null ? _boundShot.ShotSource : null;

        /// <summary>Оружие в руке бота; null — нет.</summary>
        public UxrFirearmWeapon Firearm => IsHolding ? _firearm : null;

        public WeaponCategory? WeaponCategory => Firearm != null &&
            Firearm.TryGetComponent(out WeaponComponent weapon) && weapon.WeaponData != null
            ? weapon.WeaponData.Category : (WeaponCategory?)null;

        /// <summary>Blaze выбирает момент атаки; настоящий выстрел исполняет только автор оружия.</summary>
        public void RequestShot()
        {
            if (NetworkServer.active && StateEventAuthority.IsWorldAuthority) _shotRequested = true;
        }

        /// <summary>Цель; null — нет.</summary>
        public PlayerController Target => _target;

        /// <summary>Цель видна из фактического ствола после стадии Manipulation.</summary>
        public bool SeesTarget { get; private set; }

        /// <summary>Директор уже велел взять оружие этому телу.</summary>
        public bool ArmRequested => _armRequested;

        /// <summary>Сколько выстрелов сделал бот — для проверок и лога.</summary>
        public int ShotsFired { get; private set; }

        /// <summary>
        /// Взять оружие: купленное (<paramref name="purchase"/>) или, если null, стартовый пистолет из кобуры.
        /// Один раз на тело — повторный вызов ничего не меняет.
        /// </summary>
        public void Arm(WeaponInfo purchase)
        {
            if (_armRequested) return;
            _armRequested = true;
            _armWith = purchase;
        }

        /// <summary>Отбой: оружие больше не брать (матча нет, раунд кончился). Что в руке — остаётся.</summary>
        public void StandDown()
        {
            _armRequested = false;
            _armWith = null;
        }

        private void Awake()
        {
            _body = GetComponent<BotBody>();
            _player = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnEnable()
        {
            UxrManager.StageUpdated += OnStageUpdated;
        }

        private void OnDisable()
        {
            UxrManager.StageUpdated -= OnStageUpdated;
        }

        /// <summary>
        /// Хват «кисть → ствол» снимается в конце стадии <see cref="UxrUpdateStage.Animation"/>:
        /// захват (<see cref="UxrUpdateStage.Manipulation"/>) уже подтянул оружие к кисти,
        /// и трансформы согласованы. Снятый посреди кадра (кисть сдвинута, оружие ещё на старом
        /// месте) он был бы ошибочным, а прицел по нему — обратной связью, уносящей ствол в бесконечность.
        /// </summary>
        private void OnStageUpdated(UxrUpdateStage stage)
        {
            // В режиме P выстрел — из LateUpdate верхнего уровня (BotRuntimeController), не из обработчика стадии.
            if (stage != UxrUpdateStage.Animation || _playerFlow) return;

            Transform hand = _body != null ? _body.RightHand : null;
            Transform muzzle = MuzzleOf(_firearm);
            if (hand == null || muzzle == null || !IsHolding)
            {
                _gripKnown = false;
                return;
            }

            Vector3 offset = muzzle.position - hand.position;
            if (offset.sqrMagnitude > 1.5f * 1.5f) return; // оружие ещё не у кисти

            _gripPosition = Quaternion.Inverse(hand.rotation) * offset;
            _gripRotation = Quaternion.Inverse(hand.rotation) * muzzle.rotation;
            _gripKnown = true;
            FireRequestedShot();
        }

        private void OnDestroy()
        {
            // Тело убрали. Перед уничтожением AvatarTeardown уже разжал руку, поэтому «держит»
            // здесь всегда false. Погибший — тело сменил призрак в кадре смерти, Update не успел
            // сбросить _weapon — выпавший ствол остаётся в мире (ShouldRemoveWeaponWithBody).
            // Пистолет из кобуры не свой — им распоряжается снаряжение (изъятие, выпадение).
            bool heldByOther = UxrGrabManager.Instance != null && UxrGrabManager.Instance.IsBeingGrabbed(_grabbable);
            if (NetworkServer.active && _spawnedByBot && _weapon != null && _grabbable != null &&
                ShouldRemoveWeaponWithBody(_player != null && _player.IsAlive, heldByOther))
            {
                NetworkServer.Destroy(_weapon);
            }
        }

        /// <summary>
        /// Убирать ли своё оружие вместе с телом. Тело погибшего уничтожается сразу — его сменяет
        /// призрак (T-35), — и выпавший ствол должен остаться в мире; своё оружие бот убирает, только
        /// если тело убрали живым (бот удалён, сменил команду) и ствол никто не держит.
        /// </summary>
        public static bool ShouldRemoveWeaponWithBody(bool bodyAlive, bool heldByOther) => bodyAlive && !heldByOther;

        private void Update()
        {
            // Бот — аватар без владельца: его автор — сервер. Только он берёт оружие и жмёт спуск.
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || _player == null || _avatar == null) return;

            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            bool fight = mode != null && !mode.IsWarmup && _player.IsAlive;

            if (!fight)
            {
                Idle();
                return;
            }

            if (!IsHolding)
            {
                if (!_armRequested || Time.time < _nextArmAttempt || !TryTakeWeapon())
                {
                    Idle();
                    return;
                }
            }

            if (!mode.WeaponsEnabled)
            {
                // Закупка и отсчёт: оружие в руке, стрелять рано.
                Idle();
                return;
            }

            AimAtTarget();
        }

        private bool IsHolding =>
            _weapon != null && _grabbable != null && UxrGrabManager.Instance != null &&
            (_playerFlow ? HeldByOwnHand() : UxrGrabManager.Instance.IsBeingGrabbedBy(_grabbable, _avatar));

        /// <summary>Режим P: держит ли предмет своя рука — по грабберам аватара, без LINQ SDK в кадре.</summary>
        private bool HeldByOwnHand()
        {
            UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
            return Holds(right) || Holds(left);

            bool Holds(UxrGrabber grabber) => grabber != null && grabber.GrabbedObject != null &&
                (grabber.GrabbedObject == _grabbable || grabber.GrabbedObject == _triggerGrip);
        }

        private void Idle()
        {
            _shotRequested = false;
            _target = null;
            _hasAimPoint = false;
            SeesTarget = false;
            _body.LookAt(null);
            _body.ClearRightHandPose();
        }

        // ── Оружие ──────────────────────────────────────────────────────────

        private bool TryTakeWeapon()
        {
            _nextArmAttempt = Time.time + ArmRetrySeconds;

            UxrGrabber grabber = _avatar.GetGrabber(UxrHandSide.Right);
            if (grabber == null || UxrGrabManager.Instance == null) return false;

            // Своё оружие выпало (не держит никто) — подобрать его же, а не спавнить ещё одно.
            if (_weapon == null || UxrGrabManager.Instance.IsBeingGrabbed(_grabbable))
            {
                _weapon = null;
                if (_armWith != null) SpawnPurchase(grabber);
                else TakeHolstered();
            }

            if (_weapon == null) return false;

            if (_firearm == null || _grabbable == null)
            {
                GameLog.Player.Warning($"[BotGunner] {name}: у '{_weapon.name}' нет UxrFirearmWeapon/UxrGrabbableObject.");
                DropWeaponRef();
                return false;
            }

            // Режим P: стрелок только выбрал оружие; кисть к снапу подводит драйвер, хватает BotHands через SDK.
            if (_playerFlow) return false;

            UxrGrabManager.Instance.GrabObject(grabber, _grabbable, 0, true);

            if (!IsHolding)
            {
                GameLog.Player.Warning($"[BotGunner] {name}: захват '{_weapon.name}' не состоялся.");
                return false;
            }

            GameLog.Player.Info($"[BotGunner] {name}: взял '{_weapon.name}', патронов {_firearm.GetAmmoLeft(0)}.");
            return true;
        }

        /// <summary>Купленный ствол — спавн у кисти, как у стены арсенала.</summary>
        private void SpawnPurchase(UxrGrabber grabber)
        {
            GameObject prefab = _armWith.WeaponPrefab;
            // Покупка — выдача предмета карты: до server Ready и после Closing её нет, как у стены.
            if (!MapRunAdmission.CanActivateActiveMap) return;
            GameObject instance = prefab != null ? MapRunAdmission.CreateActiveMapItem(prefab) : null;
            if (instance == null) return;

            instance.transform.SetPositionAndRotation(grabber.transform.position, grabber.transform.rotation);
            instance.SetActive(true);
            // Режим P: снап основной рукояти — сразу в граббере (после активации: снапы инициализирует SDK), чтобы
            // штатная проверка расстояния (CanBeGrabbedByGrabber) пропустила хват в этом же кадре, до шага физики.
            if (_playerFlow) AlignPrimaryToGrabber(instance, grabber);
            NetworkUxrIdentity.SpawnServerObject(instance);

            Bind(instance, spawnedByBot: true);
            _armWith = null; // ствол один на закупку: потерял — дальше пистолетом из кобуры
        }

        /// <summary>Стартовый пистолет из своей кобуры (выдаёт <c>StartingSidearmPolicy</c>).</summary>
        private void TakeHolstered()
        {
            foreach (UxrGrabbableObjectAnchor anchor in _avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>())
            {
                UxrGrabbableObject placed = anchor != null ? anchor.CurrentPlacedObject : null;
                if (placed == null || placed.GetComponent<UxrFirearmWeapon>() == null) continue;

                Bind(placed.gameObject, spawnedByBot: false);
                return;
            }
        }

        /// <summary>Ставит предмет так, чтобы снап основной рукояти совпал с граббером (геометрия — из SDK).</summary>
        private static void AlignPrimaryToGrabber(GameObject instance, UxrGrabber grabber)
        {
            var firearm = instance.GetComponent<UxrFirearmWeapon>();
            if (!BotHands.TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out _)) return;
            if (!grip.ComputeRequiredGrabberTransform(grabber, primary, out Vector3 snapPosition, out Quaternion snapRotation, false)) return;

            Transform root = instance.transform;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            Vector3 snapLocal = inverse * (snapPosition - root.position);
            Quaternion snapLocalRotation = inverse * snapRotation;
            Quaternion rotation = grabber.transform.rotation * Quaternion.Inverse(snapLocalRotation);
            root.SetPositionAndRotation(grabber.transform.position - rotation * snapLocal, rotation);
        }

        private void Bind(GameObject weapon, bool spawnedByBot)
        {
            _weapon = weapon;
            _firearm = weapon.GetComponent<UxrFirearmWeapon>();
            _grabbable = weapon.GetComponent<UxrGrabbableObject>();
            _spawnedByBot = spawnedByBot;
            _boundShot = ShotOf(_firearm);
            _triggerGrip = _firearm != null && _firearm.TryGetTriggerGrip(0, out UxrGrabbableObject grip, out _) ? grip : null;
        }

        private void DropWeaponRef()
        {
            if (_spawnedByBot && _weapon != null) NetworkServer.Destroy(_weapon);
            _weapon = null;
            _firearm = null;
            _grabbable = null;
            _boundShot = null;
            _triggerGrip = null;
        }

        // ── Прицел и стрельба ───────────────────────────────────────────────

        private void AimAtTarget()
        {
            // BotCombatDriver добавляется после стрелка — ссылка берётся лениво, один раз.
            if (_combat == null) _combat = GetComponent<BotCombatDriver>();
            _target = _combat != null && _combat.CombatActive ? _combat.Target : null;

            if (_target == null)
            {
                Idle();
                return;
            }

            if (_playerFlow)
            {
                // Режим P: без GetComponentsInChildren в кадре — коллайдеры цели снимаются при смене цели.
                Bounds bounds = CachedBodyOf(_target);
                _aimCenter = bounds.center;
                _aimPoint = ZeroAimSpread ? bounds.center : bounds.center + Vector3.Scale(_aimOffset, bounds.extents);
                _hasAimPoint = true;
                _body.LookAt(bounds.center);
                return;
            }

            Bounds body = BodyOf(_target);
            Vector3 aimPoint = body.center + Vector3.Scale(_aimOffset, body.extents);
            _body.LookAt(body.center);
            PoseRightHand(aimPoint);
        }

        /// <summary>Габариты твёрдых коллайдеров цели по снятому при смене цели списку (режим P, без аллокаций в кадре).</summary>
        private Bounds CachedBodyOf(PlayerController target)
        {
            if (_cachedTarget != target)
            {
                _cachedTarget = target;
                target.GetComponentsInChildren(false, _targetColliders);
            }

            bool found = false;
            Bounds bounds = default;
            for (int i = 0; i < _targetColliders.Count; i++)
            {
                Collider collider = _targetColliders[i];
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                if (found) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                found = true;
            }
            return found ? bounds : new Bounds(BotSenses.FeetOf(target) + Vector3.up * 1.3f, new Vector3(0.4f, 0.8f, 0.4f));
        }

        // ── Режим P: видимость и выстрел из LateUpdate верхнего уровня ─────

        /// <summary>
        /// Видимость цели для директора (режим P): луч из дула в центр тела первым упирается в цель. Каденцию задаёт
        /// <see cref="BotRuntimeController.SeesIntervalFrames"/>; гейт выстрела от неё не зависит.
        /// </summary>
        public void RefreshSees()
        {
            using (SeesMarker.Auto())
            {
                SeesTarget = _playerFlow && CanFightNow() && _boundShot != null && _boundShot.ShotSource != null &&
                             IsTargetHit(_boundShot.ShotSource.position, _aimCenter - _boundShot.ShotSource.position);
            }
        }

        /// <summary>
        /// Исполнить запрос Blaze (режим P). LateUpdate верхнего уровня, после всех стадий кадра. Гейт:
        /// (а) фактический луч дула отклонён от точки прицеливания ИИ не больше
        /// <see cref="BotPlayerFlowSettings.MaxShotAimErrorDegrees"/> — иначе <see cref="BotShotOutcome.NotAimed"/>;
        /// (б) луч дула маской выстрела до дистанции точки прицеливания не упирается ни во что, кроме коллайдеров цели
        /// (мир, укрытие, собственное тело/оружие — <see cref="BotShotOutcome.Occluded"/>). Промах по силуэту допустим.
        /// Выстрел только у автора предмета, вне StateSync, после true от <c>RequestAutomationPreparation</c>; один
        /// <c>TryToShootRound(0)</c> на запрос.
        /// </summary>
        public BotShotOutcome ExecuteRequestedShot()
        {
            _lastGateAimError = float.NaN;
            _lastBlocker = null;
            if (!_shotRequested) return BotShotOutcome.None;
            _shotRequested = false;
            if (!_playerFlow || !CanFightNow()) return BotShotOutcome.Blocked;
            Transform muzzle = Muzzle;
            if (muzzle == null) return BotShotOutcome.Blocked;

            Vector3 toAim = _aimPoint - muzzle.position;
            float distance = toAim.magnitude;
            if (distance < 1e-3f) return BotShotOutcome.Blocked;
            _lastGateAimError = Vector3.Angle(muzzle.forward, toAim);
            if (_lastGateAimError > BotPlayerFlowSettings.MaxShotAimErrorDegrees) return BotShotOutcome.NotAimed;

            bool blocked;
            using (GateMarker.Auto())
            {
                blocked = IsPathBlocked(muzzle.position, muzzle.forward, distance, out _lastBlocker);
            }
            if (blocked) return BotShotOutcome.Occluded;

            // Выстрел шлёт только автор предмета в руке (Issue 23); у бота — сервер. Не изнутри replay.
            if (!StateEventAuthority.IsAuthorOfItem(_firearm)) return BotShotOutcome.NotAuthor;
            if (UxrManager.HasInstance && UxrManager.Instance.IsInsideStateSync) return BotShotOutcome.Blocked;

            // Профиль готовности — у хоста оружия на префабе (WeaponSystem._profile), как у человека; WeaponData его не
            // дублирует (у AK105/PPK там пусто). Без настроенного хоста бот не стреляет и патроны сеттером не доливает.
            if (!_firearm.TryGetComponent(out VrBattlegrounds.Weapons.WeaponSystem system) || !system.IsConfigured ||
                system.Profile == null || !system.Profile.TryValidate(out _)) return BotShotOutcome.NotReady;
            if (system.Profile.AmmoCapability == VrBattlegrounds.Weapons.WeaponAmmoCapability.LegacyAmmo)
            {
                // Legacy-учёт — штатный магазин SDK, как у человека: стрелять, только если ствол готов.
                if (_firearm.UsesReadinessLedger(0) || !_firearm.IsReadyToFire(0)) return BotShotOutcome.NotReady;
            }
            else
            {
                // Грант automation ключуется (Kind, User, Epoch): запрос — перед каждым выстрелом, после перехвата — заново.
                if (!_firearm.UsesReadinessLedger(0)) return BotShotOutcome.NotReady;
                bool prepared;
                using (AutomationMarker.Auto())
                {
                    prepared = system.RequestAutomationPreparation();
                }
                if (!prepared) return BotShotOutcome.Preparing;
            }

            bool fired;
            using (ShootRoundMarker.Auto())
            {
                // Весь тракт SDK и weapon-system, включая подписчиков ProjectileShot (в стенде — наблюдатели).
                fired = _firearm.TryToShootRound(0);
            }
            if (!fired) return BotShotOutcome.Rejected;
            ShotsFired++;
            PickAimOffset();
            return BotShotOutcome.Fired;
        }

        /// <summary>
        /// Путь пули перекрыт: первое попадание луча маской выстрела (без триггеров) до <paramref name="distance"/> — не
        /// коллайдер цели. Ничего не задето — путь свободен (промах мимо силуэта допустим).
        /// </summary>
        private bool IsPathBlocked(Vector3 origin, Vector3 direction, float distance, out Collider blocker)
        {
            blocker = null;
            if (_boundShot == null) return true;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance, _boundShot.CollisionLayerMask,
                                 QueryTriggerInteraction.Ignore)) return false;
            if (_cachedTarget == _target)
            {
                for (int i = 0; i < _targetColliders.Count; i++)
                {
                    if (_targetColliders[i] == hit.collider) return false;
                }
            }
            blocker = hit.collider;
            return true;
        }

        /// <summary>Первое попадание луча маской выстрела — коллайдер цели (список снят при смене цели).</summary>
        private bool IsTargetHit(Vector3 origin, Vector3 direction)
        {
            if (_boundShot == null || direction.sqrMagnitude < 1e-8f) return false;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, MaxRange, _boundShot.CollisionLayerMask,
                                 QueryTriggerInteraction.Ignore)) return false;
            if (_cachedTarget != _target) return false;
            for (int i = 0; i < _targetColliders.Count; i++)
            {
                if (_targetColliders[i] == hit.collider) return true;
            }
            return false;
        }

        private void PickAimOffset()
        {
            // Следующая пуля — в другую точку тела; больше единицы — мимо края. Эталон стенда — без разброса.
            _aimOffset = ZeroAimSpread ? Vector3.zero : Random.insideUnitSphere * AimSpread;
        }

        private bool CanFightNow()
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            return NetworkServer.active && StateEventAuthority.IsWorldAuthority && _player != null && _player.IsAlive &&
                   IsHolding && mode != null && mode.WeaponsEnabled && _target != null && _target.IsAlive &&
                   BotSenses.Stage() == BotStage.Combat;
        }

        /// <summary>После Manipulation ствол уже следует кисти: LOS и выстрел используют одну позу.</summary>
        private void FireRequestedShot()
        {
            bool shoot = _shotRequested;
            _shotRequested = false;
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || _player == null || !_player.IsAlive ||
                !IsHolding || mode == null || !mode.WeaponsEnabled || _target == null || !_target.IsAlive ||
                BotSenses.Stage() != BotStage.Combat)
            {
                SeesTarget = false;
                return;
            }
            SeesTarget = Sees(_target, BodyOf(_target).center);
            if (!SeesTarget || !shoot) return;

            // Выстрел шлёт только автор предмета в руке (Issue 23); у бота — сервер.
            if (!StateEventAuthority.IsAuthorOfItem(_firearm)) return;

            // Capability берётся у фактически выбранного оружия, а не из заявки на закупку.
            // Missing opt-in не разрешает молча вернуться к старому ammo setter.
            if (!_firearm.TryGetComponent(out WeaponComponent selected) || selected.WeaponData == null ||
                selected.WeaponData.ReadinessProfile == null ||
                !selected.WeaponData.ReadinessProfile.TryValidate(out _)) return;
            var profile = selected.WeaponData.ReadinessProfile;
            bool ledger = _firearm.UsesReadinessLedger(0);
            if (profile.AmmoCapability == VrBattlegrounds.Weapons.WeaponAmmoCapability.LegacyAmmo)
            {
                if (ledger) return;
                // Явная legacy capability сохраняет штатный SDK magazine writer.
                if (_firearm.GetAmmoLeft(0) <= 0)
                    _firearm.SetAmmoLeft(0, _firearm.GetAmmoCapacity(0));
            }
            else
            {
                if (!ledger || !_firearm.TryGetComponent(out VrBattlegrounds.Weapons.WeaponReadinessController readiness) ||
                    readiness.Profile != profile) return;
                if (!_firearm.IsReadyToFire(0) && !readiness.RequestAutomationPreparation()) return;
            }

            if (_firearm.TryToShootRound(0))
            {
                ShotsFired++;

                // Следующая пуля — в другую точку тела; больше единицы — мимо края.
                _aimOffset = Random.insideUnitSphere * AimSpread;

            }
        }

        /// <summary>
        /// Куда целиться: габариты твёрдых коллайдеров цели — в них и попадает пуля
        /// (<c>QueryTriggerInteraction.Ignore</c>). Не голова-камера: у игрока без шлема
        /// (редактор) камера лежит на полу, а тело стоит в позе префаба.
        /// </summary>
        private static Bounds BodyOf(PlayerController target)
        {
            bool found = false;
            Bounds bounds = new Bounds(BotSenses.FeetOf(target) + Vector3.up * 1.3f, new Vector3(0.4f, 0.8f, 0.4f));

            foreach (Collider collider in target.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || !collider.enabled) continue;

                if (found) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                found = true;
            }

            return bounds;
        }

        /// <summary>
        /// Ставит правую кисть так, чтобы ствол (<c>ShotSource</c>) смотрел в точку.
        /// Хват «кисть → ствол» постоянен: оружие жёстко следует за грабером.
        /// База обеих кистей — текущий humanoid-клип; прицел корректирует их вместе.
        /// </summary>
        private void PoseRightHand(Vector3 aimPoint)
        {
            if (!_gripKnown || !_body.TryGetWeaponPose(out Pose left, out Pose right)) return;

            Vector3 relPosition = _gripPosition;
            Quaternion relRotation = _gripRotation;

            Vector3 muzzleAt = right.position + right.rotation * relPosition;

            Vector3 direction = aimPoint - muzzleAt;
            if (direction.sqrMagnitude < 0.01f) return;

            Quaternion muzzleRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Quaternion handRotation = muzzleRotation * Quaternion.Inverse(relRotation);
            Vector3 handPosition = muzzleAt - handRotation * relPosition;

            Quaternion correction = handRotation * Quaternion.Inverse(right.rotation);
            Pose aimedLeft = new Pose(handPosition + correction * (left.position - right.position), correction * left.rotation);
            _body.SetWeaponHands(aimedLeft, new Pose(handPosition, handRotation));
        }

        /// <summary>Луч из ствола в центр тела цели маской выстрела первым упирается в цель.</summary>
        private bool Sees(PlayerController target, Vector3 center)
        {
            UxrShotDescriptor shot = ShotOf(_firearm);
            if (shot == null || shot.ShotSource == null) return false;

            Vector3 origin = shot.ShotSource.position;
            if (!Physics.Raycast(origin, center - origin, out RaycastHit hit, MaxRange,
                                 shot.CollisionLayerMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            UxrActor actor = hit.collider.GetComponentInParent<UxrActor>();
            return actor != null && actor == target._actor;
        }

        private static UxrShotDescriptor ShotOf(UxrFirearmWeapon firearm)
        {
            UxrProjectileSource source = firearm != null ? firearm.GetComponent<UxrProjectileSource>() : null;
            return source != null && source.ShotTypes.Count > 0 ? source.ShotTypes[0] : null;
        }

        /// <summary>Источник выстрела (дуло) первого типа выстрела; null — нет оружия или источника.</summary>
        public static Transform MuzzleOf(UxrFirearmWeapon firearm)
        {
            UxrShotDescriptor shot = ShotOf(firearm);
            return shot != null ? shot.ShotSource : null;
        }
    }
}
