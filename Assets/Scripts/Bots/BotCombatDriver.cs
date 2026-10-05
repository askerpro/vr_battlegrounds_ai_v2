using BlazeAISpace;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Серверный адаптер готового Blaze Cover Shooter. Пакет ведёт самостоятельный риг у ног,
    /// BotBody переносит его позу в VR-аватар, BotGunner исполняет shootEvent настоящим оружием.
    /// Этот компонент не содержит собственных боевых решений.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBody), typeof(BotGunner))]
    public sealed class BotCombatDriver : MonoBehaviour
    {
        private BotBody _body;
        private BotGunner _gunner;
        private PlayerController _player;
        private GameObject _proxy;
        private BlazeAI _blaze;
        private Animator _animator;
        private BotCombatAssets _assets;
        private float _nextAttempt;
        private bool _warned;

        public bool CombatActive => _blaze != null && _proxy != null && _proxy.activeInHierarchy;
        public PlayerController Target => _blaze != null && _blaze.enemyToAttack != null
            ? _blaze.enemyToAttack.GetComponentInParent<PlayerController>() : null;
        public BlazeAI Brain => _blaze;
        /// <summary>Фактический источник оружейной позы для наблюдателя стенда, включая idle.</summary>
        public Animator PoseAnimator => _animator;

        private void Awake()
        {
            _body = GetComponent<BotBody>();
            _gunner = GetComponent<BotGunner>();
            _player = GetComponent<PlayerController>();
        }

        /// <summary>Единственная граница Combat: вне неё директор ведёт домой прежним навигатором.</summary>
        public void SetCombatEnabled(bool active)
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority || NetworkServer.isLoadingScene ||
                _player == null || !_player.IsAlive)
            {
                RemoveProxy();
                return;
            }
            // Оружейный idle нужен и при возвращении/закупке. Вне Combat риг только играет позу,
            // его корень следует BotBody; навигации и боевых компонентов на нём нет.
            if (!active && _gunner.Firearm == null)
            {
                RemoveProxy();
                return;
            }
            if (_proxy != null && active != CombatActive)
            {
                RemoveProxy();
                _nextAttempt = 0f;
            }
            if (_proxy == null && Time.time >= _nextAttempt) CreateProxy(active);
        }

        private void Update()
        {
            if (_proxy == null) return;
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority ||
                NetworkServer.isLoadingScene || !_player.IsAlive)
            {
                RemoveProxy();
                return;
            }
            if (CombatActive && BotSenses.Stage() != BotStage.Combat)
            {
                SetCombatEnabled(false);
                return;
            }
            if (CombatActive && (_blaze.navmeshAgent == null || !_blaze.navmeshAgent.isOnNavMesh))
            {
                RemoveProxy();
                _nextAttempt = Time.time + 2f;
                return;
            }
            var controller = _gunner.WeaponCategory == WeaponCategory.Rifle ? _assets.rifle : _assets.pistol;
            if (_animator.runtimeAnimatorController != controller)
            {
                _animator.runtimeAnimatorController = controller;
                if (_blaze != null) _blaze.animManager.ResetLastState();
            }
            if (!CombatActive) _proxy.transform.SetPositionAndRotation(_body.Feet, _body.BodyRotation);
        }

        private void CreateProxy(bool combat)
        {
            _nextAttempt = Time.time + 2f;
            _assets = Resources.Load<BotCombatAssets>(BotCombatAssets.ResourcePath);
            var avatarController = GetComponent<UxrStandardAvatarController>();
            GameObject rig = avatarController != null ? avatarController.Legs.locomotionRig : null;
            Vector3 start = _body.Feet;
            if (_assets == null || _assets.pistol == null || _assets.rifle == null || rig == null ||
                combat && !BotNavMesh.TryGetPoint(_body.Feet, out start))
            {
                if (!_warned)
                {
                    GameLog.Player.Warning($"[BotCombat] {name}: нужны BotCombatAssets, humanoid rig и NavMesh; бой остановлен.", this);
                    _warned = true;
                }
                return;
            }

            _proxy = Instantiate(rig, start, _body.BodyRotation);
            _proxy.name = name + "_BlazeCombat";
            _proxy.SetActive(false);
            _proxy.hideFlags = HideFlags.DontSave;
            foreach (Transform child in _proxy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            _proxy.transform.localScale = transform.lossyScale;
            _animator = _proxy.GetComponent<Animator>();
            if (_animator == null || _animator.avatar == null || !_animator.avatar.isHuman)
            {
                GameLog.Player.Error($"[BotCombat] {name}: копия рига не humanoid.", this);
                RemoveProxy();
                return;
            }
            _animator.runtimeAnimatorController = _gunner.WeaponCategory == WeaponCategory.Rifle ? _assets.rifle : _assets.pistol;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (!combat)
            {
                _proxy.SetActive(true);
                _body.SetCombatPoseSource(_animator, false);
                return;
            }

            _blaze = _proxy.AddComponent<BlazeAI>();
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
            _blaze.ColliderIgnoreFilter = collider => collider.GetComponentInParent<UxrAvatar>() == GetComponent<UxrAvatar>();
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
            _body.SetCombatPoseSource(_animator, true);
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

        private void RemoveProxy()
        {
            if (_body != null) _body.SetCombatPoseSource(null);
            if (_proxy != null)
            {
                _proxy.SetActive(false); // Close освобождает занятость укрытия до Destroy.
                Destroy(_proxy);
            }
            _proxy = null;
            _blaze = null;
            _animator = null;
        }

        private void OnDisable() => RemoveProxy();
        private void OnDestroy() => RemoveProxy();
    }
}
