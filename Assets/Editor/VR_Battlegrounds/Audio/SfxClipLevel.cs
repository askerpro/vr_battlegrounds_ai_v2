using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Audio
{
    /// <summary>
    /// Уровень и тождество исходных файлов звуков для проверок сборщиков (тот же смысл, что
    /// <c>Tools/Audio/audio_cut.py analyze</c>): пик в dBFS и флаг «почти тишина», побайтное совпадение двух клипов.
    ///
    /// <para>
    /// Уровень WAV читается из самого файла, а не через <c>AudioClip.GetData</c>: дефект — в исходнике пака, а
    /// <c>GetData</c> требует Decompress On Load (у Compressed In Memory/Streaming данных нет без смены импорта),
    /// отдаёт сигнал после Vorbis и нормализации импорта (<c>normalize</c> поднимает тихий шум) и зависит от загрузки
    /// данных клипа. Чтение файла не меняет ассет и совпадает с офлайн-анализом. Не-WAV (mp3, ogg) — запасной путь
    /// через <c>GetData</c>, если клип распакован; иначе уровень «не измерен», это не ошибка.
    /// </para>
    /// </summary>
    public static class SfxClipLevel
    {
        /// <summary>Порог «почти тишины» по пику — как у <c>audio_cut.py analyze</c> по умолчанию.</summary>
        public const float NearSilenceDbfs = -40f;

        /// <summary>Пик клипа в dBFS; false — уровень не измерен (<paramref name="how"/> — почему).</summary>
        public static bool TryPeakDbfs(AudioClip clip, out float peakDbfs, out string how)
        {
            peakDbfs = float.NegativeInfinity;
            string path = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
            if (string.IsNullOrEmpty(path)) { how = "нет файла"; return false; }
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    peakDbfs = ToDbfs(WavPeak(File.ReadAllBytes(FullPath(path))));
                    how = "файл WAV";
                    return true;
                }
                catch (Exception exception) { how = "WAV не прочитан: " + exception.Message; return false; }
            }
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad) { how = "не WAV и не распакован — уровень не измерен"; return false; }
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) { how = "GetData не отдал данные"; return false; }
            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            peakDbfs = ToDbfs(peak);
            how = "GetData (после импорта)";
            return true;
        }

        /// <summary>Два клипа — один ассет или файлы с одинаковыми байтами.</summary>
        public static bool SameBytes(AudioClip a, AudioClip b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            string pa = AssetDatabase.GetAssetPath(a), pb = AssetDatabase.GetAssetPath(b);
            if (string.IsNullOrEmpty(pa) || string.IsNullOrEmpty(pb)) return false;
            if (pa == pb) return true;
            FileInfo fa = new FileInfo(FullPath(pa)), fb = new FileInfo(FullPath(pb));
            if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
            byte[] ba = File.ReadAllBytes(fa.FullName), bb = File.ReadAllBytes(fb.FullName);
            for (int i = 0; i < ba.Length; i++) if (ba[i] != bb[i]) return false;
            return true;
        }

        private static float ToDbfs(float peak) => peak > 0f ? 20f * Mathf.Log10(peak) : float.NegativeInfinity;

        private static string FullPath(string assetPath) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

        /// <summary>Пик PCM 8/16/24/32 бит или float 32 (WAVE_FORMAT_EXTENSIBLE — по SubFormat), 0..1.</summary>
        private static float WavPeak(byte[] data)
        {
            if (data.Length < 12 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' || data[8] != 'W')
                throw new InvalidDataException("не RIFF/WAVE");
            int pos = 12, tag = 0, bits = 0, dataStart = -1, dataSize = 0;
            while (pos + 8 <= data.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                int size = BitConverter.ToInt32(data, pos + 4);
                if (id == "fmt ")
                {
                    tag = BitConverter.ToUInt16(data, pos + 8);
                    bits = BitConverter.ToUInt16(data, pos + 22);
                    if (tag == 0xFFFE && size >= 26) tag = BitConverter.ToUInt16(data, pos + 8 + 24);
                }
                else if (id == "data") { dataStart = pos + 8; dataSize = Math.Min(size, data.Length - dataStart); }
                pos += 8 + size + (size & 1);
            }
            if (dataStart < 0 || bits == 0) throw new InvalidDataException("нет fmt/data");
            int width = bits / 8, end = dataStart + dataSize - dataSize % width;
            double peak = 0;
            for (int i = dataStart; i < end; i += width)
            {
                double v;
                if (tag == 3 && bits == 32) v = BitConverter.ToSingle(data, i);
                else if (tag == 1 && bits == 8) v = (data[i] - 128) / 128.0;
                else if (tag == 1 && bits == 16) v = BitConverter.ToInt16(data, i) / 32768.0;
                else if (tag == 1 && bits == 24) v = ((data[i] | (data[i + 1] << 8) | (data[i + 2] << 16)) << 8 >> 8) / 8388608.0;
                else if (tag == 1 && bits == 32) v = BitConverter.ToInt32(data, i) / 2147483648.0;
                else throw new InvalidDataException($"формат {tag}/{bits} бит не поддерживается");
                peak = Math.Max(peak, Math.Abs(v));
            }
            return (float)peak;
        }
    }
}
