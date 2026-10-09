using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Audio
{
    /// <summary>
    /// Настройки импорта коротких игровых звуков под Quest (<c>Docs/sound-library.md</c>): Force To Mono, Vorbis ~70 %,
    /// Load Type — Decompress On Load для клипов короче <see cref="ShortClipSeconds"/>, иначе Compressed In Memory.
    /// Применяется к указанным клипам или папкам; клип с уже верными настройками не переимпортируется (повторный вызов
    /// ничего не меняет). Переопределение Android, если оно есть, приводится к тем же значениям.
    /// </summary>
    public static class SfxImportSettings
    {
        public const float ShortClipSeconds = 1f;
        public const float VorbisQuality = 0.7f;
        private const string AndroidPlatform = "Android";

        [MenuItem("Tools/VR Battlegrounds/Audio/Apply SFX Import Settings")]
        private static void ApplyToSelection()
        {
            string[] paths = Selection.objects.Select(AssetDatabase.GetAssetPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            if (paths.Length == 0)
            {
                VrBattlegrounds.Core.GameLog.Error("Apply SFX Import Settings: выделите звуки или папки в Project.");
                return;
            }
            VrBattlegrounds.Core.GameLog.Debug.Info(Newtonsoft.Json.JsonConvert.SerializeObject(Apply(paths), Newtonsoft.Json.Formatting.Indented));
        }

        /// <summary>
        /// Применить настройки к клипам (пути ассетов) и ко всем клипам в папках. Отчёт: <c>changed</c>, <c>unchanged</c>,
        /// <c>failures</c>, <c>passed</c>. Для <c>execute_code</c>: <c>SfxImportSettings.Apply("Assets/Audio/SFX/...")</c>.
        /// </summary>
        public static Dictionary<string, object> Apply(params string[] paths)
        {
            var changed = new List<string>(); var unchanged = new List<string>(); var failures = new List<string>();
            foreach (string path in Expand(paths, failures))
            {
                try
                {
                    if (Configure(path)) changed.Add(path); else unchanged.Add(path);
                }
                catch (Exception exception) { failures.Add(path + ": " + exception.Message); }
            }
            return new Dictionary<string, object>
            {
                ["passed"] = failures.Count == 0, ["changed"] = changed, ["unchanged"] = unchanged, ["failures"] = failures,
                ["rule"] = $"mono, Vorbis {VorbisQuality:0.##}, DecompressOnLoad < {ShortClipSeconds:0.#} с, иначе CompressedInMemory"
            };
        }

        /// <summary>Текущие настройки клипа одной строкой (readback).</summary>
        public static string Describe(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter ?? throw new InvalidOperationException("не звук");
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            return $"mono={importer.forceToMono} load={s.loadType} format={s.compressionFormat} quality={s.quality:0.##} " +
                   $"length={(clip != null ? clip.length : -1f):0.###} channels={(clip != null ? clip.channels : -1)}";
        }

        private static IEnumerable<string> Expand(IEnumerable<string> paths, List<string> failures)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string raw in paths)
            {
                string path = raw.Replace('\\', '/').TrimEnd('/');
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { path }))
                        result.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else if (AssetImporter.GetAtPath(path) is AudioImporter) result.Add(path);
                else failures.Add(path + ": не звук и не папка");
            }
            return result;
        }

        /// <summary>true — настройки изменены и клип переимпортирован.</summary>
        private static bool Configure(string path)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new InvalidOperationException("клип не загружается");
            AudioClipLoadType load = clip.length < ShortClipSeconds ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
            bool dirty = false;
            if (!importer.forceToMono) { importer.forceToMono = true; dirty = true; }
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (Adjust(ref settings, load)) { importer.defaultSampleSettings = settings; dirty = true; }
            if (importer.ContainsSampleSettingsOverride(AndroidPlatform))
            {
                AudioImporterSampleSettings android = importer.GetOverrideSampleSettings(AndroidPlatform);
                if (Adjust(ref android, load)) { importer.SetOverrideSampleSettings(AndroidPlatform, android); dirty = true; }
            }
            if (dirty) importer.SaveAndReimport();
            return dirty;
        }

        private static bool Adjust(ref AudioImporterSampleSettings settings, AudioClipLoadType load)
        {
            bool dirty = settings.loadType != load || settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                         Mathf.Abs(settings.quality - VorbisQuality) > 0.001f;
            settings.loadType = load;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = VorbisQuality;
            return dirty;
        }
    }
}
