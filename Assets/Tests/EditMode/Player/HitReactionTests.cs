using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Реакция тела на попадание (T-37, пункт 2): короткий наклон торса по направлению пули, сила — от
    /// толчка попадания, с пределом; затухает сам. Что кисти остаются у контроллеров, проверяется в шлеме —
    /// IK рук решает UltimateXR в рантайме.
    /// </summary>
    public class HitReactionTests
    {
        [Test]
        public void Наклон_быстро_нарастает_и_затухает()
        {
            Assert.AreEqual(0f, HitReaction.Curve(0f), 1e-4f, "Наклон до попадания.");
            Assert.AreEqual(1f, HitReaction.Curve(HitReaction.Attack), 1e-3f, "Нет пика после нарастания.");
            Assert.Greater(HitReaction.Curve(HitReaction.Attack * 2f), HitReaction.Curve(HitReaction.Duration * 0.9f), "Наклон не затухает.");
            Assert.AreEqual(0f, HitReaction.Curve(HitReaction.Duration), 1e-4f, "Наклон не вернулся к нулю — тело останется кривым.");
        }

        [Test]
        public void Угол_от_силы_попадания_с_пределом()
        {
            Assert.AreEqual(HitReaction.MinAngle, HitReaction.AngleFor(0f), 1e-3f);
            Assert.AreEqual(HitReaction.MaxAngle, HitReaction.AngleFor(10000f), 1e-3f, "Угол не ограничен.");
            Assert.Greater(HitReaction.AngleFor(DeathImpact.Magnitude(25f, 400f)), HitReaction.AngleFor(DeathImpact.Magnitude(5f, 300f)),
                "Винтовка наклоняет не сильнее слабого пистолета.");
        }

        [Test]
        public void Торс_уходит_по_направлению_пули()
        {
            var go = new GameObject("Torso");
            try
            {
                Vector3 axis = HitReaction.AxisFor(Vector3.forward * 30f);
                go.transform.rotation = Quaternion.AngleAxis(8f, axis) * Quaternion.identity;
                Assert.Greater(go.transform.up.z, 0f, "Пуля летит вперёд, а торс наклоняется назад — навстречу выстрелу.");
                Assert.AreEqual(Vector3.zero, HitReaction.AxisFor(Vector3.up), "Вертикальная пуля даёт наклон без направления.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
