using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Devices.Integrations;
using UltimateXR.Locomotion;
using UltimateXR.Manipulation;
using UltimateXR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Оснащение каждого выбираемого аватара: карманы, скелет UltimateXR, хваты оружия,
    /// руки, лазер и телепорт.
    ///
    /// <para>
    /// <see cref="PrefabCompositionTests" /> смотрит на сетевой каркас аватара (корневые
    /// компоненты, камера). Здесь — то, что делает аватар <b>играбельным</b>: без карманов
    /// некуда убрать оружие, без позы в хвате рука не сжимается на рукояти, без слоя
    /// пола в маске телепорт не находит ни одной точки. Ни одна из этих поломок не даёт
    /// ошибки в консоли — она видна только в шлеме.
    /// </para>
    ///
    /// <para>
    /// Источник аватаров — <see cref="AvatarRegistry" />: проверяется ровно то, что игрок
    /// может выбрать в меню. Источник оружия — ассеты <see cref="WeaponInfo" />: новое
    /// оружие в арсенале автоматически попадает под проверку карманов и хватов.
    /// </para>
    ///
    /// <para>
    /// Каждая проверка — отдельный тест на каждый аватар, чтобы в Test Runner было видно,
    /// какой аватар в каком аспекте сломан, а не один общий красный тест.
    /// </para>
    /// </summary>
    public class AvatarLoadoutTests
    {
        // Имена — контракт: по ним карманы находит PlayerLoadoutManager и их создаёт
        // AvatarPocketSetup («Tools/VR Battlegrounds/Avatars/Add Weapon Pockets…»).
        private const string PrimaryPocketName   = "Anchor_Back";
        private const string SecondaryPocketName = "Anchor_Hip_R";
        private const string MagazinePocketName  = "MagazinePocket";

        // ══════════════════════════════════════════════════════════════════
        //  Источники данных
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Пути префабов всех аватаров из всех реестров, по одному тест-кейсу на аватар.</summary>
        private static IEnumerable<TestCaseData> RegisteredAvatars()
        {
            foreach (AvatarData data in LoadAll<AvatarRegistry>().SelectMany(r => r.avatars))
            {
                if (data == null || data.prefab == null) continue; // отдельная проверка ниже

                string path = AssetDatabase.GetAssetPath(data.prefab);
                yield return new TestCaseData(path).SetName($"{{m}}({data.prefab.name})");
            }
        }

        private static IEnumerable<WeaponInfo> Weapons()
        {
            return LoadAll<WeaponInfo>().Where(w => w.WeaponPrefab != null);
        }

        private static List<T> LoadAll<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                                .Select(AssetDatabase.LoadAssetAtPath<T>)
                                .Where(a => a != null)
                                .ToList();
        }

        private static UxrAvatar LoadAvatar(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Префаб аватара не найден: {path}");

            UxrAvatar avatar = prefab.GetComponent<UxrAvatar>();
            Assert.IsNotNull(avatar, $"{prefab.name}: нет UxrAvatar на корне");
            return avatar;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Реестр
        // ══════════════════════════════════════════════════════════════════

        [Test]
        public void Реестр_аватаров_не_пуст_и_без_дыр()
        {
            List<AvatarRegistry> registries = LoadAll<AvatarRegistry>();
            Assert.IsNotEmpty(registries, "Не найден ни один AvatarRegistry — проверять нечего.");

            List<string> problems = new List<string>();

            foreach (AvatarRegistry registry in registries)
            {
                if (registry.avatars.Length == 0)
                    problems.Add($"{registry.name}: пустой");

                for (int i = 0; i < registry.avatars.Length; i++)
                {
                    AvatarData data = registry.avatars[i];
                    if (data == null)
                        problems.Add($"{registry.name}[{i}]: пустая ссылка на AvatarData");
                    else if (data.prefab == null)
                        problems.Add($"{registry.name}[{i}] '{data.name}': не задан префаб");
                    else if (data.prefab.GetComponent<UxrAvatar>() == null)
                        problems.Add($"{registry.name}[{i}] '{data.name}': префаб '{data.prefab.name}' не аватар (нет UxrAvatar)");
                }
            }

            Assert.IsEmpty(problems, "Реестр аватаров:\n  " + string.Join("\n  ", problems));
        }

        // ══════════════════════════════════════════════════════════════════
        //  Карманы
        // ══════════════════════════════════════════════════════════════════

        [Test]
        public void Генератор_кармана_берёт_новые_теги_магазинов_из_реестра()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Scene scene = EditorSceneManager.NewPreviewScene();
            var registry = ScriptableObject.CreateInstance<WeaponRegistry>();
            var firstWeapon = ScriptableObject.CreateInstance<WeaponInfo>();
            var secondWeapon = ScriptableObject.CreateInstance<WeaponInfo>();
            try
            {
                GameObject first = new GameObject("Magazine Z");
                SceneManager.MoveGameObjectToScene(first, scene);
                first.AddComponent<UxrGrabbableObject>().Tag = "MagRegressionZ";
                GameObject second = new GameObject("Magazine A");
                SceneManager.MoveGameObjectToScene(second, scene);
                second.AddComponent<UxrGrabbableObject>().Tag = "MagRegressionA";
                SetTemporaryMagazine(firstWeapon, first);
                SetTemporaryMagazine(secondWeapon, second);
                SetTemporaryRegistry(registry, firstWeapon, secondWeapon);

                System.Reflection.MethodInfo source = PocketSetupMethod("GetMagazineCompatibleTags", true);
                var tags = (IReadOnlyList<string>)source.Invoke(null, new object[] { registry });
                Assert.That(tags, Does.Contain("MagRegressionZ").And.Contain("MagRegressionA"));
                Assert.That(tags, Does.Contain("Cartridge:Herrington").And.Contain("Cartridge:FabarmSDASS"),
                    "Авторские теги одиночных патронов не должны исчезать при появлении нового магазина.");
                Assert.That(tags, Is.EqualTo(tags.Distinct().OrderBy(t => t, System.StringComparer.Ordinal).ToArray()));
                Assert.Throws<System.NotSupportedException>(() => ((ICollection<string>)tags).Add("InjectedTag"));
                SetTemporaryRegistry(registry, secondWeapon, firstWeapon);
                Assert.That((IReadOnlyList<string>)source.Invoke(null, new object[] { registry }), Is.EqualTo(tags),
                    "Порядок каталога не должен менять сериализованные compatible tags.");

                // Проверяем сам путь записи, а не только расчёт: authored tags кармана
                // должны переживать upsert с новым тегом из каталога.
                GameObject pocket = new GameObject("Temporary MagazinePocket");
                SceneManager.MoveGameObjectToScene(pocket, scene);
                var anchor = pocket.AddComponent<UxrGrabbableObjectAnchor>();
                var authored = new SerializedObject(anchor);
                SerializedProperty compatible = authored.FindProperty("_compatibleTags");
                compatible.arraySize = 1;
                compatible.GetArrayElementAtIndex(0).stringValue = "Cartridge:AuthoredRegression";
                authored.ApplyModifiedPropertiesWithoutUndo();
                System.Reflection.MethodInfo write = PocketSetupMethod("EnsureAnchorTags", false);
                write.Invoke(null, new object[] { pocket, tags.ToArray(), 0.1f, true });
                string[] once = ReadCompatibleTags(anchor);
                Assert.That(once, Does.Contain("Cartridge:AuthoredRegression").And.Contain("MagRegressionZ"));
                write.Invoke(null, new object[] { pocket, tags.ToArray(), 0.1f, true });
                Assert.That(ReadCompatibleTags(anchor), Is.EqualTo(once), "Повторный upsert должен быть идемпотентным.");
            }
            finally
            {
                // Запись Editor helper пользуется Undo. Убираем только записи стенда,
                // чтобы Test Runner не оставлял их в пользовательской истории.
                Undo.RevertAllDownToGroup(undoGroup);
                Object.DestroyImmediate(firstWeapon);
                Object.DestroyImmediate(secondWeapon);
                Object.DestroyImmediate(registry);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void Генератор_кармана_отказывает_при_неполном_каталоге()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var registry = ScriptableObject.CreateInstance<WeaponRegistry>();
            var weapon = ScriptableObject.CreateInstance<WeaponInfo>();
            try
            {
                System.Reflection.MethodInfo source = PocketSetupMethod("GetMagazineCompatibleTags", true);
                var absent = Assert.Throws<System.Reflection.TargetInvocationException>(() => source.Invoke(null, new object[] { null }));
                Assert.That(absent.InnerException, Is.TypeOf<System.ArgumentNullException>());
                var empty = Assert.Throws<System.Reflection.TargetInvocationException>(() => source.Invoke(null, new object[] { registry }));
                Assert.That(empty.InnerException, Is.TypeOf<System.InvalidOperationException>());

                GameObject magazine = new GameObject("Broken Magazine");
                SceneManager.MoveGameObjectToScene(magazine, scene);
                SetTemporaryMagazine(weapon, magazine);
                SetTemporaryRegistry(registry, weapon);
                var broken = Assert.Throws<System.Reflection.TargetInvocationException>(() => source.Invoke(null, new object[] { registry }));
                Assert.That(broken.InnerException, Is.TypeOf<System.InvalidOperationException>());
                magazine.AddComponent<UxrGrabbableObject>().Tag = " ";
                var untagged = Assert.Throws<System.Reflection.TargetInvocationException>(() => source.Invoke(null, new object[] { registry }));
                Assert.That(untagged.InnerException, Is.TypeOf<System.InvalidOperationException>());
            }
            finally
            {
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(registry);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Editor-сборка недоступна по asmdef-ссылке: reflection сохраняет настоящую
        // проверку генератора, не копируя его алгоритм в тестовую сборку.
        private static System.Reflection.MethodInfo PocketSetupMethod(string name, bool isPublic)
        {
            System.Type type = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VrBattlegrounds.Editor.Avatars.AvatarPocketSetup"))
                .FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Не найдена Editor-сборка AvatarPocketSetup.");
            var method = type.GetMethod(name, System.Reflection.BindingFlags.Static |
                (isPublic ? System.Reflection.BindingFlags.Public : System.Reflection.BindingFlags.NonPublic));
            Assert.That(method, Is.Not.Null, $"AvatarPocketSetup.{name} не найден.");
            return method;
        }

        private static void SetTemporaryMagazine(WeaponInfo weapon, GameObject magazine)
        {
            var so = new SerializedObject(weapon);
            so.FindProperty("_weaponPrefab").objectReferenceValue = magazine;
            so.FindProperty("_magazinePrefab").objectReferenceValue = magazine;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetTemporaryRegistry(WeaponRegistry registry, params WeaponInfo[] weapons)
        {
            var so = new SerializedObject(registry);
            SerializedProperty list = so.FindProperty("_weapons");
            list.arraySize = weapons.Length;
            for (int i = 0; i < weapons.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = weapons[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string[] ReadCompatibleTags(UxrGrabbableObjectAnchor anchor)
        {
            SerializedProperty tags = new SerializedObject(anchor).FindProperty("_compatibleTags");
            return Enumerable.Range(0, tags.arraySize).Select(i => tags.GetArrayElementAtIndex(i).stringValue).ToArray();
        }

        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Часы_HUD_едут_вместе_с_предплечьем(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var watches = avatar.GetComponentsInChildren<VrBattlegrounds.UI.HUD.WristDisplay>(true);
            Assert.That(watches.Length, Is.EqualTo(1), "T-46: весь HUD расположен на одних часах.");
            Assert.That(new[] { avatar.AvatarRig.LeftArm.Forearm, avatar.AvatarRig.RightArm.Forearm }
                .Any(arm => arm != null && watches[0].transform.IsChildOf(arm)), Is.True,
                "Часы должны наследовать движение предплечья, а не корня аватара.");
        }

        /// <summary>
        /// Три кармана на своих местах: основное оружие за спиной, дополнительное на бедре,
        /// магазины на поясе. Карман обязан лежать внутри скелета — иначе он не едет
        /// вместе с телом и остаётся висеть в точке спавна.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Карманы_оружия_и_магазинов_на_месте(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            UxrAvatarRig rig = avatar.AvatarRig;
            List<string> problems = new List<string>();

            Transform torso = rig.Spine != null ? rig.Spine : rig.Hips;

            UxrGrabbableObjectAnchor primary = FindAnchor(avatar, PrimaryPocketName);
            if (primary == null)
                problems.Add($"нет кармана основного оружия '{PrimaryPocketName}'");
            else
            {
                RequireInside(primary.transform, torso, "позвоночника (Spine)", problems);
                if (primary.GrabProxy == null)
                    problems.Add($"'{PrimaryPocketName}': не задан GrabProxy — оружие за спиной не достать, рука его не видит");
            }

            UxrGrabbableObjectAnchor secondary = FindAnchor(avatar, SecondaryPocketName);
            if (secondary == null)
                problems.Add($"нет кармана дополнительного оружия '{SecondaryPocketName}'");
            else
                RequireInside(secondary.transform, rig.Hips, "таза (Hips)", problems);

            UxrMagazinePocket[] magPockets = avatar.GetComponentsInChildren<UxrMagazinePocket>(true);
            if (magPockets.Length != 1)
                problems.Add($"карманов магазинов (UxrMagazinePocket): {magPockets.Length}, ожидается ровно 1");

            foreach (UxrMagazinePocket pocket in magPockets)
            {
                RequireInside(pocket.transform, rig.Hips, "таза (Hips)", problems);

                UxrGrabbableObjectAnchor anchor = pocket.GetComponent<UxrGrabbableObjectAnchor>();
                if (anchor != null && anchor.GrabProxy == null)
                    problems.Add($"'{pocket.name}': не задан GrabProxy — магазин из кармана не вытащить");
            }

            Assert.IsEmpty(problems, $"{avatar.name}, карманы:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Каждое оружие арсенала влезает в свой карман: <see cref="WeaponCategory.Rifle" /> —
        /// за спину, <see cref="WeaponCategory.Pistol" /> — на бедро, магазин любого
        /// оружия — в карман магазинов. Совместимость решает сам SDK
        /// (<see cref="UxrGrabbableObjectAnchor.IsCompatibleObject" />, по тегу).
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Карманы_принимают_оружие_и_магазины_арсенала(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            UxrGrabbableObjectAnchor primary   = FindAnchor(avatar, PrimaryPocketName);
            UxrGrabbableObjectAnchor secondary = FindAnchor(avatar, SecondaryPocketName);
            UxrMagazinePocket        magPocket = avatar.GetComponentInChildren<UxrMagazinePocket>(true);
            UxrGrabbableObjectAnchor magAnchor = magPocket != null ? magPocket.GetComponent<UxrGrabbableObjectAnchor>() : null;

            if (primary == null || secondary == null || magAnchor == null)
                Assert.Fail($"{avatar.name}: карманов нет — см. Карманы_оружия_и_магазинов_на_месте");

            List<WeaponInfo> weapons = Weapons().ToList();
            Assert.IsNotEmpty(weapons, "Не найдено ни одного WeaponInfo с префабом — проверять нечего.");

            List<string> problems = new List<string>();

            foreach (WeaponInfo weapon in weapons)
            {
                UxrGrabbableObjectAnchor holster = weapon.Category == WeaponCategory.Rifle  ? primary
                                                 : weapon.Category == WeaponCategory.Pistol ? secondary
                                                 : null;

                if (holster != null)
                    RequireCompatible(holster, weapon.WeaponPrefab, weapon, problems);

                if (weapon.MagazinePrefab != null)
                    RequireCompatible(magAnchor, weapon.MagazinePrefab, weapon, problems);
            }

            Assert.IsEmpty(problems, $"{avatar.name}, карманы не принимают:\n  " + string.Join("\n  ", problems) +
                                     "\nТеги добавляются в Compatible Tags якоря; эталонный список — AvatarPocketSetup.Pockets.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  UltimateXR: скелет, позы, хваты
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Скелет UltimateXR размечен полностью: голова, таз, обе руки до пальцев. Без
        /// разметки не работает IK рук и тела, а позы пальцев некуда применить.
        /// </summary>
        /// <summary>
        /// Аватар, которого можно выбрать в команде, оставляет на месте гибели труп (T-35):
        /// <c>CorpseSource</c> ставит сборщик трупов. Киборг (запасной, в командах не бывает) и
        /// призрак не умирают — им труп не нужен. Подробные проверки трупа — <c>CorpseTests</c>.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Аватар_команды_оставляет_труп(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool inTeam = LoadAll<TeamData>().Any(t => t.avatars != null && t.avatars.Any(a => a != null && a.prefab == prefab));
            if (!inTeam) Assert.Pass($"{prefab.name} не выбирается в команде — трупа не нужно.");

            var source = prefab.GetComponent<CorpseSource>();
            Assert.IsTrue(source != null && source.CorpsePrefab != null && source.ModelRoot != null,
                $"{prefab.name}: нет трупа (CorpseSource) — погибший исчезнет без тела. Tools/VR Battlegrounds/Avatars/Build Corpses.");
        }

        /// <summary>
        /// Карман магазинов вмещает норму любого ствола арсенала (<c>WeaponInfo.MaxMagazineCount</c>) — предел на
        /// тип магазина; типов — сколько угодно. Меньше — выдача режет норму.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Карман_вмещает_норму_каждого_ствола(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var pocket = avatar.GetComponentInChildren<UxrMagazinePocket>(true);
            Assert.IsNotNull(pocket, $"{avatar.name}: нет кармана магазинов.");

            int need = Weapons().Select(w => w.MaxMagazineCount).DefaultIfEmpty(0).Max();
            Assert.GreaterOrEqual(pocket.PerTypeLimit, need,
                $"{avatar.name}: карман держит {pocket.PerTypeLimit} магазина одного типа, а норма ствола — {need}.");
        }

        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Скелет_UltimateXR_размечен(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            UxrAvatarRig rig = avatar.AvatarRig;
            List<string> problems = new List<string>();

            if (rig.Head == null || rig.Head.Head == null) problems.Add("не размечена голова (Head)");
            if (rig.Hips == null)                          problems.Add("не размечен таз (Hips)");
            if (rig.Spine == null)                         problems.Add("не размечен позвоночник (Spine)");

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrAvatarArm arm = avatar.GetArm(side);
                if (arm == null || arm.UpperArm == null) problems.Add($"{side}: не размечено плечо (UpperArm)");
                if (arm == null || arm.Forearm == null)  problems.Add($"{side}: не размечено предплечье (Forearm)");
                if (avatar.GetHandBone(side) == null)    problems.Add($"{side}: не размечена кисть (Wrist)");
                if (!rig.HasFingerData(side))            problems.Add($"{side}: не размечены пальцы");
            }

            Assert.IsEmpty(problems, $"{avatar.name}, AvatarRig:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Контроллер аватара сжимает руку на Grip и выставляет палец на Button1 — для обеих
        /// рук, и каждая поза реально есть у аватара (своя или унаследованная от
        /// родительского префаба). Иначе хват и указание пальцем визуально не работают.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Позы_захвата_и_указания_настроены(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            List<string> problems = new List<string>();

            if (avatar.DefaultHandPose == null)
                problems.Add("не задана поза рук по умолчанию (Default Hand Pose)");

            UxrStandardAvatarController controller = avatar.GetComponent<UxrStandardAvatarController>();
            if (controller == null)
            {
                problems.Add("нет UxrStandardAvatarController на корне");
            }
            else
            {
                var required = new[]
                {
                    UxrAnimationType.LeftHandGrab, UxrAnimationType.RightHandGrab,
                    UxrAnimationType.LeftFingerPoint, UxrAnimationType.RightFingerPoint
                };

                foreach (UxrAnimationType type in required)
                {
                    UxrAvatarControllerEvent evt = controller.ControllerEvents.FirstOrDefault(e => e.TypeOfAnimation == type);

                    if (evt == null)
                        problems.Add($"нет события контроллера {type}");
                    else if (string.IsNullOrEmpty(evt.PoseName))
                        problems.Add($"{type}: не задана поза");
                    else if (avatar.GetHandPose(evt.PoseName) == null)
                        problems.Add($"{type}: позы '{evt.PoseName}' нет у аватара и его родительских префабов");
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}, позы рук:\n  " + string.Join("\n  ", problems));
        }

        // Позы хвата предметов (предмет × аватар, включая предметы сцен) — в GrabPoseCoverageTests:
        // проверка переехала туда целиком, чтобы у класса ошибки была одна точка правды.

        // ══════════════════════════════════════════════════════════════════
        //  Руки: захват, лазер, телепорт
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// У каждой руки ровно один <see cref="UxrGrabber" />, лазер для UI и телепорт с мишенью.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void У_каждой_руки_есть_захват_лазер_и_телепорт(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            List<string> problems = new List<string>();

            UxrGrabber[]              grabbers  = avatar.GetComponentsInChildren<UxrGrabber>(true);
            UxrLaserPointer[]         lasers    = avatar.GetComponentsInChildren<UxrLaserPointer>(true);
            UxrTeleportLocomotionBase[] teleports = avatar.GetComponentsInChildren<UxrTeleportLocomotionBase>(true);

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                int sideGrabbers = grabbers.Count(g => g.Side == side);
                if (sideGrabbers != 1)
                    problems.Add($"{side}: UxrGrabber {sideGrabbers} шт., ожидается 1");

                if (lasers.All(l => l.HandSide != side))
                    problems.Add($"{side}: нет UxrLaserPointer — рукой не выбрать пункт меню");

                UxrTeleportLocomotionBase[] sideTeleports = teleports.Where(t => t.HandSide == side).ToArray();
                if (sideTeleports.Length == 0)
                    problems.Add($"{side}: нет телепорта");

                // Target отдаёт рантайм-экземпляр мишени, созданный в Awake; в ассете он всегда
                // null. Настройку префаба хранит сериализованное поле _target.
                foreach (UxrTeleportLocomotionBase teleport in sideTeleports.Where(t =>
                             new SerializedObject(t).FindProperty("_target").objectReferenceValue == null))
                    problems.Add($"{side}: у '{teleport.name}' не задана мишень (Target)");
            }

            Assert.IsEmpty(problems, $"{avatar.name}, руки:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Телепорт попадает в пол каждой игровой сцены.
        ///
        /// <para>
        /// SDK признаёт точку валидной, только если слой коллайдера под лучом входит в
        /// <c>ValidTargetLayers</c> (<c>UxrTeleportLocomotionBase.IsValidDestination</c>).
        /// Слой пола здесь не угадывается, а берётся из сцен: луч вниз из каждой
        /// <see cref="TeamSpawnZone" /> в сценах Build Settings.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Телепорт_попадает_в_пол_карт(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            Dictionary<int, string> floors = FloorLayers();
            Assert.IsNotEmpty(floors, "Ни в одной сцене Build Settings не найден пол под TeamSpawnZone — сверять не с чем.");

            List<string> problems = new List<string>();

            foreach (UxrTeleportLocomotionBase teleport in avatar.GetComponentsInChildren<UxrTeleportLocomotionBase>(true))
            {
                foreach (KeyValuePair<int, string> floor in floors)
                {
                    if ((teleport.ValidTargetLayers.value & (1 << floor.Key)) == 0)
                        problems.Add($"'{teleport.name}': слой пола '{LayerMask.LayerToName(floor.Key)}' ({floor.Key}) " +
                                     $"не входит в Valid Target Layers (маска {teleport.ValidTargetLayers.value}); пол: {floor.Value}");
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}, телепорт не видит пол:\n  " + string.Join("\n  ", problems));
        }

        private static Dictionary<int, string> s_floorLayers;

        /// <summary>Слой пола → пример, где он найден. Считается один раз на прогон: сцены открываются долго.</summary>
        private static Dictionary<int, string> FloorLayers()
        {
            if (s_floorLayers != null) return s_floorLayers;

            s_floorLayers = new Dictionary<int, string>();

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(buildScene.path);

                try
                {
                    PhysicsScene physics = scene.GetPhysicsScene();

                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (TeamSpawnZone zone in root.GetComponentsInChildren<TeamSpawnZone>(true))
                        {
                            Bounds  bounds = zone.GetComponent<BoxCollider>().bounds;
                            Vector3 origin = bounds.center + Vector3.up * bounds.extents.y;

                            if (physics.Raycast(origin, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, ~0, QueryTriggerInteraction.Ignore)
                                && !s_floorLayers.ContainsKey(hit.collider.gameObject.layer))
                            {
                                s_floorLayers[hit.collider.gameObject.layer] = $"{buildScene.path} / {hit.collider.name}";
                            }
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            return s_floorLayers;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Корень аватара
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// <see cref="PlayerLoadoutManager" /> пополняет карман магазинами в начале раунда.
        /// Без него карман всегда пуст — тот же класс дефекта, что ARCH-01: код есть,
        /// в префабе нет, и рантайм об этом молчит.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void На_корне_есть_менеджер_снаряжения(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);

            Assert.IsNotNull(avatar.GetComponent<PlayerLoadoutManager>(),
                $"{avatar.name}: нет PlayerLoadoutManager на корне — магазины в начале раунда не выдаются.");
        }

        /// <summary>
        /// <see cref="PocketHaptics" /> — единственный способ найти карман в игре: зоны карманов
        /// не видны, и игрок узнаёт «сейчас положу / сейчас достану» по вибрации контроллера.
        /// Без компонента карманы работают, но вслепую — и ошибки в консоли нет.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void На_корне_есть_хаптики_карманов(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);

            Assert.IsNotNull(avatar.GetComponent<PocketHaptics>(),
                $"{avatar.name}: нет PocketHaptics на корне — карманы не отзываются вибрацией.");
        }

        // Требование «у выбывшего прячется всё тело» (SpectatorController.Geo/ExtraGeo/GhostPrefab) снято
        // в T-35: выбывший — отдельный аватар-призрак (GhostAvatarTests), тело погибшего — труп
        // (CorpseTests), а живое тело при выбывании уничтожается целиком — прятать в нём нечего.

        private static bool IsActiveInPrefab(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
            }
            return true;
        }

        /// <summary>
        /// <see cref="VrBattlegrounds.Weapons.MagazineEjectInput" /> — кнопка выброса магазина (A/X у
        /// руки с оружием). Без компонента магазин вынимается только второй рукой, ошибки нет.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void На_корне_есть_кнопка_выброса_магазина(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);

            Assert.IsNotNull(avatar.GetComponent<VrBattlegrounds.Weapons.MagazineEjectInput>(),
                $"{avatar.name}: нет MagazineEjectInput на корне — магазин не выбрасывается кнопкой A/X.");
        }

        /// <summary>
        /// Каждый карман звучит, когда рука кладёт в него предмет и когда достаёт
        /// (<see cref="AnchorSound" />) — подтверждение «принял / отдал», глазами карман не видно.
        /// Источник — не на объекте <c>Activate On …</c> якоря: при хвате из якоря SDK выключает
        /// <c>Activate On Placed</c>, и звук доставания обрывался бы на первом кадре.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Карманы_звучат_при_укладке_и_доставании(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var problems = new List<string>();

            foreach (string pocketName in new[] { PrimaryPocketName, SecondaryPocketName, MagazinePocketName })
            {
                UxrGrabbableObjectAnchor anchor = avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true)
                                                        .FirstOrDefault(a => a.name.Contains(pocketName));
                if (anchor == null)
                {
                    problems.Add($"{pocketName}: нет кармана");
                    continue;
                }

                AnchorSound sound = anchor.GetComponent<AnchorSound>();
                if (sound == null)
                {
                    problems.Add($"{anchor.name}: нет AnchorSound");
                    continue;
                }

                if (sound.Source == null || sound.InsertClip == null) problems.Add($"{anchor.name}: нет источника или звука вставки");
                if (sound.TakeOutClip == null) problems.Add($"{anchor.name}: нет звука доставания (Take Out Clip)");
                if (!sound.TakeOutOnlyByHand) problems.Add($"{anchor.name}: доставание звучит и без руки — карман магазинов сам программно вынимает вложенное, звук будет на каждую укладку");

                GameObject[] activated = { anchor.ActivateOnPlaced, anchor.ActivateOnEmpty, anchor.ActivateOnCompatibleNear, anchor.ActivateOnCompatibleNotNear, anchor.ActivateOnHandNearAndGrabbable };
                if (sound.Source != null && activated.Any(go => go != null && sound.Source.transform.IsChildOf(go.transform)))
                {
                    problems.Add($"{anchor.name}: источник '{sound.Source.name}' на объекте, который включает/выключает якорь — звук доставания оборвётся");
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}: карманы без звука:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Пистолет из кобуры достаётся хватом в области вокруг неё, а не точным попаданием в
        /// рукоять: в динамике виртуальное бедро не совпадает с реальным, и игрок, тянущийся к
        /// кобуре на ощупь, промахивается. Поэтому у <c>Anchor_Hip_R</c>, как у спины, есть
        /// прокси-хват (перенаправление захвата на предмет в якоре — патч SDK №4).
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void У_кобуры_есть_прокси_хват(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            UxrGrabbableObjectAnchor hip = avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true)
                                                 .FirstOrDefault(a => a.name.Contains(SecondaryPocketName));

            Assert.IsNotNull(hip, $"{avatar.name}: нет кармана '{SecondaryPocketName}'.");
            Assert.IsNotNull(hip.GrabProxy,
                $"{avatar.name}: у '{hip.name}' нет Grab Proxy — пистолет берётся только точным попаданием в рукоять.");
        }

        /// <summary>
        /// Карман с прокси-хватом кладёт предмет там же, где его отдаёт: точка укладки якоря
        /// (<c>DropProximityTransform</c>) — это прокси. Игрок подносит оружие туда, где прокси
        /// подсвечивается; если SDK меряет укладку от другой точки, до неё может быть дальше
        /// <c>MaxPlaceDistance</c> — и оружие падает на землю.
        ///
        /// <para>
        /// Так было у MEF: к <c>Anchor_Back</c> привязали <c>BackGrabProxy</c> у плеча, а точка
        /// укладки осталась в центре спины, в 35 см от прокси при радиусе 0.2 м.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Карман_с_прокси_кладёт_там_же_где_отдаёт(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var problems = new List<string>();

            foreach (UxrGrabbableObjectAnchor anchor in avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                UxrGrabbableObject proxy = anchor.GrabProxy;
                if (proxy == null) continue;

                float gap = Vector3.Distance(anchor.DropProximityTransform.position, proxy.transform.position);
                if (gap > 0.01f)
                {
                    problems.Add($"{anchor.name}: точка укладки '{anchor.DropProximityTransform.name}' в {gap:0.00} м " +
                                 $"от прокси '{proxy.name}' (радиус укладки {anchor.MaxPlaceDistance:0.00} м)");
                }
            }

            Assert.IsEmpty(problems,
                $"{avatar.name}: предмет, поднесённый к прокси, в карман не встанет.\n  " + string.Join("\n  ", problems) +
                "\nПочинка — у якоря снять Drop Proximity Transform Use Self и указать сам прокси.");
        }

        /// <summary>
        /// Палец нажимает UI (планшет, меню) только если у обеих рук есть <see cref="UxrFingerTip" />
        /// и его <c>forward</c> смотрит вдоль пальца: <c>UxrFingerTipRaycaster</c> пускает луч по
        /// <see cref="UxrFingerTip.WorldDir" /> и отбрасывает касание под большим углом к канвасу.
        ///
        /// <para>
        /// Так сломался MEF: утилита <c>AvatarFingertipSetup</c> пропускала фаланги короче 3.2 см
        /// (порог по квадрату длины), кончик остался с нулевым поворотом и смотрел вбок. Граница
        /// 60° — как в утилите: меньший наклон бывает намеренным (у киборга 30°).
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Кончики_пальцев_для_UI_смотрят_вдоль_пальца(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var problems = new List<string>();
            var sides = new HashSet<UxrHandSide>();

            foreach (UxrFingerTip tip in avatar.GetComponentsInChildren<UxrFingerTip>(true))
            {
                Transform bone = tip.transform.parent;
                if (bone == null || bone.parent == null) continue;

                if (UxrAvatarRig.GetHandSide(tip.transform, out UxrHandSide side)) sides.Add(side);

                Vector3 fingerDir = (bone.position - bone.parent.position).normalized;
                float   dot       = Vector3.Dot(tip.transform.forward, fingerDir);

                if (dot < 0.5f)
                {
                    problems.Add($"{AnimationUtility.CalculateTransformPath(tip.transform, avatar.transform)}: " +
                                 $"forward отклонён от пальца на {Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg:0}°");
                }
            }

            if (!sides.Contains(UxrHandSide.Left)) problems.Add("нет UxrFingerTip у левой руки");
            if (!sides.Contains(UxrHandSide.Right)) problems.Add("нет UxrFingerTip у правой руки");

            Assert.IsEmpty(problems,
                $"{avatar.name}: палец не нажмёт UI.\n  " + string.Join("\n  ", problems) +
                "\nПочинка — Tools/VR Battlegrounds/Avatars/Setup Avatar UI Fingertips (выравнивает испорченные кончики).");
        }

        /// <summary>
        /// <see cref="UxrDummyControllerInput" /> ровно один: два компонента одного типа на
        /// корне — лишний id в сетевых ссылках и неоднозначный <c>GetComponent</c>.
        /// Наличие проверяет <see cref="PrefabCompositionTests" />, здесь — дубли.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void На_корне_нет_дублей_dummy_ввода(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            int count = avatar.GetComponents<UxrDummyControllerInput>().Length;

            Assert.LessOrEqual(count, 1, $"{avatar.name}: UxrDummyControllerInput на корне {count} шт.");
        }

        /// <summary>
        /// Калибровка hand tracking ссылается только на живые кости. Пустая ссылка —
        /// <c>UxrHandTracking.BuildCalibrationCache</c> в <c>Awake</c> кладёт в словарь второй
        /// ключ <c>null</c> и падает с <c>ArgumentException</c> на каждом спавне аватара.
        /// Так было у варианта, заменившего риг <c>PlayerBase</c>: ссылки на кости базы
        /// обнулились. Нет своей калибровки — списки должны быть пустыми.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Калибровка_hand_tracking_без_пустых_костей(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var problems = new List<string>();

            foreach (UxrHandTracking tracking in avatar.GetComponentsInChildren<UxrHandTracking>(true))
            {
                var so = new SerializedObject(tracking);
                foreach (string list in new[] { "_leftCalibrationData", "_rightCalibrationData" })
                {
                    SerializedProperty data = so.FindProperty(list);
                    int empty = 0;
                    for (int i = 0; i < data.arraySize; i++)
                    {
                        if (data.GetArrayElementAtIndex(i).FindPropertyRelative("_transform").objectReferenceValue == null)
                            empty++;
                    }

                    if (empty > 0)
                        problems.Add($"{tracking.GetType().Name}.{list}: {empty} из {data.arraySize} без кости");
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}:\n{string.Join("\n", problems)}");
        }

        /// <summary>
        /// Структурный контракт native UXR: humanoid-риг, обе ноги своего аватара,
        /// отдельная humanoid-копия для клипов и включённый контроллер ног.
        /// Legacy LegsAnimator/bridge заменены native UXR (Docs/avatar-animation.md).
        /// Алгоритмы приседа и сидения здесь не проверяются: их пользовательская
        /// приёмка остаётся отдельной задачей.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Native_UXR_ноги_настроены_на_своих_костях(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            Animator animator = avatar.GetComponentsInChildren<Animator>(true)
                                      .FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
            Assert.IsNotNull(animator, $"{avatar.name}: нет humanoid-рига (Animator с человеческим Avatar) — ног нет");

            UxrStandardAvatarController controller = avatar.GetComponent<UxrStandardAvatarController>();
            Assert.That(controller, Is.Not.Null, $"{avatar.name}: нет UxrStandardAvatarController");
            Assert.That(controller.enabled && controller.UseNativeLegIK, Is.True,
                $"{avatar.name}: native ноги выключены в префабе");
            Assert.That(avatar.AvatarRigType, Is.EqualTo(UxrAvatarRigType.HalfOrFullBody),
                $"{avatar.name}: native ноги требуют риг HalfOrFullBody");
            Assert.That(controller.Legs, Is.Not.Null, $"{avatar.name}: нет настроек native ног");

            GameObject copy = controller.Legs.locomotionRig;
            Assert.That(copy, Is.Not.Null, $"{avatar.name}: не назначена отдельная копия рига для клипов");
            Assert.That(EditorUtility.IsPersistent(copy), Is.True, $"{avatar.name}: копия рига должна быть ассетом");
            // UxrAnimatedLegs берёт _rig.GetComponent<Animator>(), без поиска в детях.
            Animator copyAnimator = copy.GetComponent<Animator>();
            Assert.That(copyAnimator, Is.Not.Null, $"{avatar.name}: на корне копии рига нет Animator");
            Assert.That(copyAnimator.avatar != null && copyAnimator.avatar.isHuman, Is.True,
                $"{avatar.name}: копия рига не humanoid");
            Assert.That(controller.Legs.locomotionController != null || copyAnimator.runtimeAnimatorController != null,
                Is.True, $"{avatar.name}: у копии рига не назначен контроллер клипов");

            var problems = new List<string>();
            foreach (Behaviour legacy in PrefabAuthoredActivity.ActiveLegacyLegWriters(avatar.gameObject))
                problems.Add($"активен legacy {legacy.GetType().Name} на '{legacy.name}' одновременно с native ногами");
            UxrAvatarLeg[] rigLegs = { avatar.AvatarRig.LeftLeg, avatar.AvatarRig.RightLeg };
            for (int i = 0; i < rigLegs.Length; i++)
            {
                Transform[] bones = { rigLegs[i].UpperLeg, rigLegs[i].LowerLeg, rigLegs[i].Foot };
                string[] names = { "UpperLeg", "LowerLeg", "Foot" };
                for (int j = 0; j < bones.Length; j++)
                {
                    if (bones[j] == null)
                        problems.Add($"UxrAvatarRig.Leg[{i}].{names[j]} пуст");
                    else if (!bones[j].IsChildOf(animator.transform))
                        problems.Add($"UxrAvatarRig.Leg[{i}].{names[j]} = '{bones[j].name}' не из своего humanoid-рига");
                }
                if (bones.All(b => b != null) &&
                    (!bones[1].IsChildOf(bones[0]) || !bones[2].IsChildOf(bones[1])))
                    problems.Add($"UxrAvatarRig.Leg[{i}]: бедро, голень и стопа не образуют цепочку");
            }

            Assert.IsEmpty(problems, $"{avatar.name}:\n{string.Join("\n", problems)}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════
        //  Глаза
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Камера стоит туда, где UltimateXR считает глаза (<c>Eyes Base Height</c>/<c>Eyes Forward Offset</c> читаются один
        /// раз при появлении аватара). Сборщик (<c>ControllerAndCameraSetup.ApplyHeadDefaults</c>) берёт их из костей глаз:
        /// настройка, разошедшаяся с костями, — камера не в глазах модели. Аватар без костей глаз (киборг) не проверяется.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Высота_глаз_UltimateXR_совпадает_с_костями_глаз(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            if (!TryEyeBones(avatar, out Vector3 eyesLocal)) Assert.Pass($"{avatar.name}: нет костей глаз — высота задана вручную.");

            var so = new SerializedObject(avatar.GetComponent<UxrStandardAvatarController>());
            float baseHeight = so.FindProperty("_bodyIKSettings._eyesBaseHeight").floatValue;
            float forward = so.FindProperty("_bodyIKSettings._eyesForwardOffset").floatValue;
            Assert.That(baseHeight, Is.EqualTo(eyesLocal.y).Within(0.002f), "Eyes Base Height ≠ высота костей глаз — пересобрать шаг 3/4 (Controller и камера).");
            Assert.That(forward, Is.EqualTo(eyesLocal.z + 0.02f).Within(0.002f), "Eyes Forward Offset ≠ вынос костей глаз + 0,02 м.");
        }

        /// <summary>
        /// MEF: кости глаз модели стояли на переносице под очками — камера была на уровне носа (2026-10-06). Положение
        /// выверено в шлеме у зеркала — центр линз очков — и задаётся сборщиком (<c>ControllerAndCameraSetup.HeadDefaults</c>).
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void MEF_глаза_на_уровне_линз_очков(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            if (!avatar.name.Contains("MEF")) Assert.Pass("Не MEF.");
            Assert.That(TryEyeBones(avatar, out Vector3 eyesLocal), Is.True, $"{avatar.name}: нет костей глаз.");
            Assert.That(eyesLocal.y, Is.EqualTo(1.7203f).Within(0.002f), "Высота глаз MEF — центр линз очков, а не исходные кости модели (1,691).");
            Assert.That(eyesLocal.z, Is.EqualTo(0.1097f).Within(0.002f), "Вынос глаз MEF вперёд — центр линз очков.");
            var so = new SerializedObject(avatar.GetComponent<UxrStandardAvatarController>());
            Assert.That(so.FindProperty("_bodyIKSettings._headFreeRangeBend").floatValue, Is.EqualTo(55f).Within(0.01f),
                "Head Free Range Bend MEF — 55°: наклон головы вниз без корпуса.");
        }

        /// <summary>Середина костей глаз (humanoid Animator модели) в осях корня аватара.</summary>
        private static bool TryEyeBones(UxrAvatar avatar, out Vector3 eyesLocal)
        {
            eyesLocal = default;
            Animator rig = avatar.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.isHuman);
            Transform left = rig != null ? rig.GetBoneTransform(HumanBodyBones.LeftEye) : null;
            Transform right = rig != null ? rig.GetBoneTransform(HumanBodyBones.RightEye) : null;
            if (left == null || right == null) return false;
            eyesLocal = avatar.transform.InverseTransformPoint((left.position + right.position) * 0.5f);
            return true;
        }

        private static UxrGrabbableObjectAnchor FindAnchor(UxrAvatar avatar, string name)
        {
            return avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true).FirstOrDefault(a => a.name == name);
        }

        private static void RequireInside(Transform item, Transform bone, string boneDescription, List<string> problems)
        {
            if (bone == null)
                problems.Add($"'{item.name}': кость {boneDescription} не размечена в AvatarRig, проверить положение нельзя");
            else if (!item.IsChildOf(bone))
                problems.Add($"'{item.name}' лежит под '{item.parent?.name}', а не внутри {boneDescription} '{bone.name}' — не будет двигаться с телом");
        }

        private static void RequireCompatible(UxrGrabbableObjectAnchor anchor, GameObject item, WeaponInfo weapon, List<string> problems)
        {
            UxrGrabbableObject grabbable = item.GetComponent<UxrGrabbableObject>();

            if (grabbable == null)
                problems.Add($"'{anchor.name}' ← {item.name} ({weapon.name}): у предмета нет UxrGrabbableObject");
            else if (!anchor.IsCompatibleObject(grabbable))
                problems.Add($"'{anchor.name}' ← {item.name} ({weapon.name}): тег '{grabbable.Tag}' не в Compatible Tags");
        }
    }
}
