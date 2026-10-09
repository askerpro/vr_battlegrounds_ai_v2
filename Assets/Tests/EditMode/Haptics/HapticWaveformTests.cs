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
    /// местах. Структурные правила, а не подобранные числа: числа подбираются в шлеме. Отклик взаимодействий
    /// (<see cref="InteractionFeedbackConfig" />) — единственный ассет, он ссылается только на префабы отклика, а их
    /// <see cref="HapticPlayer" /> — только на формы проекта.
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

        private const string FeedbackFolder = "Assets/Prefabs/Feedback/";

        [Test]
        public void Конфиг_отклика_грузится_и_ссылается_на_префабы_отклика()
        {
            var config = Resources.Load<InteractionFeedbackConfig>(nameof(InteractionFeedbackConfig));
            Assert.IsNotNull(config, "Нет Assets/Resources/InteractionFeedbackConfig.asset — отклика взаимодействий не будет.");

            var errors = new List<string>();
            SerializedProperty property = new SerializedObject(config).GetIterator();
            while (property.NextVisible(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null ||
                    property.name == "m_Script") continue;
                string path = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (!path.StartsWith(FeedbackFolder)) errors.Add($"{property.propertyPath}: отклик вне {FeedbackFolder} — {path}");
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Вибрация_префабов_отклика_играет_формы_проекта()
        {
            var errors = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { FeedbackFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (HapticPlayer player in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<HapticPlayer>(true))
                {
                    UxrHapticClip clip = player.Clip;
                    if (clip == null || !clip.HasWaveform) errors.Add($"{path}/{player.name}: клип без формы — вибрации не будет");
                    else if (!AssetDatabase.GetAssetPath(clip.Waveform).StartsWith("Assets/Data/"))
                        errors.Add($"{path}/{player.name}: форма вне Assets/Data — {AssetDatabase.GetAssetPath(clip.Waveform)}");
                }
            }

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
