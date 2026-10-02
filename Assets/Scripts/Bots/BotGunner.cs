using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
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
    /// Бот не аимбот: реагирует с задержкой, стреляет очередями с паузой, каждая пуля —
    /// в случайную точку тела цели, часть уходит мимо края. Стреляет, только пока цель видна
    /// из ствола (луч в центр её тела первым упирается в неё) — сквозь укрытия не палит.
    /// Проверка видимости идёт по центру, а не по точке разброса: иначе разброс, ушедший
    /// за край тела, навсегда запрещал бы выстрел.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody))]
    public sealed class BotGunner : MonoBehaviour
    {
        private const float ReactionSeconds = 0.8f;
        private const float BurstPauseMin = 0.6f;
        private const float BurstPauseMax = 1.4f;
        private const int BurstShots = 3;
        /// <summary>
        /// Разброс в долях габаритов тела цели (общий бокс тела, шлема, рук — шире силуэта).
        /// 0.5 — попадает примерно половина пуль.
        /// </summary>
        private const float AimSpread = 0.5f;
        /// <summary>Дальность стрельбы, метры. Дальше бот подходит (<see cref="BotOrder.Hunt"/>).</summary>
        public const float MaxRange = 40f;
        private const float MuzzleFromChest = 0.55f;
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
        private float _nextBurstAt;
        private int _shotsLeftInBurst;
        private Vector3 _aimOffset;

        /// <summary>Оружие в руке бота; null — нет.</summary>
        public UxrFirearmWeapon Firearm => IsHolding ? _firearm : null;

        /// <summary>Цель; null — нет.</summary>
        public PlayerController Target => _target;

        /// <summary>Цель видна из ствола в этот кадр — директор останавливает бота стрелять.</summary>
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
            if (stage != UxrUpdateStage.Animation) return;

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

            AimAndShoot();
        }

        private bool IsHolding =>
            _weapon != null && _grabbable != null && UxrGrabManager.Instance != null &&
            UxrGrabManager.Instance.IsBeingGrabbedBy(_grabbable, _avatar);

        private void Idle()
        {
            _target = null;
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
            GameObject instance = prefab != null ? NetworkUxrIdentity.CreateInstance(prefab) : null;
            if (instance == null) return;

            instance.transform.SetPositionAndRotation(grabber.transform.position, grabber.transform.rotation);
            instance.SetActive(true);
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

        private void Bind(GameObject weapon, bool spawnedByBot)
        {
            _weapon = weapon;
            _firearm = weapon.GetComponent<UxrFirearmWeapon>();
            _grabbable = weapon.GetComponent<UxrGrabbableObject>();
            _spawnedByBot = spawnedByBot;
        }

        private void DropWeaponRef()
        {
            if (_spawnedByBot && _weapon != null) NetworkServer.Destroy(_weapon);
            _weapon = null;
            _firearm = null;
            _grabbable = null;
        }

        // ── Прицел и стрельба ───────────────────────────────────────────────

        private void AimAndShoot()
        {
            PlayerController target = BotSenses.NearestEnemy(_player.Session, _body.Feet, MaxRange);
            if (target != _target)
            {
                _target = target;
                _shotsLeftInBurst = 0;
                _nextBurstAt = Time.time + ReactionSeconds;
            }

            if (_target == null)
            {
                Idle();
                return;
            }

            Bounds body = BodyOf(_target);
            Vector3 aimPoint = body.center + Vector3.Scale(_aimOffset, body.extents);
            _body.LookAt(body.center);
            PoseRightHand(aimPoint);

            // Сквозь укрытия не стреляем: цель должна быть видна из ствола.
            SeesTarget = Sees(_target, body.center);
            if (!SeesTarget || Time.time < _nextBurstAt) return;

            if (_shotsLeftInBurst <= 0)
            {
                _shotsLeftInBurst = BurstShots;
            }

            // Выстрел шлёт только автор предмета в руке (Issue 23); у бота — сервер.
            if (!StateEventAuthority.IsAuthorOfItem(_firearm)) return;

            if (_firearm.GetAmmoLeft(0) <= 0)
            {
                // Перезарядки у бота нет — магазин просто снова полный.
                _firearm.SetAmmoLeft(0, _firearm.GetAmmoCapacity(0));
            }

            if (_firearm.TryToShootRound(0))
            {
                ShotsFired++;
                _shotsLeftInBurst--;

                // Следующая пуля — в другую точку тела; больше единицы — мимо края.
                _aimOffset = Random.insideUnitSphere * AimSpread;

                if (_shotsLeftInBurst <= 0)
                {
                    _nextBurstAt = Time.time + Random.Range(BurstPauseMin, BurstPauseMax);
                }
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
        /// Грудь — над ногами бота (<see cref="BotBody.Feet"/>), а не над корнем: бот ходит головой.
        /// </summary>
        private void PoseRightHand(Vector3 aimPoint)
        {
            if (!_gripKnown) return;

            Vector3 relPosition = _gripPosition;
            Quaternion relRotation = _gripRotation;

            Quaternion bodyRotation = _body.BodyRotation;
            Vector3 chest = _body.Feet + Vector3.up * 1.4f + bodyRotation * Vector3.right * 0.15f;
            Vector3 flat = aimPoint - chest;
            flat.y = 0f;
            Vector3 muzzleAt = chest + (flat.sqrMagnitude > 0.01f ? flat.normalized : bodyRotation * Vector3.forward) * MuzzleFromChest;

            Vector3 direction = aimPoint - muzzleAt;
            if (direction.sqrMagnitude < 0.01f) return;

            Quaternion muzzleRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Quaternion handRotation = muzzleRotation * Quaternion.Inverse(relRotation);
            Vector3 handPosition = muzzleAt - handRotation * relPosition;

            _body.SetRightHandPose(handPosition, handRotation);
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

        private static Transform MuzzleOf(UxrFirearmWeapon firearm)
        {
            UxrShotDescriptor shot = ShotOf(firearm);
            return shot != null ? shot.ShotSource : null;
        }
    }
}
