using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>Единый наблюдатель сборки: не меняет Git, настройки проекта или состояние сцен.</summary>
    public static class BuildProvenance
    {
        public sealed class SourceSnapshot
        {
            internal JObject Fields;
        }

        /// <summary>Снимается до временных записей BuildRevisionStamp и build scopes.</summary>
        public static SourceSnapshot CaptureSource()
        {
            string root = ProjectRoot;
            string sha = Git(root, "rev-parse HEAD", out string shaError)?.Trim();
            if (sha == null || sha.Length != 40 || sha.Any(c => !Uri.IsHexDigit(c)))
            {
                sha = null;
                shaError = shaError ?? "Git HEAD не вернул полный SHA из 40 hex символов";
            }

            string status = Git(root, "status --porcelain=v1 -z --untracked-files=all -- Assets Packages ProjectSettings Tools/release Tools/e2e", out string dirtyError);
            var entries = new JArray();
            if (status != null)
            {
                string[] records = status.Split('\0');
                for (int i = 0; i < records.Length; i++)
                {
                    string record = records[i];
                    if (record.Length < 4) continue;
                    string path = record.Substring(3).Replace('\\', '/');
                    var entry = new JObject { ["status"] = record.Substring(0, 2), ["path"] = path };
                    if (record[0] == 'R' || record[0] == 'C' || record[1] == 'R' || record[1] == 'C')
                        entry["originalPath"] = ++i < records.Length ? records[i].Replace('\\', '/') : null;
                    string absolute = Path.GetFullPath(Path.Combine(root, path));
                    try
                    {
                        entry["sha256"] = File.Exists(absolute) ? Hash(absolute) : null;
                        if (!File.Exists(absolute)) entry["reason"] = "Файл удалён или отсутствует в рабочей копии";
                    }
                    catch (Exception e)
                    {
                        entry["sha256"] = null;
                        entry["reason"] = e.GetType().Name + ": " + e.Message;
                    }
                    entries.Add(entry);
                }
            }
            return new SourceSnapshot
            {
                Fields = new JObject
                {
                    ["sourceSha"] = sha,
                    ["sourceShaReason"] = sha == null ? shaError : null,
                    ["sourceKind"] = "unknown",
                    ["sourceKindReason"] = "Подтверждённый accepted/checkpoint proof не передан; HEAD и short stamp не доказывают приёмку",
                    ["dirty"] = new JObject
                    {
                        ["value"] = status == null ? JValue.CreateNull() : new JValue(entries.Count > 0),
                        ["entries"] = entries,
                        ["reason"] = status == null ? dirtyError : null,
                        ["scope"] = "tracked и untracked Assets, Packages, ProjectSettings, Tools/release, Tools/e2e; ignored reports исключены Git"
                    }
                }
            };
        }

        private static readonly HashSet<string> _pendingStatusPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Attempt> _completedAttempts = new Dictionary<string, Attempt>(StringComparer.OrdinalIgnoreCase);

        public static string ValidateStatusPath(string statusPath)
        {
            if (string.IsNullOrWhiteSpace(statusPath))
                throw new InvalidOperationException("Путь статуса сборки пуст");
            string full = Path.GetFullPath(statusPath);
            string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "build.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Путь статуса зарезервирован для provenance manifest build.json: " + full);
            return full;
        }

        public static string BeginStatusWrite(string statusPath)
        {
            string full = ValidateStatusPath(statusPath);
            _pendingStatusPaths.Add(full);
            return full;
        }

        public static void EndPendingStatusWrite(string statusPath)
        {
            if (!string.IsNullOrWhiteSpace(statusPath))
                _pendingStatusPaths.Remove(Path.GetFullPath(statusPath));
        }

        public static void CompleteStatusWrite(string statusPath)
        {
            if (string.IsNullOrWhiteSpace(statusPath)) return;
            string full = Path.GetFullPath(statusPath);
            _pendingStatusPaths.Remove(full);
            if (!File.Exists(full)) return;

            string dir = Path.GetDirectoryName(full);
            while (!string.IsNullOrEmpty(dir))
            {
                if (_completedAttempts.TryGetValue(dir, out Attempt attempt))
                {
                    attempt.RefreshInventory();
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        public static Attempt Begin(string profile, string outputRoot, string configuration, SourceSnapshot source = null) =>
            new Attempt(profile, outputRoot, configuration, source ?? CaptureSource());

        public sealed class Attempt
        {
            private SourceSnapshot _source;
            private readonly string _outputRoot;
            private readonly JObject _actual = new JObject();
            private readonly JObject _reasons = new JObject();
            private readonly JObject _terminal;
            private readonly string _profile;
            private bool _reportSucceeded;
            private bool _finished;
            private bool _passed;

            internal Attempt(string profile, string outputRoot, string configuration, SourceSnapshot source)
            {
                _profile = profile;
                _source = source;
                _outputRoot = Path.GetFullPath(outputRoot);
                _completedAttempts.Remove(_outputRoot);
                _terminal = new JObject { ["startedUtc"] = Utc(DateTime.UtcNow), ["endedUtc"] = null, ["result"] = null };
                foreach (string field in new[] { "developmentBuild", "scenePaths", "buildGuid", "appVersion", "unityVersion", "companyName", "productName", "buildTarget", "subtarget", "scriptingBackend", "graphicsApis", "buildOptions" })
                {
                    _actual[field] = null;
                    _reasons[field] = "Сборка не дошла до наблюдения применённых настроек/BuildReport";
                }
                _actual["configuration"] = configuration;
                _actual["unavailableReasons"] = _reasons;
            }

            /// <summary>Обновляет снимок исходников после постоянной предполётной нормализации до временных scopes.</summary>
            public void UpdateSource(SourceSnapshot source)
            {
                if (_finished) throw new InvalidOperationException("Build provenance уже завершён");
                _source = source ?? throw new ArgumentNullException(nameof(source));
            }

            /// <summary>Вызывается внутри всех применённых scopes непосредственно перед BuildPlayer.</summary>
            public void Capture(BuildPlayerOptions options, NamedBuildTarget namedTarget)
            {
                Observe("unityVersion", () => Application.unityVersion);
                Observe("unityEditor", () => FileRecord(EditorApplication.applicationPath, EditorApplication.applicationPath.Replace('\\', '/')));
                Observe("appVersion", () => PlayerSettings.bundleVersion);
                Observe("companyName", () => PlayerSettings.companyName);
                Observe("productName", () => PlayerSettings.productName);
                Observe("applicationIdentifier", () => PlayerSettings.GetApplicationIdentifier(namedTarget));
                Observe("buildTarget", () => EditorUserBuildSettings.activeBuildTarget.ToString());
                Observe("developmentBuild", () => (options.options & BuildOptions.Development) != 0);
                Observe("buildOptions", () => options.options.ToString());
                Observe("scriptingBackend", () => PlayerSettings.GetScriptingBackend(namedTarget).ToString());
                Observe("graphicsApis", () => PlayerSettings.GetGraphicsAPIs(options.target).Select(api => api.ToString()).ToArray());
                Observe("useDefaultGraphicsApis", () => PlayerSettings.GetUseDefaultGraphicsAPIs(options.target));
                Observe("scenePaths", () => options.scenes.Select(CanonicalScene).ToArray());
                Observe("requestedSubtarget", () => options.subtarget);
                Observe("configuredStandaloneSubtarget", () => EditorUserBuildSettings.standaloneBuildSubtarget.ToString());
                Observe("extraScriptingDefines", () => options.extraScriptingDefines ?? Array.Empty<string>());
                Observe("defines", () => PlayerSettings.GetScriptingDefineSymbols(namedTarget));
                Observe("il2cppCompilerConfiguration", () => PlayerSettings.GetIl2CppCompilerConfiguration(namedTarget).ToString());
                Observe("il2cppCodeGeneration", () => PlayerSettings.GetIl2CppCodeGeneration(namedTarget).ToString());
                Observe("managedStrippingLevel", () => PlayerSettings.GetManagedStrippingLevel(namedTarget).ToString());
                Observe("packages", () => Fingerprint(new[] { "Packages/manifest.json", "Packages/packages-lock.json" }));
                Observe("config", () => Fingerprint(Directory.GetFiles(Path.Combine(ProjectRoot, "ProjectSettings"), "*", SearchOption.TopDirectoryOnly).Select(RelativeToProject)));
                Observe("tools", () => Fingerprint(new[]
                {
                    "Assets/Editor/VR_Battlegrounds/Release/BuildProvenance.cs",
                    "Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs",
                    "Assets/Editor/VR_Battlegrounds/Debug/E2EPlayerBuilder.cs",
                    "Assets/Editor/VR_Battlegrounds/Release/BuildConfigScope.cs",
                    "Assets/Editor/VR_Battlegrounds/Release/BuildRevisionStamp.cs",
                    "Assets/Editor/VR_Battlegrounds/Release/BuildSceneResolver.cs",
                    "Assets/Editor/VR_Battlegrounds/Release/XrBuildSettingsScope.cs",
                    "Tools/release/Build-Game.ps1", "Tools/release/BuildCheckoutGenerated.ps1",
                    "Tools/e2e/Run-E2E.ps1", "Tools/e2e/Run-QuestE2E.ps1"
                }));
                _actual["capturedUtc"] = Utc(DateTime.UtcNow);
            }

            /// <summary>Сохраняет результат до восстановления scopes, не выводит успех из существующего файла.</summary>
            public void RecordReport(BuildReport report)
            {
                if (report == null)
                {
                    _terminal["result"] = null;
                    _terminal["reason"] = "BuildPipeline вернул null BuildReport";
                    return;
                }
                BuildSummary summary = report.summary;
                _reportSucceeded = summary.result == BuildResult.Succeeded;
                _terminal["result"] = summary.result.ToString();
                _terminal["reportStartedUtc"] = Utc(summary.buildStartedAt.ToUniversalTime());
                _terminal["reportEndedUtc"] = Utc(summary.buildEndedAt.ToUniversalTime());
                _terminal["durationSeconds"] = summary.totalTime.TotalSeconds;
                _terminal["totalErrors"] = summary.totalErrors;
                _terminal["totalWarnings"] = summary.totalWarnings;
                Observe("buildGuid", () => summary.guid.ToString());
                Observe("buildTarget", () => summary.platform.ToString());
                Observe("buildOptions", () => summary.options.ToString());
                Observe("developmentBuild", () => (summary.options & BuildOptions.Development) != 0);
                Observe("subtarget", () => summary.GetType().GetProperty("subtarget")?.GetValue(summary) ?? throw new InvalidOperationException("BuildSummary не предоставляет subtarget"));
            }

            /// <summary>Даже failed attempt заменяет прежний manifest; старые outputs не становятся доказательством успеха.</summary>
            public bool Finish(bool success, string failureReason = null)
            {
                if (_finished) throw new InvalidOperationException("Build provenance уже завершён");
                _finished = true;
                _passed = success && _reportSucceeded;
                WriteCurrentState(failureReason);
                if (_passed) _completedAttempts[_outputRoot] = this;
                else _completedAttempts.Remove(_outputRoot);
                return _passed;
            }

            internal void RefreshInventory()
            {
                if (!_finished || !_passed) return;
                WriteCurrentState(null);
                if (!_passed)
                {
                    _completedAttempts.Remove(_outputRoot);
                    throw new InvalidOperationException((string)_terminal["reason"] ?? "Не удалось обновить inventory после записи статуса");
                }
            }

            private void WriteCurrentState(string failureReason)
            {
                var files = new JArray();
                if (_passed)
                {
                    try
                    {
                        foreach (string path in OutputFiles(_outputRoot).OrderBy(p => p, StringComparer.Ordinal))
                            files.Add(FileRecord(path, Relative(_outputRoot, path)));
                        if (files.Count == 0) throw new InvalidOperationException("Успешный BuildReport не оставил файлов в outputRoot");
                        _reasons.Remove("files");
                    }
                    catch (Exception e)
                    {
                        _passed = false;
                        files.Clear();
                        failureReason = "Inventory недоступен: " + e.Message;
                    }
                }
                if (!_passed) _reasons["files"] = failureReason ?? "Нет успешного текущего BuildReport и завершённой валидации; старые outputs не атрибутируются";
                _terminal["endedUtc"] = Utc(DateTime.UtcNow);
                if (!_passed) _terminal["reason"] = failureReason ?? (string)_terminal["reason"] ?? "Попытка не завершилась успешно";
                var record = new JObject
                {
                    ["profile"] = _profile, ["outputRoot"] = _outputRoot, ["files"] = files,
                    ["actual"] = _actual, ["terminal"] = _terminal, ["success"] = _passed
                };
                JObject manifest = (JObject)_source.Fields.DeepClone();
                manifest["schemaVersion"] = 1;
                manifest["profiles"] = new JArray(record);
                Directory.CreateDirectory(_outputRoot);
                string pathToManifest = Path.Combine(_outputRoot, "build.json");
                string temporary = pathToManifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, manifest.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
                    if (File.Exists(pathToManifest)) File.Replace(temporary, pathToManifest, null);
                    else File.Move(temporary, pathToManifest);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        try { File.Delete(temporary); } catch { }
                    }
                }
            }

            private void Observe(string field, Func<object> read)
            {
                try
                {
                    object value = read();
                    if (value == null) throw new InvalidOperationException("Наблюдаемое значение отсутствует");
                    _actual[field] = JToken.FromObject(value);
                    _reasons.Remove(field);
                }
                catch (Exception e)
                {
                    _actual[field] = null;
                    _reasons[field] = e.GetType().Name + ": " + e.Message;
                }
            }
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Utc(DateTime time) => time.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        private static string RelativeToProject(string path) => Relative(ProjectRoot, Path.GetFullPath(path));
        private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
        private static string CanonicalScene(string path)
        {
            string scene = RelativeToProject(Path.GetFullPath(Path.Combine(ProjectRoot, path)));
            if (!scene.StartsWith("Assets/", StringComparison.Ordinal) || !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Сцена не является canonical Assets/...unity: " + path);
            return scene;
        }

        private static JObject Fingerprint(IEnumerable<string> paths)
        {
            var files = new JArray();
            foreach (string relative in paths.Distinct().OrderBy(p => p, StringComparer.Ordinal))
            {
                string path = Path.Combine(ProjectRoot, relative);
                if (!File.Exists(path)) throw new FileNotFoundException("Файл отпечатка отсутствует", relative);
                files.Add(FileRecord(path, relative.Replace('\\', '/')));
            }
            string canonical = files.ToString(Formatting.None);
            using (var sha = SHA256.Create())
                return new JObject { ["sha256"] = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))), ["files"] = files };
        }

        private static JObject FileRecord(string absolute, string relative) => new JObject
        {
            ["path"] = relative, ["bytes"] = new FileInfo(absolute).Length, ["sha256"] = Hash(absolute)
        };

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static IEnumerable<string> OutputFiles(string root, string manifestPath = null)
        {
            manifestPath = manifestPath ?? Path.GetFullPath(Path.Combine(root, "build.json"));
            foreach (string path in Directory.GetFileSystemEntries(root))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Inventory не следует через reparse point: " + path);
                if (Directory.Exists(path))
                {
                    foreach (string child in OutputFiles(path, manifestPath)) yield return child;
                }
                else
                {
                    string full = Path.GetFullPath(path);
                    if (!string.Equals(full, manifestPath, StringComparison.OrdinalIgnoreCase) &&
                        !_pendingStatusPaths.Contains(full))
                        yield return path;
                }
            }
        }

        private static string Git(string root, string arguments, out string error)
        {
            error = null;
            try
            {
                var info = new ProcessStartInfo("git", "-C \"" + root + "\" " + arguments)
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new IOException("Git не запущен");
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000))
                    {
                        process.Kill();
                        throw new TimeoutException("Read-only Git не завершился за 10 секунд");
                    }
                    string output = stdout.GetAwaiter().GetResult();
                    string diagnostics = stderr.GetAwaiter().GetResult();
                    if (process.ExitCode != 0) throw new IOException("Git exit " + process.ExitCode + ": " + diagnostics.Trim());
                    return output;
                }
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; return null; }
        }
    }
}
