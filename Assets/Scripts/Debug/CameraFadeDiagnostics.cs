using System;
using UltimateXR.CameraUtils;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Debugging
{
    /// <summary>Записывает источник запросов затемнения камеры, не меняя поведение SDK.</summary>
    internal static class CameraFadeDiagnostics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            UxrCameraFade.FadeDiagnostic -= HandleFadeDiagnostic;
            UxrCameraFade.FadeDiagnostic += HandleFadeDiagnostic;
        }

        private static void HandleFadeDiagnostic(UxrCameraFade fade, string operation)
        {
            // Диагностика никогда не должна прерывать затемнение или перемещение игрока.
            try
            {
                if (!GameLog.Debug.IsEnabled(LogLevel.Info))
                    return;

                string avatar = fade.Avatar != null ? fade.Avatar.name : "без аватара";
                GameLog.Debug.Info($"[CameraFade] frame={Time.frameCount}, time={Time.realtimeSinceStartup:F3}, " +
                                   $"cameraId={fade.GetInstanceID()}, avatar='{avatar}', {operation}\n{Environment.StackTrace}", fade);
            }
            catch (Exception)
            {
                // Уничтожение камеры или завершение процесса не должно ломаться из-за лога.
            }
        }
    }
}
