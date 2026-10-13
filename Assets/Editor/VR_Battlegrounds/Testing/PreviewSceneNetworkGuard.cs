using System;
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>
    /// Находит NetworkIdentity в preview-сценах редактора: содержимое после LoadPrefabContents без выгрузки
    /// и открытый Prefab Mode. Mirror NetworkScenePostProcess (FindObjectsOfTypeAll) выдаёт им sceneId,
    /// сервер спавнит их как scene objects, а у клиентов таких объектов нет — «Spawn scene object not found».
    /// Только чтение; вызывается перед стартом Play, не каждый кадр.
    /// </summary>
    internal static class PreviewSceneNetworkGuard
    {
        internal const int MaxEntries = 20;

        /// <summary>Строки «источник: путь в иерархии», по одной на префаб/сцену, не больше <see cref="MaxEntries"/>.</summary>
        internal static IReadOnlyList<string> FindPreviewNetworkObjects() => FindPreviewNetworkObjects(out _, out _);

        /// <param name="total">Общее число найденных NetworkIdentity во всех preview-сценах.</param>
        /// <param name="sources">Число разных источников (префабов/сцен) до обрезки списка.</param>
        internal static IReadOnlyList<string> FindPreviewNetworkObjects(out int total, out int sources)
        {
            total = 0;
            var entries = new List<string>();
            var sourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var sourceCounts = new List<int>();
            var seen = new HashSet<NetworkIdentity>();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();

            void Add(NetworkIdentity identity, ref int count)
            {
                if (identity == null || !seen.Add(identity)) return;
                GameObject go = identity.gameObject;
                // Тот же фильтр скрытых объектов, что у Mirror NetworkScenePostProcess: их Mirror не трогает.
                if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave) return;
                count++;
                string source = SourceName(go, stage);
                if (sourceIndex.TryGetValue(source, out int index)) { sourceCounts[index]++; return; }
                sourceIndex[source] = sourceCounts.Count;
                sourceCounts.Add(1);
                entries.Add(source + ": " + HierarchyPath(go.transform));
            }

            foreach (var identity in Resources.FindObjectsOfTypeAll<NetworkIdentity>())
            {
                if (identity == null || EditorUtility.IsPersistent(identity.gameObject)) continue;
                if (!EditorSceneManager.IsPreviewScene(identity.gameObject.scene)) continue;
                Add(identity, ref total);
            }
            // Сцена Prefab Mode тоже preview; явный обход страхует от объектов, которые FindObjectsOfTypeAll не вернул.
            if (stage != null && stage.prefabContentsRoot != null)
                foreach (var identity in stage.prefabContentsRoot.GetComponentsInChildren<NetworkIdentity>(true))
                    Add(identity, ref total);

            var result = new List<string>(Math.Min(entries.Count, MaxEntries));
            for (int i = 0; i < entries.Count && i < MaxEntries; i++)
                result.Add(sourceCounts[i] > 1 ? entries[i] + " (+" + (sourceCounts[i] - 1) + ")" : entries[i]);
            sources = entries.Count;
            return result;
        }

        /// <summary>Текст отказа preflight или null, если preview-сцен с NetworkIdentity нет.</summary>
        internal static string DescribeRefusal()
        {
            var entries = FindPreviewNetworkObjects(out int total, out int sources);
            if (total == 0) return null;
            var text = new StringBuilder();
            text.Append("В редакторе открыты preview-сцены/Prefab Mode с NetworkIdentity — Mirror заспавнит их на сервере, ")
                .Append("у клиентов будет \"Spawn scene object not found\". Закрой Prefab Mode / выгрузи содержимое префабов ")
                .Append("(агент — PrefabUtility.UnloadPrefabContents) или перезапусти редактор. Найдено NetworkIdentity: ")
                .Append(total).Append(", источников: ").Append(sources).Append(':');
            foreach (string entry in entries) text.Append('\n').Append(entry);
            if (sources > entries.Count) text.Append("\n… и ещё источников: ").Append(sources - entries.Count).Append('.');
            return text.ToString();
        }

        private static string SourceName(GameObject go, PrefabStage stage)
        {
            if (stage != null && go.scene == stage.scene && !string.IsNullOrEmpty(stage.assetPath)) return stage.assetPath;
            // Сцена LoadPrefabContents несёт путь префаба: из него Mirror считает hash-часть sceneId.
            if (!string.IsNullOrEmpty(go.scene.path)) return go.scene.path;
            return string.IsNullOrEmpty(go.scene.name) ? "<preview>" : go.scene.name;
        }

        private static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            for (var current = transform; current != null; current = current.parent) parts.Add(current.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
