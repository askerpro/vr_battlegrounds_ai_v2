using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Economy
{
    /// <summary>
    /// Класс ошибки (T-45): «оружие, выданное не стеной, — не оружие». <see cref="WeaponComponent"/> ставила только
    /// стена (<c>AssignNetworkItem</c>), поэтому стартовый пистолет в кобуре и ствол бота у клиентов не знали своего
    /// <see cref="WeaponInfo"/>: ни магазинов к раунду, ни учёта снаряжения. Теперь компонент ставится при создании
    /// любого сетевого инстанса оружия из реестра — тем же путём на сервере и у клиента.
    /// </summary>
    public class WeaponComponentPrefabHookTests
    {
        [Test]
        public void Инстанс_оружия_из_реестра_сразу_знает_свой_WeaponInfo()
        {
            WeaponComponent.InstallPrefabHook();

            WeaponInfo sidearm = WeaponRegistry.Instance.DefaultSidearm;
            Assert.IsNotNull(sidearm, "Контроль: стартовый пистолет задан.");

            GameObject instance = NetworkUxrIdentity.CreateInstance(sidearm.WeaponPrefab);
            try
            {
                WeaponComponent component = instance.GetComponent<WeaponComponent>();
                Assert.IsNotNull(component, "Ствол создан не стеной — и остался без WeaponComponent.");
                Assert.AreEqual(sidearm, component.WeaponData);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Не_оружие_компонента_не_получает()
        {
            WeaponComponent.InstallPrefabHook();

            var prefab = new GameObject("NotAWeapon");
            GameObject instance = NetworkUxrIdentity.CreateInstance(prefab);
            try
            {
                Assert.IsNull(instance.GetComponent<WeaponComponent>());
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(prefab);
            }
        }
    }
}
