using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.Linq;
using System.Diagnostics;
#endif

/// <summary>
/// Утилитарный класс для проверки фокуса приложения Unity Editor
/// Помогает определить, должен ли текущий инстанс обрабатывать инпут и обновления
/// </summary>
public static class EditorWindowFocusHelper
{
    /// <summary>
    /// Проверяет, должен ли текущий инстанс обрабатывать инпут
    /// В билде всегда возвращает true если приложение в фокусе
    /// В редакторе проверяет фокус именно этого инстанса Unity Editor
    /// </summary>
    /// <returns>True если инпут должен обрабатываться</returns>
    public static bool ShouldProcessInput()
    {
        // В билде проверяем только фокус приложения
        #if !UNITY_EDITOR
        return Application.isFocused;
        #else
        
        // В редакторе проверяем фокус именно этого инстанса Unity Editor
        return IsThisUnityEditorInstanceFocused();
        #endif
    }

    /// <summary>
    /// Проверяет, следует ли игнорировать инпут
    /// </summary>
    /// <returns>True если инпут следует игнорировать</returns>
    public static bool ShouldIgnoreInput()
    {
        return !ShouldProcessInput();
    }

    /// <summary>
    /// Проверяет, является ли именно ЭТОТ инстанс Unity Editor активным приложением в ОС
    /// В билде всегда возвращает true если приложение в фокусе
    /// </summary>
    /// <returns>True если именно этот Unity Editor инстанс - активное приложение в ОС</returns>
    public static bool IsThisUnityEditorInstanceFocused()
    {
        #if !UNITY_EDITOR
        return Application.isFocused; // В билде всегда полагаемся на Application.isFocused
        #else
        try
        {
            // Получаем PID текущего процесса Unity Editor
            int currentProcessId = Process.GetCurrentProcess().Id;
            
            // Получаем активное окно в системе
            var activeProcess = GetForegroundProcess();
            if (activeProcess == null)
                return false;

            // Проверяем, что активный процесс - именно наш инстанс Unity Editor
            return activeProcess.Id == currentProcessId;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning($"[EditorWindowFocusHelper] Error checking foreground process: {e.Message}");
            // Fallback на стандартную проверку
            return Application.isFocused;
        }
        #endif
    }

    /// <summary>
    /// Альтернативный метод: проверяет фокус через состояние окон редактора
    /// Более надежный для случая с несколькими инстансами Unity
    /// </summary>
    /// <returns>True если этот инстанс Unity Editor имеет фокусированные окна</returns>
    public static bool IsThisEditorInstanceActive()
    {
        #if !UNITY_EDITOR
        return Application.isFocused;
        #else
        try
        {
            // Проверяем, что у нас есть активные окна и хотя бы одно имеет фокус
            var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            bool hasAnyFocusedWindow = allWindows.Any(w => w != null && w.hasFocus);
            
            // Также проверяем общий фокус приложения
            return Application.isFocused && hasAnyFocusedWindow;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning($"[EditorWindowFocusHelper] Error checking editor windows: {e.Message}");
            return Application.isFocused;
        }
        #endif
    }

    /// <summary>
    /// Проверяет, есть ли активное окно в Unity Editor (для дополнительной проверки)
    /// </summary>
    /// <returns>True если есть активное окно</returns>
    public static bool HasActiveEditorWindow()
    {
        #if !UNITY_EDITOR
        return true; // В билде всегда true
        #else
        // Проверяем, что есть сфокусированное окно в редакторе
        return EditorWindow.focusedWindow != null;
        #endif
    }
    
    /// <summary>
    /// Более строгая проверка - проверяет и фокус именно этого инстанса, и наличие активного окна
    /// </summary>
    /// <returns>True если все проверки пройдены</returns>
    public static bool ShouldProcessInputStrict()
    {
        #if !UNITY_EDITOR
        return Application.isFocused;
        #else
        return IsThisUnityEditorInstanceFocused() && HasActiveEditorWindow();
        #endif
    }
    
    /// <summary>
    /// Комбинированная проверка с использованием двух методов для надежности
    /// </summary>
    /// <returns>True если инстанс активен</returns>
    public static bool IsInstanceActiveCombined()
    {
        #if !UNITY_EDITOR
        return Application.isFocused;
        #else
        // Используем оба метода для большей надежности
        bool processCheck = IsThisUnityEditorInstanceFocused();
        bool windowCheck = IsThisEditorInstanceActive();
        
        // Если хотя бы один метод говорит, что мы активны, и у нас есть фокус - считаем активными
        return (processCheck || windowCheck);
        #endif
    }
    
    /// <summary>
    /// Логирует информацию о текущем состоянии фокуса (для отладки)
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogFocusState()
    {
        #if UNITY_EDITOR
        var focusedWindow = EditorWindow.focusedWindow;
        string windowInfo = focusedWindow != null ? focusedWindow.GetType().Name : "null";
        int currentPID = Process.GetCurrentProcess().Id;

        UnityEngine.Debug.Log($"[EditorWindowFocusHelper] " +
                  $"Application.isFocused: {Application.isFocused}, " +
                  $"FocusedWindow: {windowInfo}, " +
                  $"CurrentPID: {currentPID}, " +
                  $"ThisInstanceFocused: {IsThisUnityEditorInstanceFocused()}, " +
                  $"ThisInstanceActive: {IsThisEditorInstanceActive()}, " +
                  $"Combined: {IsInstanceActiveCombined()}, " +
                  $"ShouldProcessInput: {ShouldProcessInput()}");
        #endif
    }

    #if UNITY_EDITOR
    /// <summary>
    /// Получает процесс, который находится на переднем плане (активное окно)
    /// </summary>
    /// <returns>Процесс активного окна или null</returns>
    private static Process GetForegroundProcess()
    {
        try
        {
            #if UNITY_EDITOR_WIN
            // На Windows используем WinAPI для точного определения
            return GetForegroundProcessWindows();
            #elif UNITY_EDITOR_OSX
            // На macOS используем более сложную логику
            return GetForegroundProcessMac();
            #elif UNITY_EDITOR_LINUX
            // На Linux - используем X11 или базовую проверку
            return GetForegroundProcessLinux();
            #else
            return null;
            #endif
        }
        catch
        {
            return null;
        }
    }

    #if UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(System.IntPtr hWnd, out int processId);

    private static Process GetForegroundProcessWindows()
    {
        System.IntPtr hwnd = GetForegroundWindow();
        if (hwnd == System.IntPtr.Zero)
            return null;

        int processId;
        GetWindowThreadProcessId(hwnd, out processId);
        
        if (processId == 0)
            return null;

        try
        {
            return Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
    }
    #endif

    #if UNITY_EDITOR_OSX
    private static Process GetForegroundProcessMac()
    {
        try
        {
            // На macOS можем попробовать использовать AppleScript или другие методы
            // Пока используем базовый подход
            return Process.GetCurrentProcess();
        }
        catch
        {
            return null;
        }
    }
    #endif

    #if UNITY_EDITOR_LINUX
    private static Process GetForegroundProcessLinux()
    {
        try
        {
            // На Linux можем попробовать использовать xprop или wmctrl
            // Пока используем базовый подход
            return Process.GetCurrentProcess();
        }
        catch
        {
            return null;
        }
    }
    #endif
    #endif
}