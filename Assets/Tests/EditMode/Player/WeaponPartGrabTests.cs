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
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Детали предметов (затвор, помпа, чека) берутся, только когда сам предмет уже в руке.
    ///
    /// <para>
    /// <b>Дефект.</b> Подношу руку к лежащему пистолету — активны и рукоять, и затвор, и рука
    /// может взять пистолет за затвор. Затвор — отдельный <see cref="UxrGrabbableObject" /> внутри
    /// оружия, и UltimateXR выбирает его наравне с рукоятью.
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> <see cref="GrabOnlyWhenParentHeld" /> на детали; <see cref="GrabRules" /> не даёт
    /// её взять, пока ближайший предмет-родитель не в руке у того же игрока. Правило работает через
    /// <c>CanGrabDelegate</c>, поэтому недоступная деталь и не подсвечивается.
    /// </para>
    /// </summary>
    public class WeaponPartGrabTests
    {
        /// <summary>
        /// Каждая деталь предмета из <c>Assets/Prefabs</c> несёт решение: компонент стоит, галочка
        /// задаёт поведение. Магазин в якоре — не деталь, у него своя логика вставки.
        /// </summary>
        [Test]
        public void У_каждой_детали_предмета_есть_решение()
        {
            var missing = new List<string>();
            int parts = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                foreach (UxrGrabbableObject part in Parts(prefab))
                {
                    parts++;

                    if (part.GetComponent<GrabOnlyWhenParentHeld>() == null)
                    {
                        missing.Add($"{path} :: {PathInPrefab(prefab.transform, part.transform)}");
                    }
                }
            }

            Assert.Greater(parts, 0, "Контроль: деталей у предметов не найдено — проверять нечего.");
            Assert.IsEmpty(missing,
                           "Детали без GrabOnlyWhenParentHeld — их можно взять у лежащего предмета:\n  " + string.Join("\n  ", missing) +
                           "\nДобавить компонент; если деталь должна браться всегда — снять в нём галочку.");
        }

        [TestCaseSource(nameof(PartCases))]
        public void Деталь_лежащего_оружия_недоступна(string weaponPath, string avatarPath, string partPath)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, 0, grabMain: false);
            UxrGrabbableObject part = PlaceLeftOnPart(harness, partPath);

            Assert.IsFalse(part.CanBeGrabbedByGrabber(harness.Left, 0),
                           $"Оружие лежит, а рука может взять его за деталь '{partPath}'.");
        }

        [TestCaseSource(nameof(PartCases))]
        public void Деталь_оружия_в_руке_доступна(string weaponPath, string avatarPath, string partPath)
        {
            using var harness = new TwoHandGrabHarness(weaponPath, avatarPath, 0);
            UxrGrabbableObject part = PlaceLeftOnPart(harness, partPath);

            Assert.IsTrue(part.CanBeGrabbedByGrabber(harness.Left, 0),
                          $"Оружие в правой руке, левая ровно на детали '{partPath}', а взять её нельзя.");
        }

        private static UxrGrabbableObject PlaceLeftOnPart(TwoHandGrabHarness harness, string partPath)
        {
            Transform partTransform = harness.Weapon.transform.Find(partPath);
            Assert.IsNotNull(partTransform, $"Контроль: деталь '{partPath}' есть в экземпляре.");
            UxrGrabbableObject part = partTransform.GetComponent<UxrGrabbableObject>();

            part.ComputeRequiredGrabberTransform(harness.Left, 0, out Vector3 position, out Quaternion rotation, false);
            harness.Left.transform.SetPositionAndRotation(position, rotation);
            harness.Left.CanGrabDelegate = (grabbable, point) => GrabRules.IsGrabAllowed(harness.Left, grabbable, point);
            return part;
        }

        /// <summary>
        /// Оружие из <see cref="WeaponInfo" /> × аватар из <see cref="AvatarRegistry" />, где у аватара
        /// есть свои позы: правой руки на основной точке, левой — на детали с включённой галочкой.
        /// </summary>
        private static IEnumerable<TestCaseData> PartCases()
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

                foreach (UxrGrabbableObject part in Parts(weapon))
                {
                    GrabOnlyWhenParentHeld rule = part.GetComponent<GrabOnlyWhenParentHeld>();

                    if (rule == null || !rule.RequireParentHeld)
                    {
                        continue; // отсутствие ловит У_каждой_детали_предмета_есть_решение
                    }

                    foreach (UxrAvatar avatar in avatars)
                    {
                        if (TwoHandGrabCases.AlignTransform(root, TwoHandGrabCases.MainPoint, avatar, UxrHandSide.Right) != null &&
                            TwoHandGrabCases.AlignTransform(part, 0, avatar, UxrHandSide.Left) != null)
                        {
                            string partPath = PathInPrefab(weapon.transform, part.transform);
                            yield return new TestCaseData(AssetDatabase.GetAssetPath(weapon), AssetDatabase.GetAssetPath(avatar.gameObject), partPath)
                                .SetName($"{{m}}({weapon.name}, {avatar.name}, {part.name})");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Детали предмета: вложенные <see cref="UxrGrabbableObject" />, кроме лежащих в якоре
        /// (<see cref="UxrGrabbableObjectAnchor" />) — это вставленные предметы, а не части.
        /// </summary>
        private static IEnumerable<UxrGrabbableObject> Parts(GameObject prefab)
        {
            UxrGrabbableObject root = prefab.GetComponent<UxrGrabbableObject>();

            if (root == null)
            {
                return Enumerable.Empty<UxrGrabbableObject>();
            }

            return prefab.GetComponentsInChildren<UxrGrabbableObject>(true)
                         .Where(g => g != root && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
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
