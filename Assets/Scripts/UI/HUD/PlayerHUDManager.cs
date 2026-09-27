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
    /// Читает данные активного режима сцены (GameMode.ModeData) и инстанцирует его hudPrefab
    /// в заранее заготовленный контейнер; пересоздаёт HUD при смене режима.
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

            // HUD — у активного режима этой сцены, а не у выбора матча: в лобби выбор
            // матча описывает следующий матч, и его HUD в лобби не нужен. Режим может
            // доехать до клиента позже аватара — поэтому подписка, а не разовый запрос.
            _subscribed = true;
            GameplayManager.ActiveGameModeChangedLocal += SetupHUDForMode;
            SetupHUDForMode(GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null);
        }

        public override void OnStopAuthority()
        {
            Unsubscribe();
            base.OnStopAuthority();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private bool _subscribed;

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            GameplayManager.ActiveGameModeChangedLocal -= SetupHUDForMode;
        }

        private void SetupHUDForMode(GameMode mode)
        {
            if (this == null || _hudContainer == null) return;

            // Режима нет (карта до старта матча, смена сцены) — HUD нечего показывать.
            if (mode == null)
            {
                ClearHUD();
                GameLog.UI.Verbose($"[{nameof(PlayerHUDManager)}] Активного режима нет — HUD снят.", this);
                return;
            }

            GameModeData modeData = mode.ModeData;
            string modeId = modeData != null ? modeData.modeId : mode.GetType().Name;

            // Данные режима не нашлись по modeId — рассинхрон данных, сам не исправится. Error.
            if (modeData == null)
            {
                ClearHUD();
                GameLog.UI.Error(
                    $"[{nameof(PlayerHUDManager)}] Отмена спавна HUD: данные режима {mode.GetType().Name} не найдены по modeId.", this);
                return;
            }

            // У режима нет своего HUD (лобби) — это его решение, а не сбой. Info.
            if (modeData.hudPrefab == null)
            {
                ClearHUD();
                GameLog.UI.Info(
                    $"[{nameof(PlayerHUDManager)}] У режима '{modeId}' нет HUD — интерфейс не показывается.", this);
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
