using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.Editor.DevTools
{
    /// <summary>
    ///     Запуск стресс-теста из редактора (Play Mode, клиент подключён): тот же запрос
    ///     серверу, что шлем шлёт удержанием обоих стиков, — см. <see cref="StressTestLauncher" />.
    /// </summary>
    internal static class StressTestMenu
    {
        private const string Root = "Tools/VR Battlegrounds/Debug/Stress Test/";

        [MenuItem(Root + "Start (9 puppets + clutter)")]
        private static void StartFull() => Start(new StressTestConfig());

        [MenuItem(Root + "Start Short (10 s phases)")]
        private static void StartShort() => Start(new StressTestConfig { warmupSeconds = 2f, phaseSeconds = 10f, settleSeconds = 2f });

        [MenuItem(Root + "Stop")]
        private static void Stop() => StressTestNetwork.RequestStop();

        [MenuItem(Root + "Open Perf Folder")]
        private static void OpenFolder()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "perf");
            System.IO.Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem(Root + "Start (9 puppets + clutter)", true)]
        [MenuItem(Root + "Start Short (10 s phases)", true)]
        private static bool CanStart() => EditorApplication.isPlaying && !StressTestClientSession.IsRunning;

        [MenuItem(Root + "Stop", true)]
        private static bool CanStop() => EditorApplication.isPlaying && StressTestClientSession.IsRunning;

        private static void Start(StressTestConfig config)
        {
            if (!StressTestNetwork.RequestStart(config, out string reason))
            {
                EditorUtility.DisplayDialog("Stress Test", "Не запущен: " + reason, "OK");
            }
        }
    }
}
