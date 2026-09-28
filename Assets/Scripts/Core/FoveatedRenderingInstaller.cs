using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Включает Fixed Foveated Rendering на шлеме: выставляет
    /// <see cref="XRDisplaySubsystem.foveatedRenderingLevel"/> из
    /// <see cref="GameSettings.FoveatedRenderingLevel"/>, как только XR-дисплей запущен.
    ///
    /// <para>
    /// <b>Почему опрос, а не событие.</b> Дисплей поднимает XR Management при старте
    /// (Initialize XR on Startup), но момент «running» не публикуется событием; к тому же
    /// провайдер может перезапустить подсистему (потеря фокуса, выход в меню Oculus). Раз в
    /// секунду сравниваем экземпляр запущенного дисплея с тем, которому уровень уже выставлен,
    /// — новый или перезапущенный дисплей получает уровень заново.
    /// </para>
    ///
    /// <para>
    /// <b>Где работает.</b> Только Android-плеер (Quest). В редакторе (Quest Link) не ставится,
    /// чтобы не мешать отладке на ПК; на выделенном сервере (<c>-batchmode</c>) нет дисплея.
    /// На планшете (Android без XR) дисплей не запускается — компонент просто опрашивает впустую.
    /// </para>
    ///
    /// <para>
    /// Требование плагина: «Foveated Rendering Method» в OculusSettings = Fixed Foveated
    /// Rendering (значение по умолчанию). Рендер в промежуточную текстуру URP (пост-обработка,
    /// HDR, render scale ≠ 1, opaque/depth texture, фичи рендерера при Intermediate Texture =
    /// Always) отключает фовеацию для основных проходов — см. <c>Docs/perf-stress-test.md</c>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoveatedRenderingInstaller : MonoBehaviour
    {
        private const float PollIntervalSeconds = 1f;

        private readonly List<XRDisplaySubsystem> _displays = new List<XRDisplaySubsystem>();
        private XRDisplaySubsystem _appliedTo;
        private float _nextPollTime;
        private int _applyCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            if (Application.platform != RuntimePlatform.Android) return;

            var host = new GameObject(nameof(FoveatedRenderingInstaller));
            DontDestroyOnLoad(host);
            host.AddComponent<FoveatedRenderingInstaller>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPollTime) return;
            _nextPollTime = Time.unscaledTime + PollIntervalSeconds;

            XRDisplaySubsystem display = FindRunningDisplay();
            if (display == null)
            {
                // Дисплей остановлен — после перезапуска уровень выставим заново.
                _appliedTo = null;
                return;
            }

            if (display == _appliedTo) return;

            Apply(display);
        }

        private XRDisplaySubsystem FindRunningDisplay()
        {
            SubsystemManager.GetSubsystems(_displays);
            foreach (XRDisplaySubsystem display in _displays)
            {
                if (display.running) return display;
            }
            return null;
        }

        private void Apply(XRDisplaySubsystem display)
        {
            float target = GameSettings.Instance.FoveatedRenderingLevel;
            display.foveatedRenderingLevel = target;
            _appliedTo = display;
            _applyCount++;

            string message = string.Format(
                CultureInfo.InvariantCulture,
                "[FoveatedRenderingInstaller] FFR: запрошено {0:F2}, дисплей отвечает {1:F2}, " +
                "флаги {2}, возможности GPU {3}.",
                target, display.foveatedRenderingLevel, display.foveatedRenderingFlags,
                SystemInfo.foveatedRenderingCaps);

            // Первое применение — событие; повторные (перезапуск дисплея) — поток.
            if (_applyCount == 1) GameLog.Perf.Info(message, this);
            else GameLog.Perf.Verbose($"{message} Повтор №{_applyCount}.", this);
        }
    }
}
