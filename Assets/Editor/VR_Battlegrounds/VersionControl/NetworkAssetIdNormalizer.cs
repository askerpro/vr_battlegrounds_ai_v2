using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mirror;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.VersionControl
{
    /// <summary>
    /// Держит на диске канонический <c>NetworkIdentity._assetId</c> —
    /// <see cref="NetworkIdentity.AssetGuidToUint" /> от GUID самого префаба.
    ///
    /// <para>
    /// <b>Откуда шум.</b> Поле пишут два механизма, и по-разному. Mirror при загрузке префаба
    /// в редакторе (<c>OnValidate</c> → <c>SetupIDs</c>) выставляет канон и помечает префаб
    /// грязным — канон уходит на диск при следующем сохранении ассетов. Сохранение через
    /// <c>PrefabUtility.LoadPrefabContents</c> / <c>SaveAsPrefabAsset</c> (редакторные
    /// инструменты, скрипты агента) пишет 0, а варианту — id базы: в изолированной сцене
    /// Mirror префаб не узнаёт. Значение скакало в диффах туда-обратно.
    /// </para>
    ///
    /// <para>
    /// Для сети число с диска не важно — Mirror пересчитывает его при загрузке, в сборку
    /// уходит канон (troubleshooting, «В диффе префаба _assetId»). Нормализатор нужен
    /// только ради чистых диффов: после любого импорта сетевого префаба с неканоническим
    /// числом он записывает канон, и Mirror больше нечего переписывать.
    /// </para>
    ///
    /// <para>
    /// Проверка — <c>NetworkAssetIdOnDiskTests</c>.
    /// </para>
    /// </summary>
    public sealed class NetworkAssetIdNormalizer : AssetPostprocessor
    {
        /// <summary>Где лежат префабы проекта. Вне этой папки нормализатор ничего не пишет.</summary>
        private const string ProjectPrefabsRoot = "Assets/Prefabs";

        private const string MenuPath = "Tools/VR Battlegrounds/VersionControl/Normalize Network Asset Ids";

        private static readonly Regex OwnValue =
            new Regex(@"^  _assetId: (\d+)", RegexOptions.Multiline);

        private static readonly Regex OverrideValue =
            new Regex(@"propertyPath: _assetId\r?\n\s+value: (\d+)", RegexOptions.Multiline);

        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static bool _scheduled;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!CanWrite()) return;

            foreach (string path in imported)
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) Pending.Add(path);
            }

            if (Pending.Count == 0 || _scheduled) return;

            // Сохранять ассеты посреди импорта нельзя — делаем это сразу после него.
            _scheduled = true;
            EditorApplication.delayCall += FlushPending;
        }

        private static void FlushPending()
        {
            _scheduled = false;
            var paths = new List<string>(Pending);
            Pending.Clear();

            if (!CanWrite()) return;
            Normalize(paths, log: false);
        }

        [MenuItem(MenuPath)]
        private static void NormalizeAll()
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ProjectPrefabsRoot }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            int fixedCount = Normalize(paths, log: true);
            Debug.Log($"[NetworkAssetIdNormalizer] Проверено префабов: {paths.Count}, исправлено: {fixedCount}.");
        }

        /// <summary>
        /// Записывает канонический <c>_assetId</c> тем сетевым префабам из списка, у которых
        /// на диске другое число. Возвращает, скольким записал.
        /// </summary>
        public static int Normalize(IEnumerable<string> paths, bool log)
        {
            int fixedCount = 0;

            foreach (string path in paths)
            {
                // Только свои префабы: ThirdParty (примеры Mirror, сэмплы UltimateXR) не трогаем —
                // первый прогон меню переписал 80 примеров Mirror.
                if (!path.StartsWith(ProjectPrefabsRoot + "/", StringComparison.Ordinal)) continue;

                // Дешёвый фильтр до загрузки: загрузка префаба запускает OnValidate всех его
                // компонентов (UltimateXR перевыдаёт id в памяти), а сетевых префабов — единицы.
                if (!File.Exists(path) || !File.ReadAllText(path).Contains("_assetId")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var identity = prefab.GetComponent<NetworkIdentity>();
                if (identity == null) continue;

                uint canonical = NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
                if (ReadOnDisk(path) == canonical) continue;

                // Правим ровно одну строку в файле, а не сохраняем префаб через Unity.
                // SaveAssetIfDirty сохранил бы всё, что редактор держит грязным в памяти, —
                // в том числе перевыданные UltimateXR _uxrUniqueId (known-issues #11): так
                // было при первой версии нормализатора, id оружия поменялись.
                if (!RewriteOnDisk(path, canonical))
                {
                    if (log)
                        Debug.LogWarning($"[NetworkAssetIdNormalizer] {path}: строки _assetId в файле нет " +
                                         "(вариант наследует id базы) — пропущен, задайте значение руками.");
                    continue;
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                fixedCount++;
                if (log) Debug.Log($"[NetworkAssetIdNormalizer] {path}: _assetId → {canonical}.");
            }

            return fixedCount;
        }

        /// <summary>
        /// Заменяет число в строке <c>_assetId</c> прямо в файле: у обычного префаба — строку
        /// компонента, у варианта — значение переопределения. Больше ничего в файле не трогает,
        /// концы строк сохраняет. false — строки нет.
        /// </summary>
        private static bool RewriteOnDisk(string path, uint canonical)
        {
            string text = File.ReadAllText(path);
            string value = canonical.ToString();

            Match own = OwnValue.Match(text);
            Match match = own.Success ? own : OverrideValue.Match(text);
            if (!match.Success) return false;

            Group number = match.Groups[1];
            text = text.Substring(0, number.Index) + value + text.Substring(number.Index + number.Length);
            File.WriteAllText(path, text);
            return true;
        }

        /// <summary>
        /// Значение на диске: у обычного префаба — строка компонента, у варианта —
        /// переопределение в <c>m_Modifications</c>. Нет ни того, ни другого — null.
        /// </summary>
        public static uint? ReadOnDisk(string path)
        {
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path);

            Match own = OwnValue.Match(text);
            if (own.Success) return uint.Parse(own.Groups[1].Value);

            Match over = OverrideValue.Match(text);
            if (over.Success) return uint.Parse(over.Groups[1].Value);

            return null;
        }

        /// <summary>
        /// Писать ассеты вправе только основной редактор вне Play Mode: процесс-импортёр и
        /// виртуальный игрок Multiplayer Play Mode сохранять не могут, а в Play Mode сохранение
        /// префаба посреди сессии ни к чему.
        /// </summary>
        private static bool CanWrite()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            return Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
        }
    }
}
