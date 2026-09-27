using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Предмет, вставленный в якорь другого предмета (магазин в оружии), рукой не берётся вовсе —
    /// ни у лежащего оружия, ни у оружия в руке. Магазин выходит только кнопкой выброса (A/X,
    /// <c>MagazineEject</c>).
    ///
    /// <para>
    /// <b>Дефекты.</b> Тянусь к кобуре за пистолетом — рука вынимает магазин из пистолета: магазин
    /// внутри рукояти ближе к ладони, чем прокси кобуры и рукоять. Кладу вторую руку на пистолет
    /// для поддержки — она берёт магазин, а не поддержку.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> <see cref="GrabRules" /> через <c>CanGrabDelegate</c> — поэтому недоступный
    /// магазин не подсвечивается и не перехватывает хват ни у прокси, ни у точки поддержки.
    /// </para>
    /// </summary>
    public class AnchoredItemGrabTests
    {
        [TestCaseSource(nameof(Cases))]
        public void Магазин_лежащего_оружия_недоступен(string weaponPath, string avatarPath, string itemPath)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, 0, grabMain: false);
            UxrGrabbableObject item = PlaceLeftOnItem(harness, itemPath);

            Assert.IsFalse(item.CanBeGrabbedByGrabber(harness.Left, 0),
                           $"Оружие не в руке (кобура, стена, пол), а рука вынимает из него '{itemPath}'.");
        }

        [TestCaseSource(nameof(Cases))]
        public void Магазин_оружия_в_руке_тоже_недоступен(string weaponPath, string avatarPath, string itemPath)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, 0);
            UxrGrabbableObject item = PlaceLeftOnItem(harness, itemPath);

            Assert.IsFalse(item.CanBeGrabbedByGrabber(harness.Left, 0),
                           $"Оружие в правой руке, левая на '{itemPath}' — и берёт магазин вместо поддержки оружия. " +
                           "Магазин из оружия выходит только кнопкой выброса (MagazineEject).");
        }

        private static UxrGrabbableObject PlaceLeftOnItem(TwoHandGrabHarness harness, string itemPath)
        {
            Transform itemTransform = harness.Weapon.transform.Find(itemPath);
            Assert.IsNotNull(itemTransform, $"Контроль: '{itemPath}' есть в экземпляре.");
            UxrGrabbableObject item = itemTransform.GetComponent<UxrGrabbableObject>();

            item.ComputeRequiredGrabberTransform(harness.Left, 0, out Vector3 position, out Quaternion rotation, false);
            harness.Left.transform.SetPositionAndRotation(position, rotation);

            // Контроль: без правил рука на этом месте предмет берёт — иначе «недоступен» ничего не докажет.
            harness.Left.CanGrabDelegate = null;
            Assert.IsTrue(item.CanBeGrabbedByGrabber(harness.Left, 0), $"Контроль харнесса: рука на '{itemPath}' не берёт его даже без правил.");
            harness.Left.CanGrabDelegate = (grabbable, point) => GrabRules.IsGrabAllowed(harness.Left, grabbable, point);

            return item;
        }

        /// <summary>
        /// Оружие из <see cref="WeaponInfo" /> × вставленный в него предмет × аватар из реестра, у
        /// которого есть своя поза правой руки на основной точке оружия.
        /// </summary>
        private static IEnumerable<TestCaseData> Cases()
        {
            List<UxrAvatar> avatars = TwoHandGrabCases.LoadAll<AvatarRegistry>().SelectMany(r => r.avatars)
                                                      .Where(d => d != null && d.prefab != null)
                                                      .Select(d => d.prefab.GetComponent<UxrAvatar>())
                                                      .Where(a => a != null)
                                                      .Distinct()
                                                      .ToList();

            foreach (GameObject weapon in TwoHandGrabCases.LoadAll<WeaponInfo>().Select(w => w.WeaponPrefab).Where(p => p != null).Distinct())
            {
                UxrGrabbableObject root = weapon.GetComponent<UxrGrabbableObject>();
                if (root == null) continue;

                foreach (UxrGrabbableObject item in weapon.GetComponentsInChildren<UxrGrabbableObject>(true).Where(g => g != root && IsInAnchor(g)))
                {
                    foreach (UxrAvatar avatar in avatars)
                    {
                        // Поза нужна только основной точке — чтобы правая рука держала оружие. Левую
                        // ставит ComputeRequiredGrabberTransform и без своей позы; что она достаёт
                        // до предмета, проверяет контроль харнесса в PlaceLeftOnItem.
                        if (TwoHandGrabCases.AlignTransform(root, TwoHandGrabCases.MainPoint, avatar, UxrHandSide.Right) != null)
                        {
                            yield return new TestCaseData(AssetDatabase.GetAssetPath(weapon), AssetDatabase.GetAssetPath(avatar.gameObject), PathInPrefab(weapon.transform, item.transform))
                                .SetName($"{{m}}({weapon.name}, {avatar.name}, {item.name})");
                        }
                    }
                }
            }
        }

        private static bool IsInAnchor(UxrGrabbableObject item)
        {
            return item.transform.parent != null && item.transform.parent.GetComponentInParent<UxrGrabbableObjectAnchor>(true) != null;
        }

        private static string PathInPrefab(Transform root, Transform node)
        {
            var names = new List<string>();

            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                names.Insert(0, t.name);
            }

            return string.Join("/", names);
        }
    }
}
