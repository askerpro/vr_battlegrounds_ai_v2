using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Haptics;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Формы вибрации (<see cref="UxrHapticWaveform" />, SDK-патч 66) — общий реестр проекта, ими пользуются клипы во многих
    /// местах. Структурные правила, а не подобранные числа: числа подбираются в шлеме. Роли (<see cref="HapticRoles" />) —
    /// единственный ассет, его клипы ссылаются только на существующие формы.
    /// </summary>
    public class HapticWaveformTests
    {
        /// <summary>Короче импульс на LRA Quest 2 почти не ощущается (исследование, п. 4).</summary>
        private const int MinPulseMs = 30;

        private static IEnumerable<UxrHapticWaveform> AllWaveforms() =>
            AssetDatabase.FindAssets("t:" + nameof(UxrHapticWaveform), new[] { "Assets/Data" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<UxrHapticWaveform>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(w => w != null);

        [Test]
        public void Формы_корректны()
        {
            var errors = new List<string>();
            foreach (UxrHapticWaveform waveform in AllWaveforms())
            {
                List<UxrHapticWaveform.Segment> segments = waveform.Segments;
                if (segments.Count == 0 || segments.All(s => s.Amplitude <= 0f)) errors.Add($"{waveform.name}: пустая форма");
                if (segments.Any(s => s.Amplitude < 0f || s.Amplitude > 1f)) errors.Add($"{waveform.name}: сила вне 0..1");
                if (segments.Any(s => s.DurationMs <= 0)) errors.Add($"{waveform.name}: отрезок нулевой длины");

                int run = 0;
                foreach (UxrHapticWaveform.Segment segment in segments.Append(new UxrHapticWaveform.Segment(0f, 0)))
                {
                    if (segment.Amplitude > 0f) { run += segment.DurationMs; continue; }
                    if (run > 0 && run < MinPulseMs) errors.Add($"{waveform.name}: импульс {run} мс короче {MinPulseMs} мс");
                    run = 0;
                }
            }

            Assert.IsNotEmpty(AllWaveforms(), "В Assets/Data нет ни одной формы вибрации.");
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Роли_грузятся_и_ссылаются_на_формы_проекта()
        {
            var roles = Resources.Load<HapticRoles>(nameof(HapticRoles));
            Assert.IsNotNull(roles, "Нет Assets/Resources/HapticRoles.asset — вибрации взаимодействий не будет.");

            var so = new SerializedObject(roles);
            var errors = new List<string>();
            SerializedProperty property = so.GetIterator();
            while (property.NextVisible(true))
            {
                if (property.name != "_waveform" || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object waveform = property.objectReferenceValue;
                if (waveform != null && !AssetDatabase.GetAssetPath(waveform).StartsWith("Assets/Data/"))
                    errors.Add($"{property.propertyPath}: форма вне Assets/Data — {AssetDatabase.GetAssetPath(waveform)}");
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
