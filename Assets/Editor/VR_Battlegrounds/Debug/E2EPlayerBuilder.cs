using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Сборка плеера под Windows для яруса C (два процесса) из Docs/testing.md.
    ///
    /// Один и тот же плеер работает и сервером, и клиентом: роль выбирает
    /// GameNetworkDiscovery по Mirror.Utils.IsHeadless(), то есть по наличию
    /// графического устройства. Поэтому сервер запускается с -batchmode -nographics,
    /// а клиенты — обычным окном.
    ///
    /// Вызов:
    ///   — из редактора: Tools/VR Battlegrounds/Debug/Собрать e2e-плеер (Windows)
    ///   — агентом:      E2EPlayerBuilder.Run()
    ///   — из CI:        -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch
    ///                   (только при закрытом редакторе — проект залочен Library/)
    /// </summary>
    public static class E2EPlayerBuilder
    {
        /// <summary>Маркер в консоли, по которому агент находит результат.</summary>
        public const string ResultMarker = "[E2EPlayerBuilder]";

        /// <summary>Путь плеера. Дирижёр Tools/e2e/Run-E2E.ps1 ждёт его именно здесь.</summary>
        public const string OutputPath = "Build/e2e/VrBattlegrounds.exe";

        /// <summary>Результат сборки.</summary>
        public class Result
        {
            public bool Passed;
            public string Message = string.Empty;
            public string Path = string.Empty;
            public double Seconds;

            public string Summary
            {
                get
                {
                    return Passed
                        ? ResultMarker + " PASS — плеер собран за " + Seconds.ToString("F0") + " с: " + Path
                        : ResultMarker + " FAIL — " + Message;
                }
            }
        }

        [MenuItem("Tools/VR Battlegrounds/Debug/Собрать e2e-плеер (Windows)")]
        private static void RunFromMenu()
        {
            Result result = Run();

            if (result.Passed)
                Debug.Log(result.Summary);
            else
                Debug.LogError(result.Summary);
        }

        /// <summary>
        /// Собирает плеер. Сцены берутся из EditorBuildSettings — те же,
        /// что и в обычном билде (Offline, Lobby, TestMap1, TestMap2).
        /// </summary>
        public static Result Run()
        {
            Result result = new Result();

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                result.Message = "в EditorBuildSettings нет включённых сцен";
                return result;
            }

            string absolutePath = Path.GetFullPath(OutputPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = absolutePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Development даёт полные стектрейсы в -logFile: без них разбор
                // упавшего сценария превращается в гадание.
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            DateTime started = DateTime.UtcNow;
            BuildReport report;

            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                result.Message = "BuildPipeline.BuildPlayer выбросил " + e.GetType().Name + ": " + e.Message;
                return result;
            }

            result.Seconds = (DateTime.UtcNow - started).TotalSeconds;
            result.Path = absolutePath;

            if (report == null)
            {
                result.Message = "BuildPipeline.BuildPlayer вернул null-отчёт";
                return result;
            }

            BuildSummary summary = report.summary;
            result.Passed = summary.result == BuildResult.Succeeded;

            if (!result.Passed)
            {
                result.Message = "результат сборки " + summary.result +
                                 ", ошибок: " + summary.totalErrors +
                                 ", предупреждений: " + summary.totalWarnings;
            }

            return result;
        }

        /// <summary>Точка входа для -executeMethod: код возврата 0 или 1.</summary>
        public static void RunBatch()
        {
            Result result = Run();
            Debug.Log(result.Summary);
            EditorApplication.Exit(result.Passed ? 0 : 1);
        }
    }
}
