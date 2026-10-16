using UltimateXR.Editor.Manipulation.HandPoses;
using UnityEditor;
using VrBattlegrounds.Editor.GripAuthoring.ContactPoints;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>
    /// Заглушка диагностического рентген-слоя SDK (режим рентгена отложен в пользу прямого автоснапа «Обхватить» и интерактивного IK конечных фаланг).
    /// Сохраняет контракт <see cref="HandPoseSdkDiagnostics"/> без фоновой нагрузки на редактор.
    /// </summary>
    [InitializeOnLoad]
    public static class HandPoseSdkContactDiagnostics
    {
        public static bool Enabled => false;
        public static int OwnedHiddenCount => 0;
        public static string CurrentFingerprint => null;

        static HandPoseSdkContactDiagnostics()
        {
            HandPoseSdkDiagnostics.Bind(() => Enabled, () => OwnedHiddenCount, () => CurrentFingerprint, Cleanup);
            UxrHandPoseEditorWindow.DiagnosticsClosed += _ => Cleanup();
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode)
                {
                    Cleanup();
                }
            };
            EditorApplication.quitting += Cleanup;
        }

        public static void Cleanup()
        {
        }
    }
}
