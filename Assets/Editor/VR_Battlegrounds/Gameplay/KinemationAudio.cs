using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Звуки оружия KINEMATION под Quest (T-39): моно, короткий хвост, сжатие.
    ///
    /// <para>
    /// Звуки пака — стерео 48 кГц по 5–10 с, перезарядка — одним файлом на весь клип. В VR звук 3D (стерео
    /// не пространственен), а действия атомарные: снять магазин, вставить, оттянуть затвор, отпустить. Поэтому
    /// из файла пака вырезается кусок: сведение в моно, затухание в конце, запись WAV в
    /// <c>Assets/Audio/SFX/Weapons/Kinemation/&lt;ствол&gt;/</c>. Файлы пака не меняются.
    /// </para>
    ///
    /// <para>
    /// Где резать перезарядку, говорит клип оружия того же действия: звук пак играет с начала клипа, а
    /// время, когда магазин отошёл от гнезда или вернулся, считается по кости (<see cref="KinemationWeapon.Excursion" />).
    /// Начало куска притягивается к ближайшему всплеску громкости (±<see cref="SnapWindow" /> с).
    /// </para>
    /// </summary>
    public static class KinemationAudio
    {
        public const string Root = "Assets/Audio/SFX/Weapons/Kinemation";
        public const float SnapWindow = 0.3f;
        private const float Fade = 0.08f;
        private const float PreRoll = 0.03f;

        /// <summary>Кусок <paramref name="length" /> с от <paramref name="start" /> (моно, затухание в конце).</summary>
        public static AudioClip Cut(string sourcePath, float start, float length, string folder, string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath) ?? throw new FileNotFoundException(sourcePath);
            float[] mono = Mono(src);
            int freq = src.frequency;
            int from = Mathf.Clamp(Mathf.RoundToInt(start * freq), 0, mono.Length - 1);
            int count = Mathf.Clamp(Mathf.RoundToInt(length * freq), 1, mono.Length - from);

            var data = new float[count];
            Array.Copy(mono, from, data, 0, count);
            int fade = Mathf.Min(count, Mathf.RoundToInt(Fade * freq));
            for (int i = 0; i < fade; i++) data[count - 1 - i] *= i / (float)fade;
            int fadeIn = Mathf.Min(count, Mathf.RoundToInt(0.004f * freq)); // без щелчка на срезе
            for (int i = 0; i < fadeIn; i++) data[i] *= i / (float)fadeIn;

            return Write(data, freq, folder, name);
        }

        /// <summary>
        /// Кусок вокруг события клипа: начинается у всплеска громкости, ближайшего к <paramref name="eventTime" />.
        /// </summary>
        public static AudioClip CutAtEvent(string sourcePath, float eventTime, float length, string folder, string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath) ?? throw new FileNotFoundException(sourcePath);
            float onset = Onset(Mono(src), src.frequency, eventTime);
            return Cut(sourcePath, Mathf.Max(0f, onset - PreRoll), length, folder, name);
        }

        /// <summary>
        /// Два куска по самой длинной паузе между серединой первой и последней третью звука: у звука затвора
        /// пака (<c>ShotBoltOnly</c>) — «открыть и оттянуть» и «дослать и закрыть».
        /// </summary>
        public static (AudioClip first, AudioClip second) SplitAtGap(string sourcePath, string folder, string firstName, string secondName)
        {
            var src = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath) ?? throw new FileNotFoundException(sourcePath);
            float[] mono = Mono(src);
            int freq = src.frequency;
            float[] env = Envelope(mono, freq, 0.01f);
            float step = 0.01f;

            float end = LastLoud(env, step);
            int a = Mathf.RoundToInt(end / 3f / step), b = Mathf.RoundToInt(end * 2f / 3f / step);
            float peak = Max(env);
            int bestStart = a, bestLength = 0, run = 0;
            for (int i = a; i < b && i < env.Length; i++)
            {
                if (env[i] < peak * 0.05f) { run++; if (run > bestLength) { bestLength = run; bestStart = i - run + 1; } }
                else run = 0;
            }
            float cut = (bestLength > 0 ? bestStart + bestLength / 2f : (a + b) / 2f) * step;

            AudioClip first = Cut(sourcePath, 0f, cut, folder, firstName);
            AudioClip second = Cut(sourcePath, cut, Mathf.Max(0.05f, end + 0.15f - cut), folder, secondName);
            return (first, second);
        }

        /// <summary>Выстрел: от начала до спада громкости ниже 1 % пика (с хвостом помещения), не дольше <paramref name="maxLength" /> с.</summary>
        public static AudioClip Shot(string sourcePath, float maxLength, string folder, string name)
        {
            var src = AssetDatabase.LoadAssetAtPath<AudioClip>(sourcePath) ?? throw new FileNotFoundException(sourcePath);
            float[] env = Envelope(Mono(src), src.frequency, 0.01f);
            float peak = Max(env);
            int last = 0;
            for (int i = 0; i < env.Length; i++) if (env[i] > peak * 0.01f) last = i;
            float length = Mathf.Min(maxLength, (last + 1) * 0.01f + Fade);
            return Cut(sourcePath, 0f, length, folder, name);
        }

        // ── Анализ ──────────────────────────────────────────────────────────────

        private static float[] Mono(AudioClip clip)
        {
            clip.LoadAudioData();
            var raw = new float[clip.samples * clip.channels];
            if (!clip.GetData(raw, 0)) throw new InvalidOperationException($"{clip.name}: не читаются сэмплы (Load Type должен быть Decompress On Load)");
            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float s = 0f;
                for (int c = 0; c < clip.channels; c++) s += raw[i * clip.channels + c];
                mono[i] = s / clip.channels;
            }
            return mono;
        }

        /// <summary>RMS окнами <paramref name="window" /> с.</summary>
        private static float[] Envelope(float[] mono, int freq, float window)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(window * freq));
            var env = new float[mono.Length / w + 1];
            for (int i = 0; i < env.Length; i++)
            {
                double sum = 0;
                int n = 0;
                for (int j = i * w; j < Mathf.Min(mono.Length, (i + 1) * w); j++, n++) sum += mono[j] * mono[j];
                env[i] = n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
            }
            return env;
        }

        /// <summary>Самый резкий рост громкости в окне ±<see cref="SnapWindow" /> вокруг <paramref name="t" />.</summary>
        private static float Onset(float[] mono, int freq, float t)
        {
            const float step = 0.005f;
            float[] env = Envelope(mono, freq, step);
            int from = Mathf.Max(1, Mathf.RoundToInt((t - SnapWindow) / step));
            int to = Mathf.Min(env.Length - 1, Mathf.RoundToInt((t + SnapWindow) / step));
            int best = Mathf.Clamp(Mathf.RoundToInt(t / step), 0, env.Length - 1);
            float bestRise = 0f;
            for (int i = from; i <= to; i++)
            {
                float rise = env[i] - env[i - 1];
                if (rise > bestRise) { bestRise = rise; best = i; }
            }
            return best * step;
        }

        private static float LastLoud(float[] env, float step)
        {
            float peak = Max(env);
            int last = 0;
            for (int i = 0; i < env.Length; i++) if (env[i] > peak * 0.03f) last = i;
            return (last + 1) * step;
        }

        private static float Max(float[] a)
        {
            float m = 0f;
            foreach (float x in a) m = Mathf.Max(m, x);
            return m;
        }

        // ── Запись ──────────────────────────────────────────────────────────────

        private static AudioClip Write(float[] data, int freq, string folder, string name)
        {
            KinemationWeapon.EnsureFolder(folder);
            string path = $"{folder}/{name}.wav";

            using (var stream = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(stream))
            {
                int bytes = data.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);  // PCM
                w.Write((short)1);  // моно
                w.Write(freq);
                w.Write(freq * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (float s in data) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            float seconds = data.Length / (float)freq;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                // Как у библиотеки звуков (Docs/sound-library.md): короткое — распакованным, длинное — сжатым в памяти.
                loadType = seconds < 1f ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory,
                compressionFormat = AudioCompressionFormat.Vorbis,
                quality = 0.7f,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
                preloadAudioData = true
            };
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
