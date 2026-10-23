using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Покрытие поз хвата: <b>каждый хватаемый предмет × каждое семейство играбельных аватаров</b> → у каждой
    /// точки хвата есть настройка хвата для этого семейства.
    ///
    /// <para>
    /// <b>Как UltimateXR выбирает хват.</b> Записи хвата (<see cref="UxrGripPoseInfo"/>: поза пальцев
    /// и точки выравнивания левой/правой руки) хранятся в точке хвата по GUID префаба аватара.
    /// <see cref="UxrGrabPointInfo.GetGripPoseInfo(UxrAvatar, bool)"/> идёт по цепочке
    /// <see cref="UxrAvatar.GetPrefabGuidChain"/> (сам префаб → его <c>_parentPrefab</c> → … до
    /// <c>PlayerBase</c>) и берёт первую найденную запись. Не нашёл — молча возвращает
    /// <see cref="UxrGrabPointInfo.DefaultGripPoseInfo"/>. Ошибки в консоли нет.
    /// </para>
    ///
    /// <para>
    /// <b>Что видит игрок без записи.</b> У default-записи нет точек выравнивания, и
    /// <c>UxrGrabbableObject.GetGrabPointGrabAlignTransform</c> отдаёт сам предмет: предмет встаёт в
    /// ладонь своим пивотом, а не рукоятью (планшет — углом, оружие — центром). Поза пальцев —
    /// default-поза, сделанная под чужой скелет, или, если её имени у аватара нет, общая поза
    /// <c>Grab</c> (<c>UxrStandardAvatarController.UpdateGrabPoseInfo</c>). Поэтому default — отказ.
    /// </para>
    ///
    /// <para>
    /// <b>Что засчитывается.</b> Запись самого аватара или его родительского префаба (штатное
    /// наследование: запись на <c>PlayerBase_NonSdkHands</c> покрывает всех MEF). Своя запись с пустой
    /// позой — тоже: тогда берётся общая <c>Grab</c> аватара, а выравнивание остаётся своим (так
    /// настроены затвор и отдача оружия). Своя запись без точки выравнивания руки — отказ, только
    /// если у другого аватара эта точка для той же руки задана: тогда пустота — забытая настройка, а
    /// не задуманный хват за пивот.
    /// </para>
    ///
    /// <para>
    /// <b>Семейство аватаров</b> (решение пользователя 2026-10-23): аватары с общей базой семейства — один аватар
    /// проверки, их хваты подходят друг другу. База семейства — префаб из <see cref="FamilyBaseGuids"/> в цепочке
    /// <see cref="UxrAvatar.GetParentPrefabChain"/> аватара реестра (кисти Cyborg — <c>CyborgHandsBase</c>); аватар вне
    /// семейства — сам себе семейство. Проверка идёт по базе: запись найдена по цепочке базы, поза зарегистрирована у
    /// базы или её родителей, align заданы. Свои записи потомков (мимо базы) запрещает
    /// <c>CyborgGripEntriesOwnershipTests</c>; один пропуск — один красный случай, а не по красному на каждый скин.
    /// </para>
    ///
    /// <para>
    /// <b>Призрак выбывшего</b> (T-35) — отдельный аватар, вариант киборга, в реестре в поле
    /// <c>AvatarRegistry.ghost</c>, а не в списке скинов, поэтому в матрицу не входит: он берёт только
    /// планшет (<c>GhostGrabRule</c>), а позы планшета наследует от киборга (цепочка префабов аватара).
    /// </para>
    ///
    /// <para>
    /// Источники без списков путей: аватары — <see cref="RegisteredAvatars"/>, предметы — все префабы <c>Assets/Prefabs</c> с
    /// <see cref="UxrGrabbableObject"/> и префабы всех <see cref="WeaponInfo"/>, а также предметы сцен
    /// Build Settings. Новый предмет или аватар попадает под проверку сам.
    /// </para>
    /// </summary>
    public class GrabPoseCoverageTests
    {
        private const string PrefabsRoot = "Assets/Prefabs";

        /// <summary>Базы семейств аватаров: CyborgHandsBase (кисти Cyborg: MEF_cyborg_hands_Player, _Black, PlayerControllersCyborgAvatar).</summary>
        public static readonly string[] FamilyBaseGuids = { "71b74b0d08444f49bbb7aa2828d0890a" };

        // ══════════════════════════════════════════════════════════════════
        //  Источники
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Играбельные аватары — реестр скинов.</summary>
        public static IEnumerable<UxrAvatar> Avatars()
        {
            var avatars = new List<UxrAvatar>();

            foreach (GameObject prefab in RegisteredAvatars.Prefabs())
            {
                UxrAvatar avatar = prefab.GetComponent<UxrAvatar>();
                if (avatar != null) avatars.Add(avatar);
            }

            return avatars.Distinct().OrderBy(a => a.name);
        }

        /// <summary>База семейства аватара: первый префаб цепочки (сам аватар, затем родители) из <see cref="FamilyBaseGuids"/>; нет — сам аватар.</summary>
        public static UxrAvatar FamilyOf(UxrAvatar avatar) =>
            new[] { avatar }.Concat(avatar.GetParentPrefabChain())
                            .FirstOrDefault(a => FamilyBaseGuids.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a)))) ?? avatar;

        /// <summary>Семейства играбельных аватаров — базы семейств аватаров реестра (аватар вне семейства — сам).</summary>
        public static IEnumerable<UxrAvatar> Families() => Avatars().Select(FamilyOf).Distinct().OrderBy(a => a.name);

        /// <summary>
        /// Префабы с хватаемыми предметами: всё из <c>Assets/Prefabs</c> (кроме самих аватаров) и
        /// оружие/магазины арсенала, где бы они ни лежали.
        /// </summary>
        public static IEnumerable<string> ItemPaths()
        {
            var paths = new SortedSet<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && Grabbables(prefab).Any()) paths.Add(path);
            }

            foreach (WeaponInfo weapon in LoadAll<WeaponInfo>())
            {
                foreach (GameObject prefab in new[] { weapon.WeaponPrefab, weapon.MagazinePrefab })
                {
                    if (prefab != null && Grabbables(prefab).Any()) paths.Add(AssetDatabase.GetAssetPath(prefab));
                }
            }

            return paths;
        }

        private static IEnumerable<TestCaseData> Pairs()
        {
            List<UxrAvatar> avatars = Families().ToList();

            foreach (string item in ItemPaths())
            {
                foreach (UxrAvatar avatar in avatars)
                {
                    yield return new TestCaseData(item, AssetDatabase.GetAssetPath(avatar))
                        .SetName($"{{m}}({System.IO.Path.GetFileNameWithoutExtension(item)}, {avatar.name})");
                }
            }
        }

        private static IEnumerable<TestCaseData> FamilyCases()
        {
            foreach (UxrAvatar avatar in Families())
            {
                yield return new TestCaseData(AssetDatabase.GetAssetPath(avatar)).SetName($"{{m}}({avatar.name})");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Тесты
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Предмет × семейство: каждая точка хвата настроена под базу семейства (или аватар вне семейства).</summary>
        [TestCaseSource(nameof(Pairs))]
        public void У_каждой_точки_хвата_есть_хват_аватара(string itemPath, string avatarPath)
        {
            GameObject item = Load(itemPath);
            UxrAvatar avatar = Load(avatarPath).GetComponent<UxrAvatar>();
            var problems = new List<string>();

            foreach (UxrGrabbableObject grabbable in Grabbables(item))
            {
                problems.AddRange(CheckGrabbable(avatar, grabbable, HierarchyPath(grabbable.transform, item.transform)));
            }

            Assert.IsEmpty(problems, $"{item.name} × {avatar.name}: нет хвата ({problems.Count}):\n  " + string.Join("\n  ", problems) +
                                     "\nНастраивается на UxrGrabbableObject: Grab Points → вкладка базы семейства (CyborgHandsBase) или аватара вне семейства; позы паков — окно арсенала, «Взаимодействие» → «Зарегистрировать позы для выбранного аватара».");
        }

        /// <summary>
        /// Предметы сцен Build Settings, чей источник — не <c>Assets/Prefabs</c>: объекты только сцены
        /// и экземпляры сэмплов UltimateXR. Проверяется экземпляр со всеми override'ами сцены;
        /// одинаковые отказы разных экземпляров одного источника схлопываются.
        /// </summary>
        [TestCaseSource(nameof(FamilyCases))]
        public void У_каждого_предмета_сцен_есть_хват_аватара(string avatarPath)
        {
            UxrAvatar avatar = Load(avatarPath).GetComponent<UxrAvatar>();
            var problems = new SortedSet<string>();

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(buildScene.path);

                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        HashSet<UxrGrabbableObject> proxies = AnchorProxies(root);

                        // Сцена проверяет доступные в игре предметы. Выключенные музейные ветки SDK
                        // не участвуют в игре; префабы отдельно проверяются вместе с неактивными детьми.
                        foreach (UxrGrabbableObject grabbable in root.GetComponentsInChildren<UxrGrabbableObject>(false))
                        {
                            if (!grabbable.isActiveAndEnabled) continue;
                            if (proxies.Contains(grabbable)) continue;

                            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(grabbable.gameObject);
                            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                            if (sourcePath != null && sourcePath.StartsWith(PrefabsRoot + "/")) continue; // проверено парами

                            string where = sourcePath != null
                                ? $"{sourcePath} :: {source.name}"
                                : $"{buildScene.path} :: {HierarchyPath(grabbable.transform, null)}";

                            problems.UnionWith(CheckGrabbable(avatar, grabbable, where));
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}, предметы сцен без хвата ({problems.Count}):\n  " + string.Join("\n  ", problems) +
                                     "\nСэмплы UltimateXR не знают аватаров проекта: либо убрать их из сцены, либо заменить префабами из Assets/Prefabs.");
        }

        /// <summary>Источники не пусты и видят то, что игрок точно берёт: оружие арсенала и планшет меню.</summary>
        [Test]
        public void Источники_видят_аватары_оружие_и_планшет()
        {
            Assert.IsNotEmpty(Avatars(), "Ни одного аватара — тест ничего не проверяет.");
            Assert.That(Families().Select(a => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))), Is.Unique, "Семейства повторяются.");
            foreach (UxrAvatar avatar in Avatars())
            {
                Assert.That(Families(), Has.Member(FamilyOf(avatar)), $"{avatar.name}: семейство не проверяется.");
            }

            List<string> items = ItemPaths().ToList();
            Assert.That(items, Has.Some.EndsWith("/Tablet.prefab"), "Планшет меню не попал в выборку предметов.");

            foreach (WeaponInfo weapon in LoadAll<WeaponInfo>().Where(w => w.WeaponPrefab != null))
            {
                Assert.That(items, Has.Member(AssetDatabase.GetAssetPath(weapon.WeaponPrefab)),
                            $"{weapon.name}: оружие не попало в выборку — у префаба нет UxrGrabbableObject?");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Правило
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Все дыры хвата одного предмета для одного аватара. Правило — в summary класса.</summary>
        public static List<string> CheckGrabbable(UxrAvatar avatar, UxrGrabbableObject grabbable, string where)
        {
            var problems = new List<string>();

            for (int i = 0; i < grabbable.GrabPointCount; i++)
            {
                UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                UxrGripPoseInfo grip = point.GetGripPoseInfo(avatar);
                string at = $"{where} / точка {i}{(string.IsNullOrEmpty(point.EditorName) ? "" : $" '{point.EditorName}'")}";

                if (grip == null || ReferenceEquals(grip, point.DefaultGripPoseInfo))
                {
                    problems.Add($"{at}: нет записи аватара — берётся default" +
                                 (point.DefaultGripPoseInfo?.HandPose != null ? $" ('{point.DefaultGripPoseInfo.HandPose.name}')" : " (пустой)"));
                    continue;
                }

                if (grip.HandPose != null && avatar.GetHandPose(grip.HandPose.name) == null)
                    problems.Add($"{at}: поза '{grip.HandPose.name}' не найдена у аватара и его родителей — рука сожмётся общей Grab");

                if (point.SnapMode == UxrSnapToHandMode.DontSnap || point.SnapReference != UxrSnapReference.UseOtherTransform) continue;

                foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    if (!point.BothHandsCompatible && point.HandSide != side) continue;
                    if (Align(grip, side) != null) continue;

                    bool othersHave = Enumerable.Range(0, point.GripPoseInfoCount) // 0 — default, дальше записи аватаров
                                                .Select(point.GetGripPoseInfo)
                                                .Any(g => g != null && g != grip && Align(g, side) != null);
                    if (othersHave)
                        problems.Add($"{at}: нет точки выравнивания {side} руки (у других аватаров есть) — предмет встанет в ладонь пивотом");
                }
            }

            return problems;
        }

        private static Transform Align(UxrGripPoseInfo grip, UxrHandSide side) =>
            side == UxrHandSide.Left ? grip.GripAlignTransformHandLeft : grip.GripAlignTransformHandRight;

        // ══════════════════════════════════════════════════════════════════
        //  Хелперы
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Хватаемые предметы префаба, которые проверяются в нём самом. Пропускаются аватары
        /// (граберы, а не предметы), <c>GrabProxy</c> карманов (хват перенаправляется на вещь внутри) и
        /// вложенные префабы — их проверяет их собственный ассет.
        /// </summary>
        private static IEnumerable<UxrGrabbableObject> Grabbables(GameObject prefab)
        {
            if (prefab.GetComponent<UxrAvatar>() != null) return Enumerable.Empty<UxrGrabbableObject>();

            HashSet<UxrGrabbableObject> proxies = AnchorProxies(prefab);
            return prefab.GetComponentsInChildren<UxrGrabbableObject>(true)
                         .Where(g => !proxies.Contains(g) && !IsInsideNestedPrefab(g.gameObject, prefab));
        }

        private static HashSet<UxrGrabbableObject> AnchorProxies(GameObject root) =>
            new HashSet<UxrGrabbableObject>(root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true)
                                                .Select(a => a.GrabProxy)
                                                .Where(p => p != null));

        private static bool IsInsideNestedPrefab(GameObject go, GameObject prefabRoot)
        {
            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            return instanceRoot != null && instanceRoot != prefabRoot;
        }

        private static string HierarchyPath(Transform t, Transform root)
        {
            if (t == root) return t.name + " (корень)";

            string result = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                result = p.name + "/" + result;
            return result;
        }

        private static List<T> LoadAll<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                         .Select(AssetDatabase.LoadAssetAtPath<T>)
                         .Where(a => a != null)
                         .ToList();

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Нет префаба {path}");
            return prefab;
        }
    }
}
