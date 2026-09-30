using NUnit.Framework;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Накопление отдачи (T-38): очередь уводит ствол всё выше до потолка, пауза возвращает его, одной рукой
    /// уводит сильнее, картина детерминирована (у наблюдателя и стрелка одна и та же).
    /// </summary>
    public class RecoilPatternTests
    {
        private const float ShotInterval = 0.1f; // 600 выстрелов в минуту

        private static RecoilPattern Ak() => new RecoilPattern(kickDegrees: 1.2f, maxPitchDegrees: 8f, maxYawDegrees: 3f, recoveryTime: 0.5f);

        [Test]
        public void Очередь_растит_подброс_монотонно_до_потолка()
        {
            RecoilPattern recoil = Ak();
            float previous = 0f;
            for (int i = 0; i < 30; i++)
            {
                recoil.Shot();
                float pitch = recoil.Pitch(oneHand: false);
                Assert.Greater(pitch, previous, $"Выстрел {i + 1}: подброс не вырос ({previous:F2} → {pitch:F2}).");
                Assert.LessOrEqual(pitch, recoil.MaxPitchDegrees + 1e-4f, $"Выстрел {i + 1}: подброс выше потолка.");
                previous = pitch;
                recoil.Tick(ShotInterval);
            }

            Assert.Greater(previous, recoil.MaxPitchDegrees * 0.6f, "Длинная очередь не подходит к потолку — отдача почти не копится.");
        }

        [Test]
        public void Первый_выстрел_даёт_заданный_подброс()
        {
            RecoilPattern recoil = Ak();
            recoil.Shot();
            Assert.AreEqual(recoil.KickDegrees, recoil.Pitch(false), 0.15f);
        }

        [Test]
        public void Пауза_возвращает_ствол()
        {
            RecoilPattern recoil = Ak();
            for (int i = 0; i < 40; i++) recoil.Shot();
            recoil.Tick(recoil.RecoveryTime);

            Assert.AreEqual(0f, recoil.Pitch(false), 1e-3f, "После паузы ствол не вернулся.");
            Assert.AreEqual(0f, recoil.Yaw(false), 1e-3f);
        }

        [Test]
        public void Одиночные_с_паузой_не_копятся()
        {
            RecoilPattern recoil = Ak();
            recoil.Shot();
            float first = recoil.Pitch(false);
            recoil.Tick(0.6f);
            recoil.Shot();
            Assert.AreEqual(first, recoil.Pitch(false), 1e-3f, "Одиночный выстрел после паузы уводит выше первого.");
        }

        [Test]
        public void Одной_рукой_сильнее()
        {
            RecoilPattern recoil = Ak();
            for (int i = 0; i < 5; i++) recoil.Shot();
            Assert.Greater(recoil.Pitch(oneHand: true), recoil.Pitch(oneHand: false));
        }

        [Test]
        public void Рыскание_по_картине_а_не_случайно()
        {
            RecoilPattern a = Ak(), b = Ak();
            bool sideways = false;
            for (int i = 0; i < 15; i++)
            {
                a.Shot();
                b.Shot();
                Assert.AreEqual(a.Yaw(false), b.Yaw(false), 1e-5f, "Одинаковая очередь — разное рыскание: картина случайна.");
                Assert.LessOrEqual(System.Math.Abs(a.Yaw(false)), a.MaxYawDegrees + 1e-4f);
                sideways |= System.Math.Abs(a.Yaw(false)) > 0.5f;
            }
            Assert.IsTrue(sideways, "Очередь не уводит ствол вбок.");
        }
    }
}
