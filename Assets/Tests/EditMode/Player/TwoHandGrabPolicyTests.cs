using System.Collections.Generic;
using NUnit.Framework;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Вторая рука не перехватывает оружие у первой, если рядом есть свободная точка хвата.
    ///
    /// <para>
    /// <b>Дефект.</b> Держу пистолет одной рукой, подношу вторую к дополнительной точке —
    /// оружие перескакивает во вторую руку. <c>UxrGrabManager.GetClosestGrabbableObject</c>
    /// сравнивает точки одного предмета только по расстоянию, и занятая основная точка
    /// компактного пистолета оказывается ближе свободной дополнительной. Захват занятой
    /// точки UltimateXR трактует как передачу из руки в руку.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> Точка, которую держит другая рука, уступает, если свободная точка того же
    /// предмета достижима. Передача из руки в руку остаётся, когда свободных точек в досягаемости нет.
    /// </para>
    /// </summary>
    public class TwoHandGrabPolicyTests
    {
        private const int Main = 0;
        private const int Support = 1;

        private static bool Yields(int point, ISet<int> heldByOther, ISet<int> reachable, int count = 2)
        {
            return TwoHandGrabPolicy.ShouldYieldToFreePoint(point, count, heldByOther.Contains, reachable.Contains);
        }

        [Test]
        public void Занятая_точка_уступает_достижимой_свободной()
        {
            Assert.IsTrue(Yields(Main, new HashSet<int> { Main }, new HashSet<int> { Support }),
                "Вторая рука выбрала точку, которую держит первая, хотя дополнительная точка в досягаемости — " +
                "оружие перехватится вместо хвата двумя руками.");
        }

        [Test]
        public void Без_достижимой_свободной_точки_передача_из_руки_в_руку_работает()
        {
            Assert.IsFalse(Yields(Main, new HashSet<int> { Main }, new HashSet<int>()),
                "Свободных точек рядом нет, а передать оружие в другую руку нельзя.");
        }

        [Test]
        public void Свободная_точка_не_запрещается()
        {
            Assert.IsFalse(Yields(Support, new HashSet<int> { Main }, new HashSet<int> { Support }));
        }

        [Test]
        public void Никто_не_держит_ничего_не_запрещается()
        {
            Assert.IsFalse(Yields(Main, new HashSet<int>(), new HashSet<int> { Main, Support }));
        }

        [Test]
        public void Точка_занятая_другой_рукой_не_считается_свободной()
        {
            // Обе точки заняты: третья рука не бывает, но достижимость занятой точки не должна
            // выдавать её за свободную альтернативу.
            Assert.IsFalse(Yields(Main, new HashSet<int> { Main, Support }, new HashSet<int> { Main, Support }));
        }
    }
}
