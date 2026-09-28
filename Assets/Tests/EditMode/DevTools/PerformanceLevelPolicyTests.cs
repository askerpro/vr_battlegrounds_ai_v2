using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Уровень производительности шлема (<see cref="PerformanceLevelInstaller"/>): что просим и
    /// когда просьбу повторяем. Сам вызов Oculus проверяется только на шлеме — по строке
    /// «Уровни шлема» в логе и по <c>ovr_cpu_lvl</c> стресс-теста.
    /// </summary>
    public class PerformanceLevelPolicyTests
    {
        [TestCase(-3, 0)]
        [TestCase(0, 0)]
        [TestCase(4, 4)]
        [TestCase(9, 4)]
        public void Уровень_в_допустимом_диапазоне_Oculus(int requested, int expected)
        {
            Assert.That(PerformanceLevelPolicy.Clamp(requested), Is.EqualTo(expected));
        }

        [Test]
        public void По_умолчанию_CPU_поднят_GPU_системный()
        {
            // Замер: главный поток упирается в бюджет при ovr_cpu_lvl = 2, GPU загружен на 50–75%.
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            try
            {
                Assert.That(settings.CpuPerformanceLevel, Is.EqualTo(4));
                Assert.That(settings.GpuPerformanceLevel, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Новый_дисплей_ставит_уровень_сразу()
        {
            Assert.IsTrue(PerformanceLevelPolicy.ShouldApply(true, 4, 4, 0f, 0f, 10f));
        }

        [Test]
        public void Система_держит_запрошенный_уровень_не_трогаем()
        {
            Assert.IsFalse(PerformanceLevelPolicy.ShouldApply(false, 4, 4, 100f, 0f, 10f));
        }

        [Test]
        public void Уровень_неизвестен_не_трогаем()
        {
            Assert.IsFalse(PerformanceLevelPolicy.ShouldApply(false, 4, -1, 100f, 0f, 10f));
        }

        [Test]
        public void Уровень_сбит_повторяем_не_чаще_интервала()
        {
            Assert.IsFalse(PerformanceLevelPolicy.ShouldApply(false, 4, 2, 5f, 0f, 10f), "Рано: спор с термоуправлением");
            Assert.IsTrue(PerformanceLevelPolicy.ShouldApply(false, 4, 2, 10f, 0f, 10f));
        }
    }
}
