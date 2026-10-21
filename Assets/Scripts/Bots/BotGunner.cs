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
    /// Стрелок бота. Только сервер, часть комплекта бота (<see cref="BotKit"/>).
    ///
    /// <para>
    /// <b>Оружие настоящее.</b> Что взять, решает директор (<see cref="Arm"/>, T-48): купленный ствол
    /// (<see cref="BotShopper"/>) заспавнен как у стены арсенала (<see cref="NetworkUxrIdentity"/>, магазин вложен в
    /// префаб), без покупки — стартовый пистолет из своей кобуры (его выдаёт <c>StartingSidearmPolicy</c>, как всем).
    /// Стрелок только <b>выбирает</b> оружие: кисть к рукояти подводит и Grip жмёт ввод бота (<see cref="BlazeInputSource"/>),
    /// хватает штатный контроллер SDK, как у человека. Захват синхронизируемый, а автор аватара без владельца — сервер
    /// (<c>StateEventAuthority</c>), поэтому клиенты видят бота с оружием в руке. Захват же делает бота владельцем
    /// оружия (<c>UxrWeapon.Owner</c>), и убийство засчитывается ему.
    /// </para>
    ///
    /// <para>
    /// <b>Выстрел — курком, как у человека (bot-input).</b> Стрелок только решает «жать или нет»
    /// (<see cref="DecideTriggerPress"/>), курок жмёт ввод бота, выстрел делает штатный <c>UxrFirearmWeapon</c> автора
    /// предмета (у бота — сервер, UltimateXR patch 89): патроны, темп, <c>CanUse</c>, учёт готовности.
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
    /// Стрелок — владелец выбора оружия (покупка/кобура), цели и точки прицеливания; кисти, хват и курок — ввод бота.
    /// Решение «жать»: фактический луч дула смотрит в точку прицеливания ИИ (допуск
    /// <see cref="BotPlayerFlowSettings.MaxShotAimErrorDegrees"/>) и путь пули до неё не перекрыт ничем, кроме цели;
    /// промах по силуэту при неточности ИИ — нормальный выстрел. Покупка после выбора стартового пистолета
    /// (<see cref="OfferPurchase"/>): пистолет отпускается (<see cref="StowFirearm"/>), затем покупка появляется у кисти.
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
        /// <summary>Кобура, из которой взят стартовый пистолет (туда же он убирается перед покупкой).</summary>
        private UxrGrabbableObjectAnchor _holster;

        private PlayerController _target;
        private bool _shotRequested;
        private Vector3 _aimOffset;

        private static readonly ProfilerMarker SeesMarker = new ProfilerMarker("BotGunner.Sees");
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


        /// <summary>Выбранное оружие (купленное/из кобуры), даже если ещё не в руке; null — нет.</summary>
        public UxrFirearmWeapon SelectedFirearm => _weapon != null ? _firearm : null;

        /// <summary>У бота есть оружие (выбрано: в руке, в кобуре или у кисти после покупки). Нет — Unarmed.</summary>
        public bool HasWeapon => _weapon != null;

        /// <summary>
        /// Пришла покупка, а выбран стартовый пистолет — сначала он убирается (кобура или, без неё, отпускание),
        /// потом покупка появляется у кисти. Пока так — стрелок не целится и не стреляет.
        /// </summary>
        public bool SwapPending => _armWith != null && _weapon != null && !_spawnedByBot;

        /// <summary>Оружие в руке, которое надо убрать перед покупкой (<see cref="SwapPending"/>); null — нечего убирать.</summary>
        public UxrFirearmWeapon StowFirearm => SwapPending && IsHolding ? _firearm : null;

        /// <summary>Кобура выбранного стартового пистолета (якорь, откуда он взят); null — оружие не из кобуры.</summary>
        public UxrGrabbableObjectAnchor Holster => _weapon != null && !_spawnedByBot ? _holster : null;

        /// <summary>Есть цель и точка прицеливания ИИ (центр тела + разброс).</summary>
        public bool HasAimPoint => _hasAimPoint;
        public Vector3 AimPoint => _aimPoint;

        /// <summary>Центр тела цели этого кадра (куда смотрит голова и идёт луч видимости).</summary>
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
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority) return;
            if (!DirectorAllowsShot()) { _shotRequested = false; return; }
            _shotRequested = true;
        }

        private bool DirectorAllowsShot() => BotDirector.Instance == null || BotDirector.Instance.IsShotAllowed(_player);

        /// <summary>Цель; null — нет.</summary>
        public PlayerController Target => _target;

        /// <summary>Цель видна из фактического ствола (<see cref="RefreshSees"/>, после всех стадий кадра).</summary>
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

        /// <summary>
        /// Покупка (этап pf-equipment), оплаченная штатным серверным путём экономики (<see cref="BotShopper"/>),
        /// пришла после выбора оружия. Оружие ещё не выбрано — как <see cref="Arm"/>; выбран стартовый пистолет — он
        /// убирается (<see cref="SwapPending"/>), покупка появляется у кисти. Уже есть купленный ствол — отказ: одна
        /// покупка на тело. false — покупка не принята (оружие не меняется).
        /// </summary>
        public bool OfferPurchase(WeaponInfo purchase)
        {
            if (purchase == null) return false;
            if (!_armRequested)
            {
                Arm(purchase);
                return true;
            }
            if (_armWith != null || (_weapon != null && _spawnedByBot)) return false;
            _armWith = purchase;
            _nextArmAttempt = 0f; // после уборки пистолета покупка — сразу, без паузы повторной попытки
            return true;
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
                // Оружие только выбирается; в руку его берёт ввод бота (Grip у рукояти). Пока не в руке — без прицела.
                if (_armRequested && Time.time >= _nextArmAttempt) SelectWeapon();
                Idle();
                return;
            }

            if (!mode.WeaponsEnabled || SwapPending)
            {
                // Закупка и отсчёт: оружие в руке, стрелять рано. Пистолет убирается под покупку — не целиться.
                Idle();
                return;
            }

            AimAtTarget();
        }

        private bool IsHolding =>
            _weapon != null && _grabbable != null && UxrGrabManager.Instance != null && HeldByOwnHand();

        /// <summary>Держит ли предмет своя рука — по грабберам аватара, без LINQ SDK в кадре.</summary>
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
        }

        // ── Оружие ──────────────────────────────────────────────────────────

        /// <summary>
        /// Выбрать оружие (не в руке): своё выпавшее — его же, иначе покупку у кисти или стартовый пистолет из кобуры.
        /// Стрелок только выбирает; кисть к рукояти подводит и хватает ввод бота (<see cref="BlazeInputSource"/>).
        /// </summary>
        private void SelectWeapon()
        {
            _nextArmAttempt = Time.time + ArmRetrySeconds;

            UxrGrabber grabber = _avatar.GetGrabber(UxrHandSide.Right);
            if (grabber == null || UxrGrabManager.Instance == null) return;

            // Своё оружие выпало (не держит никто) — подобрать его же, а не спавнить ещё одно. Стартовый пистолет
            // убран под покупку (SwapPending, не в руке) — теперь покупка.
            if (_weapon == null || UxrGrabManager.Instance.IsBeingGrabbed(_grabbable) || SwapPending)
            {
                _weapon = null;
                if (_armWith != null) SpawnPurchase(grabber);
                else TakeHolstered();
            }

            if (_weapon != null && (_firearm == null || _grabbable == null))
            {
                GameLog.Player.Warning($"[BotGunner] {name}: у '{_weapon.name}' нет UxrFirearmWeapon/UxrGrabbableObject.");
                DropWeaponRef();
            }
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
            // Снап основной рукояти — сразу в граббере (после активации: снапы инициализирует SDK), чтобы штатная
            // проверка расстояния (CanBeGrabbedByGrabber) пропустила хват в этом же кадре, до шага физики.
            AlignPrimaryToGrabber(instance, grabber);
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
                _holster = anchor;
                return;
            }
        }

        /// <summary>Ставит предмет так, чтобы снап основной рукояти совпал с граббером (геометрия — из SDK).</summary>
        private static void AlignPrimaryToGrabber(GameObject instance, UxrGrabber grabber)
        {
            var firearm = instance.GetComponent<UxrFirearmWeapon>();
            if (!TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out _)) return;
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

            // Без GetComponentsInChildren в кадре — коллайдеры цели снимаются при смене цели.
            Bounds bounds = CachedBodyOf(_target);
            _aimCenter = bounds.center;
            _aimPoint = ZeroAimSpread ? bounds.center : bounds.center + Vector3.Scale(_aimOffset, bounds.extents);
            _hasAimPoint = true;
            _body.LookAt(bounds.center);
        }

        /// <summary>
        /// Куда целиться: габариты твёрдых коллайдеров цели — в них и попадает пуля (<c>QueryTriggerInteraction.Ignore</c>),
        /// по снятому при смене цели списку, без аллокаций в кадре. Не голова-камера: у игрока без шлема (редактор)
        /// камера лежит на полу, а тело стоит в позе префаба.
        /// </summary>
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

        // ── Видимость и выстрел из LateUpdate верхнего уровня ──────────────

        /// <summary>
        /// Видимость цели для директора: луч из дула в центр тела первым упирается в цель. Каденцию задаёт
        /// <see cref="BlazeInputSource"/>; решение «жать» от неё не зависит.
        /// </summary>
        public void RefreshSees()
        {
            using (SeesMarker.Auto())
            {
                SeesTarget = CanFightNow() && _boundShot != null && _boundShot.ShotSource != null &&
                             IsTargetHit(_boundShot.ShotSource.position, _aimCenter - _boundShot.ShotSource.position);
            }
        }

        /// <summary>
        /// Решение мозга «нажать курок сейчас» по запросу Blaze (bot-input): директор, бой, ствол в руке, наведение ≤
        /// допуска, путь пули свободен. Действий SDK нет: выстрел делает штатный <c>UxrFirearmWeapon</c> по курку руки.
        /// Запрос гасится при любом исходе; <see cref="BotShotOutcome.Fired"/> — жать.
        /// </summary>
        public BotShotOutcome DecideTriggerPress()
        {
            _lastGateAimError = float.NaN;
            _lastBlocker = null;
            if (!_shotRequested) return BotShotOutcome.None;
            _shotRequested = false;
            if (!DirectorAllowsShot()) return BotShotOutcome.None;
            if (!CanFightNow()) return BotShotOutcome.Blocked;
            Transform muzzle = Muzzle;
            if (muzzle == null) return BotShotOutcome.Blocked;

            Vector3 toAim = _aimPoint - muzzle.position;
            float distance = toAim.magnitude;
            if (distance < 1e-3f) return BotShotOutcome.Blocked;
            _lastGateAimError = Vector3.Angle(muzzle.forward, toAim);
            if (_lastGateAimError > BotPlayerFlowSettings.MaxShotAimErrorDegrees) return BotShotOutcome.NotAimed;
            if (IsPathBlocked(muzzle.position, muzzle.forward, distance, out _lastBlocker)) return BotShotOutcome.Occluded;

            PickAimOffset();
            ShotsFired++;
            return BotShotOutcome.Fired;
        }

        /// <summary>
        /// Рукояти огнестрела: основная — рукоять спуска 0, поддержка — первая другая точка хвата, доступная левой руке
        /// (−1 — нет). false — не огнестрел или нет рукояти спуска.
        /// </summary>
        public static bool TryResolveGrips(UxrFirearmWeapon firearm, out UxrGrabbableObject grip, out int primary, out int support)
        {
            grip = null;
            primary = 0;
            support = -1;
            if (firearm == null || !firearm.TryGetTriggerGrip(0, out grip, out primary) || grip == null) return false;

            int count = grip.GrabPointCount;
            for (int i = 0; i < count; i++)
            {
                if (i == primary) continue;
                UxrGrabPointInfo info = grip.GetGrabPoint(i);
                if (info == null || (!info.BothHandsCompatible && info.HandSide != UxrHandSide.Left)) continue;
                support = i;
                break;
            }
            return true;
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

        private static UxrShotDescriptor ShotOf(UxrFirearmWeapon firearm)
        {
            UxrProjectileSource source = firearm != null ? firearm.GetComponent<UxrProjectileSource>() : null;
            return source != null && source.ShotTypes.Count > 0 ? source.ShotTypes[0] : null;
        }

    }
}
