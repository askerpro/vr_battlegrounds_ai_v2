using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Снаряжение не переживает смену режима, паузу и переход на другую карту.
    ///
    /// <para>
    /// Что доказывает. <see cref="EquipmentStrip.ServerStrip"/> отпускает руки и <b>уничтожает</b>
    /// оружие и магазины игрока — в руках, в кобурах, в кармане. Не роняет на пол, как смерть
    /// (<c>PlayerLoadoutManager.ServerDropEquipment</c>): упавшее снова стало бы ничьим и
    /// дожило бы до следующего режима. Харнесс — настоящий аватар проекта с настоящим
    /// оружием в руке (<see cref="TwoHandGrabHarness"/>).
    /// </para>
    /// </summary>
    public class EquipmentStripTests
    {
        [Test]
        public void Оружие_в_руке_уничтожается_и_рука_свободна()
        {
            List<TwoHandGrabCase> cases = TwoHandGrabCases.Collect();
            Assume.That(cases.Count, Is.GreaterThan(0), "Нет ни одной пары оружие–аватар с позами хвата.");
            TwoHandGrabCase c = cases[0];

            using (var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint))
            {
                PlayerController player = harness.Avatar.GetComponent<PlayerController>();
                Assert.IsNotNull(player, $"У аватара {c.AvatarPath} нет PlayerController.");
                Assert.IsTrue(UxrGrabManager.Instance.IsBeingGrabbed(harness.Grabbable), "Контроль харнесса: оружие в руке.");

                int removed = EquipmentStrip.ServerStrip(player, "тест");

                Assert.AreEqual(1, removed, "Оружие из руки не уничтожено.");
                Assert.IsTrue(harness.Weapon == null, "Оружие осталось в мире — после смены режима оно стало бы ничьим.");
                Assert.IsNull(harness.Right.GrabbedObject, "Рука после снятия снаряжения всё ещё что-то держит.");
            }
        }

        [Test]
        public void Не_снаряжение_в_руке_отпускается_но_не_уничтожается()
        {
            List<TwoHandGrabCase> cases = TwoHandGrabCases.Collect();
            Assume.That(cases.Count, Is.GreaterThan(0));
            TwoHandGrabCase c = cases[0];

            var tablet = new GameObject("Tablet");
            try
            {
                using (var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint, grabMain: false))
                {
                    Assert.IsFalse(EquipmentStrip.IsEquipment(tablet.AddComponent<UxrGrabbableObject>()),
                        "Произвольный хватаемый предмет (планшет, жетон) принят за снаряжение — его уничтожили бы.");
                    Assert.IsTrue(EquipmentStrip.IsEquipment(harness.Grabbable), "Оружие не распознано как снаряжение.");
                }
            }
            finally
            {
                Object.DestroyImmediate(tablet);
            }
        }
    }
}
