using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Сборка use-context на стволах NPC (этап use-context, принят пользователем в шлеме 2026-10-11): PPK и AK105 несут
    /// <see cref="WeaponEquipmentBinding"/> — единственного владельца equipment claim. SDK70 (запрет отдачи корня) и SDK72
    /// (владелец оружия) читают политики с того же корневого grabbable, что выбирает <c>UxrFirearmWeapon.RootGrabbable</c>:
    /// trigger 0 и его непосредственная grabbable-зависимость; <see cref="WeaponUseContext.Resolve"/> берёт тот же корень.
    ///
    /// Классы ошибок: binding не на том объекте (SDK его не видит — отдача и владелец по-старому), второй binding,
    /// потерянный скрипт; ненастроенный (ownerNetId == 0) binding в префабе запрещает отдачу, хват или подменяет владельца;
    /// пользователь «выводится» без хвата и без claim. Активный claim, SDK-хват с <c>GrabLifetimeEpoch</c>, конфликт и
    /// сетевая проекция snapshot требуют Play Mode с Mirror и UxrGrabManager — покрыты шлемом и NPC-прогонами.
    /// </summary>
    public class WeaponUseContextPrefabTests
    {
        public static IEnumerable<string> NpcWeapons()
        {
            yield return "Assets/Prefabs/Weapons/PPK/PPK.prefab";
            yield return "Assets/Prefabs/Weapons/AK105/AK105.prefab";
        }

        [TestCaseSource(nameof(NpcWeapons))]
        public void Один_WeaponEquipmentBinding_на_корне_без_потерянных_скриптов(string path)
        {
            GameObject prefab = Load(path);
            var bindings = prefab.GetComponentsInChildren<WeaponEquipmentBinding>(true);
            Assert.That(bindings.Length, Is.EqualTo(1), "Ровно один владелец equipment claim на стволе.");
            Assert.That(bindings[0].gameObject, Is.SameAs(prefab), "Binding на корне оружия.");
            Assert.That(prefab.GetComponent<NetworkIdentity>(), Is.Not.Null, "Snapshot binding — SyncVar: на корне нужен NetworkIdentity.");

            int missing = prefab.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Assert.That(missing, Is.Zero, "На префабе компоненты удалённых скриптов.");
        }

        [TestCaseSource(nameof(NpcWeapons))]
        public void Binding_на_корневом_grabbable_который_читают_SDK70_и_SDK72(string path)
        {
            GameObject prefab = Load(path);
            UxrFirearmWeapon weapon = prefab.GetComponentInChildren<UxrFirearmWeapon>(true);
            Assert.That(weapon.TryGetTriggerGrip(0, out UxrGrabbableObject grip, out _), Is.True, "Контроль: у спуска 0 есть рукоять.");
            UxrGrabbableObject root = grip.UsesGrabbableParentDependency ? grip.GrabbableParent : grip;
            Assert.That(root, Is.Not.Null, "Корневой grabbable огнестрела.");

            var binding = prefab.GetComponent<WeaponEquipmentBinding>();
            Assert.That(root.gameObject, Is.SameAs(binding.gameObject),
                "Binding на том grabbable, с которого SDK берёт политику отдачи и владельца; иначе claim не действует.");

            IUxrFirearmRootRecoilPolicy[] policies = root.GetComponents<IUxrFirearmRootRecoilPolicy>();
            Assert.That(policies, Is.EqualTo(new IUxrFirearmRootRecoilPolicy[] { binding }), "SDK70: единственная политика отдачи корня — binding.");
            IUxrWeaponOwnerProvider[] providers = root.GetComponents<IUxrWeaponOwnerProvider>();
            Assert.That(providers, Is.EqualTo(new IUxrWeaponOwnerProvider[] { binding }), "SDK72: единственный источник владельца — binding.");
        }

        [TestCaseSource(nameof(NpcWeapons))]
        public void Ненастроенный_binding_неактивен_и_не_меняет_поведение_SDK(string path)
        {
            GameObject prefab = Load(path);
            var binding = prefab.GetComponent<WeaponEquipmentBinding>();
            Assert.That(binding.IsActive, Is.False, "В префабе claim нет (ownerNetId == 0).");
            Assert.That(binding.Epoch, Is.Zero);
            Assert.That(binding.Owner, Is.Null);
            Assert.That(binding.IsGrabAvailable, Is.True, "Без claim обычный SDK-хват разрешён.");

            Assert.That(((IUxrFirearmRootRecoilPolicy)binding).AllowsRootRecoil, Is.True, "SDK70: без claim отдача корня штатная.");
            Assert.That(((IUxrWeaponOwnerProvider)binding).TryGetOwner(out UxrActor owner), Is.False,
                "SDK72: без claim override нет — SDK оставляет прежнего владельца.");
            Assert.That(owner, Is.Null);

            UxrFirearmWeapon weapon = prefab.GetComponentInChildren<UxrFirearmWeapon>(true);
            Assert.That(weapon.Owner, Is.Null, "SDK72: неактивный provider не подменяет владельца (в префабе прежнего владельца нет).");
        }

        [TestCaseSource(nameof(NpcWeapons))]
        public void Resolve_без_хвата_и_без_claim_не_выводит_пользователя(string path)
        {
            UxrFirearmWeapon weapon = Load(path).GetComponentInChildren<UxrFirearmWeapon>(true);
            AssertNone(WeaponUseContext.Resolve(weapon, 0), "Спуск 0 без SDK-хвата и без claim.");
            AssertNone(WeaponUseContext.Resolve(weapon, weapon.TriggerCount), "Несуществующий спуск.");
        }

        [Test]
        public void Resolve_без_оружия_не_выводит_пользователя()
        {
            AssertNone(WeaponUseContext.Resolve(null, 0), "null-оружие.");
        }

        private static void AssertNone(in WeaponUseContext context, string what)
        {
            Assert.That(context.Kind, Is.EqualTo(WeaponUseKind.None), what);
            Assert.That(context.User, Is.Null, what);
            Assert.That(context.Hand, Is.Null, what);
            Assert.That(context.AuthoredHere, Is.False, what);
            Assert.That(context.Epoch, Is.Zero, what);
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"Контроль: префаб '{path}' загружается.");
            Assert.That(prefab.GetComponentInChildren<UxrFirearmWeapon>(true), Is.Not.Null, "Контроль: это огнестрельное оружие.");
            return prefab;
        }
    }
}
