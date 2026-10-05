using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using VrBattlegrounds.Editor.Arsenal;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Захваченная команда: SHA в фоне, свежесть перед записью, общая аренда арсенала.</summary>
    internal sealed class AvatarActionPlan : IDisposable
    {
        internal readonly string Title, Scope;
        internal readonly string[] Paths;
        readonly string identity, workspace;
        readonly string instanceStamp;
        readonly string[] changedOutputs;
        readonly AvatarEditorContext target;
        readonly Func<string> writer;
        readonly Func<CancellationToken, Task<string>> backgroundWriter;
        readonly Func<string, string> completeWriter;
        readonly Action cleanupWriter;
        readonly Func<Dictionary<string, string>, string> validateSnapshot;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        Task<Dictionary<string, string>> task;
        Task<string> writing;
        bool cleanupDone;
        DateTime nextLeaseCheck, nextRenew;
        Dictionary<string, string> baseline;
        bool applying, disposed;
        ArsenalEditorActions.Lease lease;
        internal string Status { get; private set; }
        internal bool Ready => baseline != null && !applying && !disposed;
        internal bool Busy => task != null || writing != null;
        internal bool Finished { get; private set; }
        internal bool Passed { get; private set; }
        internal static string BlockReason => ArsenalEditorActions.BlockReason;
        internal static string LockStatus => ArsenalEditorActions.LockStatus;

        internal AvatarActionPlan(AvatarEditorContext context, string title, string scope, string[] paths, Func<string> action,
            Func<Dictionary<string, string>, string> snapshotValidation = null,
            Func<CancellationToken, Task<string>> background = null, Func<string, string> completion = null, Action cleanup = null, string[] changingPaths = null)
        {
            target = context; identity = context?.Identity; Title = title; Scope = scope; writer = action; validateSnapshot = snapshotValidation;
            backgroundWriter = background; completeWriter = completion; cleanupWriter = cleanup;
            instanceStamp = context != null && !context.IsAsset ? InstanceStamp(context) : null;
            workspace = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            changedOutputs = (changingPaths ?? Array.Empty<string>()).Select(p => Path.GetFullPath(Path.Combine(workspace, p))).ToArray();
            Paths = paths.Concat(new[] { "Packages/manifest.json", "Packages/packages-lock.json" }).Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p == "Packages/manifest.json" || p == "Packages/packages-lock.json" ? p : AvatarScopedActions.SnapshotPath(p))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (string path in Paths)
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("ProjectSettings/", StringComparison.Ordinal)
                    && !path.StartsWith("Packages/", StringComparison.Ordinal) && !Path.IsPathRooted(path))
                    throw new InvalidOperationException("Недопустимая область команды: " + path);
            Status = "Снимаю SHA входов, выходов и .meta…";
            task = Task.Run(() => ArsenalFileSnapshot.Read(workspace, Paths, cancel.Token));
        }

        internal void Poll(AvatarEditorContext current)
        {
            if (writing != null)
            {
                if (DateTime.UtcNow >= nextLeaseCheck)
                {
                    try
                    {
                        lease.RequireActive(); RequireTarget(current);
                        if (DateTime.UtcNow >= nextRenew) { lease.Renew(); nextRenew = DateTime.UtcNow.AddMinutes(1); }
                        nextLeaseCheck = DateTime.UtcNow.AddSeconds(1);
                    }
                    catch (Exception e)
                    {
                        cancel.Cancel(); try { writing.GetAwaiter().GetResult(); } catch { }
                        writing = null; Status = e.GetBaseException().Message; Passed = false;
                        try { Cleanup(); } finally { lease?.Dispose(); lease = null; Finished = true; applying = false; }
                        return;
                    }
                }
                if (!writing.IsCompleted) return;
                if (task == null)
                {
                    var inputs = Paths.Where(p => !IsOutput(Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(workspace, p)))).ToArray();
                    task = Task.Run(() => ArsenalFileSnapshot.Read(workspace, inputs, cancel.Token));
                    Status = "Повторная проверка входов после Blender…"; return;
                }
                if (!task.IsCompleted) return;
                try
                {
                    var snapshot = task.GetAwaiter().GetResult(); task = null;
                    var expected = baseline.Where(pair => !IsOutput(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                    if (!Equal(expected, snapshot)) throw new InvalidOperationException("Входы изменились во время Blender. Завершение pipeline остановлено.");
                    string result = writing.GetAwaiter().GetResult();
                    lease.RequireActive(); RequireTarget(current);
                    ArsenalEditorActions.RequireSavedAssets(true); ArsenalEditorActions.RequireCleanAssets(Paths);
                    Status = completeWriter != null ? completeWriter(result) : result; Passed = true;
                }
                catch (Exception e) { Status = e.GetBaseException().Message + "\nОперация остановлена; проверьте отдельную выходную копию."; Passed = false; }
                finally { writing = null; try { Cleanup(); } finally { lease?.Dispose(); lease = null; Finished = true; applying = false; } }
                return;
            }
            if (task == null || !task.IsCompleted) return;
            var completed = task; task = null;
            try
            {
                var snapshot = completed.GetAwaiter().GetResult();
                if (disposed) return;
                RequireTarget(current);
                string violation = validateSnapshot?.Invoke(snapshot);
                if (violation != null) throw new InvalidOperationException(violation);
                if (!applying)
                {
                    baseline = snapshot; Status = "План готов. Проверьте цель и область записи."; return;
                }
                lease.RequireActive();
                ArsenalEditorActions.RequireSavedAssets(true);
                ArsenalEditorActions.RequireCleanAssets(Paths);
                if (!Equal(baseline, snapshot)) throw new InvalidOperationException("Входы или выходы изменились. Подготовьте новый план.");
                // Unity API и writer выполняются только здесь, в editor update на главном потоке.
                if (backgroundWriter != null)
                {
                    writing = backgroundWriter(cancel.Token); Status = "Blender обрабатывает отдельную копию FBX…";
                }
                else { Status = writer(); Passed = true; }
            }
            catch (Exception e)
            {
                Status = e.GetBaseException().Message + (applying ? "\nКоманда остановлена. При ошибке writer возможны частичные изменения в указанной области; откат автоматически не выполняется." : "");
                Passed = false;
            }
            finally
            {
                if (writing == null && (applying || disposed || baseline == null)) { try { Cleanup(); } finally { lease?.Dispose(); lease = null; Finished = true; applying = false; } }
            }
        }

        internal void Apply(AvatarEditorContext current)
        {
            if (!Ready) throw new InvalidOperationException("План ещё не готов.");
            RequireTarget(current);
            ArsenalEditorActions.RequireSavedAssets(true);
            ArsenalEditorActions.RequireCleanAssets(Paths);
            lease = ArsenalEditorActions.AcquireLease("Аватар: " + Title);
            applying = true; Status = "Повторная проверка SHA под общей арендой…";
            task = Task.Run(() => ArsenalFileSnapshot.Read(workspace, Paths, cancel.Token));
        }

        void RequireTarget(AvatarEditorContext current)
        {
            if (target != null && (!target.IsValid || current == null || current.Identity != identity))
                throw new InvalidOperationException("Цель изменена или уничтожена. Подготовьте новый план.");
            if (instanceStamp != null && InstanceStamp(target) != instanceStamp)
                throw new InvalidOperationException("Рабочий экземпляр изменился после подготовки. Подготовьте новый план.");
        }
        static string InstanceStamp(AvatarEditorContext context) => string.Join("\n", context.Root.GetComponentsInChildren<UnityEngine.Component>(true)
            .Where(c => c).Select(c => c.GetEntityId() + ":" + EditorJsonUtility.ToJson(c)));
        bool IsOutput(string path) => changedOutputs.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase) || string.Equals(path, p + ".meta", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        internal static bool Equal(Dictionary<string, string> a, Dictionary<string, string> b) =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && pair.Value == value);

        internal string SaveReport()
        {
            if (!Finished || lease != null) throw new InvalidOperationException("Отчёт сохраняется после завершения операции и release.");
            string directory = Path.Combine(workspace, "tmp", "avatar-editor-workbench");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { Title, Scope, Paths, Passed, Status }, Newtonsoft.Json.Formatting.Indented));
            return path;
        }

        public void Dispose()
        {
            disposed = true; cancel.Cancel();
            // CancellationToken уничтожает и дожидается собственного процесса до освобождения аренды.
            if (writing != null) { try { writing.GetAwaiter().GetResult(); } catch { } writing = null; }
            try { Cleanup(); }
            finally { lease?.Dispose(); lease = null; }
        }
        void Cleanup() { if (cleanupDone) return; cleanupDone = true; cleanupWriter?.Invoke(); }
    }
}
