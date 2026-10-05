using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Файловый снимок без Unity API: потоковое чтение, отмена и один SHA на физический путь.</summary>
    internal static class ArsenalFileSnapshot
    {
        internal sealed class Progress
        {
            public readonly string Path;
            public readonly int Files, TotalFiles;
            public readonly long Bytes, TotalBytes;
            public readonly double Seconds;
            public Progress(string path, int files, int totalFiles, long bytes, long totalBytes, double seconds)
            { Path = path; Files = files; TotalFiles = totalFiles; Bytes = bytes; TotalBytes = totalBytes; Seconds = seconds; }
        }

        public static Dictionary<string, string> Read(string workspace, string[] paths, CancellationToken cancel,
            Action<Progress> progress = null)
        {
            var clock = Stopwatch.StartNew();
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths.Where(p => !string.IsNullOrEmpty(p)))
            {
                cancel.ThrowIfCancellationRequested();
                string absolute = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(workspace, path));
                if (Directory.Exists(absolute))
                    foreach (string file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
                    { cancel.ThrowIfCancellationRequested(); files.Add(System.IO.Path.GetFullPath(file)); }
                else files.Add(absolute);
                if (!absolute.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) files.Add(absolute + ".meta");
            }
            var ordered = files.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            long totalBytes = 0;
            foreach (string file in ordered)
            { cancel.ThrowIfCancellationRequested(); if (File.Exists(file)) totalBytes += new FileInfo(file).Length; }
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[128 * 1024];
            long bytes = 0;
            int completed = 0;
            double lastReport = -1;
            void Report(string path, bool force = false)
            {
                double seconds = clock.Elapsed.TotalSeconds;
                if (force || seconds - lastReport >= .1)
                { lastReport = seconds; progress?.Invoke(new Progress(path, completed, ordered.Length, bytes, totalBytes, seconds)); }
            }
            Report("Перечень файлов", true);
            using var hash = SHA256.Create();
            foreach (string file in ordered)
            {
                cancel.ThrowIfCancellationRequested();
                if (!File.Exists(file)) result[file] = "отсутствует";
                else
                {
                    var before = new FileInfo(file);
                    long length = before.Length;
                    DateTime written = before.LastWriteTimeUtc;
                    hash.Initialize();
                    using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                    {
                        int count;
                        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                            bytes += count;
                            Report(file);
                        }
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    }
                    var after = new FileInfo(file);
                    if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != written)
                        throw new IOException("Файл изменился во время снимка: " + file);
                    result[file] = BitConverter.ToString(hash.Hash);
                }
                completed++;
                Report(file);
            }
            cancel.ThrowIfCancellationRequested();
            Report("Снимок завершён", true);
            return result;
        }
    }
}
