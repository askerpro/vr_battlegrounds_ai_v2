using UnityEditor;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Меню трассы тела своего аватара в живой игре (<see cref="BodyTraceRecorder"/>): только чтение поз, в
    /// <c>tmp/body_trace_*.csv</c>. Начинать стоя прямо, взгляд вперёд — первый кадр задаёт покой груди и таза.
    /// </summary>
    public static class BodyTraceMenu
    {
        [MenuItem("Tools/VR Battlegrounds/Debug/Body Trace/Start")]
        private static void StartTrace() => GameLog.Debug.Info("[BodyTraceMenu] " + BodyTraceRecorder.Start());

        [MenuItem("Tools/VR Battlegrounds/Debug/Body Trace/Start", true)]
        private static bool CanStart() => EditorApplication.isPlaying && !BodyTraceRecorder.IsRecording;

        [MenuItem("Tools/VR Battlegrounds/Debug/Body Trace/Stop")]
        private static void StopTrace() => GameLog.Debug.Info("[BodyTraceMenu] " + BodyTraceRecorder.Stop());

        [MenuItem("Tools/VR Battlegrounds/Debug/Body Trace/Stop", true)]
        private static bool CanStop() => BodyTraceRecorder.IsRecording;
    }
}
