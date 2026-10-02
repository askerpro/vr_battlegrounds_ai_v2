using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Разброс по CS2 (T-38): первый выстрел — в конусе «неточность стоя + spread», очередь растит конус до потолка,
    /// пауза за <c>recovery_time</c> возвращает к 10 %, случайность детерминирована по (зерно, выстрел).
    /// Числа — MP9 и AK-47 из weapons.vdata CS2.
    /// </summary>
    public class WeaponAccuracyTests
    {
        private const float Mp9Interval = 0.07f; // 857 в минуту

        private static SpreadPattern Mp9() => new SpreadPattern(spread: 0.6f, inaccuracyStand: 9f, inaccuracyMove: 29.04f, inaccuracyFire: 3.7f, recoveryTime: 0.25789f);
        private static SpreadPattern Ak() => new SpreadPattern(spread: 0.6f, inaccuracyStand: 6.41f, inaccuracyMove: 175.06f, inaccuracyFire: 7.8f, recoveryTime: 0.368f);

        [Test]
        public void Первый_выстрел_в_конусе_CS2()
        {
            var accuracy = new WeaponAccuracy(Ak());
            float cone = accuracy.Cone(moveSpeed: 0f);
            Assert.AreEqual(6.41f + 0.6f, cone, 1e-4f, "Первый выстрел стоя: неточность стоя + spread, без штрафа.");

            float maxSeen = 0f;
            for (uint shot = 0; shot < 500; shot++)
            {
                Vector2 offset = WeaponAccuracy.Offset(accuracy.Inaccuracy(0f), Ak().Spread, seed: 7u, shot);
                maxSeen = Mathf.Max(maxSeen, offset.magnitude);
                Assert.LessOrEqual(offset.magnitude, cone + 1e-4f, $"Выстрел {shot}: отклонение {offset.magnitude:F2} мрад вне конуса {cone:F2}.");
            }
            Assert.Greater(maxSeen, cone * 0.6f, "Отклонения почти нулевые — разброса нет.");
            Assert.Less(WeaponAccuracy.ToDegrees(cone), 0.5f, "Первый выстрел винтовки CS2 — доли градуса.");
        }

        [Test]
        public void Очередь_растит_конус_монотонно_до_потолка()
        {
            var accuracy = new WeaponAccuracy(Mp9());
            float ceiling = accuracy.SteadyPenalty(Mp9Interval);
            float previous = -1f;
            for (int i = 0; i < 40; i++)
            {
                float before = accuracy.Penalty;
                // Строго растёт, пока не упёрся в потолок с точностью float; дальше — не падает.
                if (i < 15) Assert.Greater(before, previous, $"Выстрел {i + 1}: штраф перед выстрелом не вырос.");
                else Assert.GreaterOrEqual(before, previous - 1e-4f, $"Выстрел {i + 1}: штраф в очереди упал.");
                Assert.LessOrEqual(before, ceiling + 1e-3f, $"Выстрел {i + 1}: штраф {before:F2} выше потолка {ceiling:F2}.");
                previous = before;
                accuracy.Shot();
                accuracy.Tick(Mp9Interval);
            }
            Assert.Greater(previous, ceiling * 0.95f, "Длинная очередь не подходит к потолку.");
            Assert.Less(WeaponAccuracy.ToDegrees(accuracy.Cone(0f)), 1f, "Конус очереди MP9 в CS2 — меньше градуса.");
        }

        [Test]
        public void Пауза_восстанавливает_за_recovery_time()
        {
            var accuracy = new WeaponAccuracy(Mp9());
            for (int i = 0; i < 20; i++) accuracy.Shot();
            float peak = accuracy.Penalty;
            accuracy.Tick(Mp9().RecoveryTime);
            Assert.AreEqual(peak * WeaponAccuracy.RecoveredFraction, accuracy.Penalty, peak * 0.01f, "За recovery_time штраф спадает до 10 %.");

            for (int i = 0; i < 10; i++) accuracy.Tick(Mp9().RecoveryTime);
            Assert.AreEqual(0f, accuracy.Penalty, 1e-3f);
            Assert.AreEqual(9f + 0.6f, accuracy.Cone(0f), 1e-3f, "После паузы выстрел снова точный, как первый.");
        }

        [Test]
        public void Шаг_по_комнате_почти_не_штрафует()
        {
            var accuracy = new WeaponAccuracy(Ak());
            Assert.AreEqual(0f, accuracy.MovePenalty(0.3f), 1e-5f, "Покачивание головы — не движение.");
            float walk = accuracy.MovePenalty(1.5f);
            Assert.Greater(walk, 0f, "Шаг не даёт штрафа вовсе.");
            Assert.LessOrEqual(accuracy.MovePenalty(10f), Ak().InaccuracyMove * WeaponAccuracy.MoveShare + 1e-4f, "Штраф движения выше доли CS2.");
            Assert.Less(WeaponAccuracy.ToDegrees(walk), 1f, "Шаг с автоматом уводит больше градуса — в VR это много.");
        }

        [Test]
        public void Случайность_детерминирована()
        {
            for (uint shot = 0; shot < 50; shot++)
            {
                Assert.AreEqual(WeaponAccuracy.Offset(10f, 2f, 42u, shot, 3u), WeaponAccuracy.Offset(10f, 2f, 42u, shot, 3u),
                                "Один и тот же (зерно, выстрел, дробина) — разное отклонение.");
            }
            Assert.AreNotEqual(WeaponAccuracy.Offset(10f, 2f, 42u, 0u), WeaponAccuracy.Offset(10f, 2f, 42u, 1u), "Соседние выстрелы — одно отклонение.");
            Assert.AreNotEqual(WeaponAccuracy.Offset(10f, 2f, 42u, 0u), WeaponAccuracy.Offset(10f, 2f, 43u, 0u), "Разные зёрна — одно отклонение.");
        }

        [Test]
        public void Дробь_одна_неточность_на_залп()
        {
            Vector2 inaccuracy = WeaponAccuracy.InaccuracyOffset(7f, 5u, 3u);
            for (uint pellet = 0; pellet < 9; pellet++)
            {
                Vector2 offset = WeaponAccuracy.Offset(7f, 40f, 5u, 3u, pellet);
                Assert.LessOrEqual((offset - inaccuracy).magnitude, 40f + 1e-3f, $"Дробина {pellet}: вне конуса spread вокруг общей неточности.");
            }
            Assert.AreNotEqual(WeaponAccuracy.Offset(7f, 40f, 5u, 3u, 0u), WeaponAccuracy.Offset(7f, 40f, 5u, 3u, 1u), "Дробины легли в одну точку.");
        }

        [Test]
        public void Поворот_по_смещению()
        {
            Vector3 up = WeaponAccuracy.ToRotation(new Vector2(0f, 10f)) * Vector3.forward;
            Vector3 right = WeaponAccuracy.ToRotation(new Vector2(10f, 0f)) * Vector3.forward;
            Assert.AreEqual(0.01f, up.y / up.z, 1e-4f, "+y — вверх на тангенс 0,01.");
            Assert.AreEqual(0.01f, right.x / right.z, 1e-4f, "+x — вправо на тангенс 0,01.");
        }
    }
}
