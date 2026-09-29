using NUnit.Framework;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Отклик кармана — один импульс, когда рука нашла готовый карман, а не вибрация всё время,
    /// пока рука в нём.
    /// </summary>
    public class PocketTapTests
    {
        private readonly object _hip = new object();
        private readonly object _chest = new object();

        private static int CountTaps(PocketTap tap, params object[] frames)
        {
            int taps = 0;
            foreach (object pocket in frames)
                if (tap.Update(pocket)) taps++;
            return taps;
        }

        [Test]
        public void Рука_у_готового_кармана_вздрагивает_один_раз()
        {
            Assert.AreEqual(1, CountTaps(new PocketTap(), _hip, _hip, _hip, _hip, _hip),
                "Пока рука у того же кармана, вибрация должна молчать после первого импульса.");
        }

        [Test]
        public void Ушла_и_вернулась_снова_импульс()
        {
            Assert.AreEqual(2, CountTaps(new PocketTap(), _hip, _hip, null, null, _hip, _hip));
        }

        [Test]
        public void Сразу_к_другому_карману_импульс()
        {
            Assert.AreEqual(2, CountTaps(new PocketTap(), _hip, _chest, _chest));
        }

        [Test]
        public void Без_кармана_тишина()
        {
            Assert.AreEqual(0, CountTaps(new PocketTap(), null, null, null));
        }
    }
}
