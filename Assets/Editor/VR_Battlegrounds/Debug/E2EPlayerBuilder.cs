using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
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
    /// дирижёр прогона на шлеме сверяет его с APK. XR на старте выключен на время сборки
    /// (<see cref="XrBuildSettingsScope" />, профиль Server), как у Dedicated Server в GameBuilder.
    ///
    /// Вызов:
    ///   — из редактора: Tools/VR Battlegrounds/Debug/Собрать e2e-плеер (Windows)
    ///   — агентом:      E2EPlayerBuilder.Run()
    ///   — из CI:        -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch
    ///                   (только при закрытом редакторе — проект залочен Library/)
    ///
    /// Пара для прогона на шлеме (APK Quest/Test + e2e-плеер сервера одной ревизии), Quest — только
    /// из редактора, скомпилированного под Android:
    ///   — агентом в открытом редакторе: E2EPlayerBuilder.StartPairBuild(путь статуса) — сразу
    ///     возвращает, сборка идёт в EditorApplication.update (если редактор не на Android — после
    ///     переключения и перекомпиляции), итог — JSON в файле статуса
    ///   — из CLI: Unity с -buildTarget Android, -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunPairBatch
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

        /// <summary>Задание пары, ждущее перекомпиляции под Android: путь статуса (SessionState переживает перезагрузку домена).</summary>
        private const string PairJobKey = "VrBattlegrounds.E2EPlayerBuilder.PairJob";

        /// <summary>Сколько перезагрузок домена задание уже ждало Android.</summary>
        private const string PairJobAttemptsKey = "VrBattlegrounds.E2EPlayerBuilder.PairJobAttempts";

        /// <summary>Столько перезагрузок без готового Android — и задание снимается с ошибкой.</summary>
        private const int MaxResumeAttempts = 3;

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

        /// <summary>
        /// Точка входа для -executeMethod (Windows e2e-плеер): код возврата 0 или 1.
        /// Необязательные аргументы: <c>-buildRevision &lt;ревизия&gt;</c>, <c>-serverStatus &lt;путь&gt;</c>.
        /// </summary>
        public static void RunBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string revisionArg = null;
            string statusArg = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildRevision") revisionArg = args[i + 1];
                if (args[i] == "-serverStatus") statusArg = args[i + 1];
            }

            Result result = revisionArg != null ? Run(revisionArg) : Run();
            if (!string.IsNullOrEmpty(statusArg))
            {
                string statusPath = ResolveStatusPath(statusArg);
                var status = new PairStatus
                {
                    serverPassed = result.Passed,
                    revision = result.Revision,
                    serverSummary = result.Summary,
                    serverPath = result.Path,
                    error = result.Passed ? string.Empty : result.Message,
                    durationSeconds = Math.Round(result.Seconds)
                };
                WriteStatusFile(statusPath, JsonUtility.ToJson(status, true));
            }

            Debug.Log(result.Summary);
            EditorApplication.Exit(result.Passed ? 0 : 1);
        }

        /// <summary>
        /// Точка входа для -executeMethod в выделенном Android сборочном worktree (<c>build-test-android</c>):
        /// собирает только APK Quest (<see cref="BuildConfig.Test" />) без переключения платформы на Windows.
        /// Необязательные аргументы: <c>-buildRevision &lt;ревизия&gt;</c>, <c>-pairStatus &lt;путь&gt;</c>
        /// (или <c>-questStatus &lt;путь&gt;</c>).
        /// </summary>
        public static void RunQuestBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string revisionArg = null;
            string statusArg = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-buildRevision") revisionArg = args[i + 1];
                if (args[i] == "-pairStatus" || args[i] == "-questStatus") statusArg = args[i + 1];
            }

            string path = ResolveStatusPath(statusArg);
            DateTime started = DateTime.UtcNow;

            if (!QuestManifestValidator.OculusManifestCompiled())
            {
                var refused = new PairStatus
                {
                    error = "editor-скрипты скомпилированы не под Android (нет OculusManifest): Quest вышел бы плоским — " +
                            "запускай Unity с -buildTarget Android"
                };
                WriteStatusFile(path, JsonUtility.ToJson(refused, true));
                LogPair(refused, path);
                EditorApplication.Exit(1);
                return;
            }

            WriteStatusFile(path, string.Empty);

            string revision = revisionArg ?? BuildRevisionStamp.ReadRevision(out _);
            GameBuilder.Result quest = GameBuilder.Build(new[] { BuildProfile.Quest }, BuildConfig.Test, revision);
            var status = new PairStatus
            {
                questPassed = quest.Passed,
                revision = quest.Revision.Length > 0 ? quest.Revision : (revision ?? string.Empty),
                questSummary = quest.Summary,
                apkPath = GameBuilder.OutputPathFor(BuildProfile.Quest),
                error = quest.Passed ? string.Empty : "APK Quest не собран",
                durationSeconds = Math.Round((DateTime.UtcNow - started).TotalSeconds)
            };
            WriteStatusFile(path, JsonUtility.ToJson(status, true));
            if (quest.Passed) Debug.Log(quest.Summary);
            else Debug.LogError(quest.Summary);
            EditorApplication.Exit(quest.Passed ? 0 : 1);
        }

        // ── Пара для прогона на шлеме ──────────────────────────────────

        /// <summary>
        /// Ставит сборку пары (APK Quest/Test, затем e2e-плеер) на кадр редактора через одноразовый
        /// <c>EditorApplication.update</c> и сразу возвращает: вызов MCP не ждёт сборку. Не <c>delayCall</c>:
        /// это общий многоадресный делегат, исключение чужого обработчика сбрасывает остальные, и сборка
        /// молча не стартует (наблюдалось на worker 2026-10-12).
        ///
        /// <para>
        /// Quest собирается только редактором, скомпилированным под Android (<c>OculusManifest</c> под
        /// <c>#if UNITY_ANDROID</c>, см. <see cref="QuestManifestValidator.OculusManifestCompiled" />). Если
        /// редактор на другой платформе, задание запоминается в <see cref="SessionState" />, платформа
        /// переключается на Android, и сборка продолжается после перекомпиляции и перезагрузки домена
        /// (<see cref="ResumePairBuild" />).
        /// </para>
        ///
        /// Файл статуса перезаписывается пустым до старта и остаётся пустым и на время переключения:
        /// пустой — сборка идёт, JSON <see cref="PairStatus" /> — закончена. Возвращает строку с путём статуса.
        /// </summary>
        public static string StartPairBuild(string statusPath)
        {
            string path = ResolveStatusPath(statusPath);

            if (_pairBuildQueued || SessionState.GetString(PairJobKey, string.Empty).Length > 0)
                return ResultMarker + " сборка пары уже идёт — статус не тронут; жди её файл статуса";

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteStatusFile(path, JsonUtility.ToJson(new PairStatus { error = "редактор в Play Mode — выйди из него и повтори" }, true));
                return ResultMarker + " FAIL — редактор в Play Mode; статус: " + path;
            }

            WriteStatusFile(path, string.Empty);
            SessionState.SetString(PairJobKey, path);
            SessionState.SetInt(PairJobAttemptsKey, 0);

            if (EditorReadyForQuest())
            {
                QueuePairBuild(path);
                return ResultMarker + " сборка пары поставлена в очередь; статус: " + path;
            }

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                // Платформа уже Android, а домен без UNITY_ANDROID: компиляция не закончилась или упала.
                // Переключать нечего, перезагрузки домена может не быть — не ждём её.
                FailPairJob(path, "активная платформа Android, но editor-скрипты без UNITY_ANDROID (нет OculusManifest): " +
                                  "дождись окончания компиляции или исправь ошибки компиляции и повтори");
                return ResultMarker + " FAIL — editor-скрипты не под Android; статус: " + path;
            }

            if (!SwitchToAndroid(path))
                return ResultMarker + " FAIL — не удалось переключиться на Android; статус: " + path;

            Debug.Log(ResultMarker + " переключаю на Android, сборка пары продолжится после перекомпиляции; статус: " + path);
            return ResultMarker + " переключаю на Android, сборка продолжится после перекомпиляции; статус: " + path;
        }

        /// <summary>
        /// Продолжение <see cref="StartPairBuild" /> после перезагрузки домена. Редактор на Android с
        /// <c>OculusManifest</c> — сборка ставится в очередь. Иначе попытка засчитывается: до
        /// <see cref="MaxResumeAttempts" /> переключение запрашивается снова (перезагрузка могла прийти раньше
        /// него), потом — ошибка в статус и задание снимается.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ResumePairBuild()
        {
            string path = SessionState.GetString(PairJobKey, string.Empty);
            if (path.Length == 0) return;

            if (EditorReadyForQuest())
            {
                QueuePairBuild(path);
                return;
            }

            int attempts = SessionState.GetInt(PairJobAttemptsKey, 0) + 1;
            SessionState.SetInt(PairJobAttemptsKey, attempts);

            if (attempts >= MaxResumeAttempts)
            {
                FailPairJob(path, "после " + attempts + " перезагрузок домена редактор не готов к сборке Quest: платформа " +
                                  EditorUserBuildSettings.activeBuildTarget + ", OculusManifest " +
                                  (QuestManifestValidator.OculusManifestCompiled() ? "есть" : "нет") +
                                  ". Переключи платформу на Android вручную, дождись компиляции и повтори");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                SwitchToAndroid(path);
            else
                CompilationPipeline.assemblyCompilationFinished += OnSwitchAssemblyCompiled;
        }

        /// <summary>
        /// Точка входа для -executeMethod: пара синхронно, код возврата 0 или 1.
        /// Аргумент <c>-pairStatus путь</c>, по умолчанию <see cref="DefaultPairStatusPath" />.
        /// Unity должен стартовать с <c>-buildTarget Android</c>: иначе editor-скрипты без UNITY_ANDROID
        /// и Quest вышел бы плоским — тогда сразу код 1.
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

            if (!QuestManifestValidator.OculusManifestCompiled())
            {
                var refused = new PairStatus
                {
                    error = "editor-скрипты скомпилированы не под Android (нет OculusManifest): Quest вышел бы плоским — " +
                            "запускай Unity с -buildTarget Android"
                };
                WriteStatusFile(path, JsonUtility.ToJson(refused, true));
                LogPair(refused, path);
                EditorApplication.Exit(1);
                return;
            }

            WriteStatusFile(path, string.Empty);

            PairStatus status = BuildPair();
            WriteStatusFile(path, JsonUtility.ToJson(status, true));
            LogPair(status, path);
            EditorApplication.Exit(status.Passed ? 0 : 1);
        }

        private static bool EditorReadyForQuest() =>
            EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android && QuestManifestValidator.OculusManifestCompiled();

        /// <summary>
        /// Переключение на следующем кадре редактора (<c>Async</c>): вызов MCP не ждёт реимпорт. Ошибка
        /// компиляции под Android перезагрузки домена не даст — её ловит <see cref="OnSwitchAssemblyCompiled" />.
        /// </summary>
        private static bool SwitchToAndroid(string path)
        {
            CompilationPipeline.assemblyCompilationFinished -= OnSwitchAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnSwitchAssemblyCompiled;

            if (EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android)) return true;

            FailPairJob(path, "не удалось переключиться на Android — модуль платформы установлен?");
            return false;
        }

        /// <summary>Сборка с ошибками компиляции оставляет старый домен — продолжения не будет, задание снимается.</summary>
        private static void OnSwitchAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            if (!messages.Any(m => m.type == CompilerMessageType.Error)) return;

            string path = SessionState.GetString(PairJobKey, string.Empty);
            if (path.Length == 0) return;

            FailPairJob(path, "ошибки компиляции под Android в " + assembly + " — исправь их и повтори");
        }

        /// <summary>
        /// Одноразовый <c>EditorApplication.update</c>: ждёт конца компиляции и импорта, снимает задание и
        /// собирает пару. Перезагрузка домена до старта не теряет задание — его подхватит <see cref="ResumePairBuild" />.
        /// </summary>
        private static void QueuePairBuild(string path)
        {
            _pairBuildQueued = true;

            EditorApplication.CallbackFunction once = null;
            once = () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

                EditorApplication.update -= once;
                ClearPairJob();
                try
                {
                    PairStatus status = BuildPair();
                    WriteStatusFile(path, JsonUtility.ToJson(status, true));
                    LogPair(status, path);
                }
                catch (Exception error)
                {
                    WriteStatusFile(path, JsonUtility.ToJson(new PairStatus { error = error.GetType().Name + ": " + error.Message }, true));
                    Debug.LogError(ResultMarker + " сборка пары упала: " + error);
                }
                finally
                {
                    _pairBuildQueued = false;
                }
            };
            EditorApplication.update += once;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void FailPairJob(string path, string error)
        {
            ClearPairJob();
            var status = new PairStatus { error = error };
            WriteStatusFile(path, JsonUtility.ToJson(status, true));
            LogPair(status, path);
        }

        private static void ClearPairJob()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnSwitchAssemblyCompiled;
            SessionState.EraseString(PairJobKey);
            SessionState.EraseInt(PairJobAttemptsKey);
        }

        /// <summary>
        /// Клиент (APK Quest, конфигурация Test) и сервер (e2e-плеер Windows) одной ревизией: она считается
        /// один раз до обеих сборок. Quest первым — редактор уже под Android; ошибка Quest останавливает пару.
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

                GameBuilder.Result quest = GameBuilder.Build(new[] { BuildProfile.Quest }, BuildConfig.Test, revision);
                status.questPassed = quest.Passed;
                status.questSummary = quest.Summary;

                if (!quest.Passed)
                {
                    status.error = "APK Quest не собран — e2e-плеер сервера не собирался";
                    return status;
                }

                Result server = Run(revision);
                if (!string.IsNullOrEmpty(warning)) server.RevisionWarning = warning;
                status.serverPassed = server.Passed;
                status.serverSummary = server.Summary;

                if (!server.Passed) status.error = "e2e-плеер сервера не собран";
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
                // Платформа переключается явно и до XR-скоупа, как в GameBuilder: переключение реимпортирует
                // настройки XR с диска, и выставленное раньше в памяти пропало бы. Пара собирает плеер после
                // Quest, из редактора на Android. Editor-скрипты под Standalone перекомпилировать не нужно:
                // XR у этой сборки выключен, а Android-обработчики (#if UNITY_ANDROID) для Windows не зовутся.
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                {
                    result.Message = "не удалось переключиться на StandaloneWindows64";
                    return result;
                }

                // Штамп только на время BuildPlayer; Dispose возвращает версию и сохраняет настройки.
                // XR — как у Dedicated Server в GameBuilder: без InitManagerOnStart. Иначе плеер берёт от
                // Standalone Oculus для Link и при старте пытается поднять шлем — и сервером, и клиентом.
                // XR-скоуп внутри штампа: он возвращает настройки до SaveAssets штампа.
                using (var stamp = new BuildRevisionStamp(revision))
                using (new XrBuildSettingsScope(BuildProfile.Server))
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
