using System;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>
    /// Совместимый фасад именованной сборки. Контактный editor-controller общей Editor-сборки
    /// регистрирует доступ к своему состоянию; геометрическая сборка на него не ссылается.
    /// </summary>
    public static class HandPoseSdkDiagnostics
    {
        static Func<bool> _enabled;
        static Func<int> _ownedHiddenCount;
        static Func<string> _fingerprint;
        static Action _cleanup;

        public static bool Enabled => _enabled?.Invoke() ?? false;
        public static int OwnedHiddenCount => _ownedHiddenCount?.Invoke() ?? 0;
        public static string CurrentFingerprint => _fingerprint?.Invoke();

        public static void Bind(Func<bool> enabled, Func<int> ownedHiddenCount, Func<string> fingerprint, Action cleanup)
        {
            if (enabled == null || ownedHiddenCount == null || fingerprint == null || cleanup == null)
                throw new ArgumentNullException("Нужен один полный владелец контактной SDK-панели.");
            if (_cleanup != null && _cleanup != cleanup)
                throw new InvalidOperationException("Владелец контактной SDK-панели уже зарегистрирован.");
            _enabled = enabled; _ownedHiddenCount = ownedHiddenCount; _fingerprint = fingerprint; _cleanup = cleanup;
        }

        public static void Cleanup() => _cleanup?.Invoke();
    }
}
