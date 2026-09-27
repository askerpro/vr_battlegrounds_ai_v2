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

        // ══════════════════════════════════════════════════════════════════
        //  Вспомогательное
        // ══════════════════════════════════════════════════════════════════

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
