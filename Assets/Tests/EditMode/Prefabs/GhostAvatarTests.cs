using System.Collections.Generic;
using System.Linq;
using Mirror;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UltimateXR.Mechanics.Weapons;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Призрак — отдельный аватар выбывшего (T-35), собранный из киборга
    /// (<c>Tools/VR Battlegrounds/Avatars/Build Ghost Avatar</c>).
    ///
    /// <para>
    /// Что доказывает. Призрака нельзя выбрать: он лежит в <see cref="AvatarRegistry.ghost"/>, а не
    /// в списке скинов, и нет ни в одной команде. Он ни с чем не сталкивается и ничего не носит:
    /// нет хитбоксов, карманов, снаряжения. Руки есть, но хватают только планшет
    /// (<see cref="GhostGrabRule"/>). Полупрозрачный — материал призрака на всём теле.
    /// </para>
    /// </summary>
    public class GhostAvatarTests
    {
        private const string StrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string GhostMaterialPath = "Assets/Prefabs/Player/Ghost/GhostMaterial.mat";
        private const string TabletPath = "Assets/Prefabs/UI/Menu/Tablet/Tablet_Base.prefab";

        private static AvatarRegistry Registry() =>
            AssetDatabase.LoadAssetAtPath<AvatarRegistry>(RegisteredAvatars.RegistryPath);

        private static GameObject GhostPrefab()
        {
            AvatarRegistry registry = Registry();
            Assert.IsNotNull(registry, $"Нет реестра {RegisteredAvatars.RegistryPath}.");
            Assert.IsNotNull(registry.ghost, "В реестре аватаров не задан призрак (AvatarRegistry.ghost) — собери его: Tools/VR Battlegrounds/Avatars/Build Ghost Avatar.");
            Assert.IsNotNull(registry.ghost.prefab, $"У {registry.ghost.name} нет префаба.");
            return registry.ghost.prefab;
        }

        [Test]
        public void Призрак_зарегистрирован_отдельно_от_скинов()
        {
            GameObject ghost = GhostPrefab();

            Assert.IsFalse(Registry().avatars.Any(a => a != null && a.prefab == ghost),
                "Призрак лежит в списке скинов реестра — на него пошли бы тесты снаряжения и поз хвата.");

            var strategy = AssetDatabase.LoadAssetAtPath<TeamAvatarStrategy>(StrategyPath);
            Assert.IsNotNull(strategy, $"Нет {StrategyPath}.");
            Assert.AreEqual(AssetDatabase.GetAssetPath(ghost), AssetDatabase.GetAssetPath(strategy.GhostPrefab),
                "Стратегия выдаёт выбывшему не того призрака, что в реестре (или никакого).");
        }

        [Test]
        public void Призрака_нельзя_выбрать()
        {
            GameObject ghost = GhostPrefab();

            List<string> teams = AssetDatabase.FindAssets("t:TeamData")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TeamData>)
                .Where(t => t != null && t.avatars != null && t.avatars.Any(a => a != null && a.prefab == ghost))
                .Select(t => t.name)
                .ToList();
            Assert.That(teams, Is.Empty, "Призрак предлагается скином команды — его можно выбрать в планшете.");

            var strategy = AssetDatabase.LoadAssetAtPath<TeamAvatarStrategy>(StrategyPath);
            var fallback = new SerializedObject(strategy).FindProperty("fallbackPrefab").objectReferenceValue as GameObject;
            Assert.AreNotSame(ghost, fallback, "Призрак — запасной аватар игрока без команды.");
        }

        [Test]
        public void Призрак_в_spawnPrefabs()
        {
            GameObject ghost = GhostPrefab();
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>(ManagersPrefabPath);
            NetworkManager manager = managers != null ? managers.GetComponentInChildren<NetworkManager>(true) : null;
            Assert.IsNotNull(manager, $"Нет NetworkManager в {ManagersPrefabPath}.");
            CollectionAssert.Contains(manager.spawnPrefabs, ghost, "Призрака нет в spawnPrefabs — клиенты не заспавнят выбывшего.");
        }

        [Test]
        public void Призрак_аватар_игрока_без_хитбоксов_и_снаряжения()
        {
            GameObject ghost = GhostPrefab();

            Assert.IsNotNull(ghost.GetComponent<TeamColorTint>(), "Нет TeamColorTint — призрак не в цвет своей команды.");
            Assert.IsNotNull(ghost.GetComponent<GhostViewEffect>(), "Нет GhostViewEffect — выбывший не видит, что выбыл.");
            Assert.IsNotNull(ghost.GetComponent<PlayerController>(), "Нет PlayerController — сессия не свяжется с призраком, зона не увидит его.");
            Assert.IsNotNull(ghost.GetComponent<UxrAvatar>(), "Нет UxrAvatar.");
            Assert.IsNotNull(ghost.GetComponent<NetworkIdentity>(), "Нет NetworkIdentity.");

            List<string> solid = ghost.GetComponentsInChildren<Collider>(true)
                .Where(c => !c.isTrigger)
                .Select(c => c.name).ToList();
            Assert.That(solid, Is.Empty, "У призрака есть сплошные коллайдеры — в него можно попасть, он толкает предметы.");

            Assert.That(ghost.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true).Select(a => a.name), Is.Empty,
                "У призрака есть карманы — он мог бы носить снаряжение.");
            Assert.IsNull(ghost.GetComponent<PlayerLoadoutManager>(), "У призрака есть PlayerLoadoutManager — ему выдадут магазины.");
        }

        [Test]
        public void Призрак_полупрозрачный()
        {
            GameObject ghost = GhostPrefab();
            var ghostMaterial = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            Assert.IsNotNull(ghostMaterial, $"Нет {GhostMaterialPath}.");

            List<SkinnedMeshRenderer> body = ghost.GetComponentsInChildren<SkinnedMeshRenderer>(false)
                .Where(r => r.GetComponentInParent<UxrHandIntegration>(true) == null)
                .ToList();
            Assert.That(body, Is.Not.Empty, "У призрака нет видимого тела.");

            List<string> opaque = body.Where(r => r.sharedMaterials.Any(m => m != ghostMaterial))
                                      .Select(r => r.name).ToList();
            Assert.That(opaque, Is.Empty, "Части тела призрака не на материале призрака — выглядят живыми.");
        }

        [TestCase(true, false, true, TestName = "Выбывший_вне_своей_зоны_видит_мир_чёрно_белым")]
        [TestCase(true, true, false, TestName = "Выбывший_в_своей_зоне_видит_обычно")]
        [TestCase(false, false, false, TestName = "Живой_вне_зоны_видит_обычно")]
        [TestCase(false, true, false, TestName = "Живой_в_зоне_видит_обычно")]
        public void Правило_вида_выбывшего(bool eliminated, bool inOwnZone, bool expected)
        {
            Assert.AreEqual(expected, GhostViewRule.Wanted(eliminated, inOwnZone));
        }

        [Test]
        public void Выбывший_хватает_только_планшет()
        {
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPath);
            Assert.IsNotNull(tablet, $"Нет {TabletPath}.");
            UxrGrabbableObject tabletGrab = tablet.GetComponentInChildren<UxrGrabbableObject>(true);
            Assert.IsNotNull(tabletGrab, "У планшета нет UxrGrabbableObject.");

            UxrGrabbableObject weaponGrab = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p != null && p.GetComponent<UxrFirearmWeapon>() != null)
                .Select(p => p.GetComponent<UxrGrabbableObject>())
                .FirstOrDefault(g => g != null);
            Assert.IsNotNull(weaponGrab, "Не нашлось ни одного оружия в Assets/Prefabs/Weapons.");

            Assert.IsTrue(GhostGrabRule.IsAllowed(false, tabletGrab), "Выбывший не может взять свой планшет — не выберет команду и не посмотрит обзор.");
            Assert.IsFalse(GhostGrabRule.IsAllowed(false, weaponGrab), "Выбывший берёт оружие.");
            Assert.IsTrue(GhostGrabRule.IsAllowed(true, weaponGrab), "Живой не может взять оружие.");
        }
    }
}
