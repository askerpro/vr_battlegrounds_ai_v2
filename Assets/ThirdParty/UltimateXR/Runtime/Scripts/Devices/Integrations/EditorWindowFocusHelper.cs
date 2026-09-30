using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// VR Battlegrounds patch 33: в фокусе ли именно этот экземпляр редактора — на одной машине их бывает несколько
/// (хост и клиенты). Вызывается каждый кадр из <c>UxrManager</c>, поэтому без аллокаций: PID своего процесса
/// берётся один раз, PID окна на переднем плане — одним вызовом WinAPI.
/// </summary>
public static class EditorWindowFocusHelper
{
#if UNITY_EDITOR_WIN
    private static int s_currentProcessId;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(System.IntPtr hWnd, out int processId);
#endif

    /// <summary>
    /// True, если на переднем плане окно этого процесса или у редактора есть окно с фокусом ввода. В билде —
    /// <see cref="Application.isFocused"/>.
    /// </summary>
    public static bool IsThisEditorInstanceFocused()
    {
#if !UNITY_EDITOR
        return Application.isFocused;
#else
        return IsForegroundProcessThis() || (Application.isFocused && EditorWindow.focusedWindow != null);
#endif
    }

#if UNITY_EDITOR
    private static bool IsForegroundProcessThis()
    {
#if UNITY_EDITOR_WIN
        if (s_currentProcessId == 0)
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                s_currentProcessId = process.Id;
            }
        }

        System.IntPtr window = GetForegroundWindow();
        if (window == System.IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(window, out int foregroundProcessId);
        return foregroundProcessId == s_currentProcessId;
#else
        // Вне Windows узнать процесс переднего окна нечем — остаётся фокус приложения.
        return Application.isFocused;
#endif
    }
#endif
}
