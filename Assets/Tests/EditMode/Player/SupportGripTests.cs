using NUnit.Framework;
using UltimateXR.Manipulation;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Точка поддержки (вторая рука у пистолета) берётся, только когда основную уже держит
    /// другая рука того же игрока.
    ///
    /// <para>
    /// <b>Дефект.</b> Подношу руку к лежащему <c>Gun_real</c> — иногда он берётся за дополнительную
    /// точку: она в 2 см от рукояти, и UltimateXR выбирает её наравне с основной.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> <see cref="SupportGripRequiresMain" /> на корне оружия; <see cref="GrabRules" />
    /// не пускает к дополнительным точкам, пока основная не в другой руке. Кейсы — оружие с точкой
    /// поддержки ближе <see cref="TwoHandGrabCases.SupportGripMaxGap" /> к основной
    /// (<see cref="TwoHandGrabCases.SupportGrips" />); цевьё винтовки правило не трогает.
    /// </para>
    /// </summary>
    public class SupportGripTests
    {
        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.SupportGrips))]
        public void Точка_поддержки_лежащего_оружия_недоступна(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint, grabMain: false);
            harness.PlaceLeftOnSupport();
            harness.Left.CanGrabDelegate = (grabbable, point) => GrabRules.IsGrabAllowed(harness.Left, grabbable, point);

            Assert.IsFalse(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, supportPoint),
                           "Оружие лежит, а рука может взять его за точку поддержки. Нужен SupportGripRequiresMain на корне оружия.");
        }

        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.SupportGrips))]
        public void Точка_поддержки_доступна_когда_основную_держит_другая_рука(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
            harness.PlaceLeftOnSupport();
            harness.Left.CanGrabDelegate = (grabbable, point) => GrabRules.IsGrabAllowed(harness.Left, grabbable, point);

            Assert.IsTrue(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, supportPoint),
                          "Правая рука держит основную точку, левая ровно на поддержке, а взять её нельзя.");
        }
    }
}
