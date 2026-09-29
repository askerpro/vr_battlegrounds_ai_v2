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

        /// <summary>
        /// У каждого <see cref="UxrGrabbableObject" /> из <c>Assets/Prefabs</c> для каждой точки
        /// хвата есть поза именно этого аватара — не дефолтная.
        ///
        /// <para>
        /// UltimateXR хранит позу хвата по GUID префаба аватара и ищет её вверх по цепочке
        /// родительских префабов (<see cref="UxrGrabPointInfo.GetGripPoseInfo(UxrAvatar, bool)" />);
        /// не нашёл — возвращает сам объект <see cref="UxrGrabPointInfo.DefaultGripPoseInfo" />.
        /// Поэтому «не default» — сравнение ссылок. Дефолтная запись не знает руки аватара:
        /// в ней нет точек выравнивания (<c>GripAlignTransformHandLeft/Right</c>), и предмет
        /// встаёт в ладонь своим пивотом, а не рукоятью; поза пальцев — чужая или общая
        /// <c>Grab</c>. Запись родительского префаба аватара засчитывается — это штатное
        /// наследование SDK.
        /// </para>
        ///
        /// <para>
        /// Своя запись с пустой позой — не отказ: <c>UxrStandardAvatarController.UpdateGrabPoseInfo</c>
        /// тогда берёт общую позу <c>Grab</c> аватара, а выравнивание остаётся своим. Так
        /// настроены, например, затвор и отдача оружия.
        /// </para>
        ///
        /// <para>
        /// Не проверяются <c>GrabProxy</c> карманов (хват перенаправляется на вещь внутри,
        /// поза прокси не видна) и вложенные префабы — их проверяет их собственный ассет.
        /// </para>
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void У_каждого_grabbable_префабов_своя_поза_хвата(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            List<string> problems = new List<string>();
            int checkedPoints = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string     prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab     = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null || prefab.GetComponent<UxrAvatar>() != null) continue;

                HashSet<UxrGrabbableObject> proxies = AnchorProxies(prefab);

                foreach (UxrGrabbableObject grabbable in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    if (proxies.Contains(grabbable) || IsInsideNestedPrefab(grabbable.gameObject, prefab)) continue;

                    string where = $"{prefabPath} :: {HierarchyPath(grabbable.transform, prefab.transform)}";
                    checkedPoints += CheckGripPoses(avatar, grabbable, where, problems);
                }
            }

            Assert.That(checkedPoints, Is.GreaterThan(0), "Не найдено ни одной точки хвата — тест ничего не проверил.");
            Assert.IsEmpty(problems, $"{avatar.name}, нет своей позы хвата ({problems.Count}):\n  " + string.Join("\n  ", problems) +
                                     "\nПоза задаётся на UxrGrabbableObject: Grab Points → вкладка аватара.");
        }

        /// <summary>
        /// То же для предметов, лежащих в сценах Build Settings, чей источник — не
        /// <c>Assets/Prefabs</c>: объекты только сцены и экземпляры сэмплов UltimateXR.
        /// Проверяется экземпляр со всеми override'ами сцены; одинаковые отказы разных
        /// экземпляров одного источника схлопываются в одну строку.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void У_каждого_grabbable_в_сценах_своя_поза_хвата(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            SortedSet<string> problems = new SortedSet<string>();

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(buildScene.path);

                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        HashSet<UxrGrabbableObject> proxies = AnchorProxies(root);

                        foreach (UxrGrabbableObject grabbable in root.GetComponentsInChildren<UxrGrabbableObject>(true))
                        {
                            if (proxies.Contains(grabbable)) continue;

                            GameObject source     = PrefabUtility.GetCorrespondingObjectFromOriginalSource(grabbable.gameObject);
                            string     sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
                            if (sourcePath != null && sourcePath.StartsWith("Assets/Prefabs/")) continue; // проверено тестом префабов

                            string where = sourcePath != null
                                ? $"{sourcePath} :: {source.name}"
                                : $"{buildScene.path} :: {HierarchyPath(grabbable.transform, null)}";

                            List<string> local = new List<string>();
                            CheckGripPoses(avatar, grabbable, where, local);
                            problems.UnionWith(local);
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(problems, $"{avatar.name}, предметы сцен без своей позы хвата ({problems.Count}):\n  " +
                                     string.Join("\n  ", problems) +
                                     "\nСэмплы UltimateXR не знают аватаров проекта: либо убрать их из сцены, либо заменить префабами из Assets/Prefabs.");
        }

        /// <summary>Проверяет все точки хвата предмета; возвращает число проверенных точек.</summary>
        private static int CheckGripPoses(UxrAvatar avatar, UxrGrabbableObject grabbable, string where, List<string> problems)
        {
            for (int i = 0; i < grabbable.GrabPointCount; i++)
            {
                UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                UxrGripPoseInfo  grip  = point.GetGripPoseInfo(avatar);
                string           at    = $"{where} / точка {i}{(string.IsNullOrEmpty(point.EditorName) ? "" : $" '{point.EditorName}'")}";

                if (grip == null || ReferenceEquals(grip, point.DefaultGripPoseInfo))
                    problems.Add($"{at}: нет записи для аватара — берётся default" +
                                 (point.DefaultGripPoseInfo?.HandPose != null ? $" ('{point.DefaultGripPoseInfo.HandPose.name}')" : " (пустой)"));
                else if (grip.HandPose != null && avatar.GetHandPose(grip.HandPose.name) == null)
                    problems.Add($"{at}: поза '{grip.HandPose.name}' не найдена у аватара и его родителей");
            }

            return grabbable.GrabPointCount;
        }

        private static HashSet<UxrGrabbableObject> AnchorProxies(GameObject root)
        {
            return new HashSet<UxrGrabbableObject>(root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true)
                                                       .Select(a => a.GrabProxy)
                                                       .Where(p => p != null));
        }

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

        /// <summary>
        /// Выбывший — призрак (<see cref="SpectatorController.GhostPrefab" />): тело (<see cref="SpectatorController.Geo" />) прячется. Всё, что
        /// нарисовано на костях скелета вне <c>Geo</c> (часы на предплечье), обязано лежать в
        /// <see cref="SpectatorController.ExtraGeo" /> — иначе оно висит в воздухе рядом с призраком.
        /// Карманы и предметы в них не в счёт: снаряжение у выбывшего снимает своя логика.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void У_выбывшего_прячется_всё_тело(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            var spectator = avatar.GetComponent<SpectatorController>();
            Assert.IsNotNull(spectator, $"{avatar.name}: нет SpectatorController.");
            Assert.IsNotNull(spectator.Geo, $"{avatar.name}: SpectatorController.Geo не назначен — тело выбывшего видно целиком.");
            Assert.IsNotNull(spectator.GhostPrefab, $"{avatar.name}: SpectatorController.GhostPrefab не назначен — выбывший исчезнет без призрака.");

            Transform hips = avatar.AvatarRig.Hips;
            Assert.IsNotNull(hips, $"{avatar.name}: в скелете UltimateXR нет Hips.");
            Transform skeleton = hips;
            while (skeleton.parent != null && skeleton.parent != avatar.transform) skeleton = skeleton.parent;

            var hidden = new List<Transform> { spectator.Geo.transform };
            hidden.AddRange(spectator.ExtraGeo.Where(g => g != null).Select(g => g.transform));

            List<string> visible = skeleton.GetComponentsInChildren<Renderer>(true)
                .Where(r => IsActiveInPrefab(r.transform))
                .Where(r => r.GetComponentInParent<UxrGrabbableObject>(true) == null && r.GetComponentInParent<UxrGrabbableObjectAnchor>(true) == null)
                .Where(r => !hidden.Any(h => r.transform.IsChildOf(h)))
                .Select(r => r.name)
                .ToList();

            Assert.IsEmpty(visible, $"{avatar.name}: у выбывшего остаются видны {string.Join(", ", visible)} — добавь в SpectatorController.ExtraGeo.");
        }

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

                if (sound.Source == null || sound.Source.clip == null) problems.Add($"{anchor.name}: нет источника или звука вставки");
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
        /// У каждого аватара с humanoid-ногами есть Legs Animator (FImpossible) и мост
        /// <c>LegsAnimatorUxrBridge</c> на объекте рига с <c>Animator</c>, а все ссылки
        /// ведут в свой риг. Без него ботинки проваливаются в пол при приседании.
        /// Настройки копируются с Heavy, и ссылки на кости при копировании не
        /// переносятся (Heavy и MEF названы по-разному) — пустая кость ломает IK молча.
        /// Сборки плагина и моста тестам недоступны — проверка по имени типа.
        /// Cyborg без humanoid-рига — у него нет ног, требование не действует.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Legs_Animator_настроен_на_своих_костях(string path)
        {
            UxrAvatar avatar = LoadAvatar(path);
            Animator animator = avatar.GetComponentsInChildren<Animator>(true)
                                      .FirstOrDefault(a => a.avatar != null && a.avatar.isHuman);
            if (animator == null)
                Assert.Pass($"{avatar.name}: нет humanoid-рига — ног нет, Legs Animator не нужен");

            Component legs   = FindByTypeName(animator.gameObject, "LegsAnimator");
            Component bridge = FindByTypeName(animator.gameObject, "LegsAnimatorUxrBridge");
            Assert.IsNotNull(legs,   $"{avatar.name}: нет LegsAnimator на '{animator.name}'");
            Assert.IsNotNull(bridge, $"{avatar.name}: нет LegsAnimatorUxrBridge на '{animator.name}'");

            var problems = new List<string>();
            // Оба включены с самого старта: плагин запоминает опорную позу таза при инициализации,
            // и она должна быть позой модели, а не позой, в которую UltimateXR уже поставил тело
            // по камере. Включённый позже (мостом или галочкой) он держал таз на +14 см.
            if (!((Behaviour)legs).enabled)
                problems.Add("LegsAnimator выключен в префабе — инициализируется поздно, в позе IK, и поднимает таз");
            if (!((Behaviour)bridge).enabled)
                problems.Add("LegsAnimatorUxrBridge выключен — корень ног не привязан к аватару");

            // Позицию таза Legs Animator возвращает в начале кадра только в режиме FixedCalibrate
            // (LegsA.Hips.Reference.PreCalibrate); остальные режимы ждут, что её перезапишет
            // анимация. Контроллера у рига нет — поправка высоты таза копится кадр за кадром,
            // таз уезжает, а UltimateXR, держа голову у камеры, вдавливает шею в плечи.
            const int fixedCalibrate = 2;
            int calibrate = new SerializedObject(legs).FindProperty("Calibrate").intValue;
            if (animator.runtimeAnimatorController == null && calibrate != fixedCalibrate)
                problems.Add($"Calibrate = {calibrate}, а у рига нет контроллера анимации — нужен FixedCalibrate ({fixedCalibrate})");

            var so = new SerializedObject(legs);
            if (so.FindProperty("Mecanim").objectReferenceValue != animator)
                problems.Add("Mecanim не свой Animator");

            string hipsName = animator.avatar.humanDescription.human.FirstOrDefault(h => h.humanName == "Hips").boneName;
            Object hips = so.FindProperty("Hips").objectReferenceValue;
            if (hips == null || hips.name != hipsName)
                problems.Add($"Hips = '{(hips != null ? hips.name : "null")}', ожидается '{hipsName}'");

            SerializedProperty legList = so.FindProperty("Legs");
            if (legList.arraySize != 2)
                problems.Add($"ног {legList.arraySize}, ожидается 2");

            for (int i = 0; i < legList.arraySize; i++)
            {
                foreach (string bone in new[] { "BoneStart", "BoneMid", "BoneEnd" })
                {
                    var t = legList.GetArrayElementAtIndex(i).FindPropertyRelative(bone).objectReferenceValue as Transform;
                    if (t == null)
                        problems.Add($"Legs[{i}].{bone} пуст");
                    else if (!t.IsChildOf(animator.transform))
                        problems.Add($"Legs[{i}].{bone} = '{t.name}' не из своего рига");
                }
            }

            // Высота лодыжки над подошвой — AnkleToHeel у каждой ноги (плагин ставит на пол пятку,
            // а не лодыжку). Нулевой — ботинки в полу, и прежде это лечили подъёмом пола в мосте,
            // а плагин поднимал под «пол» всё тело: таз +15 см, голова в плечах.
            for (int i = 0; i < legList.arraySize; i++)
            {
                Vector3 heel = legList.GetArrayElementAtIndex(i).FindPropertyRelative("AnkleToHeel").vector3Value;
                if (heel.magnitude < 0.03f)
                    problems.Add($"Legs[{i}].AnkleToHeel = {heel} — не вычислен (Leg.RefreshLegAnkleToHeelAndFeet в позе префаба)");
            }

            float floorLift = new SerializedObject(bridge).FindProperty("footHeightOffset").floatValue;
            if (Mathf.Abs(floorLift) > 0.02f)
                problems.Add($"LegsAnimatorUxrBridge.footHeightOffset = {floorLift} — плагин поднимет под «пол» всё тело; высоту лодыжки задаёт AnkleToHeel");

            SerializedProperty modules = so.FindProperty("CustomModules");
            for (int i = 0; i < modules.arraySize; i++)
            {
                if (modules.GetArrayElementAtIndex(i).FindPropertyRelative("ModuleReference").objectReferenceValue == null)
                    problems.Add($"CustomModules[{i}] без модуля");
            }

            Assert.IsEmpty(problems, $"{avatar.name}:\n{string.Join("\n", problems)}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

        private static Component FindByTypeName(GameObject go, string typeName)
        {
            return go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
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
