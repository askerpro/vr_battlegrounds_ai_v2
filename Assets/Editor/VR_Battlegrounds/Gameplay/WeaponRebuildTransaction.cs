using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Откат полной пересборки ствола (<see cref="WeaponRebuildFinish.Run" />). До первой записи сборщика снимает
    /// побайтную копию префабов оружия и магазина, всех их зависимостей в проекте (меши пака и разрезанные меши
    /// прицела, позы, области подсветки, звуки, данные механики) и заявленных путей. У ассетов в
    /// <c>Assets/ThirdParty</c> копируется только <c>.meta</c>: сборщик меняет у исходников пака лишь настройки импорта.
    /// При сбое возвращает изменённые байты, удаляет файлы и папки, которых до сборки не было в каталогах
    /// скопированных ассетов, и переимпортирует изменённое. Файлы вне копии не трогает. Несохранённые правки, появившиеся в
    /// памяти редактора за сборку, отбрасывает (<see cref="OwnAssetSave.Discard" />).
    /// </summary>
    internal sealed class WeaponRebuildTransaction
    {
        private static readonly string[] CodeExtensions = { ".cs", ".dll", ".shader", ".hlsl", ".cginc", ".compute", ".asmdef", ".asmref" };
        private const string ThirdParty = "Assets/ThirdParty/";

        private readonly string[] roots;
        private readonly Dictionary<string, byte[]> backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        // Каталог → его файлы и подкаталоги до сборки (без рекурсии): новое в нём создала сборка.
        private readonly Dictionary<string, HashSet<string>> listings = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> declared = new List<string>();
        // Несохранённое до сборки: чужое (правка человека, Unity) — его не выгружать.
        private HashSet<string> dirtyBefore = new HashSet<string>(StringComparer.Ordinal);

        private static string Project => Directory.GetParent(Application.dataPath).FullName;

        public int FileCount => backups.Count;
        public long ByteCount => backups.Values.Sum(b => (long)b.Length);

        /// <summary>
        /// Своё у пересборки: скопированные ассеты (без исходников паков) и заявленные пути. По ней сборка сохраняет
        /// результат (<see cref="OwnAssetSave" />) и отказывает, если там до начала есть несохранённые изменения.
        /// </summary>
        public string[] Scope => backups.Keys.Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Concat(roots).Concat(declared.Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        private WeaponRebuildTransaction(string[] roots) { this.roots = roots; }

        /// <param name="roots">Префабы ствола; порядок импорта при откате обратный (магазин — база вложенного).</param>
        /// <param name="extraPaths">Файлы и папки, которые сборка пишет, хотя текущие префабы на них не ссылаются.</param>
        public static WeaponRebuildTransaction Begin(IEnumerable<string> roots, IEnumerable<string> extraPaths)
        {
            var rootPaths = roots.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
            var tx = new WeaponRebuildTransaction(rootPaths);
            string[] existing = rootPaths.Where(p => File.Exists(Disk(p))).ToArray();
            foreach (string asset in rootPaths.Concat(existing.Length == 0 ? Array.Empty<string>() : AssetDatabase.GetDependencies(existing, true)))
                tx.AddAsset(asset);
            foreach (string path in extraPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(path)) continue;
                tx.declared.Add(path);
                if (Directory.Exists(Disk(path))) tx.AddFolder(path);
                else if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) tx.Copy(path); // настройки импорта исходника
                else if (File.Exists(Disk(path))) tx.AddAsset(path);
                else if (Path.HasExtension(path)) tx.Watch(Parent(path));
                else tx.missingFolders.Add(path);
            }
            foreach (string root in rootPaths) tx.Watch(Parent(root));
            tx.dirtyBefore = OwnAssetSave.AllDirty();
            return tx;
        }

        /// <summary>
        /// После успешной сборки: ассеты, ставшие несохранёнными за сборку вне её области (Unity обновляет материал пака
        /// при загрузке и т. п.). Сборка их не пишет — изменения в памяти отбрасываются, иначе их записал бы следующий
        /// общий SaveAssets. Возвращает отчёт; пустая строка — такого нет.
        /// </summary>
        public string ReleaseForeign()
        {
            string[] scope = Scope;
            string[] foreign = OwnAssetSave.AllDirty().Where(p => !dirtyBefore.Contains(p) && !InScope(p, scope)).ToArray();
            if (foreign.Length == 0) return "";
            string[] discarded = OwnAssetSave.Discard(foreign, out string[] kept);
            string report = discarded.Length > 0 ? "несохранённое вне области сборки отброшено: " + string.Join(", ", discarded) : "";
            if (kept.Length > 0) report += (report.Length > 0 ? "\n" : "") + "несохранённые префабы вне области сборки: " + string.Join(", ", kept) + " — не сохранять";
            return report;
        }

        private static bool InScope(string path, string[] scope) =>
            scope.Any(r => path == r.TrimEnd('/') || path.StartsWith(r.TrimEnd('/') + "/", StringComparison.Ordinal));

        private void AddAsset(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || CodeExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return;
            bool pack = path.StartsWith(ThirdParty, StringComparison.Ordinal);
            if (!pack) Copy(path);
            Copy(path + ".meta");
            if (!pack) Watch(Parent(path));
        }

        private void AddFolder(string folder)
        {
            Watch(folder);
            Copy(folder + ".meta");
            foreach (string entry in Directory.EnumerateFileSystemEntries(Disk(folder)))
            {
                string relative = Relative(entry);
                if (Directory.Exists(entry)) AddFolder(relative);
                else Copy(relative);
            }
        }

        private void Copy(string path)
        {
            if (backups.ContainsKey(path) || !File.Exists(Disk(path))) return;
            backups[path] = File.ReadAllBytes(Disk(path));
        }

        private void Watch(string folder)
        {
            if (string.IsNullOrEmpty(folder) || listings.ContainsKey(folder) || !Directory.Exists(Disk(folder))) return;
            listings[folder] = new HashSet<string>(Directory.EnumerateFileSystemEntries(Disk(folder)).Select(Relative), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Вернуть состояние до сборки. Возвращает отчёт; остаток расхождений — в отчёте, не исключением.</summary>
        public string Rollback()
        {
            var restored = new List<string>();
            foreach (KeyValuePair<string, byte[]> backup in backups)
            {
                string disk = Disk(backup.Key);
                if (File.Exists(disk) && File.ReadAllBytes(disk).SequenceEqual(backup.Value)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(disk));
                File.WriteAllBytes(disk, backup.Value);
                restored.Add(backup.Key);
            }

            var created = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> listing in listings)
            {
                if (!Directory.Exists(Disk(listing.Key))) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(Disk(listing.Key)).Select(Relative))
                    if (!listing.Value.Contains(entry) && !backups.ContainsKey(entry)) created.Add(entry);
            }
            created.AddRange(missingFolders.Where(f => Directory.Exists(Disk(f))));
            var deleted = new List<string>();
            foreach (string path in created.Distinct().OrderByDescending(p => p.Length))
            {
                bool meta = path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
                string asset = meta ? path.Substring(0, path.Length - 5) : path;
                if (meta && (File.Exists(Disk(asset)) || Directory.Exists(Disk(asset)))) continue; // .meta уйдёт вместе с ассетом
                if (!meta && AssetDatabase.DeleteAsset(path)) { deleted.Add(path); continue; }
                if (Directory.Exists(Disk(path))) Directory.Delete(Disk(path), true);
                else if (File.Exists(Disk(path))) File.Delete(Disk(path));
                if (!meta && File.Exists(Disk(path) + ".meta")) File.Delete(Disk(path) + ".meta");
                deleted.Add(path);
            }

            // Импорт: сначала не-префабы, потом префабы; корни — в обратном порядке (магазин раньше оружия).
            int Rank(string p) => Array.IndexOf(roots, p) is int i && i >= 0 ? 2 + roots.Length - i
                : p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            foreach (string path in restored.Select(p => p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 5) : p)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Where(p => File.Exists(Disk(p)) || Directory.Exists(Disk(p))).OrderBy(Rank))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Импорт мог переписать файл (постпроцессоры) — сверить ещё раз.
            var drift = backups.Where(b => !File.Exists(Disk(b.Key)) || !File.ReadAllBytes(Disk(b.Key)).SequenceEqual(b.Value)).Select(b => b.Key).ToArray();
            // Файлы вернулись, но несохранённые правки сборки остались в памяти (меши, области): отбросить всё, что стало
            // несохранённым за сборку, — внутри области и вне её. Чужое несохранённое до сборки не трогается.
            string[] discarded = OwnAssetSave.Discard(OwnAssetSave.AllDirty().Where(p => !dirtyBefore.Contains(p)), out string[] dirty);
            string report = $"откат: возвращено файлов {restored.Count}, удалено созданных {deleted.Count} (копия {backups.Count} файлов, {ByteCount / 1024} КБ)";
            if (restored.Count > 0) report += "\nвозвращены: " + string.Join(", ", restored.Take(12)) + (restored.Count > 12 ? $" … ещё {restored.Count - 12}" : "");
            if (deleted.Count > 0) report += "\nудалены: " + string.Join(", ", deleted.Take(12)) + (deleted.Count > 12 ? $" … ещё {deleted.Count - 12}" : "");
            if (discarded.Length > 0) report += $"\nотброшены несохранённые правки в памяти: {discarded.Length} ассетов (" + string.Join(", ", discarded.Take(6)) + (discarded.Length > 6 ? " …" : "") + ")";
            if (drift.Length > 0) report += "\nОТКАТ НЕПОЛНЫЙ, после импорта отличаются: " + string.Join(", ", drift) + " — вернуть из Git";
            if (dirty.Length > 0) report += "\nв памяти остались несохранённые изменения: " + string.Join(", ", dirty) + " — не сохранять, перезапустить Unity";
            return report;
        }

        private static string Disk(string path) => Path.Combine(Project, path);

        private static string Relative(string disk) => Path.GetFullPath(disk).Substring(Project.Length + 1).Replace('\\', '/');

        private static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash > 0 ? path.Substring(0, slash) : null;
        }
    }
}
