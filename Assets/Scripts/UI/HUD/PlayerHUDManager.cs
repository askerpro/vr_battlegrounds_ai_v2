using UnityEngine;
using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Player.UI
{
    /// <summary>
    /// Управляет локальным интерфейсом игрока в VR.
    /// Вешается на префаб игрока (там же где PlayerController).
    /// Срабатывает только для локального игрока.
    /// Читает текущий GameModeData и инстанцирует его hudPrefab в заранее заготовленный контейнер.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerHUDManager : NetworkBehaviour
    {
        [Tooltip("Контейнер (обычно World Space Canvas) внутри иерархии аватара, куда будет заспавнен HUD.")]
        [SerializeField] private RectTransform _hudContainer;

        private GameObject _activeHudInstance;
        private PlayerController _playerController;

        private void Awake()
        {
            _playerController = GetComponent<PlayerController>();
        }

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();

            // Ошибка сборки префаба: контейнера нет, значит HUD не появится никогда
            // и сам собой не починится. Error.
            if (_hudContainer == null)
            {
                GameLog.UI.Error($"[{nameof(PlayerHUDManager)}] HUD Container не назначен на префабе игрока!", this);
                return;
            }

            // Подписываемся на события изменения сцены или старта сессии,
            // чтобы пересоздавать HUD если режим сменился.
            // Но в нашей архитектуре, так как игрок спавнится после загрузки карты,
            // SessionManager уже знает режим.
            SetupHUDForCurrentMode();
        }

        private void SetupHUDForCurrentMode()
        {
            // Сбой порядка инициализации: HUD спавнится после загрузки карты, к этому
            // моменту менеджер сессии обязан существовать. Error.
            if (SessionManager.Instance == null)
            {
                GameLog.UI.Error(
                    $"[{nameof(PlayerHUDManager)}] Отмена спавна HUD: SessionManager.Instance равен null. " +
                    "Возможно, сцена загрузилась неверно.", this);
                return;
            }

            // Единственная из пяти веток, которая лечится сама: режим доезжает до клиента
            // по сети и на момент спавна аватара может быть ещё не получен. Warning.
            if (string.IsNullOrEmpty(SessionManager.Instance.SelectedModeId))
            {
                GameLog.UI.Warning(
                    $"[{nameof(PlayerHUDManager)}] Отмена спавна HUD: в SessionManager пустой SelectedModeId. " +
                    "Это может быть из-за задержки сети при входе на сервер.", this);
                return;
            }

            string modeId = SessionManager.Instance.SelectedModeId;
            GameModeData modeData = SessionManager.Instance.SelectedGameModeData;

            // Режим выбран, но в реестре его нет — рассинхрон данных, сам не исправится. Error.
            if (modeData == null)
            {
                GameLog.UI.Error(
                    $"[{nameof(PlayerHUDManager)}] Отмена спавна HUD: GameMode '{modeId}' не найден в реестре.", this);
                return;
            }

            // Режим найден и исправен, у него просто нет своего HUD. Игра работает,
            // но игрок остаётся без интерфейса — это проблема, а не сбой. Warning.
            if (modeData.hudPrefab == null)
            {
                GameLog.UI.Warning(
                    $"[{nameof(PlayerHUDManager)}] GameMode '{modeId}' не имеет hudPrefab. HUD не заспавнен.", this);
                return;
            }

            // Удаляем старый HUD если есть
            ClearHUD();

            try
            {
                // Спавним новый HUD, делаем _hudContainer его родителем
                _activeHudInstance = Instantiate(modeData.hudPrefab, _hudContainer);

                // Сбрасываем трансформации (чтобы префаб встал ровно по центру и с нужным скейлом)
                RectTransform hudRect = _activeHudInstance.GetComponent<RectTransform>();
                if (hudRect != null)
                {
                    hudRect.localPosition = Vector3.zero;
                    hudRect.localRotation = Quaternion.identity;
                    hudRect.localScale = Vector3.one;
                }

                GameLog.UI.Info(
                    $"[{nameof(PlayerHUDManager)}] Успешный спавн HUD префаба '{modeData.hudPrefab.name}' для режима '{modeId}'.");
            }
            catch (System.Exception ex)
            {
                GameLog.UI.Error($"[{nameof(PlayerHUDManager)}] Фатальная ошибка при спавне HUD для '{modeId}': {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ClearHUD()
        {
            if (_activeHudInstance != null)
            {
                Destroy(_activeHudInstance);
                _activeHudInstance = null;
            }
        }

        // ── Система Уведомлений (Notifications) ──────────────────────────────

        /// <summary>Событие, на которое подписывается виджет HUDWidget_GameNotification на клиенте.</summary>
        public event System.Action<string, float> OnNotificationReceived;

        [TargetRpc]
        public void TargetShowNotification(NetworkConnection target, string message, float duration)
        {
            // Вызывается только на нужном клиенте
            OnNotificationReceived?.Invoke(message, duration);
        }

        /// <summary>
        /// Отправляет уведомление конкретному игроку (Вызывать только на сервере).
        /// </summary>
        [Server]
        public static void SendToPlayer(PlayerSession player, string message, float duration = 3f)
        {
            if (player == null || player.connectionToClient == null) return;

            PlayerHUDManager hud = player.GetComponent<PlayerHUDManager>();
            if (hud != null)
            {
                hud.TargetShowNotification(player.connectionToClient, message, duration);
            }
        }

        /// <summary>
        /// Отправляет уведомление всем подключенным игрокам (Вызывать только на сервере).
        /// Гарантированно рассылает сообщение всем инстансам через PlayersManager.
        /// </summary>
        [Server]
        public static void SendToAll(string message, float duration = 3f)
        {
            if (PlayersManager.Instance == null) return;

            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null)
                {
                    SendToPlayer(session, message, duration);
                }
            }
        }
    }
}
