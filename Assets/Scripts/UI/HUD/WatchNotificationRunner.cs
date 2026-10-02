using UnityEngine;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Хост нотификаций часов на машине игрока (T-46): ставит себя сам при старте игры
    /// (как <c>PerformanceLevelInstaller</c>), держит <see cref="WatchGameEvents"/> и раз в кадр продвигает
    /// очередь <see cref="WatchNotifications"/>. Не зависит от аватара — переживает смену аватара и сцены.
    /// В batch mode (выделенный сервер, CI) не ставится: часов и игрока там нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WatchNotificationRunner : MonoBehaviour
    {
        private readonly WatchGameEvents _events = new WatchGameEvents();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode) return;
            if (FindAnyObjectByType<WatchNotificationRunner>() != null) return;

            var host = new GameObject(nameof(WatchNotificationRunner));
            DontDestroyOnLoad(host);
            host.AddComponent<WatchNotificationRunner>();
        }

        private void OnEnable() => _events.Subscribe();

        private void OnDisable() => _events.Dispose();

        private void Update()
        {
            float now = Time.unscaledTime;
            _events.Poll(now);
            WatchNotifications.Tick(now);
        }
    }
}
