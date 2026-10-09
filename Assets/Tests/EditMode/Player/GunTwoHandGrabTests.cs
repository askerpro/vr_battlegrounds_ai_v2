using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Вторая рука берёт дополнительную точку, а не перехватывает оружие у первой.
    /// Все пары «оружие арсенала × аватар реестра» с позами для обеих точек — см. <see cref="TwoHandGrabCases" />.
    ///
    /// <para>
    /// <b>Дефект.</b> Правая рука держит основную точку <c>Gun_real</c>, левая подносится к
    /// дополнительной — пистолет перескакивает в левую. Места ладоней двух точек для MEF разнесены
    /// на 2 см, а <c>UxrGrabbableObject.GetDistanceFromGrabber</c> прибавлял 100000 к любой точке,
    /// чьё место ближе <c>MinHandGrabInterDistance</c> (5 см) к занятой. Дополнительная точка
    /// становилась недосягаемой, и ближайшей оказывалась занятая основная — передача из руки в руку.
    /// Исправлено патчем SDK 11 (<c>Docs/UltimateXR/sdk-patches.md</c>) вместе с
    /// <see cref="TwoHandGrabPolicy" />.
    /// </para>
    /// </summary>
    public class GunTwoHandGrabTests
    {
        [Test]
        public void Есть_что_проверять()
        {
            Assert.IsNotEmpty(TwoHandGrabCases.Collect(),
                              "Ни у одного оружия арсенала нет пары «основная + дополнительная точка» с позами хоть одного аватара — " +
                              "тесты хвата двумя руками молча ничего не проверяют.");
        }

        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.All))]
        public void Дополнительная_точка_досягаема_пока_основную_держит_другая_рука(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
            harness.PlaceLeftOnSupport();

            Assert.IsTrue(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, supportPoint),
                          "Левая ладонь стоит на дополнительной точке, а UltimateXR считает её недосягаемой — " +
                          "штраф за близость к занятой основной точке вернулся (патч SDK 11).");
        }

        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.All))]
        public void Вторая_рука_берёт_дополнительную_точку_а_не_перехватывает_оружие(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
            harness.PlaceLeftOnSupport();
            harness.Left.CanGrabDelegate = (grabbable, point) => TwoHandGrabPolicy.IsGrabAllowed(harness.Left, grabbable, point, ManipulationConstraints.AllowsHandTransfer(grabbable));

            bool found = TwoHandGrabHarness.Manager.GetClosestGrabbableObject(harness.Left, out UxrGrabbableObject grabbable, out int grabPoint,
                                                                             new[] { harness.Grabbable });

            Assert.IsTrue(found && grabbable == harness.Grabbable, "Контроль: левая рука видит оружие.");
            Assert.AreEqual(supportPoint, grabPoint,
                            "Левая рука выбрала основную точку, которую держит правая, — оружие перескочит в левую руку " +
                            "вместо хвата двумя руками.");
        }

        /// <summary>
        /// Решение пользователя 2026-10-09 (<see cref="ManipulationConstraints" />): рука, потянувшаяся к магазину
        /// в кармане рядом с рукоятью, перехватывала оружие. Своя вторая рука не берёт точку, которую держит первая,
        /// даже стоя ладонью ровно на ней.
        /// </summary>
        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.All))]
        public void Своя_вторая_рука_не_перехватывает_оружие(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
            PlaceLeftOnMain(harness);

            Assert.IsFalse(ManipulationConstraints.AllowsHandTransfer(harness.Grabbable),
                           "Перехват огнестрела своей второй рукой разрешён — выключатель ManipulationConstraints.AllowHandTransfer " +
                           "включён или оружие не распознано как огнестрел (UxrFirearmWeapon на корне).");
            Assert.IsFalse(TwoHandGrabPolicy.IsGrabAllowed(harness.Left, harness.Grabbable, TwoHandGrabCases.MainPoint,
                                                           ManipulationConstraints.AllowsHandTransfer(harness.Grabbable)),
                           "Левая ладонь на основной точке, которую держит правая, и политика её отдаёт — оружие перескочит в левую руку.");
        }

        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.All))]
        public void Выключатель_возвращает_перехват_оружия(string weaponPath, string avatarPath, int supportPoint)
        {
            bool saved = ManipulationConstraints.AllowHandTransfer;

            try
            {
                ManipulationConstraints.AllowHandTransfer = true;
                using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
                PlaceLeftOnMain(harness);

                Assert.IsTrue(ManipulationConstraints.AllowsHandTransfer(harness.Grabbable),
                              "Выключатель AllowHandTransfer включён, а ограничение всё равно запрещает перехват.");
                Assert.IsTrue(TwoHandGrabPolicy.IsOwnOtherHand(harness.Right, harness.Left),
                              "Контроль: правая и левая — руки одного аватара.");
            }
            finally
            {
                ManipulationConstraints.AllowHandTransfer = saved;
            }
        }

        private static void PlaceLeftOnMain(TwoHandGrabHarness harness)
        {
            harness.Grabbable.ComputeRequiredGrabberTransform(harness.Left, TwoHandGrabCases.MainPoint, out Vector3 position, out Quaternion rotation, false);
            harness.Left.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
