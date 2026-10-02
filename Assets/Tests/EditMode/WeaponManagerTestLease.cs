using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Tests
{
    /// <summary>Харнесс занимает существующий менеджер; удалять можно только созданный самим тестом.</summary>
    internal static class WeaponManagerTestLease
    {
        internal static UxrWeaponManager Acquire(out UxrWeaponManager created)
        {
            TestEnvironmentContract.ResetDestroyedSingleton<UxrWeaponManager>();
            var existing = Object.FindFirstObjectByType<UxrWeaponManager>(FindObjectsInactive.Include);
            var manager = UxrWeaponManager.Instance;
            created = existing == null ? manager : null;
            var soleManager = TestEnvironmentContract.ExactlyOneInScene<UxrWeaponManager>();
            NUnit.Framework.Assert.That(manager, NUnit.Framework.Is.SameAs(soleManager),
                "[TestEnvironment] Singleton указывает не на единственный менеджер сцены.");
            TestEnvironmentContract.IsActive(manager);
            return manager;
        }
    }
}
