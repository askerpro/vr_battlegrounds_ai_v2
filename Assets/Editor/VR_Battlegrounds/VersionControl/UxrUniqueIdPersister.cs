using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UltimateXR.Core.Components;
using UltimateXR.Extensions.Unity;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.VersionControl
{
    /// <summary>
    /// Держит на диске верные флаги <c>__isInPrefab</c> / <c>__prefabGuid</c> UXR-компонентов
    /// префабов, чтобы основной редактор не перевыдавал им <c>_uxrUniqueId</c> (MPPM-02).
    ///
    /// <para>
    /// <b>Откуда расхождение.</b> <c>UxrUniqueIdImplementer.NotifyOnValidate</c> сверяет флаги
    /// с фактическими и при несовпадении выдаёт компоненту новый случайный id — при реимпорте
    /// префаба, только в памяти, без сохранения. Хост MPPM живёт со случайными id, клон (патч 7)
    /// берёт их из файла, и канал состояния отвергает события (<c>UxrComponentNotFoundException</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Откуда неверные флаги.</b> У экземпляра на сцене флаги свои (<c>__isInPrefab: 0</c>),
    /// и <b>Apply to Prefab</b> переносит их в ассет обычными переопределениями. Так же пишут
    /// <c>LoadPrefabContents</c>/<c>SaveAsPrefabAsset</c>: в изолированной сцене компонент
    /// «не в префабе». Варианты и вложенные префабы наследуют флаги своей базы.
    /// </para>
    ///
    /// <para>
    /// <b>Что делает.</b> После импорта префаба из <c>Assets/Prefabs</c>: в каждом UXR-компоненте
    /// файла ставит <c>__isInPrefab: 1</c> и <c>__prefabGuid</c> самого префаба — правкой строк,
    /// id на диске не меняя, — и переимпортирует. Флаги, которые файл не хранит (вариант
    /// наследует их от базы), строкой не поправить: тогда префаб сохраняется через Unity с
    /// исправленными флагами. Только основной редактор вне Play Mode.
    /// </para>
    ///
    /// <para>Проверка — <c>UxrUniqueIdOnDiskTests</c>.</para>
    /// </summary>
    public sealed class UxrUniqueIdPersister : AssetPostprocessor
    {
        private const string ProjectPrefabsRoot = "Assets/Prefabs";
        private const string MenuPath = "Tools/VR Battlegrounds/VersionControl/Persist UltimateXR Unique Ids";

        private static readonly Regex OwnIsInPrefab =
            new Regex(@"^(  __isInPrefab: )0\b", RegexOptions.Multiline);

        private static readonly Regex OwnPrefabGuid =
            new Regex(@"^(  __prefabGuid: )([0-9a-f]*)[ \t]*(?=\r?$)", RegexOptions.Multiline);

        private static readonly Regex OverrideIsInPrefab =
            new Regex(@"(propertyPath: __isInPrefab\r?\n\s+value: )0\b");

        private static readonly Regex OverridePrefabGuid =
            new Regex(@"(propertyPath: __prefabGuid\r?\n\s+value: )([0-9a-f]*)[ \t]*(?=\r?\n)");

        private static readonly Regex GuidPattern =
            new Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}");

        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static bool _scheduled;

        // ── Автоматически после импорта ────────────────────────────────

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!CanWrite()) return;

            foreach (string path in imported)
            {
                if (IsProjectPrefab(path)) Pending.Add(path);
            }

            if (Pending.Count == 0 || _scheduled) return;

            // Писать ассеты посреди импорта нельзя — делаем это сразу после него.
            _scheduled = true;
            EditorApplication.delayCall += FlushPending;
        }

        private static void FlushPending()
        {
            if (!CanWrite())
            {
                EditorApplication.update -= RetryPending;
                EditorApplication.update += RetryPending;
                return;
            }
            _scheduled = false;
            var paths = new List<string>(Pending);
            Pending.Clear();

            foreach (string path in paths)
            {
                string result = Normalize(path);
                if (result != null)
                    VrBattlegrounds.Core.GameLog.Debug.Info($"[UxrUniqueIdPersister] {path}: {result}");
            }
        }

        private static void RetryPending()
        {
            if (!CanWrite()) return;
            EditorApplication.update -= RetryPending;
            FlushPending();
        }

        [MenuItem(MenuPath)]
        private static void NormalizeAllFromMenu()
        {
            if (!CanWrite())
            {
                VrBattlegrounds.Core.GameLog.Debug.Warning("[UxrUniqueIdPersister] Сохранять префабы может только основной редактор вне Play Mode.");
                return;
            }

            VrBattlegrounds.Core.GameLog.Debug.Info($"[UxrUniqueIdPersister] {NormalizeAll()}");
        }

        /// <summary>Прогон по всем префабам проекта. Базы раньше вариантов. Возвращает отчёт.</summary>
        public static string NormalizeAll()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ProjectPrefabsRoot }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            paths.Sort((a, b) => VariantDepth(a).CompareTo(VariantDepth(b)));

            var report = new List<string>();
            foreach (string path in paths)
            {
                string result = Normalize(path);
                if (result != null) report.Add($"{path}: {result}");
            }

            return $"Проверено префабов: {paths.Count}, исправлено: {report.Count}.\n{string.Join("\n", report)}";
        }

        /// <summary>
        /// Приводит флаги одного префаба к верным. Возвращает описание сделанного или null,
        /// если префаб уже в порядке.
        /// </summary>
        public static string Normalize(string path)
        {
            if (!IsProjectPrefab(path) || !File.Exists(path)) return null;

            string actions = null;

            // 1. Флаги, записанные в самом файле, — правкой строк: id на диске не меняются.
            string guid = AssetDatabase.AssetPathToGUID(path);
            string text = File.ReadAllText(path);
            string fixedText = OwnIsInPrefab.Replace(text, "${1}1");
            fixedText = OwnPrefabGuid.Replace(fixedText, "${1}" + guid);
            fixedText = OverrideIsInPrefab.Replace(fixedText, "${1}1");
            fixedText = OverridePrefabGuid.Replace(fixedText, "${1}" + guid);

            if (fixedText != text)
            {
                File.WriteAllText(path, fixedText);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                actions = "флаги в файле исправлены";
            }

            // 2. Флаги, которых в файле нет (наследуются от базы), или id, перевыданные в памяти
            //    до исправления файла, — сохранением через Unity. Id такого компонента на диске
            //    сменится один раз и дальше будет стабилен.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return actions;

            HashSet<string> onDisk = ReadIdsOnDisk(path);
            bool needsSave = false;

            foreach (UxrComponent component in prefab.GetComponentsInChildren<UxrComponent>(true))
            {
                if (FixFlags(component)) needsSave = true;

                SerializedProperty id = new SerializedObject(component).FindProperty("_uxrUniqueId");
                if (id != null && !onDisk.Contains(id.stringValue)) needsSave = true;
            }

            if (needsSave)
            {
                PrefabUtility.SavePrefabAsset(prefab);
                actions = actions == null ? "сохранён с верными флагами" : actions + ", сохранён с верными флагами";
            }

            // Сохранение UXR не должно отменять уже завершённую нормализацию Mirror.
            if (actions != null) NetworkAssetIdNormalizer.Normalize(new[] { path }, false);

            return actions;
        }

        // ── Проверки (общие с UxrUniqueIdOnDiskTests) ──────────────────

        /// <summary>
        /// Расходятся ли флаги компонента в памяти с фактическими — условие, при котором
        /// <c>NotifyOnValidate</c> перевыдаёт id.
        /// </summary>
        public static bool HasMismatch(UxrComponent component, out bool actualIsInPrefab, out string actualGuid)
        {
            actualIsInPrefab = false;
            if (!component.GetPrefabGuid(out actualGuid, out _)) return false;
            actualIsInPrefab = component.IsInPrefab();

            var so = new SerializedObject(component);
            return so.FindProperty("__isInPrefab").boolValue != actualIsInPrefab ||
                   so.FindProperty("__prefabGuid").stringValue != actualGuid;
        }

        /// <summary>
        /// Все id, которые префаб мог унаследовать с диска: из его файла и из файлов его
        /// баз и вложенных префабов.
        /// </summary>
        public static HashSet<string> ReadIdsOnDisk(string path)
        {
            var ids = new HashSet<string>();

            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (!dependency.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !File.Exists(dependency)) continue;

                foreach (Match m in GuidPattern.Matches(File.ReadAllText(dependency)))
                    ids.Add(m.Value);
            }

            return ids;
        }

        private static bool FixFlags(UxrComponent component)
        {
            if (!HasMismatch(component, out bool isInPrefab, out string guid)) return false;

            var so = new SerializedObject(component);
            so.FindProperty("__isInPrefab").boolValue = isInPrefab;
            so.FindProperty("__prefabGuid").stringValue = guid;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool IsProjectPrefab(string path)
        {
            return path.StartsWith(ProjectPrefabsRoot + "/", StringComparison.Ordinal) &&
                   path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private static int VariantDepth(string path)
        {
            int depth = 0;
            UnityEngine.Object current = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            while ((current = PrefabUtility.GetCorrespondingObjectFromSource(current)) != null && depth < 16)
                depth++;

            return depth;
        }

        /// <summary>
        /// Писать ассеты вправе только основной редактор вне Play Mode: процесс-импортёр и
        /// виртуальный игрок MPPM сохранять не могут.
        /// </summary>
        private static bool CanWrite()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            return Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
        }
    }
}
