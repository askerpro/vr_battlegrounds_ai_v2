using System;
using NUnit.Framework;
using UltimateXR.CameraUtils;
using UnityEngine;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>Источник Fade записывается до MoveNext, пока в стеке ещё есть вызывающий код.</summary>
    public class CameraFadeDiagnosticsTests
    {
        [Test]
        public void Запрос_корутины_сразу_сообщает_параметры_и_источник()
        {
            var camera = new GameObject("DiagnosticCamera");
            string message = null;
            string stack = null;
            int count = 0;
            Action<UxrCameraFade, string> listener = (fade, operation) =>
            {
                count++;
                message = operation;
                stack = Environment.StackTrace;
            };
            UxrCameraFade.FadeDiagnostic += listener;
            try
            {
                var fade = camera.AddComponent<UxrCameraFade>();
                var iterator = fade.StartFadeCoroutine(0.4f, Color.clear, Color.black);
                Assert.IsNotNull(iterator);
                Assert.AreEqual(1, count, "Диагностика нужна до первого MoveNext.");
                StringAssert.Contains("StartFadeCoroutine", message);
                StringAssert.Contains("from=", message);
                StringAssert.Contains("to=", message);
                StringAssert.Contains(nameof(Запрос_корутины_сразу_сообщает_параметры_и_источник), stack);
            }
            finally
            {
                UxrCameraFade.FadeDiagnostic -= listener;
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }
    }
}
