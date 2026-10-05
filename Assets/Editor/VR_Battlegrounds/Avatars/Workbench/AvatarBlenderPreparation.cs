using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Blender пишет только в новую копию. Unity API вызывается до запуска и после завершения процесса.</summary>
    internal sealed class AvatarBlenderPreparation
    {
        internal const string Scripts = "Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts";
        readonly string source, output, executable, workspace, log;
        readonly string[] scripts;
        bool refreshSuspended;

        internal AvatarBlenderPreparation(string source, string output, string executable, string[] scripts)
        {
            this.source = source; this.output = output; this.executable = executable; this.scripts = scripts;
            workspace = Directory.GetParent(Application.dataPath).FullName;
            log = Path.Combine(workspace, "tmp", "avatar-editor-workbench", "blender-" + Guid.NewGuid().ToString("N") + ".log");
            if (!File.Exists(executable)) throw new InvalidOperationException("Выберите существующий blender.exe в настройке Blender.");
            foreach (string script in scripts)
                if (script != "amputate_avatar_hands.py" && script != "add_wrist_torsion_bones.py" && script != "add_eye_bones.py")
                    throw new InvalidOperationException("Неизвестный шаг Blender.");
        }

        internal Task<string> Start(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            AvatarRigPreparation.CopyFbx(source, output);
            AssetDatabase.DisallowAutoRefresh(); refreshSuspended = true;
            // Строки и физические пути захвачены на главном потоке; worker не обращается к Unity.
            string fbx = Path.GetFullPath(Path.Combine(workspace, output));
            return Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log));
                using (var stream = new StreamWriter(log, false, new System.Text.UTF8Encoding(false)))
                    foreach (string script in scripts)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string path = Path.Combine(workspace, Scripts, script);
                        if (!File.Exists(path)) throw new FileNotFoundException("Скрипт Blender не найден.", path);
                        RunProcess(executable, "--background --python-exit-code 1 --python " + Quote(path) + " -- " + Quote(fbx), workspace, stream, cancellation);
                    }
                return "Blender завершён. Журнал: " + log;
            });
        }

        internal string Complete(string message, bool mapEyes, bool fullRig, string rigName, AvatarHandsMode mode, bool poses)
        {
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (mapEyes) VRBattlegrounds.Editor.ApplyEyeMapping.MapEyes(output);
            if (fullRig) message += "\n" + AvatarRigPreparation.Build(output, rigName, mode, mode == AvatarHandsMode.Sdk, poses);
            return message + "\nПодготовленный FBX: " + output;
        }
        internal void Cleanup()
        {
            if (!refreshSuspended) return;
            refreshSuspended = false;
            AssetDatabase.AllowAutoRefresh();
        }

        internal static void RunProcess(string executable, string arguments, string directory, TextWriter output, CancellationToken cancel)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments) { WorkingDirectory = directory, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                object outputLock = new object();
                DataReceivedEventHandler receive = (_, e) => { if (e.Data != null) lock (outputLock) output.WriteLine(e.Data); };
                process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                cancel.ThrowIfCancellationRequested();
                process.Start();
                using (cancel.Register(() => Stop(process)))
                {
                    try
                    {
                        process.BeginOutputReadLine(); process.BeginErrorReadLine();
                        var deadline = DateTime.UtcNow.AddMinutes(10);
                        while (!process.WaitForExit(100))
                        {
                            cancel.ThrowIfCancellationRequested();
                            if (DateTime.UtcNow > deadline) throw new TimeoutException("Шаг Blender превысил 10 минут.");
                        }
                        process.WaitForExit(); cancel.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0) throw new InvalidOperationException("Blender завершился с кодом " + process.ExitCode + ". Следующие шаги не запущены.");
                    }
                    finally { Stop(process); }
                }
            }
        }
        static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
            catch (InvalidOperationException) { }
        }
        static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
