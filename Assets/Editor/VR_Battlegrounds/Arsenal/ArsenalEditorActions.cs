using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Gameplay;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Явные writer-команды: собственная аренда, повторная проверка условий и отчёт.</summary>
    internal static class ArsenalEditorActions
    {
        private const string Owner = "arsenal-editor-window";
        private static string Workspace => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        private static string LockPath => SafePath(Path.Combine(Workspace, "tmp", "unity-lock"));
        private static string SafePath(string path)
        {
            string resolved = Path.GetFullPath(path);
            string allowed = Path.GetFullPath(Path.Combine(Workspace, "tmp")) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Путь аренды вне tmp проекта.");
            return resolved;
        }
        public static string LockStatus
        {
            get
            {
                try
                {
                    string info = Path.Combine(LockPath, "info");
                    if (!Directory.Exists(LockPath)) return "Замок свободен";
                    if (!File.Exists(info)) return "Замок занят: владелец ещё не записан";
                    var lines = File.ReadAllLines(info);
                    string until = lines.FirstOrDefault(l => l.StartsWith("until="))?.Substring(6);
                    bool expired = long.TryParse(until, out long end) && end < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    return (expired ? "Аренда истекла; освободите штатным Tools/agents/unity-lock.sh acquire/release. " : "") + string.Join("; ", lines.Where(l => l.StartsWith("owner=") || l.StartsWith("what=")));
                }
                catch (IOException) { return "Замок недоступен для чтения"; }
            }
        }
        private static string RuntimeBlockReason => EditorApplication.isPlayingOrWillChangePlaymode ? "Редактор в Play Mode"
            : EditorApplication.isCompiling ? "Идёт компиляция"
            : EditorApplication.isUpdating ? "Идёт импорт" : null;
        public static string EditorBlockReason => RuntimeBlockReason ?? (ArsenalEditorStatus.DirtyScene ? "Есть несохранённая сцена: сохраните её самостоятельно" : null)
            ?? (PrefabStageUtility.GetCurrentPrefabStage() != null ? "Закройте режим редактирования префаба перед операцией" : null);
        public static string BlockReason => EditorBlockReason ?? (Directory.Exists(LockPath) ? "Редактор занят: " + LockStatus : null);

        public static string[] DirtySavedAssets() => UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
            .Where(o => o != null && EditorUtility.IsPersistent(o) && EditorUtility.IsDirty(o) && AssetDatabase.IsNativeAsset(o))
            .Select(AssetDatabase.GetAssetPath).Where(p => p.StartsWith("Assets/", StringComparison.Ordinal))
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        public static void RequireSavedAssets(bool savesAllAssets)
        {
            if (!savesAllAssets) return;
            var paths = DirtySavedAssets();
            if (paths.Length > 0) throw new InvalidOperationException("Есть несохранённые изменения ассетов; этот сборщик использует SaveAssets. Сохраните их самостоятельно:\n" +
                string.Join("\n", paths.Take(8)) + (paths.Length > 8 ? "\n… ещё " + (paths.Length - 8) : ""));
        }

        public static void RequireCleanAssets(IEnumerable<string> paths)
        {
            var scope = paths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            var dirty = DirtySavedAssets().Where(p => scope.Any(s => p == s || p.StartsWith(s.TrimEnd('/') + "/", StringComparison.Ordinal))).ToArray();
            if (dirty.Length > 0) throw new InvalidOperationException("В области операции или её входов есть несохранённые ассеты:\n" + string.Join("\n", dirty));
        }

        internal sealed class AuthoringContext
        {
            private readonly ArsenalLayoutAuthoringStand marker;
            private readonly int markerId;
            private readonly int sceneHandle;
            internal AuthoringContext(ArsenalLayoutAuthoringStand stand)
            {
                marker = stand; markerId = stand != null ? stand.GetInstanceID() : 0;
                sceneHandle = stand != null ? stand.gameObject.scene.handle : 0;
            }
            internal string BlockReason
            {
                get
                {
                    if (RuntimeBlockReason != null) return RuntimeBlockReason;
                    if (PrefabStageUtility.GetCurrentPrefabStage() != null) return "Закройте PrefabStage перед authoring операцией";
                    if (marker == null || marker.GetInstanceID() != markerId || marker.Owner != ArsenalLayoutAuthoringStand.OwnerId)
                        return "Authoring marker утрачен или заменён";
                    var scene = marker.gameObject.scene;
                    if (!scene.isLoaded || scene.handle != sceneHandle || scene.path != ArsenalLayoutAuthoringStand.ScenePath || marker.transform.parent != null)
                        return "Authoring scene context утрачен или заменён";
                    if (scene.GetRootGameObjects().Length != 1 || scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalLayoutAuthoringStand>(true)).Count() != 1)
                        return "Authoring scene содержит чужие корни или дубли marker";
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var other = SceneManager.GetSceneAt(i);
                        if (other.handle != sceneHandle && other.isDirty) return "Есть несохранённая чужая сцена: authoring исключение её не разрешает";
                    }
                    return null;
                }
            }
        }

        public sealed class Lease : IDisposable
        {
            private readonly string token;
            private bool disposed;
            private readonly AuthoringContext authoring;
            internal Lease(string token, AuthoringContext authoring) { this.token = token; this.authoring = authoring; }
            private bool IsOwned => File.Exists(Path.Combine(LockPath, "info")) &&
                File.ReadAllLines(Path.Combine(LockPath, "info")).Contains("token=" + token);
            public void RequireActive()
            {
                if (disposed || !IsOwned) throw new InvalidOperationException("Собственная аренда потеряна; подготовьте план снова.");
                string blocked = authoring != null ? authoring.BlockReason : EditorBlockReason;
                if (blocked != null) throw new InvalidOperationException(blocked);
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (IsOwned) Directory.Delete(SafePath(LockPath), true);
            }
            public void Renew()
            {
                RequireActive();
                string info = Path.Combine(LockPath, "info"), temporary = Path.Combine(LockPath, "renew-" + token);
                var lines = File.ReadAllLines(info);
                long until = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 900;
                for (int i = 0; i < lines.Length; i++) if (lines[i].StartsWith("until=", StringComparison.Ordinal)) lines[i] = "until=" + until;
                try
                {
                    File.WriteAllLines(temporary, lines);
                    RequireActive();
                    File.Replace(temporary, info, null);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        public static Lease AcquireLease(string title) => AcquireLeaseCore(title, null);
        public static Lease AcquireAuthoringLease(string title, ArsenalLayoutAuthoringStand stand) => AcquireLeaseCore(title, new AuthoringContext(stand));
        private static Lease AcquireLeaseCore(string title, AuthoringContext authoring)
        {
            string blocked = authoring != null ? authoring.BlockReason ?? (Directory.Exists(LockPath) ? "Редактор занят: " + LockStatus : null) : BlockReason;
            if (blocked != null) throw new InvalidOperationException(blocked);
            string token = Guid.NewGuid().ToString("N");
            string temporary = SafePath(LockPath + "-" + token);
            Directory.CreateDirectory(temporary);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.WriteAllText(Path.Combine(temporary, "info"), $"owner={Owner}\nwhat={title}\nsince={now}\nuntil={now + 900}\ntoken={token}\n");
            try
            {
                // Переименование каталога атомарно: чужую аренду не удаляем даже после её истечения.
                Directory.Move(SafePath(temporary), SafePath(LockPath));
                var lease = new Lease(token, authoring);
                try { lease.RequireActive(); return lease; }
                catch { lease.Dispose(); throw; }
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(SafePath(temporary), true); }
        }

        public static string Run(string title, string[] paths, Func<string> action, bool savesAllAssets = true)
        {
            string blocked = BlockReason;
            if (blocked != null) throw new InvalidOperationException(blocked);
            RequireSavedAssets(savesAllAssets);
            RequireCleanAssets(paths);
            using (var lease = AcquireLease(title))
            {
                lease.RequireActive();
                RequireSavedAssets(savesAllAssets);
                RequireCleanAssets(paths);
                var before = ArsenalEditorStatus.Snapshot(paths);
                string report;
                try { report = action(); }
                catch (Exception exception)
                {
                    string partial = ArsenalEditorStatus.Diff(before, ArsenalEditorStatus.Snapshot(paths));
                    throw new InvalidOperationException(exception.Message + "\nИзменения до остановки:\n" + (string.IsNullOrEmpty(partial) ? "Нет изменений в заявленной области" : partial), exception);
                }
                string diff = ArsenalEditorStatus.Diff(before, ArsenalEditorStatus.Snapshot(paths));
                return report + "\nИзменённые файлы в заявленной области:\n" + (string.IsNullOrEmpty(diff) ? "Нет изменений" : diff);
            }
        }

        public static KinemationWeaponRecipe Kinemation(WeaponInfo weapon) => weapon == null ? null :
            KinemationWeaponBuilder.All.FirstOrDefault(r => KinemationWeaponBuilder.PrefabPath(r) == UnityEditor.AssetDatabase.GetAssetPath(weapon.WeaponPrefab));
        public static HandsPackWeaponRecipe Hands(WeaponInfo weapon) => weapon == null ? null :
            HandsPackWeaponBuilder.T38.FirstOrDefault(r => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.Name}.prefab" == AssetDatabase.GetAssetPath(weapon.WeaponPrefab));
        public static UnityEngine.GameObject SourcePrefab(KinemationWeaponRecipe recipe) => AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(KinemationWeapon.Pack + "Prefabs/Weapons/" + recipe.PackPrefab + ".prefab")
            ?? throw new InvalidOperationException("Нет исходного префаба KINEMATION: " + recipe.PackPrefab);
        public static string[] SourceSettingsPaths(KinemationWeaponRecipe recipe)
        {
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(KinemationWeapon.Pack + "Prefabs/Weapons/" + recipe.PackPrefab + ".prefab");
            return source == null ? Array.Empty<string>() : KinemationWeapon.ReadableSourcePaths(source, recipe.Attachments).Select(p => p + ".meta").ToArray();
        }
        public static string ReportSource(KinemationWeaponRecipe recipe)
        {
            KinemationWeapon.RequireReadable(SourcePrefab(recipe), recipe.Attachments);
            return KinemationWeaponBuilder.Report(recipe);
        }
        public static string Source(WeaponInfo weapon) => Kinemation(weapon) != null ? "KINEMATION" : Hands(weapon) != null ? "Hands T-38" :
            weapon != null && new[] { "BrowningHiPower", "AR15", "FabarmSDASS" }.Contains(weapon.WeaponPrefab != null ? weapon.WeaponPrefab.name : "") ? "Hands: набор из трёх" : "SDK / нет поддержанного полного рецепта";
    }
}
