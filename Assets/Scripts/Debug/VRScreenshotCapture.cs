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
    /// Утилита для создания скриншотов во время игры по нажатию кнопки B (правый контроллер).
    /// Снимает вид с камеры аватара и сохраняет PNG в указанную папку.
    /// Используется для того, чтобы предоставить ИИ помощнику скриншоты для анализа сцены.
    /// Использование:
    ///   1. Добавить компонент на любой GameObject в сцене (или на тот же объект, что и DebugOrchestrator).
    ///   2. Скриншоты сохраняются в папку Screenshots/ в корне проекта.
    ///   3. Нажать B (правый контроллер) для захвата.
    /// </summary>
    public class VRScreenshotCapture : MonoBehaviour
    {
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
                $"[VRScreenshotCapture] 📸 Скриншот сохранён: {fullPath}");
        }
    }
}
