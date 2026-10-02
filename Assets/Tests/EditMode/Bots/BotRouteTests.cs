using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Bots;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>Ходьба бота по маршруту, места на базе и отбор геометрии для навигационной сетки (T-48).</summary>
    public class BotRouteTests
    {
        private static readonly List<Vector3> Corners = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 3)
        };

        [Test]
        public void Шаг_идёт_по_отрезку()
        {
            int next = 1;
            Vector3 p = BotRoute.Step(Vector3.zero, Corners, ref next, 1f, out bool arrived);
            Assert.That(Vector3.Distance(p, new Vector3(1, 0, 0)), Is.LessThan(1e-4f));
            Assert.AreEqual(1, next);
            Assert.IsFalse(arrived);
        }

        [Test]
        public void Остаток_шага_переходит_за_угол()
        {
            int next = 1;
            Vector3 p = BotRoute.Step(new Vector3(1, 0, 0), Corners, ref next, 2f, out bool arrived);
            Assert.That(Vector3.Distance(p, new Vector3(2, 0, 1)), Is.LessThan(1e-4f));
            Assert.AreEqual(2, next);
            Assert.IsFalse(arrived);
        }

        [Test]
        public void Последний_угол_приход_без_перелёта()
        {
            int next = 1;
            Vector3 p = BotRoute.Step(Vector3.zero, Corners, ref next, 100f, out bool arrived);
            Assert.That(Vector3.Distance(p, Corners[2]), Is.LessThan(1e-4f));
            Assert.IsTrue(arrived);

            p = BotRoute.Step(p, Corners, ref next, 1f, out arrived);
            Assert.That(Vector3.Distance(p, Corners[2]), Is.LessThan(1e-4f), "после прихода стоит");
            Assert.IsTrue(arrived);
        }

        [Test]
        public void Пустой_маршрут_стоит_на_месте()
        {
            int next = 0;
            Vector3 start = new Vector3(5, 0, 5);
            Assert.AreEqual(start, BotRoute.Step(start, new List<Vector3>(), ref next, 1f, out bool arrived));
            Assert.IsTrue(arrived);
            Assert.AreEqual(start, BotRoute.Step(start, null, ref next, 1f, out arrived));
            Assert.IsTrue(arrived);
        }

        [Test]
        public void Места_на_базе_разные_и_рядом_с_центром()
        {
            Assert.AreEqual(Vector3.zero, BotHomeSlot.Offset(0));

            var seen = new List<Vector3>();
            for (int slot = 0; slot < 8; slot++)
            {
                Vector3 o = BotHomeSlot.Offset(slot);
                Assert.AreEqual(0f, o.y);
                Assert.That(o.magnitude, Is.LessThanOrEqualTo(BotHomeSlot.Spacing * 2.01f), $"место {slot} далеко от центра");
                foreach (Vector3 other in seen)
                    Assert.That(Vector3.Distance(o, other), Is.GreaterThan(BotHomeSlot.Spacing * 0.5f), $"место {slot} совпадает с другим");
                seen.Add(o);
            }
        }

        [Test]
        public void В_сетку_идёт_только_неподвижная_геометрия_карты()
        {
            Assert.IsTrue(BotNavMeshSources.Include(enabled: true, isTrigger: false, onEnvironmentLayer: true, hasRigidbody: false, onAvatarOrItem: false));
            Assert.IsFalse(BotNavMeshSources.Include(false, false, true, false, false), "выключенный");
            Assert.IsFalse(BotNavMeshSources.Include(true, true, true, false, false), "триггер — зона спавна, сетка");
            Assert.IsFalse(BotNavMeshSources.Include(true, false, false, false, false), "чужой слой (хитбоксы, трупы)");
            Assert.IsFalse(BotNavMeshSources.Include(true, false, true, true, false), "подвижное");
            Assert.IsFalse(BotNavMeshSources.Include(true, false, true, false, true), "аватар, оружие");
        }
    }
}
