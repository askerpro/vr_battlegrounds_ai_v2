using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Владеет только запасным слушателем между локальными аватарами и паузой звука
    /// выделенного сервера. Слушатели игровых камер остаются под управлением их владельцев.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class LocalAudioListenerOwner : MonoBehaviour
    {
        private AudioListener _fallback;
        private bool _ownsServerPause;
        private bool _pauseBeforeServer;

        private void OnEnable()
        {
            EnsureFallback();
            UxrAvatar.LocalAvatarChanged += OnLocalAvatarChanged;
            UxrAvatar.GlobalDisabled += OnAvatarDisabled;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Reconcile();
        }

        private void OnDisable()
        {
            UxrAvatar.LocalAvatarChanged -= OnLocalAvatarChanged;
            UxrAvatar.GlobalDisabled -= OnAvatarDisabled;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_fallback != null) _fallback.enabled = false;
            RestoreServerPause();
        }

        private void OnDestroy()
        {
            RestoreServerPause();
            if (_fallback != null) Destroy(_fallback.gameObject);
        }

        private void LateUpdate()
        {
            // Сигналы SDK ускоряют смену слушателя, но камера может смениться отдельно
            // от аватара. Каждый кадр проверяем фактических слушателей.
            Reconcile();
        }

        private void OnLocalAvatarChanged(object sender, UxrAvatarEventArgs args) => Reconcile();

        // Отключение аватара не обязано менять LocalAvatar. Этот сигнал приходит
        // после SDK OnDisable: запасной слушатель сверяется сразу, до следующего кадра.
        private void OnAvatarDisabled(UxrAvatar avatar) => Reconcile();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Reconcile();

        private void EnsureFallback()
        {
            if (_fallback != null) return;

            var listenerObject = new GameObject("Запасной AudioListener");
            // AddComponent на активном объекте на мгновение создал бы второго слушателя.
            listenerObject.SetActive(false);
            listenerObject.transform.SetParent(transform, false);
            _fallback = listenerObject.AddComponent<AudioListener>();
            _fallback.enabled = false;
            listenerObject.SetActive(true);
        }

        private void Reconcile()
        {
            if (!isActiveAndEnabled) return;
            EnsureFallback();

            NetworkManager manager = NetworkManager.singleton;
            bool serverOnly = manager != null && manager.mode == NetworkManagerMode.ServerOnly;
            if (serverOnly)
            {
                if (!_ownsServerPause)
                {
                    _pauseBeforeServer = AudioListener.pause;
                    _ownsServerPause = true;
                    GameLog.Debug.Info(
                        $"[LocalAudioListenerOwner] ServerOnly: включаю паузу звука; исходная пауза={_pauseBeforeServer}.");
                }
                // Текущий игровой контент не использует AudioSource.ignoreListenerPause.
                // Это временная политика роли, а не изменение настроек звука пользователя.
                AudioListener.pause = true;
            }
            else
            {
                RestoreServerPause();
            }

            bool hasRealListener = false;
            foreach (AudioListener listener in FindObjectsByType<AudioListener>())
            {
                if (listener != _fallback && listener.isActiveAndEnabled)
                {
                    hasRealListener = true;
                    break;
                }
            }

            _fallback.enabled = !hasRealListener;
            if (!hasRealListener)
            {
                Camera camera = Camera.main;
                if (camera != null && camera.isActiveAndEnabled)
                    _fallback.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            }
        }

        private void RestoreServerPause()
        {
            if (!_ownsServerPause) return;
            AudioListener.pause = _pauseBeforeServer;
            _ownsServerPause = false;
            GameLog.Debug.Info(
                $"[LocalAudioListenerOwner] Серверная политика звука завершена; пауза восстановлена={_pauseBeforeServer}.");
        }
    }
}
