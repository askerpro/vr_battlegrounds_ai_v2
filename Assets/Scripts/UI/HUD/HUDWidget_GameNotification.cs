using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Player.UI;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Универсальный виджет системных уведомлений. 
    /// Подписывается на глобальные и целевые уведомления от PlayerHUDManager.
    /// Обрабатывает появление и плавное затухание текста.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class HUDWidget_GameNotification : MonoBehaviour
    {
        [SerializeField] private Text _notificationText;
        [SerializeField] private float _fadeDuration = 0.5f;

        private CanvasGroup _canvasGroup;
        private PlayerHUDManager _hudManager;
        private Coroutine _fadeCoroutine;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        private void Start()
        {
            // Подписываемся на целевые уведомления (если сервер шлёт конкретному игроку)
            _hudManager = GetComponentInParent<PlayerHUDManager>();
            if (_hudManager != null)
                _hudManager.OnNotificationReceived += ShowNotification;

            // Глобальные семантические события для всех режимов
            GameMode.OnMatchStartedLocal += HandleMatchStarted;
            GameMode.OnMatchEndedLocal += HandleMatchEnded;

            // Семантические события специфичные для EliminationMode
            EliminationMode.OnSetStartedLocal += HandleSetStarted;
            EliminationMode.OnSetEndedLocal += HandleSetEnded;
            EliminationMode.OnRoundStartedLocal += HandleRoundStarted;
            EliminationMode.OnRoundEndedLocal += HandleRoundEnded;
            EliminationMode.OnRoundStateChangedLocal += HandleRoundStateChanged;
        }

        private void OnDestroy()
        {
            if (_hudManager != null)
                _hudManager.OnNotificationReceived -= ShowNotification;

            GameMode.OnMatchStartedLocal -= HandleMatchStarted;
            GameMode.OnMatchEndedLocal -= HandleMatchEnded;

            EliminationMode.OnSetStartedLocal -= HandleSetStarted;
            EliminationMode.OnSetEndedLocal -= HandleSetEnded;
            EliminationMode.OnRoundStartedLocal -= HandleRoundStarted;
            EliminationMode.OnRoundEndedLocal -= HandleRoundEnded;
            EliminationMode.OnRoundStateChangedLocal -= HandleRoundStateChanged;
        }

        // ── Обработчики семантических событий ────────────────────────────────

        private void HandleMatchStarted() 
            => ShowNotification("Матч начался! В бой!", 4f);

        private void HandleMatchEnded(TeamData winner)
        {
            if (winner != null)
                ShowNotification($"Матч завершен!\nПобедили {winner.displayName}!", 5f);
            else
                ShowNotification("Матч завершился вничью!", 5f);
        }

        private void HandleSetStarted(int setNum)
            => ShowNotification($"Сет {setNum} начинается", 3f);

        private void HandleSetEnded(TeamData winner)
        {
            if (winner != null)
                ShowNotification($"Сет за командой {winner.displayName}!", 4f);
            else
                ShowNotification("Сет завершился вничью!", 4f);
        }

        private void HandleRoundStarted(int roundNum)
            => ShowNotification($"Раунд {roundNum} начался", 2f);

        private void HandleRoundEnded(TeamData winner)
        {
            if (winner != null)
                ShowNotification($"Раунд выиграла команда {winner.displayName}!", 3f);
            else
                ShowNotification("Раунд завершился вничью!", 3f);
        }

        private void HandleRoundStateChanged(RoundState state)
        {
            if (state == RoundState.Countdown)
                ShowNotification("Приготовьтесь!", 3f);
            else if (state == RoundState.Active)
                ShowNotification("В бой!", 2f);
        }

        // ── Core логика показа ─────────────────────────────────────────────

        private void ShowNotification(string message, float duration)
        {
            if (_notificationText != null)
            {
                _notificationText.text = message;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            _fadeCoroutine = StartCoroutine(ShowAndFadeOutRoutine(duration));
        }

        private IEnumerator ShowAndFadeOutRoutine(float displayDuration)
        {
            // Мгновенно показываем сообщение
            _canvasGroup.alpha = 1f;

            // Ждем указанное время
            yield return new WaitForSeconds(displayDuration);

            // Плавное затухание
            float timer = 0f;
            while (timer < _fadeDuration)
            {
                timer += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / _fadeDuration);
                yield return null;
            }

            _canvasGroup.alpha = 0f;
        }
    }
}
