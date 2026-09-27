using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Поддерживающая рука не поворачивает оружие. Все пары «оружие арсенала × аватар реестра»,
    /// где дополнительная точка — поддержка, а не цевьё (ближе <see cref="TwoHandGrabCases.SupportGripMaxGap" />
    /// к основной), — см. <see cref="TwoHandGrabCases" />.
    ///
    /// <para>
    /// <b>Дефект.</b> Вторая рука на дополнительной точке <c>Gun_real</c>, и пистолет в основной руке
    /// поворачивается. UltimateXR при хвате двумя руками всегда доворачивает предмет к фактическому
    /// положению второй руки (<c>UxrGrabManager.SolveUsingLookAtAveraging</c> →
    /// <c>RotateObjectTowardsGrab</c>), настройки против этого нет. У пистолета точки в 2 см друг от
    /// друга, и сдвиг второй руки на 3 см поворачивал его на 126°.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> <see cref="MainGripAimLock" /> на оружии с поддержкой держит позу относительно
    /// основной руки такой же, как при хвате одной рукой. Винтовки с цевьём сюда не попадают:
    /// там вторая рука и должна наводить ствол.
    /// </para>
    /// </summary>
    public class GunTwoHandAimTests
    {
        private const float MaxDriftDegrees = 0.5f;

        [TestCaseSource(typeof(TwoHandGrabCases), nameof(TwoHandGrabCases.SupportGrips))]
        public void Сдвиг_поддерживающей_руки_не_поворачивает_оружие(string weaponPath, string avatarPath, int supportPoint)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, supportPoint);
            harness.AssertManipulationLive();
            Quaternion oneHand = RelativeToMainHand(harness);

            harness.PlaceLeftOnSupport();
            TwoHandGrabHarness.Manager.GrabObject(harness.Left, harness.Grabbable, supportPoint, false);
            harness.UpdateManipulation();

            // Рука в VR никогда не стоит ровно в точке хвата: 3 см вбок — обычное дрожание.
            harness.Left.transform.position += harness.Right.transform.right * 0.03f;
            harness.UpdateManipulation();

            float drift = Quaternion.Angle(oneHand, RelativeToMainHand(harness));
            string hint = harness.Weapon.GetComponent<MainGripAimLock>() == null
                              ? " На оружии нет MainGripAimLock — дополнительная точка у самой основной, это поддержка, компонент обязателен."
                              : "";

            Assert.Less(drift, MaxDriftDegrees,
                        $"Поддерживающая рука сдвинулась на 3 см, и оружие в основной руке повернулось на {drift:F1}°.{hint}");
        }

        private static Quaternion RelativeToMainHand(TwoHandGrabHarness harness) =>
            Quaternion.Inverse(harness.Right.transform.rotation) * harness.Weapon.transform.rotation;
    }
}
