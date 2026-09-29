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

namespace VrBattlegrounds.DevTools.Bots
{
    /// <summary>
    /// Стрелок бота. Только сервер, вешается рядом с <see cref="BotBody"/>.
    ///
    /// <para>
    /// <b>Оружие настоящее.</b> Бот берёт в правую руку ствол из <see cref="WeaponRegistry"/>
    /// (заспавнен как у стены арсенала, <see cref="NetworkUxrIdentity"/>, магазин вложен
    /// в префаб) штатным <c>UxrGrabManager.GrabObject</c>. Захват синхронизируемый,
    /// а автор аватара без владельца — сервер (<c>StateEventAuthority</c>), поэтому
    /// клиенты видят бота с оружием в руке. Захват же делает бота владельцем оружия
    /// (<c>UxrWeapon.Owner</c>), и убийство засчитывается ему.
    /// </para>
    ///
    /// <para>
    /// <b>Выстрел настоящий.</b> <c>UxrFirearmWeapon.TryToShootRound</c> — весь тракт:
    /// патроны, темп, <c>CanUse</c> (оружие выключено вне фазы боя), снаряд
    /// <c>UxrWeaponManager</c>, урон по коллайдеру, в который попал луч. Как у человека —
    /// с той разницей, что спуск жмёт этот компонент, а не контроллер: у аватара в режиме
    /// <c>UpdateExternally</c> ввода нет.
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
        private const float MaxRange = 40f;
        private const float MuzzleFromChest = 0.55f;

        private BotBody _body;
        private PlayerController _player;
        private UxrAvatar _avatar;

        private GameObject _weapon;
        private UxrFirearmWeapon _firearm;
        private UxrGrabbableObject _grabbable;

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

        /// <summary>Сколько выстрелов сделал бот — для проверок и лога.</summary>
        public int ShotsFired { get; private set; }

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
            // Тело убрали (смена команды, бот удалён). Перед уничтожением AvatarTeardown уже
            // разжал руку, поэтому «держит» здесь всегда false — убираем своё оружие, если его
            // не подобрал кто-то другой. Выпавшее при смерти (_weapon сброшен в Update) не трогаем.
            if (NetworkServer.active && _weapon != null && _grabbable != null &&
                (UxrGrabManager.Instance == null || !UxrGrabManager.Instance.IsBeingGrabbed(_grabbable)))
            {
                NetworkServer.Destroy(_weapon);
            }
        }

        private void Update()
        {
            if (!NetworkServer.active || _player == null || _avatar == null) return;

            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            bool fight = mode != null && !mode.IsWarmup && _player.IsAlive;

            if (!fight)
            {
                Idle();
                return;
            }

            if (!IsHolding)
            {
                // Смерть выбила оружие из руки — оно лежит в мире, его уберёт уборка раунда.
                _weapon = null;
                if (!TryTakeWeapon())
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
            _body.LookAt(null);
            _body.ClearRightHandPose();
        }

        // ── Оружие ──────────────────────────────────────────────────────────

        private bool TryTakeWeapon()
        {
            UxrGrabber grabber = _avatar.GetGrabber(UxrHandSide.Right);
            if (grabber == null || UxrGrabManager.Instance == null) return false;

            GameObject prefab = PickWeaponPrefab();
            if (prefab == null) return false;

            GameObject instance = NetworkUxrIdentity.CreateInstance(prefab);
            if (instance == null) return false;

            instance.transform.SetPositionAndRotation(grabber.transform.position, grabber.transform.rotation);
            instance.SetActive(true);
            NetworkUxrIdentity.SpawnServerObject(instance);

            _weapon = instance;
            _firearm = instance.GetComponent<UxrFirearmWeapon>();
            _grabbable = instance.GetComponent<UxrGrabbableObject>();

            if (_firearm == null || _grabbable == null)
            {
                GameLog.Debug.Warning($"[BotGunner] {name}: у '{prefab.name}' нет UxrFirearmWeapon/UxrGrabbableObject.");
                NetworkServer.Destroy(instance);
                _weapon = null;
                return false;
            }

            UxrGrabManager.Instance.GrabObject(grabber, _grabbable, 0, true);

            if (!IsHolding)
            {
                GameLog.Debug.Warning($"[BotGunner] {name}: захват '{prefab.name}' не состоялся — оружие убрано.");
                NetworkServer.Destroy(instance);
                _weapon = null;
                return false;
            }

            GameLog.Debug.Info($"[BotGunner] {name}: взял '{prefab.name}', патронов {_firearm.GetAmmoLeft(0)}.");
            return true;
        }

        /// <summary>Первая винтовка реестра, иначе первый огнестрел.</summary>
        private static GameObject PickWeaponPrefab()
        {
            WeaponRegistry registry = WeaponRegistry.Instance;
            if (registry == null) return null;

            GameObject fallback = null;
            foreach (WeaponInfo info in registry.Weapons)
            {
                if (info == null || info.WeaponPrefab == null) continue;
                if (info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() == null) continue;

                if (info.Category == WeaponCategory.Rifle) return info.WeaponPrefab;
                if (fallback == null) fallback = info.WeaponPrefab;
            }
            return fallback;
        }

        // ── Прицел и стрельба ───────────────────────────────────────────────

        private void AimAndShoot()
        {
            PlayerController target = FindTarget();
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

            if (Time.time < _nextBurstAt) return;

            if (_shotsLeftInBurst <= 0)
            {
                _shotsLeftInBurst = BurstShots;
            }

            // Сквозь укрытия не стреляем: цель должна быть видна из ствола.
            if (!Sees(_target, body.center)) return;

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

        /// <summary>Ближайший живой противник в пределах дальности.</summary>
        private PlayerController FindTarget()
        {
            PlayersManager players = PlayersManager.Instance;
            PlayerSession own = _player.Session;
            if (players == null || own == null) return null;

            PlayerController best = null;
            float bestDistance = MaxRange * MaxRange;

            foreach (PlayerSession session in players.Sessions)
            {
                if (session == null || session == own || session.TeamIndex == own.TeamIndex) continue;

                PlayerController other = session.ActiveAvatar;
                if (other == null || !other.IsAlive) continue;

                float distance = (other.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// Куда целиться: габариты твёрдых коллайдеров цели — в них и попадает пуля
        /// (<c>QueryTriggerInteraction.Ignore</c>). Не голова-камера: у игрока без шлема
        /// (редактор) камера лежит на полу, а тело стоит в позе префаба.
        /// </summary>
        private static Bounds BodyOf(PlayerController target)
        {
            bool found = false;
            Bounds bounds = new Bounds(target.transform.position + Vector3.up * 1.3f, new Vector3(0.4f, 0.8f, 0.4f));

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
        /// </summary>
        private void PoseRightHand(Vector3 aimPoint)
        {
            if (!_gripKnown) return;

            Vector3 relPosition = _gripPosition;
            Quaternion relRotation = _gripRotation;

            Transform root = transform;
            Vector3 chest = root.position + Vector3.up * 1.4f + root.right * 0.15f;
            Vector3 flat = aimPoint - chest;
            flat.y = 0f;
            Vector3 muzzleAt = chest + (flat.sqrMagnitude > 0.01f ? flat.normalized : root.forward) * MuzzleFromChest;

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
