using System;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Накопленная отдача ставится последней в <c>ConstraintsApplied</c>: <see cref="MainGripAimLock"/> в том же событии
    /// возвращает позу основной руки и затёр бы подброс, если бы подписался позже (порядок OnEnable не гарантирован).
    /// </summary>
    public class RecoilAccumulatorOrderTests
    {
        [Test]
        public void После_хвата_отдача_последняя_в_ConstraintsApplied()
        {
            var go = new GameObject("Weapon");
            try
            {
                go.AddComponent<UxrFirearmWeapon>();
                var recoil = go.AddComponent<RecoilAccumulator>();
                var aimLock = go.AddComponent<MainGripAimLock>();
                var grabbable = go.GetComponent<UxrGrabbableObject>();

                // Худший порядок: отдача включилась раньше фиксатора прицела.
                Call(recoil, "Awake");
                Call(recoil, "OnEnable");
                Call(aimLock, "Awake");
                Call(aimLock, "OnEnable");
                Assert.AreSame(aimLock, LastHandler(grabbable), "Подготовка теста: фиксатор должен оказаться последним.");

                Call(recoil, "OnGrabbed", null, null);

                Assert.AreSame(recoil, LastHandler(grabbable), "После хвата отдача не последняя — MainGripAimLock затрёт подброс.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static object LastHandler(UxrGrabbableObject grabbable)
        {
            FieldInfo field = typeof(UxrGrabbableObject).GetField("ConstraintsApplied", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "У UxrGrabbableObject нет поля события ConstraintsApplied — поправь тест.");
            var handler = (Delegate)field.GetValue(grabbable);
            Assert.IsNotNull(handler, "На ConstraintsApplied никто не подписан.");
            Delegate[] list = handler.GetInvocationList();
            return list[list.Length - 1].Target;
        }

        private static void Call(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} не найден — поправь тест.");
            info.Invoke(target, args.Length == 0 ? null : args);
        }
    }
}
