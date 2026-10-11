using System;
using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Ассет запасных клипов для вибраций UltimateXR без формы (<see cref="SdkHapticFallbacks" />, SDK-патч 65,
    /// <c>tasks/haptics-system/Details.md</c> §5.3). Структура, а не подобранные числа: силы и формы подбираются в шлеме, и
    /// правка ассета не должна ронять тест, пока клипы ссылаются на формы проекта.
    /// </summary>
    public class SdkHapticFallbacksTests
    {
        private const string WaveformFolder = "Assets/Data/Haptics/Waveforms/";

        /// <summary>
        /// Без этих клипов в игре молчат клики хвата и UI, выстрел SDK (ShotHapticClip стволов) и сырые запросы планшета и
        /// перетаскивания UI.
        /// </summary>
        private static readonly UxrHapticClipType[] Required =
        {
            UxrHapticClipType.Click, UxrHapticClipType.Shot, UxrHapticClipType.ShotBig, UxrHapticClipType.ShotBigger,
        };

        private static SdkHapticFallbacks Load()
        {
            var fallbacks = Resources.Load<SdkHapticFallbacks>(nameof(SdkHapticFallbacks));
            Assert.IsNotNull(fallbacks, "Нет Assets/Resources/SdkHapticFallbacks.asset — вибрации UltimateXR без формы будут тихими.");
            return fallbacks;
        }

        private static IEnumerable<(string name, UxrHapticClip clip)> Clips(SdkHapticFallbacks fallbacks)
        {
            foreach (UxrHapticClipType type in Enum.GetValues(typeof(UxrHapticClipType)))
                if (type != UxrHapticClipType.None) yield return (type.ToString(), fallbacks.Get(type));
            yield return ("Raw", fallbacks.Raw);
        }

        [Test]
        public void У_каждого_типа_SDK_есть_место_для_запасного_клипа()
        {
            SdkHapticFallbacks fallbacks = Load();
            var missing = new List<string>();
            foreach ((string name, UxrHapticClip clip) in Clips(fallbacks))
                if (clip == null) missing.Add(name);

            Assert.IsEmpty(missing, "Тип UltimateXR без поля в SdkHapticFallbacks — его вибрация всегда тихая: " +
                                    string.Join(", ", missing));
        }

        [Test]
        public void Заданные_клипы_играют_формы_проекта()
        {
            SdkHapticFallbacks fallbacks = Load();
            var errors = new List<string>();
            foreach ((string name, UxrHapticClip clip) in Clips(fallbacks))
            {
                if (clip == null || !clip.HasWaveform) continue;
                string path = AssetDatabase.GetAssetPath(clip.Waveform);
                if (!path.StartsWith(WaveformFolder)) errors.Add($"{name}: форма вне {WaveformFolder} — {path}");
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Обязательные_клипы_заданы()
        {
            SdkHapticFallbacks fallbacks = Load();
            var missing = new List<string>();
            foreach (UxrHapticClipType type in Required)
            {
                UxrHapticClip clip = fallbacks.Get(type);
                if (clip == null || !clip.HasWaveform) missing.Add(type.ToString());
            }
            if (fallbacks.Raw == null || !fallbacks.Raw.HasWaveform) missing.Add("Raw");

            Assert.IsEmpty(missing, "Запасной клип без формы — эта вибрация SDK в игре молчит: " + string.Join(", ", missing));
        }
    }
}
