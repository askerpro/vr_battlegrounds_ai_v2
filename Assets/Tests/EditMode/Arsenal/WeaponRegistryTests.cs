using System.Linq;
using NUnit.Framework;
using UnityEditor;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Стартовый пистолет задан в реестре, а не «первым пистолетом списка» или именем в коде: выдача бесплатного
    /// ствола (экономика раунда) берёт <see cref="WeaponRegistry.DefaultSidearm" />. Он обязан быть в реестре (то есть
    /// на стене и в сети) и быть пистолетом — иначе в кобуру на бедре его не положить.
    /// </summary>
    public class WeaponRegistryTests
    {
        private const string RegistryPath = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";

        [Test]
        public void Стартовый_пистолет_задан_в_реестре_и_это_пистолет()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath);
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");

            WeaponInfo sidearm = registry.DefaultSidearm;
            Assert.IsNotNull(sidearm, "WeaponRegistry.DefaultSidearm не задан — нечего выдавать бесплатно.");
            Assert.IsTrue(registry.Weapons.Contains(sidearm), $"{sidearm.name} не в реестре — его нет на стене и в сети.");
            Assert.AreEqual(WeaponCategory.Pistol, sidearm.Category, $"{sidearm.name}: стартовый ствол — не пистолет, в кобуру на бедре не ляжет.");
            Assert.IsNotNull(sidearm.WeaponPrefab, $"{sidearm.name}: нет префаба.");
        }

        /// <summary>Стартовый — самый дешёвый пистолет: дешевле него на стене продавать нечего (как Glock в CS2).</summary>
        [Test]
        public void Стартовый_пистолет_самый_дешёвый()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath);
            WeaponInfo sidearm = registry.DefaultSidearm;
            Assume.That(sidearm, Is.Not.Null);
            var cheaper = registry.Weapons.Where(w => w != null && w.Category == WeaponCategory.Pistol && w.Price < sidearm.Price).Select(w => w.name).ToList();
            Assert.IsEmpty(cheaper, $"Пистолеты дешевле стартового {sidearm.name}: {string.Join(", ", cheaper)}");
        }
    }
}
