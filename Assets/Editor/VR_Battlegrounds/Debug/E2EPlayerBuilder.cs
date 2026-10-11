using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VrBattlegrounds.EditorTools.Release;

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
    /// Плеер получает штамп ревизии исходников в версии (<see cref="BuildRevisionStamp" />):
    /// дирижёр прогона на шлеме сверяет его с APK.
    ///
    /// Вызов:
    ///   — из редактора: Tools/VR Battlegrounds/Debug/Собрать e2e-плеер (Windows)
    ///   — агентом:      E2EPlayerBuilder.Run()
    ///   — из CI:        -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch
    ///                   (только при закрытом редакторе — проект залочен Library/)
    ///
    /// Пара для прогона на шлеме (e2e-плеер сервера + APK Quest/Test одной ревизии):
    ///   — агентом в открытом редакторе: E2EPlayerBuilder.StartPairBuild(путь статуса) — сразу
    ///     возвращает, сборка идёт в delayCall, итог — JSON в файле статуса
    ///   — из CLI: -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunPairBatch
    ///     [-pairStatus путь] (Tools/e2e/Run-QuestE2E.ps1 -Build)
    /// </summary>
    public static class E2EPlayerBuilder
    {
        /// <summary>Маркер в консоли, по которому агент находит результат.</summary>
        public const string ResultMarker = "[E2EPlayerBuilder]";

        /// <summary>Путь плеера. Дирижёр Tools/e2e/Run-E2E.ps1 ждёт его именно здесь.</summary>
        public const string OutputPath = "Build/e2e/VrBattlegrounds.exe";

        /// <summary>Файл статуса сборки пары по умолчанию.</summary>
        public const string DefaultPairStatusPath = "Build/e2e-pair-status.json";

        private static bool _pairBuildQueued;

        /// <summary>Результат сборки.</summary>
        public class Result
        {
            public bool Passed;
            public string Message = string.Empty;
            public string Path = string.Empty;
            public double Seconds;

            /// <summary>Штамп ревизии исходников в версии плеера.</summary>
            public string Revision = string.Empty;

            /// <summary>Почему ревизия не определена полностью; пусто — всё в порядке.</summary>
            public string RevisionWarning = string.Empty;

            public string Summary
            {
                get
                {
                    string revision = Revision.Length > 0 ? ", ревизия " + Revision : string.Empty;
                    string warning = RevisionWarning.Length > 0 ? "\nВНИМАНИЕ: " + RevisionWarning : string.Empty;

                    return (Passed
                        ? ResultMarker + " PASS — плеер собран за " + Seconds.ToString("F0") + " с" + revision + ": " + Path
                        : ResultMarker + " FAIL — " + Message + revision) + warning;
                }
            }
        }

        /// <summary>Статус сборки пары: JSON в файле статуса.</summary>
        [Serializable]
        public class PairStatus
        {
            public bool serverPassed;
            public bool questPassed;
            public string revision = string.Empty;
            public string serverSummary = string.Empty;
            public string questSummary = string.Empty;
            public string apkPath = string.Empty;
            public string serverPath = string.Empty;
            public string error = string.Empty;
            public double durationSeconds;

            public bool Passed => serverPassed && questPassed;
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
            return RunForScenes();
        }

        /// <summary>То же с заданной ревизией штампа (пара собирается одной ревизией).</summary>
        public static Result Run(string revision)
        {
            return Build(revision, Array.Empty<string>());
        }

        /// <summary>Automation включает только явно требуемые Debug-fixtures, остальные игровые сцены сохраняются.</summary>
        public static Result RunForScenes(params string[] requiredDebugScenes)
        {
            return Build(null, requiredDebugScenes);
        }

        /// <summary>Точка входа для -executeMethod: код возврата 0 или 1.</summary>
        public static void RunBatch()
        {
            Result result = Run();
            Debug.Log(result.Summary);
            EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        // ── Пара для прогона на шлеме ──────────────────────────────────

        /// <summary>
        /// Ставит сборку пары (e2e-плеер, затем Quest/Test) в <c>EditorApplication.delayCall</c> и сразу
        /// возвращает: вызов MCP не ждёт сборку. Файл статуса перезаписывается пустым до старта; пустой —
        /// сборка идёт, JSON <see cref="PairStatus" /> — закончена. Возвращает строку с полным путём статуса.
        /// </summary>
        public static string StartPairBuild(string statusPath)
        {
            string path = ResolveStatusPath(statusPath);

            if (_pairBuildQueued)
                return ResultMarker + " сборка пары уже идёт — статус не тронут; жди её файл статуса";

            WriteStatusFile(path, string.Empty);
            _pairBuildQueued = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    PairStatus status = BuildPair();
                    WriteStatusFile(path, JsonUtility.ToJson(status, true));
                    LogPair(status, path);
                }
                finally
                {
                    _pairBuildQueued = false;
                }
            };

            return ResultMarker + " сборка пары поставлена в очередь; статус: " + path;
        }

        /// <summary>
        /// Точка входа для -executeMethod: пара синхронно, код возврата 0 или 1.
        /// Аргумент <c>-pairStatus путь</c>, по умолчанию <see cref="DefaultPairStatusPath" />.
        /// </summary>
        public static void RunPairBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string statusArg = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-pairStatus") statusArg = args[i + 1];
            }

            string path = ResolveStatusPath(statusArg);
            WriteStatusFile(path, string.Empty);

            PairStatus status = BuildPair();
            WriteStatusFile(path, JsonUtility.ToJson(status, true));
            LogPair(status, path);
            EditorApplication.Exit(status.Passed ? 0 : 1);
        }

        /// <summary>
        /// Сервер (e2e-плеер Windows) и клиент (APK Quest, конфигурация Test) одной ревизией: она
        /// считается один раз до обеих сборок. Ошибка сервера останавливает пару.
        /// </summary>
        private static PairStatus BuildPair()
        {
            var status = new PairStatus
            {
                apkPath = GameBuilder.OutputPathFor(BuildProfile.Quest),
                serverPath = System.IO.Path.GetFullPath(OutputPath)
            };
            DateTime started = DateTime.UtcNow;

            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    status.error = "редактор в Play Mode — выйди из него и повтори";
                    return status;
                }

                string revision = BuildRevisionStamp.ReadRevision(out string warning);
                status.revision = revision;

                Result server = Run(revision);
                if (!string.IsNullOrEmpty(warning)) server.RevisionWarning = warning;
                status.serverPassed = server.Passed;
                status.serverSummary = server.Summary;

                if (!server.Passed)
                {
                    status.error = "e2e-плеер сервера не собран — APK не собирался";
                    return status;
                }

                GameBuilder.Result quest = GameBuilder.Build(new[] { BuildProfile.Quest }, BuildConfig.Test, revision);
                status.questPassed = quest.Passed;
                status.questSummary = quest.Summary;

                if (!quest.Passed) status.error = "APK Quest не собран";
            }
            catch (Exception e)
            {
                status.error = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                status.durationSeconds = Math.Round((DateTime.UtcNow - started).TotalSeconds);
            }

            return status;
        }

        private static string ResolveStatusPath(string statusPath) =>
            System.IO.Path.GetFullPath(string.IsNullOrEmpty(statusPath) ? DefaultPairStatusPath : statusPath);

        /// <summary>Запись через временный файл: читатель не увидит половину JSON.</summary>
        private static void WriteStatusFile(string path, string text)
        {
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static void LogPair(PairStatus status, string path)
        {
            string text = ResultMarker + (status.Passed ? " PAIR PASS" : " PAIR FAIL") + " — ревизия " + status.revision +
                          (status.error.Length > 0 ? ", " + status.error : string.Empty) + "; статус: " + path +
                          "\n" + status.serverSummary + "\n" + status.questSummary;

            if (status.Passed) Debug.Log(text);
            else Debug.LogError(text);
        }

        // ── Сборка плеера ──────────────────────────────────────────────

        /// <summary><paramref name="revision" /> null — посчитать ревизию здесь.</summary>
        private static Result Build(string revision, string[] requiredDebugScenes)
        {
            Result result = new Result();

            string[] scenes = BuildSceneResolver.Resolve(true, requiredDebugScenes);

            if (scenes.Length == 0)
            {
                result.Message = "в EditorBuildSettings нет включённых сцен";
                return result;
            }

            if (revision == null)
            {
                revision = BuildRevisionStamp.ReadRevision(out string warning);
                if (!string.IsNullOrEmpty(warning)) result.RevisionWarning = warning;
            }

            string absolutePath = System.IO.Path.GetFullPath(OutputPath);
            string directory = System.IO.Path.GetDirectoryName(absolutePath);
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
                // Штамп только на время BuildPlayer; Dispose возвращает версию и сохраняет настройки.
                using (var stamp = new BuildRevisionStamp(revision))
                {
                    result.Revision = stamp.Revision;
                    report = BuildPipeline.BuildPlayer(options);
                }
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
    }
}
