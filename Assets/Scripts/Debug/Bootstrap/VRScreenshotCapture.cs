using System;
using System.IO;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Скриншоты во время Play в редакторе по кнопке B (правый контроллер): вид с камеры аватара в PNG, папка
    /// <c>Screenshots/</c> в корне проекта. Нужны, чтобы дать ИИ-помощнику скриншоты для анализа сцены.
    ///
    /// <para>
    /// Только редактор: сборка <c>VrBattlegrounds.DebugBootstrap</c>, в сцены и префабы не кладётся. Включается
    /// личной галочкой <see cref="DebugBootstrapSettings.ScreenshotOnButtonB"/> (меню
    /// <c>Tools/VR Battlegrounds/Debug/Screenshot on B Button</c> или окно Bootstrap Settings), по умолчанию выключен —
    /// иначе отнимает кнопку B у игры. Тогда <see cref="SpawnOnPlay"/> создаёт его в начале Play.
    /// </para>
    /// </summary>
    public class VRScreenshotCapture : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SpawnOnPlay()
        {
            if (!DebugBootstrapSettings.ScreenshotOnButtonB) return;

            var go = new GameObject(nameof(VRScreenshotCapture));
            DontDestroyOnLoad(go);
            go.AddComponent<VRScreenshotCapture>();
        }

        [Header("Настройки")]
        [Tooltip("Множитель разрешения скриншота (1 = нативное, 2 = двойное)")]
        [SerializeField] private int _superSize = 2;

        [Tooltip("Папка для сохранения скриншотов (относительно корня проекта)")]
        [SerializeField] private string _outputFolder = "Screenshots";

        [Tooltip("Минимальный интервал между скриншотами (секунды)")]
        [SerializeField] private float _cooldown = 0.5f;

        private float _lastCaptureTime;
        private string _absoluteOutputPath;

        private void Start()
        {
            // Формируем абсолютный путь к папке скриншотов
            _absoluteOutputPath = Path.Combine(Application.dataPath, "..", _outputFolder);
            _absoluteOutputPath = Path.GetFullPath(_absoluteOutputPath);

            // Создаём папку если не существует
            if (!Directory.Exists(_absoluteOutputPath))
            {
                Directory.CreateDirectory(_absoluteOutputPath);
                GameLog.Debug.Info(
                    $"[VRScreenshotCapture] Создана папка для скриншотов: {_absoluteOutputPath}");
            }

            GameLog.Debug.Info(
                $"[VRScreenshotCapture] Инициализирован. Скриншоты → {_absoluteOutputPath}. Нажмите B для захвата.");
        }

        private void Update()
        {
            if (UxrAvatar.LocalAvatar == null) return;

            // Кнопка B на правом контроллере (Button2 = B/Y)
            bool pressed = UxrAvatar.LocalAvatarInput.GetButtonsPressUp(
                UxrHandSide.Right, UxrInputButtons.Button2);

            if (pressed && Time.time - _lastCaptureTime > _cooldown)
            {
                _lastCaptureTime = Time.time;
                CaptureScreenshot();
            }
        }

        private void CaptureScreenshot()
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            string filename = $"VR_Screenshot_{timestamp}.png";
            string fullPath = Path.Combine(_absoluteOutputPath, filename);

            ScreenCapture.CaptureScreenshot(fullPath, _superSize);

            GameLog.Debug.Info(
                $"[VRScreenshotCapture] Скриншот сохранён: {fullPath}");
        }
    }
}
