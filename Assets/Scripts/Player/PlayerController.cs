using Mirror;
using System;
using VrBattlegrounds.Managers;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Контроллер игрока. Управляет командой и состоянием (жив/мёртв).
    /// Живёт на том же GameObject, что и UxrMirrorAvatar и UxrActor.
    /// Использует физическое перемещение по арене (телепортация не используется).
    ///
    /// Команда хранится как <see cref="TeamData.teamIndex"/> (int) — синхронизируется через SyncVar.
    /// Объект <see cref="TeamData"/> получается из <see cref="TeamRegistry"/> по индексу.
    /// </summary>
    [RequireComponent(typeof(UxrActor))]
    public class PlayerController : NetworkBehaviour
    {
        /// <summary>Событие смерти игрока.</summary>
        public event Action<PlayerController> PlayerDied;

        // ── Сетевые данные ────────────────────────────────────────────────────



        /// <summary>
        /// Сетевой ID сессии, к которой привязан этот аватар
        /// </summary>
        [SyncVar] public uint SessionNetId;

        /// <summary>
        /// Имя игрока, полученное из сессии для синхронизации имени объекта
        /// </summary>
        [SyncVar(hook = nameof(OnAvatarPlayerNameChanged))]
        public string AvatarPlayerName;

        private void OnAvatarPlayerNameChanged(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(newName)) return;
            string postfix = netIdentity.isOwned ? " (Local)" : " (Remote)";
            string debugName = $"{newName}{postfix}";

            gameObject.name = debugName;

            var uxrAvatar = GetComponent<UltimateXR.Networking.Integrations.Net.Mirror.UxrMirrorAvatar>();
            if (uxrAvatar != null && uxrAvatar.AvatarName != debugName)
            {
                uxrAvatar.AvatarName = debugName;
            }
        }

        /// <summary>
        /// Ссылка на PlayerSession, если он уже заспавнен на клиенте/сервере
        /// </summary>
        public PlayerSession Session
        {
            get
            {
                if (SessionNetId == 0) return null;
                // На сервере можем искать в spawned
                if (NetworkServer.active && NetworkServer.spawned.TryGetValue(SessionNetId, out NetworkIdentity sIdentity))
                    return sIdentity.GetComponent<PlayerSession>();
                // На клиенте - аналогично
                if (NetworkClient.active && NetworkClient.spawned.TryGetValue(SessionNetId, out NetworkIdentity cIdentity))
                    return cIdentity.GetComponent<PlayerSession>();

                return null;
            }
        }

        // ── Публичный API ─────────────────────────────────────────────────────

        /// <summary>Данные команды игрока. Null если команда не назначена или сессия отсутствует.</summary>
        public TeamData Team => Session != null ? Session.Team : null;

        public float Health => _actor != null ? _actor.Life : 0f;

        /// <summary>Числовой индекс команды.</summary>
        public int TeamIndex => Session != null ? Session.TeamIndex : 0;

        public bool IsAlive => !_actor.IsDead;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        public UxrActor _actor;

        private void Awake()
        {
            _actor = GetComponent<UxrActor>();
            _actor.DamageReceived += OnDamageReceived;
            _actor.Died += OnActorDied;
        }

        private void Start()
        {
            // Убеждаемся что синхронизированное имя применяется после всех OnStartClient/OnStartServer и UXR инициализации
            if (!string.IsNullOrEmpty(AvatarPlayerName))
            {
                OnAvatarPlayerNameChanged("", AvatarPlayerName);
            }
        }

        private void OnDestroy()
        {
            if (_actor != null)
            {
                _actor.DamageReceived -= OnDamageReceived;
                _actor.Died -= OnActorDied;
            }
        }

        public override void OnStartClient()
        {
        }

        private void OnActorDied(UxrActor actor)
        {
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: UxrActor сообщил о смерти.", this);

            Die();
        }

        // ── SyncVar hooks (вызываются на всех клиентах при изменении) ─────────



        private void OnIsAliveChanged(bool oldValue, bool newValue)
        {
            GameLog.Verbose(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: сетевое состояние жизни изменено {oldValue} -> {newValue}", this);
        }


        // ── Игровая логика ────────────────────────────────────────────────────

        private void OnDamageReceived(object sender, UxrDamageEventArgs e)
        {
            GameLog.Verbose(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: получен урон {e.Damage:F1} (тип: {e.DamageType}). Текущее здоровье: {_actor.Life:F1}", this);

            if (!isServer) return;
        }

        /// <summary>Убивает игрока на сервере и уведомляет клиентов.</summary>
        [Server]
        public void Die()
        {
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: смерть подтверждена на сервере. Переход в режим наблюдателя.", this);

            // Trigger spectator mode on server for synchronization
            var spectator = GetComponent<SpectatorController>();
            if (spectator != null)
            {
                spectator.StartSpectating();
            }

            RpcOnDied();
            PlayerDied?.Invoke(this);

            // Уведомляем активный режим о гибели игрока.
            if (GameplayManager.Instance != null)
                GameplayManager.Instance.OnPlayerDied(this);
        }

        /// <summary>Возрождает игрока на заданной точке спавна.</summary>
        [Server]
        public void Respawn(Transform spawnPoint)
        {
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: респаун на точке {spawnPoint.name} ({spawnPoint.position})\nStack Trace:\n{new System.Diagnostics.StackTrace()}", this);
            _actor.Life = 100f;

            var spectator = GetComponent<SpectatorController>();
            if (spectator != null)
            {
                spectator.EndSpectating();
            }

            RpcOnRespawned(spawnPoint.position, spawnPoint.rotation);
        }

        [ClientRpc]
        private void RpcOnDied()
        {
            // PlayerGrabManager already subscribes to PlayerDied event
            // which is invoked in Die() on server. But events are not networked.
            // We need to make sure items are dropped on all clients or handled by server.
            // UltimateXR usually handles it locally, but we can force it here.
            var grabManager = GetComponent<PlayerGrabManager>();
            if (grabManager != null)
            {
                grabManager.ReleaseAllGrabbedObjects();
            }
        }

        [Server]
        public void RestoreHealth(float health)
        {
            if (_actor != null)
            {
                _actor.Life = health;
            }
        }

        [ClientRpc]
        private void RpcOnRespawned(Vector3 position, Quaternion rotation)
        {
            UltimateXR.Avatar.UxrAvatar avatar = GetComponent<UltimateXR.Avatar.UxrAvatar>();
            if (avatar != null && UltimateXR.Core.UxrManager.Instance != null)
            {
                UltimateXR.Core.UxrManager.Instance.MoveAvatarTo(avatar, position, rotation * Vector3.forward);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }
        }
    }
}

