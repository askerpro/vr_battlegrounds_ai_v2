using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Контроллер игрока. Управляет командой и состоянием (жив/мёртв).
    /// Живёт на том же GameObject, что и UxrMirrorAvatar и UxrActor.
    /// Использует физическое перемещение по арене (телепортация не используется).
    /// </summary>
    [RequireComponent(typeof(UxrActor))]
    public class PlayerController : NetworkBehaviour
    {
        [SyncVar] private Team _team = Team.None;
        [SyncVar] private bool _isAlive = true;

        public Team Team
        {
            get => _team;
            set => _team = value;
        }

        public bool IsAlive => _isAlive;

        private UxrActor _actor;

        private void Awake()
        {
            _actor = GetComponent<UxrActor>();
            _actor.AutomaticDeadHandling = false;

            _actor.DamageReceived += OnDamageReceived;
        }

        private void OnDestroy()
        {
            if (_actor != null)
                _actor.DamageReceived -= OnDamageReceived;
        }

        /// <summary>
        /// Вызывается UxrActor при получении урона.
        /// Если HP упали до нуля — инициируем смерть на сервере.
        /// </summary>
        private void OnDamageReceived(object sender, UxrDamageEventArgs e)
        {
            if (!isServer)
                return;

            if (e.Dies || _actor.Life <= 0f)
                Die();
        }

        /// <summary>Убивает игрока на сервере и уведомляет клиентов.</summary>
        [Server]
        public void Die()
        {
            if (!_isAlive)
                return;

            _isAlive = false;
            RpcOnDied();

            RoundManager roundManager = FindObjectOfType<RoundManager>();
            roundManager?.OnPlayerDied(this);
        }

        /// <summary>Возрождает игрока на заданной точке спавна.</summary>
        [Server]
        public void Respawn(Transform spawnPoint)
        {
            _isAlive = true;
            _actor.Life = _actor.GetComponent<UxrActor>() != null ? 100f : 100f;
            RpcOnRespawned(spawnPoint.position, spawnPoint.rotation);
        }

        /// <summary>Синхронизирует анимацию смерти / деактивацию на всех клиентах.</summary>
        [ClientRpc]
        private void RpcOnDied()
        {
            // TODO: воспроизвести анимацию смерти, скрыть модель
        }

        /// <summary>Синхронизирует позицию возрождения на всех клиентах.</summary>
        [ClientRpc]
        private void RpcOnRespawned(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
        }
    }
}