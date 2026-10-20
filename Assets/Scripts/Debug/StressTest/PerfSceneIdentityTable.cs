using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>Read-only сопоставление serialized refs диагностической сборки с авторскими ID.</summary>
    public sealed class PerfSceneIdentityTable : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public Object target;
            public string authoringId;
        }

        [SerializeField] private string _scenePath;
        [SerializeField] private string _sceneGuid;
        [SerializeField] private string _sceneSourceSha256;
        [SerializeField] private string _tableSha256;
        [SerializeField] private int _unresolved;
        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();
        private readonly Dictionary<Object, string> _index = new Dictionary<Object, string>();
        private readonly HashSet<Object> _ambiguous = new HashSet<Object>();
        private int _missingRuntimeReferences;
        private static readonly List<PerfSceneIdentityTable> Tables = new List<PerfSceneIdentityTable>();

        public string ScenePath => _scenePath;
        public string SceneGuid => _sceneGuid;
        public string SceneSourceSha256 => _sceneSourceSha256;
        public string TableSha256 => _tableSha256;
        public int EntryCount => _entries.Length;
        public int UnresolvedCount => _unresolved + _ambiguous.Count + _missingRuntimeReferences;

        private void OnEnable()
        {
            _index.Clear();
            _ambiguous.Clear();
            _missingRuntimeReferences = 0;
            foreach (Entry entry in _entries)
            {
                if (entry.target == null || string.IsNullOrEmpty(entry.authoringId)) { _missingRuntimeReferences++; continue; }
                if (_ambiguous.Contains(entry.target)) continue;
                if (_index.TryGetValue(entry.target, out string prior))
                {
                    if (prior != entry.authoringId) { _index.Remove(entry.target); _ambiguous.Add(entry.target); }
                }
                else _index.Add(entry.target, entry.authoringId);
            }
            if (!Tables.Contains(this)) Tables.Add(this);
        }

        private void OnDisable() { Tables.Remove(this); _index.Clear(); _ambiguous.Clear(); }

        public static bool TryResolve(Object target, out string authoringId)
        {
            authoringId = null;
            if (target == null) return false;
            foreach (PerfSceneIdentityTable table in Tables)
            {
                if (table == null || !table._index.TryGetValue(target, out string candidate)) continue;
                if (authoringId != null && authoringId != candidate) { authoringId = null; return false; }
                authoringId = candidate;
            }
            return authoringId != null;
        }

        public static object[] CaptureProvenance()
        {
            var rows = new List<object>(Tables.Count);
            foreach (PerfSceneIdentityTable table in Tables)
                if (table != null) rows.Add(new { scenePath = table.ScenePath, sceneGuid = table.SceneGuid,
                    sceneSourceSha256 = table.SceneSourceSha256, tableSha256 = table.TableSha256,
                    entryCount = table.EntryCount, unresolvedCount = table.UnresolvedCount,
                    runtimeResolvedCount = table._index.Count,
                    join = "serialized object reference; dynamic objects and GPU batching need separate evidence" });
            return rows.ToArray();
        }

        public static object[] CaptureReferences()
        {
            var rows = new List<object>();
            foreach (PerfSceneIdentityTable table in Tables)
                if (table != null)
                    foreach (KeyValuePair<Object, string> entry in table._index)
                        if (entry.Key != null) rows.Add(new { authoringId = entry.Value,
                            runtimeInstanceId = entry.Key.GetInstanceID(), runtimeInstanceIdScope = "process-local",
                            runtimeType = entry.Key.GetType().Name, scenePath = table.ScenePath, tableSha256 = table.TableSha256,
                            confidence = "serializedBuildReference", gpuResourceJoin = "unresolved" });
            return rows.ToArray();
        }

#if UNITY_EDITOR
        // Вызывает только build processor для временной build-сцены; в Player setter отсутствует.
        public void SetBuildData(string scenePath, string sceneGuid, string sceneSha, string tableSha, int unresolved, Entry[] entries)
        {
            _scenePath = scenePath; _sceneGuid = sceneGuid; _sceneSourceSha256 = sceneSha;
            _tableSha256 = tableSha; _unresolved = unresolved; _entries = entries;
        }
#endif
    }
}
