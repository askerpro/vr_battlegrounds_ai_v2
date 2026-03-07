using Mirror;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;

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
        // ── Отображение в Inspector (только для чтения, обновляются через hook) ──

        [Header("Состояние (только чтение)")]
        [Tooltip("Текущая команда игрока.")]
        [SerializeField] private TeamData _teamDisplay;

        [Tooltip("Жив ли игрок.")]
        [SerializeField] private bool _isAliveDisplay;

        // ── Сетевые данные ────────────────────────────────────────────────────

        /// <summary>
        /// teamIndex = 0 означает "нет команды".
        /// hook вызывается на всех клиентах при каждом изменении значения.
        /// </summary>
        [SyncVar(hook = nameof(OnTeamIndexChanged))]
        private int _teamIndex = 0;

        [SyncVar(hook = nameof(OnIsAliveChanged))]
        private bool _isAlive = true;

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
                RefreshDisplay();
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[PlayerController] {name}: команда назначена → {(value != null ? value.displayName : "нет")}");
            }
        }

        /// <summary>Числовой индекс команды (для сетевой синхронизации).</summary>
        public int TeamIndex => _teamIndex;

        public bool IsAlive => _isAlive;

        // ── Unity lifecycle ───────────────────────────────────────────────────

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

        public override void OnStartClient()
        {
            // Обновляем display-поля после того как SyncVar пришли с сервера
            RefreshDisplay();
        }

        // ── SyncVar hooks (вызываются на всех клиентах при изменении) ─────────

        private void OnTeamIndexChanged(int oldIndex, int newIndex)
        {
            RefreshDisplay();
            TeamData team = TeamRegistry.Instance?.GetByIndex(newIndex);
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[PlayerController] {name}: команда изменена → {(team != null ? team.displayName : "нет")}");
        }

        private void OnIsAliveChanged(bool oldValue, bool newValue)
        {
            RefreshDisplay();
        }

        // ── Внутреннее ───────────────────────────────────────────────────────

        /// <summary>Синхронизирует display-поля в Inspector с текущим состоянием.</summary>
        private void RefreshDisplay()
        {
            _teamDisplay   = TeamRegistry.Instance?.GetByIndex(_teamIndex);
            _isAliveDisplay = _isAlive;
        }

        // ── Игровая логика ────────────────────────────────────────────────────

        private void OnDamageReceived(object sender, UxrDamageEventArgs e)
        {
            if (!isServer) return;
            if (e.Dies || _actor.Life <= 0f) Die();
        }

        /// <summary>Убивает игрока на сервере и уведомляет клиентов.</summary>
        [Server]
        public void Die()
        {
            if (!_isAlive) return;

            _isAlive = false;
            RpcOnDied();

            // Уведомляем активный режим о гибели игрока.
            // EliminationMode делегирует в RoundManager; другие режимы обрабатывают по-своему.
            if (MatchManager.Instance != null)
                MatchManager.Instance.OnPlayerDied(this);
        }

        /// <summary>Возрождает игрока на заданной точке спавна.</summary>
        [Server]
        public void Respawn(Transform spawnPoint)
        {
            _isAlive = true;
            _actor.Life = 100f;
            RpcOnRespawned(spawnPoint.position, spawnPoint.rotation);
        }

        [ClientRpc]
        private void RpcOnDied()
        {
            // TODO: воспроизвести анимацию смерти, скрыть модель
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

