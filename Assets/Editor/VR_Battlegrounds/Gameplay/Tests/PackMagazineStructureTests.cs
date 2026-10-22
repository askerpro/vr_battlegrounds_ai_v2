using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay.Tests
{
    /// <summary>
    /// Магазины стволов из паков после пересборки (решение пользователя 2026-10-22): геометрии базы SDK
    /// (<c>MagMachinegunGeo</c>, <c>MagGunGeo</c>, <c>MagShotGunGeo</c>, <c>Geo</c> доноров) и записей хвата сэмпловых
    /// аватаров SDK нет; поза руки магазина — snap-точка <c>Grabs/Orig/*_orig</c>; магазин — самостоятельный префаб.
    /// </summary>
    public class PackMagazineStructureTests
    {
        // Сэмпловые аватары UltimateXR: CyborgAvatar, BigHandsAvatar, SmallHandsAvatar.
        private static readonly string[] SdkSampleAvatars =
        {
            "e7468ec6b7af89c4d94d451ec3c1807b", "dc011efb41f2d4a419389aefb4b4270d", "7195d091dac462e4db9ba0793d9a6727"
        };

        private static readonly string[] SdkGeometry = { "MagMachinegunGeo", "MagGunGeo", "MagShotGunGeo", "Geo" };

        private static IEnumerable<string> Magazines() =>
            KinemationWeaponBuilder.All.Select(KinemationWeaponBuilder.MagazinePath)
                .Concat(HandsPackWeaponBuilder.T38.Select(r => $"Assets/Prefabs/Weapons/{r.PrefabFolder}/{r.MagazineName ?? r.Name + "_mag"}.prefab"))
                .Concat(new[]
                {
                    "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower_Magazine.prefab", "Assets/Prefabs/Weapons/AR15/AR15_Magazine.prefab",
                    "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS_Ammo.prefab"
                });

        [TestCaseSource(nameof(Magazines))]
        public void Магазин_без_геометрии_и_сэмплов_SDK(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(root, "Нет префаба магазина " + path);

            string[] geometry = root.GetComponentsInChildren<Transform>(true).Where(t => SdkGeometry.Contains(t.name))
                .Select(t => t.name).ToArray();
            Assert.IsEmpty(geometry, "Узлы геометрии базы SDK остались: " + string.Join(", ", geometry));

            var grabbable = root.GetComponent<UxrGrabbableObject>();
            Assert.IsNotNull(grabbable, "У магазина нет UxrGrabbableObject");
            string[] samples = Enumerable.Range(0, grabbable.GrabPointCount)
                .SelectMany(i => grabbable.GetGrabPoint(i).AvatarGripPoseEntries)
                .Where(e => SdkSampleAvatars.Contains(e.AvatarPrefabGuid)).Select(e => e.AvatarPrefabGuid).ToArray();
            Assert.IsEmpty(samples, "Записи хвата сэмпловых аватаров SDK остались: " + string.Join(", ", samples));
        }

        /// <summary>Магазин — самостоятельный префаб, не вариант удалённых баз SDK (решение пользователя 2026-10-22).</summary>
        [TestCaseSource(nameof(Magazines))]
        public void Магазин_самостоятельный_префаб(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(root), path + ": магазин должен быть обычным префабом");
            Assert.IsNull(PrefabUtility.GetCorrespondingObjectFromSource(root), path + ": у магазина остался исходный префаб (вариант)");
        }

        /// <summary>
        /// Экспортированная поза руки магазина (<c>*_Magazine_orig</c> / <c>*_Shell_orig</c>) — со snap-точкой в его префабе.
        /// Позы нет, если ни в одном клипе перезарядки рука не держит деталь жёстко (TR15): тогда нет и точки.
        /// </summary>
        [TestCaseSource(nameof(Magazines))]
        public void Поза_магазина_со_snap_точкой(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string[] poses = AssetDatabase.FindAssets("t:UxrHandPoseAsset", new[] { PackOrigPoses.Root })
                .Select(AssetDatabase.GUIDToAssetPath).Select(System.IO.Path.GetFileNameWithoutExtension)
                .Where(n => n.EndsWith("_Magazine" + PackOrigPoses.Suffix) || n.EndsWith("_Shell" + PackOrigPoses.Suffix))
                .Where(n => n.Contains("_" + System.IO.Path.GetFileNameWithoutExtension(path).Split('_')[0] + "_")).ToArray();
            if (poses.Length == 0) Assert.Pass("Позы магазина нет — snap-точка не нужна.");
            Transform orig = root.transform.Find("Grabs/" + PackOrigPoses.SnapFolder);
            Assert.IsNotNull(orig, $"{path}: есть поза {poses[0]}, но нет Grabs/{PackOrigPoses.SnapFolder}");
            foreach (string pose in poses)
                Assert.IsNotNull(orig.Find(pose), $"{path}: нет snap-точки Grabs/{PackOrigPoses.SnapFolder}/{pose}");
        }
    }
}
