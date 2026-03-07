using UnityEngine;
using UnityEngine.UI;
using System.Collections;
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
            // Поскольку виджет спавнится внутри префаба игрока (дочерне камере),
            // мы можем найти PlayerHUDManager вверх по иерархии
            _hudManager = GetComponentInParent<PlayerHUDManager>();

            if (_hudManager != null)
            {
                _hudManager.OnNotificationReceived += HandleNotification;
            }
            else
            {
                Debug.LogWarning($"[{nameof(HUDWidget_GameNotification)}] PlayerHUDManager не найден в родителях!");
            }
        }

        private void OnDestroy()
        {
            if (_hudManager != null)
            {
                _hudManager.OnNotificationReceived -= HandleNotification;
            }
        }

        private void HandleNotification(string message, float duration)
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
