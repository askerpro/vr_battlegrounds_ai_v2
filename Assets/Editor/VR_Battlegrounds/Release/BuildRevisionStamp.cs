using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using UnityEditor;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>
    /// Штамп ревизии исходников в версии приложения на время одной сборки:
    /// <c>PlayerSettings.bundleVersion = &lt;база&gt;+&lt;ревизия&gt;</c>. Сборка видит его как
    /// <c>Application.version</c> (на Android — versionName), e2e-харнесс отдаёт его в результате
    /// (<c>appVersion</c>), и дирижёр Tools/e2e/Run-QuestE2E.ps1 сверяет APK и сервер одной ревизии.
    ///
    /// <para>
    /// <see cref="Dispose" /> возвращает исходную версию и сохраняет проектные настройки: Unity пишет
    /// Player Settings на диск в начале каждой сборки, и без сохранения в ProjectSettings.asset остался бы
    /// штамп. Только для тестовых и e2e-сборок — прод-версию магазина не трогаем.
    /// </para>
    /// </summary>
    public sealed class BuildRevisionStamp : IDisposable
    {
        /// <summary>Ревизия, когда git недоступен или не ответил.</summary>
        public const string Unknown = "unknown";

        /// <summary>Суффикс ревизии при изменённых отслеживаемых файлах в Assets/, Packages/, ProjectSettings/.</summary>
        public const string DirtySuffix = "-dirty";

        private const int GitTimeoutMs = 15000;

        private readonly string _original;

        /// <summary>Ревизия штампа.</summary>
        public string Revision { get; }

        /// <summary>Версия на время сборки.</summary>
        public string StampedVersion { get; }

        public BuildRevisionStamp(string revision)
        {
            Revision = string.IsNullOrEmpty(revision) ? Unknown : revision;
            _original = PlayerSettings.bundleVersion;
            StampedVersion = Compose(_original, Revision);
            PlayerSettings.bundleVersion = StampedVersion;
        }

        public void Dispose()
        {
            PlayerSettings.bundleVersion = _original;
            AssetDatabase.SaveAssets();
        }

        /// <summary>Версия со штампом: прежний суффикс <c>+…</c> отбрасывается.</summary>
        public static string Compose(string version, string revision)
        {
            string baseVersion = version ?? string.Empty;
            int plus = baseVersion.IndexOf('+');
            if (plus >= 0) baseVersion = baseVersion.Substring(0, plus);
            return baseVersion + "+" + revision;
        }

        /// <summary>
        /// Ревизия корня проекта: <c>git rev-parse --short=12 HEAD</c> и <c>-dirty</c>, если по отслеживаемым
        /// файлам Assets/, Packages/, ProjectSettings/ есть изменения. Без git — <see cref="Unknown" />
        /// и причина в <paramref name="warning" />; сборку это не останавливает.
        /// </summary>
        public static string ReadRevision(out string warning)
        {
            warning = null;
            string root = Directory.GetCurrentDirectory();

            if (!RunGit(root, "rev-parse --short=12 HEAD", out string head, out string error) || head.Length == 0)
            {
                warning = "ревизия сборки не определена (git rev-parse: " + error + ") — штамп " + Unknown;
                return Unknown;
            }

            if (!RunGit(root, "status --porcelain -uno -- Assets Packages ProjectSettings", out string status, out error))
            {
                warning = "не удалось проверить изменения рабочей копии (git status: " + error + ") — ревизия без признака -dirty";
                return head;
            }

            return status.Length > 0 ? head + DirtySuffix : head;
        }

        private static bool RunGit(string root, string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = string.Empty;

            var startInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        error = "процесс не запустился";
                        return false;
                    }

                    // Чтение асинхронно: полный буфер stdout иначе заблокировал бы git до таймаута.
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(GitTimeoutMs))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        error = "таймаут " + GitTimeoutMs / 1000 + " с";
                        return false;
                    }

                    output = stdout.Wait(2000) ? stdout.Result.Trim() : string.Empty;
                    string stderrText = stderr.Wait(2000) ? stderr.Result.Trim() : string.Empty;

                    if (process.ExitCode != 0)
                    {
                        error = "код " + process.ExitCode + (stderrText.Length > 0 ? ": " + stderrText : "");
                        return false;
                    }

                    return true;
                }
            }
            catch (Win32Exception e)
            {
                error = "git не найден: " + e.Message;
                return false;
            }
            catch (InvalidOperationException e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
