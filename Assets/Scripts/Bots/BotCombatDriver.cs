using BlazeAISpace;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Серверный адаптер готового Blaze Cover Shooter. В бою Blaze ведёт минимальный прокси у ног (свой GameObject с
    /// NavMeshAgent и generic-аниматором без клипов, <see cref="ProxyControllerResource"/>); <see cref="BotBody"/> берёт
    /// с его корня ноги и курс (<see cref="BotBody.SetCombatLegsSource"/>), <see cref="BotGunner"/> исполняет shootEvent
    /// настоящим оружием. Прокси существует только в Combat. Этот компонент не содержит собственных боевых решений;
    /// типы Blaze есть только здесь.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody), typeof(BotGunner))]
    public sealed class BotCombatDriver : MonoBehaviour
    {
        /// <summary>
        /// Контроллер прокси в Resources: 6 состояний слоя 0 без motion — Idle (по умолчанию), Move, Back, Left, Right,
        /// Shoot (их имена играет Blaze). Генерирует <c>BotBlazeProxySetup</c> (меню редактора), ассет коммитится.
        /// </summary>
        public const string ProxyControllerResource = "Bots/BotBlazeProxy";

        /// <summary>Пауза перед повторной попыткой создать прокси, секунды.</summary>
        private const float RetrySeconds = 2f;

        private static RuntimeAnimatorController _proxyController;
        private static bool _proxyControllerLoaded;

        private BotBody _body;
        private BotGunner _gunner;
        private PlayerController _player;
        private UxrAvatar _avatar;
        private System.Func<Collider, bool> _ignoreOwnCollider;
        private GameObject _proxy;
        private BlazeAI _blaze;
        private float _nextAttempt;
        private bool _warned;
        private GameObject _targetSource;
        private PlayerController _target;

        public bool CombatActive => _blaze != null && _proxy != null && _proxy.activeInHierarchy;

        /// <summary>Цель Blaze; игрок ищется только при смене цели (без GetComponentInParent в каждом обращении).</summary>
        public PlayerController Target
        {
            get
            {
                GameObject enemy = _blaze != null ? _blaze.enemyToAttack : null;
                if (!ReferenceEquals(enemy, _targetSource))
                {
                    _targetSource = enemy;
                    _target = enemy != null ? enemy.GetComponentInParent<PlayerController>() : null;
                }
                return _target;
            }
        }

        public BlazeAI Brain => _blaze;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _proxyController = null;
            _proxyControllerLoaded = false;
        }

        private void Awake()
        {
            _body = GetComponent<BotBody>();
            _gunner = GetComponent<BotGunner>();
            _player = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
            _ignoreOwnCollider = IsOwnCollider;
        }

        /// <summary>Единственная граница Combat: вне неё прокси нет, директор ведёт домой навигатором.</summary>
        public void SetCombatEnabled(bool active)
        {
            if (!active || !NetworkServer.active || !StateEventAuthority.IsWorldAuthority || NetworkServer.isLoadingScene ||
                _player == null || !_player.IsAlive)
            {
                RemoveProxy();
                return;
            }
            if (_proxy == null && Time.time >= _nextAttempt) CreateProxy();
        }

        private void Update()
        {
            if (_proxy == null) return;
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority ||
                NetworkServer.isLoadingScene || !_player.IsAlive || BotSenses.Stage() != BotStage.Combat)
            {
                RemoveProxy();
                return;
            }
            if (_blaze.navmeshAgent == null || !_blaze.navmeshAgent.isOnNavMesh)
            {
                RemoveProxy();
                _nextAttempt = Time.time + RetrySeconds;
            }
        }

        /// <summary>Контроллер прокси из Resources — один раз на процесс; нет ассета — одна ошибка, бой ботов выключен.</summary>
        private static RuntimeAnimatorController ProxyController()
        {
            if (_proxyControllerLoaded) return _proxyController;
            _proxyControllerLoaded = true;
            _proxyController = Resources.Load<RuntimeAnimatorController>(ProxyControllerResource);
            if (_proxyController == null)
                GameLog.Player.Error($"[BotCombat] Нет контроллера прокси Blaze Resources/{ProxyControllerResource}.controller — " +
                                     "сгенерируйте его меню Tools/VR Battlegrounds/Bots/Build Blaze Proxy Controller; бой ботов выключен.");
            return _proxyController;
        }

        /// <summary>
        /// Минимальный прокси: неактивный GameObject (позиция, курс, масштаб тела, слой IgnoreRaycast, DontSave) →
        /// BlazeAI (Animator/NavMeshAgent/CapsuleCollider/AudioSource добавляет RequireComponent) → контроллер без клипов,
        /// без root motion, AlwaysAnimate → агент, капсула, поведения → активация. Humanoid и кости не нужны.
        /// </summary>
        private void CreateProxy()
        {
            _nextAttempt = Time.time + RetrySeconds;
            RuntimeAnimatorController controller = ProxyController();
            if (controller == null) return;
            if (!BotNavMesh.TryGetPoint(_body.Feet, out Vector3 start))
            {
                if (!_warned)
                {
                    GameLog.Player.Warning($"[BotCombat] {name}: нет NavMesh под ногами; бой остановлен.", this);
                    _warned = true;
                }
                return;
            }

            _proxy = new GameObject(name + "_BlazeCombat");
            _proxy.SetActive(false);
            _proxy.hideFlags = HideFlags.DontSave;
            _proxy.layer = 2;
            _proxy.transform.SetPositionAndRotation(start, _body.BodyRotation);
            _proxy.transform.localScale = transform.lossyScale;

            _blaze = _proxy.AddComponent<BlazeAI>();
            var animator = _proxy.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var agent = _proxy.GetComponent<NavMeshAgent>();
            agent.radius = 0.3f;
            agent.height = 1.8f;
            agent.baseOffset = 0f;
            agent.avoidancePriority = 30 + Mathf.Abs(GetInstanceID() % 40);
            var capsule = _proxy.GetComponent<CapsuleCollider>();
            capsule.center = Vector3.up * 0.9f;
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
            capsule.isTrigger = true;

            var alert = _proxy.AddComponent<AlertStateBehaviour>();
            alert.idleAnim = new[] { "Idle" };
            alert.moveAnim = "Move";
            alert.moveSpeed = BotNavigator.WalkSpeed;
            alert.idleTime = new Vector2(0.5f, 1.5f);
            alert.onStateEnter = new UnityEvent();
            alert.onStateExit = new UnityEvent();

            var shooter = _proxy.AddComponent<CoverShooterBehaviour>();
            shooter.idleAnim = "Idle";
            shooter.moveAnim = "Move";
            shooter.moveSpeed = BotNavigator.WalkSpeed;
            shooter.shootingAnim = "Shoot";
            shooter.shootEvery = new Vector2(0.8f, 1.6f);
            shooter.singleShotDuration = 0.16f;
            shooter.totalShootTime = new Vector2(0.5f, 1f);
            shooter.distanceFromEnemy = 14f;
            shooter.attackDistance = 18f;
            shooter.attackEnemyCover = CoverShooterBehaviour.AttackEnemyCover.AlwaysAttackEnemy;
            shooter.braveMeter = 3;
            shooter.changeCoverFrequency = 7;
            shooter.moveForwards = true;
            shooter.moveForwardsToDistance = 7f;
            shooter.moveForwardsSpeed = BotNavigator.WalkSpeed;
            shooter.moveForwardsAnim = "Move";
            shooter.moveBackwards = true;
            shooter.moveBackwardsDistance = 2.5f;
            shooter.moveBackwardsSpeed = BotNavigator.WalkSpeed;
            shooter.moveBackwardsAnim = "Back";
            shooter.strafe = true;
            shooter.strafeSpeed = BotNavigator.WalkSpeed;
            shooter.strafeTime = new Vector2(0.5f, 1.5f);
            shooter.strafeWaitTime = new Vector2(0.8f, 1.8f);
            shooter.leftStrafeAnim = "Left";
            shooter.rightStrafeAnim = "Right";
            shooter.strafeLayersToAvoid = Physics.AllLayers;
            shooter.layersCheckOnAttacking = LayerMask.GetMask("Default", "Ground");
            shooter.returnPatrolAnim = "Idle";
            shooter.returnPatrolTime = 2f;
            shooter.onStateEnter = new UnityEvent();
            shooter.onStateExit = new UnityEvent();
            shooter.onEquipEvent = new UnityEvent();
            shooter.onUnequipEvent = new UnityEvent();
            shooter.shootEvent = new UnityEvent();
            shooter.shootEvent.AddListener(_gunner.RequestShot);

            var cover = _proxy.AddComponent<GoingToCoverBehaviour>();
            cover.coverLayers = LayerMask.GetMask("Default", "Ground");
            cover.searchDistance = 25f;
            // Низкий присед требует ещё не принятого контракта native legs.
            cover.minCoverHeight = 1.7f;
            cover.highCoverHeight = 1.7f;
            cover.highCoverAnim = "Idle";
            cover.lowCoverAnim = "Idle";
            cover.rotateToCoverNormal = false;
            cover.onStateEnter = new UnityEvent();
            cover.onStateExit = new UnityEvent();

            _blaze.coverShooterMode = true;
            _blaze.coverShooterBehaviour = shooter;
            _blaze.goingToCoverBehaviour = cover;
            _blaze.alertStateBehaviour = alert;
            _blaze.useAlertStateOnAwake = true;
            _blaze.useNormalStateOnAwake = false;
            _blaze.groundLayers = LayerMask.GetMask("Default", "Ground");
            _blaze.useLocalAvoidance = true;
            _blaze.layersToAvoid = 1 << 2;
            _blaze.avoidanceRadius = 0.9f;
            _blaze.avoidanceOffsetStrength = 0.5f;
            _blaze.checkEnemyContact = false;
            _blaze.canDistract = false;
            _blaze.warnEmptyBehavioursOnStart = false;
            _blaze.warnEmptyAnimations = true;
            _blaze.TargetFilter = IsEnemy;
            _blaze.CoverFilter = IsProtectiveCover;
            _blaze.RequireCompletePaths = true;
            _blaze.ColliderIgnoreFilter = _ignoreOwnCollider;
            _blaze.waypoints = new Waypoints { randomize = true, randomizeRadius = 15f };
            _blaze.fallBackPoints = System.Array.Empty<Vector3>();
            _blaze.vision = new Vision
            {
                visionPosition = Vector3.up * 1.55f,
                maxSightLevel = 2f,
                useMinLevel = false,
                layersToDetect = LayerMask.GetMask("Default", "Ground"),
                hostileAndAlertLayers = LayerMask.GetMask("Player"),
                hostileTags = new[] { GameTags.Player },
                alertTags = System.Array.Empty<Vision.AlertTags>(),
                multiRayVision = false,
                visionFrameSkipping = 2,
                visionDuringNormalState = new Vision.normalVision(120f, BotGunner.MaxRange),
                visionDuringAlertState = new Vision.alertVision(120f, BotGunner.MaxRange),
                visionDuringAttackState = new Vision.attackVision(360f, BotGunner.MaxRange),
                enemyEnterEvent = new UnityEvent(),
                enemyLeaveEvent = new UnityEvent()
            };
            _proxy.SetActive(true);
            _body.SetCombatLegsSource(_proxy.transform);
        }

        private bool IsEnemy(GameObject candidate)
        {
            PlayerController other = candidate.GetComponentInParent<PlayerController>();
            PlayerSession own = _player != null ? _player.Session : null;
            PlayerSession target = other != null ? other.Session : null;
            return own != null && target != null && target != own && other.IsAlive &&
                   target.Role == GameRole.Player && target.TeamIndex != 0 && target.TeamIndex != own.TeamIndex;
        }

        private static bool IsProtectiveCover(Collider collider)
        {
            if (collider == null || collider.isTrigger || collider.attachedRigidbody != null ||
                collider.GetComponentInParent<UxrAvatar>() != null ||
                collider.GetComponentInParent<UxrGrabbableObject>() != null) return false;
            CoverSurface surface = CoverSurface.Of(collider);
            return surface == null || surface.Class == CoverClass.Hard;
        }

        /// <summary>Свой коллайдер (тело этого бота) Blaze не считает ни укрытием, ни препятствием обзора.</summary>
        private bool IsOwnCollider(Collider collider) => collider.GetComponentInParent<UxrAvatar>() == _avatar;

        private void RemoveProxy()
        {
            if (_body != null) _body.SetCombatLegsSource(null);
            if (_proxy != null)
            {
                _proxy.SetActive(false); // Close освобождает занятость укрытия до Destroy.
                Destroy(_proxy);
            }
            _proxy = null;
            _blaze = null;
            _targetSource = null;
            _target = null;
        }

        private void OnDisable() => RemoveProxy();
        private void OnDestroy() => RemoveProxy();
    }
}
