using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Лог сессии в файл на шлеме: всё, что пишет игра и движок (<see cref="Application.logMessageReceivedThreaded"/>),
    /// копируется в <c>&lt;persistentDataPath&gt;/logs/session-&lt;время&gt;.log</c>. logcat Quest хранит лишь последние
    /// минуты-часы, и баг с тестовой сессии к разбору уже не достать; файл забирается позже:
    /// <c>adb pull /sdcard/Android/data/&lt;пакет&gt;/files/logs</c>.
    ///
    /// <para>
    /// Хранятся последние <see cref="KeepSessions"/> сессий, файл сессии — не больше <see cref="MaxBytes"/>
    /// (дальше пишется одна строка об обрезке). Запись буферизована: сброс на диск при Warning/Error,
    /// раз в <see cref="FlushSeconds"/> и при сворачивании/выходе. У каждой строки — метка времени
    /// (в логе Unity <c>-logFile</c> её нет, связать лог с рассказом игрока было нечем).
    /// </para>
    ///
    /// <para>
    /// Где: Android (шлем, планшет) — <c>&lt;persistentDataPath&gt;/logs</c>; Windows-плеер и выделенный сервер —
    /// <c>Logs</c> рядом с exe (там же <c>server-*.log</c> от Start-Server.ps1). В редакторе не пишется: есть Editor.log.
    /// </para>
    /// </summary>
    public sealed class SessionLogFile : MonoBehaviour
    {
        public const int KeepSessions = 10;
        public const long MaxBytes = 32L * 1024 * 1024;
        public const double FlushSeconds = 2.0;

        private static readonly object Gate = new object();
        private static StreamWriter s_writer;
        private static long s_written;
        private static bool s_truncated;
        private static readonly Stopwatch SinceFlush = new Stopwatch();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor) return;

            string path;
            try
            {
                string dir = Application.platform == RuntimePlatform.Android
                    ? Path.Combine(Application.persistentDataPath, "logs")
                    : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? Application.persistentDataPath, "Logs");
                Directory.CreateDirectory(dir);
                PruneOld(dir);
                // pid — несколько плееров из одной папки (e2e) стартуют в одну секунду.
                path = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Process.GetCurrentProcess().Id}.log");
                s_writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 64 * 1024);
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SessionLogFile] Не удалось открыть файл лога сессии: {ex.Message}");
                return;
            }

            SinceFlush.Start();
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting += Flush;

            var go = new GameObject(nameof(SessionLogFile));
            DontDestroyOnLoad(go);
            go.AddComponent<SessionLogFile>();

            GameLog.Debug.Info($"[SessionLogFile] Лог сессии пишется в {path} ({Application.version}, {SystemInfo.deviceModel}).");
        }

        private static void PruneOld(string dir)
        {
            string[] files = Directory.GetFiles(dir, "session-*.log");
            Array.Sort(files, StringComparer.Ordinal);
            // Новый файл займёт одно место из KeepSessions.
            for (int i = 0; i < files.Length - (KeepSessions - 1); i++)
            {
                try { File.Delete(files[i]); }
                catch (IOException) { }
            }
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (Gate)
            {
                if (s_writer == null || s_truncated) return;

                var line = new StringBuilder(message.Length + 32);
                line.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(Tag(type)).Append(' ').Append(message);
                bool important = type != LogType.Log;
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !string.IsNullOrEmpty(stackTrace))
                    line.Append('\n').Append(stackTrace.TrimEnd());

                try
                {
                    if (s_written + line.Length > MaxBytes)
                    {
                        s_writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} W [SessionLogFile] Достигнут предел {MaxBytes / (1024 * 1024)} МБ — дальше лог не пишется.");
                        s_writer.Flush();
                        s_truncated = true;
                        return;
                    }

                    s_writer.WriteLine(line.ToString());
                    s_written += line.Length + 1;

                    if (important || SinceFlush.Elapsed.TotalSeconds >= FlushSeconds)
                    {
                        s_writer.Flush();
                        SinceFlush.Restart();
                    }
                }
                catch (IOException)
                {
                    // Диск переполнен или файл отняли — лог в файл прекращается, игра продолжает.
                    s_truncated = true;
                }
            }
        }

        private static string Tag(LogType type)
        {
            switch (type)
            {
                case LogType.Warning: return "W";
                case LogType.Error: return "E";
                case LogType.Exception: return "X";
                case LogType.Assert: return "A";
                default: return "I";
            }
        }

        private static void Flush()
        {
            lock (Gate)
            {
                try { s_writer?.Flush(); }
                catch (IOException) { }
            }
        }

        // Quest при снятии шлема/выходе в меню ставит приложение на паузу и может выгрузить его без quitting.
        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }
    }
}
