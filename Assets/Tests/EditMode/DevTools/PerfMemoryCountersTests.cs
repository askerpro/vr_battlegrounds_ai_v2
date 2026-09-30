using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Стресс-тест пишет память графики отдельно от памяти процесса: <c>gfx_mb</c> (вся графика Unity), <c>tex_mb</c>
    /// (текстуры), <c>mesh_mb</c> (меши). На Quest память общая, и <c>mem_mb</c> не отделяет графику — без этих колонок
    /// не видно, сколько стоят текстуры оружия и аватаров. Имена счётчиков сверяются с профайлером редактора: опечатка
    /// в имени молча выбросила бы колонку в «недоступные».
    /// </summary>
    public class PerfMemoryCountersTests
    {
        private static readonly Dictionary<string, string> Expected = new Dictionary<string, string>
        {
            { "gfx_mb", "Gfx Used Memory" },
            { "tex_mb", "Texture Memory" },
            { "mesh_mb", "Mesh Memory" },
        };

        [Test]
        public void Стресс_тест_пишет_память_графики()
        {
            var recorderType = typeof(VrBattlegrounds.DevTools.StressTest.PerfFrameRecorder);
            MethodInfo create = recorderType.GetMethod("CreateMetrics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(create, "PerfFrameRecorder.CreateMetrics не найден — поправь тест.");

            var stats = new Dictionary<string, string>();
            foreach (object metric in (IEnumerable)create.Invoke(null, null))
            {
                System.Type type = metric.GetType();
                string key = (string)type.GetField("Key").GetValue(metric);
                string stat = (string)type.GetField("StatName").GetValue(metric);
                stats[key] = stat;
            }

            var available = new HashSet<string>();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle handle in handles)
                available.Add(ProfilerRecorderHandle.GetDescription(handle).Name);

            foreach (var pair in Expected)
            {
                Assert.IsTrue(stats.TryGetValue(pair.Key, out string stat), $"Стресс-тест не пишет {pair.Key} ({pair.Value}).");
                Assert.AreEqual(pair.Value, stat, $"{pair.Key}: не тот счётчик.");
                Assert.IsTrue(available.Contains(stat), $"Счётчика «{stat}» нет в профайлере — колонка {pair.Key} выпадет.");
            }
        }
    }
}
