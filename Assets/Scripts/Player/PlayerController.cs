using Mirror;
using System;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;

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
        /// teamIndex = 0 означает "нет команды".
        /// hook вызывается на всех клиентах при каждом изменении значения.
        /// </summary>
        [SyncVar(hook = nameof(OnTeamIndexChanged))]
        private int _teamIndex = 0;

        // ── Публичный API ─────────────────────────────────────────────────────

        /// <summary>Данные команды игрока. Null если команда не назначена.</summary>
        public TeamData Team
        {
            get => TeamRegistry.Instance?.GetByIndex(_teamIndex);
            set
            {
                int newIndex = value != null ? value.teamIndex : 0;
                if (_teamIndex == newIndex) return;
                _teamIndex = newIndex;
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[PlayerController] {name}: команда назначена → {(value != null ? value.displayName : "нет")}");
            }
        }

        public float Health => _actor != null ? _actor.Life : 0f;

        /// <summary>Числовой индекс команды (для сетевой синхронизации).</summary>
        public int TeamIndex => _teamIndex;

        public bool IsAlive => !_actor.IsDead;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        public UxrActor _actor;

        private void Awake()
        {
            _actor = GetComponent<UxrActor>();
            _actor.DamageReceived += OnDamageReceived;
            _actor.Died += OnActorDied;
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

        private void OnTeamIndexChanged(int oldIndex, int newIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(newIndex);
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[PlayerController] {name}: команда изменена → {(team != null ? team.displayName : "нет")}");
        }

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
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerController] {name}: респаун на точке {spawnPoint.name} ({spawnPoint.position})", this);
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

