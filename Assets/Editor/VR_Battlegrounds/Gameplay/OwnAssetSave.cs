using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Адресная замена <c>AssetDatabase.SaveAssets()</c> для писателей оружия: сохраняет только несохранённые ассеты,
    /// чей путь совпадает с одним из путей писателя или лежит в одной из его папок. Чужие несохранённые изменения
    /// (материалы паков после обновления URP, настройки, правки человека) остаются в памяти редактора нетронутыми.
    /// </summary>
    public static class OwnAssetSave
    {
        /// <summary>Сохранить несохранённые ассеты области; возвращает пути сохранённых файлов.</summary>
        public static string[] Save(IEnumerable<string> scope)
        {
            var saved = new List<string>();
            foreach (UnityEngine.Object asset in DirtyObjects(scope))
            {
                AssetDatabase.SaveAssetIfDirty(asset);
                saved.Add(AssetDatabase.GetAssetPath(asset));
            }
            return saved.Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Пути несохранённых ассетов области — без записи.</summary>
        public static string[] Dirty(IEnumerable<string> scope) =>
            DirtyObjects(scope).Select(AssetDatabase.GetAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        /// <summary>Пути всех несохранённых ассетов проекта — снимок до записи, чтобы отличить своё от чужого.</summary>
        public static HashSet<string> AllDirty() =>
            new HashSet<string>(AllDirtyObjects().Select(AssetDatabase.GetAssetPath), StringComparer.Ordinal);

        /// <summary>
        /// Отбросить несохранённые изменения ассетов по путям: объект выгружается, следующая загрузка читает файл.
        /// Префабы так не выгрузить — их пути возвращаются в <paramref name="kept" />.
        /// </summary>
        public static string[] Discard(IEnumerable<string> paths, out string[] kept)
        {
            var set = new HashSet<string>(paths, StringComparer.Ordinal);
            var discarded = new HashSet<string>(StringComparer.Ordinal);
            var left = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnityEngine.Object o in AllDirtyObjects())
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (!set.Contains(path)) continue;
                if (o is GameObject || o is Component) { left.Add(path); continue; }
                Resources.UnloadAsset(o);
                discarded.Add(path);
            }
            kept = left.OrderBy(p => p, StringComparer.Ordinal).ToArray();
            return discarded.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        private static List<UnityEngine.Object> DirtyObjects(IEnumerable<string> scope)
        {
            string[] roots = scope.Where(p => !string.IsNullOrEmpty(p)).Select(p => p.TrimEnd('/')).Distinct().ToArray();
            if (roots.Length == 0) return new List<UnityEngine.Object>();
            return AllDirtyObjects().Where(o => InScope(AssetDatabase.GetAssetPath(o), roots)).ToList();
        }

        private static IEnumerable<UnityEngine.Object> AllDirtyObjects() =>
            Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(o => o != null && EditorUtility.IsPersistent(o) && EditorUtility.IsDirty(o) && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o)))
                .ToList();

        private static bool InScope(string path, string[] roots) =>
            roots.Any(r => path == r || path.StartsWith(r + "/", StringComparison.Ordinal));
    }
}
