using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// Конфиг переразвёртки: какие сабмеши каких моделей получают box-UV при импорте.
    /// Единственный писатель — <see cref="BoxUvTargetScanner"/>; читает <see cref="BoxUvModelPostprocessor"/>.
    /// Файл в Git, поэтому в любой Library (worker, build roots, редактор) импорт даёт одинаковые меши.
    /// </summary>
    public sealed class BoxUvTargets
    {
        public const string FilePath = "Assets/Editor/VR_Battlegrounds/Performance/BoxUv/BoxUvTargets.json";

        public sealed class Target
        {
            [JsonProperty("model")] public string Model;
            [JsonProperty("mesh")] public string Mesh;
            [JsonProperty("submeshes")] public List<int> Submeshes = new List<int>();
        }

        public sealed class Skipped
        {
            [JsonProperty("model")] public string Model;
            [JsonProperty("mesh")] public string Mesh;
            [JsonProperty("reason")] public string Reason;
        }

        [JsonProperty("schemaVersion")] public int SchemaVersion = 1;
        [JsonProperty("note")] public string Note =
            "Генерирует Tools/VR Battlegrounds/Performance/Box UV/Scan And Apply. Руками не править.";
        [JsonProperty("materials")] public List<string> Materials = new List<string>();
        [JsonProperty("scenes")] public List<string> Scenes = new List<string>();
        [JsonProperty("targets")] public List<Target> Targets = new List<Target>();
        [JsonProperty("skipped")] public List<Skipped> SkippedMeshes = new List<Skipped>();
        [JsonProperty("unverifiedScenes")] public List<string> UnverifiedScenes = new List<string>();

        private static BoxUvTargets _cached;
        private static DateTime _cachedStamp;

        /// <summary>Текущий конфиг с диска (кэш по времени записи файла); пустой, если файла нет.</summary>
        public static BoxUvTargets Load()
        {
            string full = Path.GetFullPath(FilePath);
            if (!File.Exists(full)) return new BoxUvTargets();
            DateTime stamp = File.GetLastWriteTimeUtc(full);
            if (_cached != null && stamp == _cachedStamp) return _cached;
            _cached = JsonConvert.DeserializeObject<BoxUvTargets>(File.ReadAllText(full)) ?? new BoxUvTargets();
            _cachedStamp = stamp;
            return _cached;
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented).Replace("\r\n", "\n") + "\n";
            File.WriteAllText(Path.GetFullPath(FilePath), json);
            _cached = null;
        }

        public IEnumerable<Target> ForModel(string modelPath)
        {
            foreach (Target target in Targets)
                if (string.Equals(target.Model, modelPath, StringComparison.OrdinalIgnoreCase)) yield return target;
        }

        /// <summary>Отпечаток целей модели — значение её custom dependency (пустая строка — целей нет).</summary>
        public Hash128 HashFor(string modelPath)
        {
            var parts = new List<string>();
            foreach (Target target in ForModel(modelPath))
                parts.Add(target.Mesh + ":" + string.Join(",", target.Submeshes));
            parts.Sort(StringComparer.Ordinal);
            return Hash128.Compute(BoxUvModelPostprocessor.Version + "|" + string.Join(";", parts));
        }
    }
}
