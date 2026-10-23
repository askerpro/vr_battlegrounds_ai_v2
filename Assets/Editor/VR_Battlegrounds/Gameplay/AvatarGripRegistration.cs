using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation;
using UltimateXR.Extensions.Unity;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.EditorTools.VersionControl;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Регистрация поз рук из паков для аватара (окно арсенала, вкладка «Взаимодействие», блок «Позы рук из паков»):
    /// по всему каталогу — точки Grip и Support оружия и точка магазина. Только из уже экспортированных поз <c>_orig</c>
    /// и snap-точек <c>Orig</c> (<see cref="PackOrigPoses" />, экспорт — шаг сборки оружия); клипы и модели паков не читаются.
    ///
    /// <para>
    /// <b>Цель.</b> Выбранный аватар или, если он из семейства кистей Cyborg (в цепочке
    /// <see cref="UxrAvatar.GetParentPrefabChain" /> есть <c>CyborgHandsBase</c>), сама база: записи хвата семейства
    /// живут только под базой (<c>CyborgGripEntriesOwnershipTests</c>). У базы нет скелета — рамка ладони и грабберы
    /// берутся у рига профиля Grip Extraction <see cref="GripTargetProfile.CyborgHands" /> (MEF_cyborg_hands_Player),
    /// оттуда же папка поз и база регистрации. Другой аватар: запись, регистрация и риг — он сам, папка поз — самая
    /// частая папка его зарегистрированных поз (или поз родителя).
    /// </para>
    ///
    /// <para>
    /// <b>Точки.</b> Оружие: Grip — точка хвата 0 (snap <c>*_Grip_orig</c>), Support — точка 1 (<c>*_Support_orig</c>);
    /// магазин: точка 0 (<c>*_Magazine_orig</c>, у дробовиков <c>*_Shell_orig</c>). Нет точки хвата у префаба —
    /// «нет точки хвата»; нет snap-точек или позы <c>_orig</c> — «нет источника».
    /// </para>
    ///
    /// <para>
    /// <b>Существующая запись</b> (<see cref="Decide" />). Если <c>GetGripPoseInfo</c> точки для выбранного аватара по
    /// цепочке отдаёт не default (как <c>GrabPoseCoverageTests</c>) или запись под GUID цели уже есть — точка не
    /// трогается, кроме двух режимов. Замена временных записей (<see cref="Options.ReplaceSdkPoses" />, по умолчанию
    /// включена): запись цели с позой SDK (<see cref="IsSdkPose" />, <c>Assets/ThirdParty/UltimateXR/</c>) получает новую позу и
    /// новые узлы, прежние align удаляются, только если на них никто больше не ссылается (записи, компоненты префаба,
    /// overrides оружия с вложенным магазином). Пересоздание из пака выбранных точек (<see cref="Options.RebuildPoints" />,
    /// только для выбранного оружия): ручная поза записи цели перезаписывается на месте данными источника (GUID и путь
    /// прежние; поза, назначенная ещё где-то, — отказ), align записи переставляются, узлы переиспользуются.
    /// </para>
    ///
    /// <para>
    /// <b>Шаблон магазина без <c>_orig</c></b> (TR15): поза — копия <see cref="MagazineTemplatePose" />, align — align
    /// записи цели у <see cref="MagazineTemplatePrefab" />, перенесённые по габариту меша магазина (<see cref="TemplatePoint" />).
    /// Корень магазина — его рамка в гнезде оружия (якорь вставки совмещает корень магазина), поэтому повороты берутся
    /// как есть, а положение — та же доля габарита меша по осям корня. Точки оружия без <c>_orig</c> — «нет источника».
    /// </para>
    ///
    /// <para>
    /// <b>Поза.</b> Копия ассета <c>_orig</c> (оба дескриптора как есть, Fixed) с именем
    /// <c>&lt;Пак&gt;_&lt;Предмет&gt;_Grip|_Support|_Mag_Grip|_Shell_Grip</c> (<see cref="GripPoseName" />) в папке поз
    /// цели; существующий ассет с таким именем не перезаписывается, а используется. Дескрипторы проверяются на
    /// вырожденные оси (правило патча 93 <c>UxrHandPoseEditorWindow.TryValidateHandDescriptor</c>, локальная копия).
    /// Регистрация в <c>_handPoses</c> базы цели — <see cref="HandsPackPoseImporter.RegisterOnPoseBase" />, без дублей.
    /// </para>
    ///
    /// <para>
    /// <b>Запись хвата.</b> <c>UxrGripPoseInfo</c> под GUID цели (поза, blend <see cref="PoseBlend" />, align L/R) и
    /// snap-узлы <c>Grabs/&lt;аватар цели&gt;/&lt;точка&gt;/Left Grab|Right Grab</c> с <see cref="UxrGrabbableObjectSnapTransform" /> —
    /// как кнопки Create Snap инспектора UXR. Align руки с snap <c>Orig</c> = snap · (рамка ладони → граббер) рига цели
    /// в этой позе: центр ладони SDK и универсальные оси кисти цели совпадают с рамкой snap (опора
    /// <see cref="GripAnchor.PalmCenter" /> Grip Extraction). Рука без snap <c>Orig</c> (у магазина — обычно правая) —
    /// зеркало align другой руки правилом патча 91 (<see cref="UxrGrabbableObjectSnapTransformEditor.MirrorSnapLocal" />,
    /// тип — подбор по паре грабберов рига), как кнопка «Отзеркалить с другой руки».
    /// </para>
    ///
    /// <para>
    /// <b>Запись в префаб</b> — по точке за раз, путь <see cref="PackOrigPoses" />: канон файла (<c>Converge</c>),
    /// <c>LoadPrefabContents</c> → <c>SaveAsPrefabAsset</c>, канон <c>_assetId</c>/флагов UXR, затем сверка YAML: новые и
    /// записанные snap-узлы, дописанные строки их родителя, документ <see cref="UxrGrabbableObject" /> — ровно прежний +
    /// блок новой записи (<see cref="AvatarGripYaml.GrabbableProblem" />); иначе файл возвращается. Оружие с вложенным
    /// магазином получает overrides <c>__prefabGuid</c> новых snap-компонентов магазина —
    /// <see cref="UxrUniqueIdPersister.Normalize" /> сразу после записи магазина.
    /// </para>
    /// </summary>
    public static class AvatarGripRegistration
    {
        public const string LogTag = "[AvatarGripRegistration]";

        /// <summary>Blend записи — как у ручного образца AK105_mag (запись CyborgHandsBase); у позы Fixed на хват не влияет.</summary>
        public const float PoseBlend = 0.33f;

        public const string GripPoint = "Grip";
        public const string SupportPoint = "Support";
        public const string MagazinePoint = "Magazine";
        public const string ShellPoint = "Shell";

        /// <summary>Позы SDK: запись цели с такой позой — временная заглушка (GrabClaw, Grab, GrabStandard, Demo*…).</summary>
        public const string SdkPoseRoot = "Assets/ThirdParty/UltimateXR/";

        /// <summary>Шаблон магазина без <c>_orig</c>: поза и магазин с ручной записью CyborgHandsBase (d7d73887e).</summary>
        public const string MagazineTemplatePose = "Assets/Prefabs/Player/HandPoses/CyborgHands/Kinemation_AK105_Mag_Grip.asset";
        public const string MagazineTemplatePrefab = "Assets/Prefabs/Weapons/AK105/AK105_mag.prefab";

        public const string StatusCreated = "создано";
        public const string StatusTemplate = "создано по шаблону AK105 (нет _orig)";
        public const string StatusReplaced = "заменено (была поза SDK)";
        public const string StatusRebuilt = "заменено ручное → из пака";
        public const string StatusSkipped = "пропущено: хват уже есть";
        public const string StatusNoSource = "нет источника";
        public const string StatusNoGrabPoint = "нет точки хвата";
        public const string StatusError = "ошибка";

        private static readonly string[] Statuses =
            { StatusCreated, StatusTemplate, StatusReplaced, StatusRebuilt, StatusSkipped, StatusNoSource, StatusNoGrabPoint, StatusError };

        private const string GrabsName = "Grabs";
        private const float AxisTolerance = 0.1f; // как HandDescriptorAxisTolerance патча 93

        private static readonly UxrHandSide[] Sides = { UxrHandSide.Left, UxrHandSide.Right };

        // ── Чистая логика ───────────────────────────────────────────────────

        /// <summary>
        /// GUID записи хвата: база семейства, если она есть в цепочке аватара (сам аватар первым, затем родители),
        /// иначе сам аватар.
        /// </summary>
        public static string RecordGuid(IReadOnlyList<string> chain, string familyBaseGuid)
        {
            if (chain == null || chain.Count == 0 || string.IsNullOrEmpty(chain[0])) throw new ArgumentException("Пустая цепочка префабов аватара.");
            return !string.IsNullOrEmpty(familyBaseGuid) && chain.Contains(familyBaseGuid) ? familyBaseGuid : chain[0];
        }

        /// <summary><c>&lt;Пак&gt;_&lt;Предмет&gt;_&lt;Точка&gt;_orig</c> → части; предмет может содержать «_».</summary>
        public static bool TryParseOrigName(string origName, out string pack, out string item, out string point)
        {
            pack = item = point = null;
            if (string.IsNullOrEmpty(origName) || !origName.EndsWith(PackOrigPoses.Suffix, StringComparison.Ordinal)) return false;
            string core = origName.Substring(0, origName.Length - PackOrigPoses.Suffix.Length);
            int first = core.IndexOf('_');
            int last = core.LastIndexOf('_');
            if (first <= 0 || last <= first + 1 || last == core.Length - 1) return false;
            pack = core.Substring(0, first);
            item = core.Substring(first + 1, last - first - 1);
            point = core.Substring(last + 1);
            return true;
        }

        /// <summary>
        /// Имя позы цели по точке <c>_orig</c>: <c>&lt;Пак&gt;_&lt;Предмет&gt;_Grip</c>, <c>…_Support</c>, у магазина
        /// <c>…_Mag_Grip</c>, у патрона дробовика <c>…_Shell_Grip</c>.
        /// </summary>
        public static string GripPoseName(string pack, string item, string point)
        {
            switch (point)
            {
                case MagazinePoint: return $"{pack}_{item}_Mag_Grip";
                case ShellPoint:    return $"{pack}_{item}_Shell_Grip";
                default:            return $"{pack}_{item}_{point}";
            }
        }

        /// <summary>Имя snap-узла руки — как у кнопок Create Left/Right Snap инспектора UXR.</summary>
        public static string SnapName(UxrHandSide side) => $"{side} Grab";

        /// <summary>Самая частая папка путей (при равенстве — первая по алфавиту); пусто — null.</summary>
        public static string MostCommonFolder(IEnumerable<string> assetPaths) =>
            (assetPaths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrEmpty(p))
                                                       .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/'))
                                                       .Where(f => !string.IsNullOrEmpty(f))
                                                       .GroupBy(f => f)
                                                       .OrderByDescending(g => g.Count())
                                                       .ThenBy(g => g.Key, StringComparer.Ordinal)
                                                       .Select(g => g.Key)
                                                       .FirstOrDefault();

        /// <summary>
        /// Вырожденные оси узлов дескриптора Fixed-позы — копия правила патча 93
        /// (<c>UxrHandPoseEditorWindow.TryValidateHandDescriptor</c>); null — дескриптор годен.
        /// </summary>
        public static string DescriptorDefect(UxrHandDescriptor descriptor)
        {
            if (descriptor == null) return "дескриптор отсутствует";
            var defects = new List<string>();
            foreach (UxrFingerType type in new[] { UxrFingerType.Thumb, UxrFingerType.Index, UxrFingerType.Middle, UxrFingerType.Ring, UxrFingerType.Little })
            {
                UxrFingerDescriptor finger = descriptor.GetFinger(type);
                if (finger.HasMetacarpalInfo) AddDefect(defects, type, "Metacarpal", finger.Metacarpal);
                AddDefect(defects, type, "Proximal", finger.Proximal);
                AddDefect(defects, type, "ProximalNoMetacarpal", finger.ProximalNoMetacarpal);
                AddDefect(defects, type, "Intermediate", finger.Intermediate);
                AddDefect(defects, type, "Distal", finger.Distal);
            }

            return defects.Count == 0 ? null : $"вырожденные оси узлов ({defects.Count}): {string.Join("; ", defects.Take(3))}{(defects.Count > 3 ? "; …" : "")}";
        }

        private static void AddDefect(List<string> defects, UxrFingerType type, string node, UxrFingerNodeDescriptor descriptor)
        {
            Vector3 right = descriptor.Right, up = descriptor.Up, forward = descriptor.Forward;
            string defect = null;
            if (Mathf.Abs(right.magnitude - 1f) > AxisTolerance || Mathf.Abs(up.magnitude - 1f) > AxisTolerance || Mathf.Abs(forward.magnitude - 1f) > AxisTolerance)
                defect = $"длины осей {right.magnitude:0.000}/{up.magnitude:0.000}/{forward.magnitude:0.000} вместо 1";
            else if (Mathf.Abs(Vector3.Dot(right, up)) > AxisTolerance || Mathf.Abs(Vector3.Dot(up, forward)) > AxisTolerance || Mathf.Abs(Vector3.Dot(right, forward)) > AxisTolerance)
                defect = "оси не ортогональны";
            else if ((Vector3.Cross(right, up) - forward).magnitude > AxisTolerance)
                defect = "тройка осей не правая";
            if (defect != null) defects.Add($"{type}.{node}: {defect}");
        }

        // ── Цель ────────────────────────────────────────────────────────────

        /// <summary>Куда пишутся позы и записи для выбранного аватара.</summary>
        public sealed class Target
        {
            public UxrAvatar Avatar;
            public string AvatarPath;
            public string RecordGuid, RecordPath, RecordName; // запись хвата и папка snap-узлов Grabs/<RecordName>
            public string RegistryPath;                       // _handPoses
            public string PoseFolder;
            public string RigPath;                            // рамка ладони и грабберы
            public bool Family;                               // семейство CyborgHandsBase

            public string Describe() =>
                (Family ? $"Семейство CyborgHandsBase: запись и регистрация — {RecordPath}; рамка ладони и грабберы — {RigPath}." :
                          $"Запись и регистрация — {RecordPath}; рамка ладони и грабберы — {RigPath}.") +
                $"\nПапка поз: {PoseFolder}. Snap-узлы: Grabs/{RecordName}/<точка>/Left Grab|Right Grab.";
        }

        /// <summary>Цель выбранного аватара (префаб-ассет). Чтение, без записи.</summary>
        public static Target ResolveTarget(UxrAvatar avatar)
        {
            if (avatar == null) throw new ArgumentException("Не выбран аватар.");
            string path = AssetDatabase.GetAssetPath(avatar);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Нужен префаб аватара из Assets, не объект сцены.");

            List<string> chain = new[] { avatar }.Concat(avatar.GetParentPrefabChain())
                                                 .Select(a => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a)))
                                                 .ToList();
            string recordGuid = RecordGuid(chain, GripTargetProfile.CyborgBaseGuid);
            if (recordGuid == GripTargetProfile.CyborgBaseGuid)
            {
                GripTargetProfile profile = GripTargetProfile.CyborgHands();
                profile.ResolveRecordGuid(); // сторож GUID базы
                return new Target
                {
                    Avatar = avatar, AvatarPath = path, RecordGuid = recordGuid, RecordPath = profile.RecordAvatarPath,
                    RecordName = PrefabName(profile.RecordAvatarPath), RegistryPath = profile.PoseRegistryPath, PoseFolder = profile.PoseFolder,
                    RigPath = profile.RigPrefabPath, Family = true
                };
            }

            string folder = new[] { avatar }.Concat(avatar.GetParentPrefabChain())
                                            .Select(a => MostCommonFolder(OwnPoses(a).Select(AssetDatabase.GetAssetPath)))
                                            .FirstOrDefault(f => f != null)
                            ?? throw new ArgumentException($"{path}: ни у аватара, ни у его родителей нет зарегистрированных поз — папка поз не определена.");
            return new Target
            {
                Avatar = avatar, AvatarPath = path, RecordGuid = recordGuid, RecordPath = path, RecordName = PrefabName(path), RegistryPath = path,
                PoseFolder = folder, RigPath = path, Family = false
            };
        }

        private static string PrefabName(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path)?.name ?? throw new FileNotFoundException($"Нет префаба {path}");

        /// <summary>Собственный список <c>_handPoses</c> аватара (без родителей).</summary>
        private static List<UxrHandPoseAsset> OwnPoses(UxrAvatar avatar)
        {
            SerializedProperty list = new SerializedObject(avatar).FindProperty("_handPoses");
            var poses = new List<UxrHandPoseAsset>();
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is UxrHandPoseAsset pose) poses.Add(pose);
            }

            return poses;
        }

        // ── Точки каталога ──────────────────────────────────────────────────

        /// <summary>Точка хвата префаба каталога: оружие (Grip/Support) или магазин.</summary>
        private sealed class Job
        {
            public string Path;
            public bool Magazine;
            public int GrabPoint;
            public string[] OrigPoints;                        // виды snap Orig, из которых берётся точка
            public readonly List<WeaponInfo> Weapons = new List<WeaponInfo>();

            public string Kind => Magazine ? "магазин" : "оружие";
            public string Names => string.Join(", ", Weapons.Select(w => w.DisplayName).Distinct());

            /// <summary>Оружие, в которое вложен магазин.</summary>
            public IEnumerable<string> Hosts => Magazine
                ? Weapons.Where(w => w.WeaponPrefab != null).Select(w => AssetDatabase.GetAssetPath(w.WeaponPrefab)).Distinct()
                : Enumerable.Empty<string>();
        }

        private static List<Job> Jobs(IEnumerable<WeaponInfo> weapons, List<string> notes)
        {
            var map = new Dictionary<string, Job>(StringComparer.Ordinal);
            void Add(WeaponInfo weapon, GameObject prefab, bool magazine, int grabPoint, string[] origPoints)
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                string key = path + "#" + grabPoint;
                if (!map.TryGetValue(key, out Job job))
                    map[key] = job = new Job { Path = path, Magazine = magazine, GrabPoint = grabPoint, OrigPoints = origPoints };
                job.Weapons.Add(weapon);
            }

            foreach (WeaponInfo weapon in weapons.Where(w => w != null))
            {
                if (weapon.WeaponPrefab == null) notes?.Add($"{weapon.DisplayName}: нет префаба оружия в WeaponInfo");
                else
                {
                    Add(weapon, weapon.WeaponPrefab, false, 0, new[] { GripPoint });
                    Add(weapon, weapon.WeaponPrefab, false, 1, new[] { SupportPoint });
                }

                if (weapon.MagazinePrefab == null) notes?.Add($"{weapon.DisplayName}: нет префаба магазина в WeaponInfo");
                else Add(weapon, weapon.MagazinePrefab, true, 0, new[] { MagazinePoint, ShellPoint });
            }

            return map.Values.OrderBy(j => j.Magazine).ThenBy(j => j.Path, StringComparer.Ordinal).ThenBy(j => j.GrabPoint).ToList();
        }

        /// <summary>Область записи: папка поз, база регистрации, префабы оружия и магазинов каталога.</summary>
        public static string[] OutputPaths(Target target, IEnumerable<WeaponInfo> weapons) =>
            new[] { target.PoseFolder, target.RegistryPath }.Concat(Jobs(weapons, null).SelectMany(j => new[] { j.Path }.Concat(j.Hosts)))
                                                             .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();

        /// <summary>
        /// Читаемые входы — только YAML-ассеты: позы и отчёты <c>_orig</c>, шаблон магазина (поза и AK105_mag), риг,
        /// выбранный аватар (без раскрытия зависимостей).
        /// </summary>
        public static string[] InputPaths(Target target) =>
            new[] { PackOrigPoses.Root, MagazineTemplatePose, MagazineTemplatePrefab, target.RigPath, target.AvatarPath }
                .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();

        // ── Запуск ──────────────────────────────────────────────────────────

        private sealed class Row
        {
            public string Kind, Point, Names, Prefab, Status, Detail, PosePath;
        }

        /// <summary>Откуда берутся align рук: snap-точки <c>Orig</c> префаба или шаблон (позы в осях корня предмета).</summary>
        private sealed class AlignSource
        {
            public string PointKind;                                         // Orig: Grip | Support | Magazine | Shell
            public List<UxrHandSide> Sides = new List<UxrHandSide>();        // Orig: руки со snap
            public Dictionary<UxrHandSide, GripLocalPose> Template;          // шаблон: обе руки
            public string Note;
        }

        /// <summary>Режимы команды регистрации.</summary>
        public sealed class Options
        {
            /// <summary>Запись цели с позой SDK (<see cref="IsSdkPose" />) — временная: заменить новой позой и узлами.</summary>
            public bool ReplaceSdkPoses = true;

            /// <summary>
            /// Пересоздать из пака записи цели выбранных точек (<see cref="RebuildPoints" />), в том числе с ручной позой проекта:
            /// поза перезаписывается на месте (GUID и путь прежние), align переставляются. Только для выбранного оружия.
            /// </summary>
            public readonly HashSet<string> RebuildPoints = new HashSet<string>();

            public bool Rebuild(string pointKey) => RebuildPoints.Contains(pointKey);
        }

        /// <summary>Что делать с точкой хвата.</summary>
        public enum EntryAction
        {
            Create,  // записи цели нет — новая
            Skip,    // хват есть (своя запись с позой проекта или по цепочке) — не трогаем
            Replace, // временная запись цели (поза SDK; пустая — только при пересоздании) — новая поза и узлы, прежние align удаляются, если свободны
            Rebuild  // запись цели с позой проекта, режим пересоздания — поза на месте из пака, align переставляются
        }

        /// <summary>
        /// Решение по существующей записи: <paramref name="hasEntry" /> — хват найден по цепочке аватара,
        /// <paramref name="ownEntry" /> — это запись GUID цели, <paramref name="posePath" /> — путь её позы (null — пустая).
        /// </summary>
        public static EntryAction Decide(bool hasEntry, bool ownEntry, string posePath, Options options, string pointKey)
        {
            if (!hasEntry) return EntryAction.Create;
            if (!ownEntry) return EntryAction.Skip;
            bool rebuild = options.Rebuild(pointKey);
            // Пустая поза — задуманная запись (общая Grab аватара со своим align, GrabPoseCoverageTests): меняется только пересозданием.
            if (string.IsNullOrEmpty(posePath)) return rebuild ? EntryAction.Replace : EntryAction.Skip;
            if (IsSdkPose(posePath)) return options.ReplaceSdkPoses || rebuild ? EntryAction.Replace : EntryAction.Skip;
            return rebuild ? EntryAction.Rebuild : EntryAction.Skip;
        }

        /// <summary>
        /// Сколько раз поза (GUID ассета) назначена записям хвата в тексте префаба или сцены: поле <c>_handPose</c> и
        /// override <c>…._handPose</c>. Регистрация в <c>_handPoses</c> аватара не считается.
        /// </summary>
        public static int CountPoseUses(string text, string poseGuid) =>
            Regex.Matches(text, @"(?:^|\n)\s*_handPose: \{fileID: -?\d+, guid: " + poseGuid).Count +
            Regex.Matches(text, @"\._handPose\r?\n\s+value: *\r?\n\s+objectReference: \{fileID: -?\d+, guid: " + poseGuid).Count;

        /// <summary>Поза общая — назначена больше чем одной записи хвата (во всех префабах и сценах); такую пересоздание не перезаписывает.</summary>
        public static bool SharedPose(IEnumerable<int> usesPerFile) => usesPerFile.Sum() > 1;

        /// <summary>Файлы префабов и сцен Assets, где поза назначена записям хвата, с числом назначений.</summary>
        private static Dictionary<string, int> PoseUses(string poseGuid, Dictionary<string, string> texts)
        {
            var uses = new Dictionary<string, int>();
            foreach (KeyValuePair<string, string> file in texts)
            {
                if (!file.Value.Contains(poseGuid)) continue;
                int count = CountPoseUses(file.Value, poseGuid);
                if (count > 0) uses[file.Key] = count;
            }

            return uses;
        }

        private static Dictionary<string, string> SceneAndPrefabTexts() =>
            Directory.EnumerateFiles("Assets", "*.prefab", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles("Assets", "*.unity", SearchOption.AllDirectories))
                     .ToDictionary(p => p.Replace('\\', '/'), File.ReadAllText);

        /// <summary>Ключ точки для режима пересоздания: Grip (рукоять), Support (цевьё), Magazine (магазин и патрон).</summary>
        private static string PointKey(Job job) => job.Magazine ? MagazinePoint : job.OrigPoints[0];

        /// <summary>Ассеты поз, которые режим пересоздания перезапишет на месте (для плана команды).</summary>
        public static string[] RebuildPosePaths(Target target, IEnumerable<WeaponInfo> weapons, Options options)
        {
            var paths = new List<string>();
            if (options.RebuildPoints.Count == 0) return paths.ToArray();
            foreach (Job job in Jobs(weapons, null).Where(j => options.Rebuild(PointKey(j))))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(job.Path);
                if (asset == null || !asset.TryGetComponent(out UxrGrabbableObject grabbable) || grabbable.GrabPointCount <= job.GrabPoint) continue;
                UxrHandPoseAsset pose = grabbable.GetGrabPoint(job.GrabPoint).GetGripPoseInfo(target.RecordGuid)?.HandPose;
                string path = pose != null ? AssetDatabase.GetAssetPath(pose) : null;
                if (!string.IsNullOrEmpty(path) && !IsSdkPose(path)) paths.Add(path);
            }

            return paths.Distinct().ToArray();
        }

        /// <summary>
        /// Команда окна арсенала: позы и записи хвата для <paramref name="avatar" /> по <paramref name="weapons" /> из готовых
        /// <c>_orig</c> (магазин без них — шаблон AK105). Ошибка точки не останавливает остальные; итог — отчёт и лог
        /// <see cref="LogTag" />.
        /// </summary>
        public static string Register(UxrAvatar avatar, IReadOnlyList<WeaponInfo> weapons, Options options = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException($"{LogTag} Запись запрещена в Play Mode.");
            options = options ?? new Options();
            Target target = ResolveTarget(avatar);
            var notes = new List<string>();
            List<Job> jobs = Jobs(weapons, notes);
            var rows = new List<Row>();
            var register = new List<UxrHandPoseAsset>();
            Dictionary<string, string> texts = options.RebuildPoints.Count > 0 ? SceneAndPrefabTexts() : null;
            using (var rig = new GripTargetRig(target.RigPath))
            {
                foreach (Job job in jobs) rows.Add(Process(job, target, rig, options, texts, register, notes));
            }

            string registration = RegisterPoses(target, register, rows);
            string report = Format(target, rows, registration, notes, options);
            if (rows.Any(r => r.Status == StatusError)) GameLog.Debug.Error(report);
            else GameLog.Debug.Info(report);
            return report;
        }

        private static Row Process(Job job, Target target, GripTargetRig rig, Options options, Dictionary<string, string> texts, List<UxrHandPoseAsset> register,
                                   List<string> notes)
        {
            var row = new Row { Kind = job.Kind, Point = job.Magazine ? MagazinePoint : job.OrigPoints[0], Names = job.Names, Prefab = job.Path };
            bool poseCreated = false;
            string poseRestorePath = null;
            byte[] poseRestore = null;
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(job.Path) ?? throw new FileNotFoundException("нет префаба");
                if (!asset.TryGetComponent(out UxrGrabbableObject grabbable)) throw new InvalidOperationException("нет UxrGrabbableObject на корне");
                if (grabbable.GrabPointCount <= job.GrabPoint)
                {
                    row.Status = StatusNoGrabPoint;
                    row.Detail = $"точек хвата {grabbable.GrabPointCount}, нужна точка {job.GrabPoint}";
                    return row;
                }

                // Уже есть хват аватара (своя запись или по цепочке): по режимам — пропуск, замена временной или пересоздание.
                UxrGripPoseInfo existing = ExistingEntry(grabbable.GetGrabPoint(job.GrabPoint), target);
                string existingPose = existing?.HandPose != null ? AssetDatabase.GetAssetPath(existing.HandPose) : null;
                EntryAction action = Decide(existing != null, existing != null && existing.AvatarPrefabGuid == target.RecordGuid, existingPose, options, PointKey(job));
                string replacedPose = action == EntryAction.Replace ? existingPose ?? "пустая" : null;
                string was = existing == null ? null : $"поза {existingPose ?? "пустая"}, blend {existing.PoseBlendValue}, align " +
                                                       $"{(existing.GripAlignTransformHandLeft != null ? GripExtractor.PathOf(existing.GripAlignTransformHandLeft, asset.transform) : "—")} | " +
                                                       $"{(existing.GripAlignTransformHandRight != null ? GripExtractor.PathOf(existing.GripAlignTransformHandRight, asset.transform) : "—")}";
                if (action == EntryAction.Skip)
                {
                    row.Status = StatusSkipped;
                    string owner = AssetDatabase.GUIDToAssetPath(existing.AvatarPrefabGuid);
                    row.Detail = $"точка {job.GrabPoint}: запись {(string.IsNullOrEmpty(owner) ? existing.AvatarPrefabGuid : Path.GetFileNameWithoutExtension(owner))}, " +
                                 $"поза {(existingPose ?? "пустая")}" + (IsSdkPose(existingPose) ? " (поза SDK; замена выключена или запись не цели)" : "");
                    return row;
                }

                // Источник: snap Orig и поза _orig; магазин без них — шаблон AK105.
                UxrHandPoseAsset source;
                string sourcePath, poseName;
                AlignSource align;
                bool template = false;
                if (OrigSource(asset.transform, job.OrigPoints, out string origName, out string pointKind, out List<UxrHandSide> sides, out string sourceProblem))
                {
                    TryParseOrigName(origName, out string pack, out string item, out _);
                    sourcePath = PackOrigPoses.PoseAssetPath(pack, origName);
                    source = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(sourcePath);
                    if (source == null) sourceProblem = $"нет позы {sourcePath} (есть snap Orig/{origName})";
                    poseName = GripPoseName(pack, item, pointKind);
                    align = new AlignSource { PointKind = pointKind, Sides = sides };
                    row.Point = pointKind;
                }
                else if (job.Magazine && TemplateSource(job, target, out poseName, out align, out string templateProblem))
                {
                    sourcePath = MagazineTemplatePose;
                    source = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(sourcePath);
                    sourceProblem = source == null ? "нет шаблона позы " + sourcePath : null;
                    template = true;
                }
                else
                {
                    source = null;
                    sourcePath = poseName = null;
                    align = null;
                    if (job.Magazine) sourceProblem += "; шаблон AK105: " + TemplateProblemText(job, target);
                }

                if (source == null)
                {
                    row.Status = StatusNoSource;
                    row.Detail = sourceProblem + (action != EntryAction.Create ? $"; прежняя запись цели оставлена ({was})" : "");
                    return row;
                }

                string defect = PoseDefect(source);
                if (defect != null) throw new InvalidOperationException($"поза источника {sourcePath}: {defect}");

                UxrHandPoseAsset pose;
                string posePath;
                if (action == EntryAction.Rebuild)
                {
                    // Пересоздание: поза записи перезаписывается на месте (GUID и путь прежние), если она только у этой записи.
                    pose = existing.HandPose;
                    posePath = existingPose;
                    string poseGuid = AssetDatabase.AssetPathToGUID(posePath);
                    Dictionary<string, int> uses = PoseUses(poseGuid, texts);
                    if (SharedPose(uses.Values))
                        throw new InvalidOperationException($"поза {posePath} используется ещё в " +
                                                            string.Join(", ", uses.Select(u => $"{u.Key} ×{u.Value}")) + " — общая поза не перезаписывается");
                    poseRestorePath = posePath;
                    poseRestore = File.ReadAllBytes(posePath);
                    string name = pose.name;
                    EditorUtility.CopySerialized(source, pose);
                    pose.name = name;
                    EditorUtility.SetDirty(pose);
                    AssetDatabase.SaveAssetIfDirty(pose);
                }
                else
                {
                    // Новая поза цели: существующая с тем же именем не перезаписывается.
                    posePath = $"{target.PoseFolder}/{poseName}.asset";
                    pose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(posePath);
                    UxrHandPoseAsset byName = target.Avatar.GetHandPose(poseName);
                    if (byName != null && byName != pose)
                        throw new InvalidOperationException($"у аватара под именем {poseName} уже зарегистрирован другой ассет {AssetDatabase.GetAssetPath(byName)}");
                    if (pose != null)
                    {
                        defect = PoseDefect(pose);
                        if (defect != null) throw new InvalidOperationException($"существующая поза {posePath}: {defect}");
                        if (pose.PoseType != UxrHandPoseType.Fixed) notes.Add($"{posePath}: существующая поза не Fixed ({pose.PoseType}) — используется как есть");
                    }
                    else
                    {
                        pose = CreatePose(source, target.PoseFolder, poseName, posePath);
                        poseCreated = true;
                    }
                }

                row.PosePath = posePath;
                string written = WriteRecord(job, target, pose, align, rig, action);
                poseRestore = null; // запись прошла — перезапись позы остаётся
                string hosts = NormalizeHosts(job, target);
                if (target.Avatar.GetHandPose(pose.name) != pose && !register.Contains(pose)) register.Add(pose);

                row.Status = action == EntryAction.Rebuild ? StatusRebuilt : action == EntryAction.Replace ? StatusReplaced : template ? StatusTemplate : StatusCreated;
                string poseNote = action == EntryAction.Rebuild ? $"поза {posePath} перезаписана из {sourcePath} (GUID прежний); было {was}"
                                : $"поза {posePath} ({(poseCreated ? "создана из " + sourcePath : "уже была")})";
                row.Detail = (replacedPose != null ? (IsSdkPose(replacedPose) ? $"была поза SDK {replacedPose}; " : "была запись без позы; ") : "") + (template ? "шаблон AK105 (нет _orig); " : "") +
                             $"{poseNote}; {written}{(hosts.Length > 0 ? "; " + hosts : "")}";
            }
            catch (Exception e)
            {
                row.Status = StatusError;
                row.Detail = e.Message + (poseCreated ? $"; поза {row.PosePath} создана, запись хвата не записана — повторный запуск использует её" : "");
                if (poseRestore != null)
                {
                    // Запись хвата не прошла — перезаписанная поза возвращается к прежним байтам.
                    File.WriteAllBytes(poseRestorePath, poseRestore);
                    AssetDatabase.ImportAsset(poseRestorePath, ImportAssetOptions.ForceUpdate);
                    row.Detail += $"; поза {poseRestorePath} возвращена";
                }
            }

            return row;
        }

        /// <summary>
        /// Запись хвата точки для цели, из-за которой точка пропускается: по цепочке выбранного аватара не default
        /// (<c>GetGripPoseInfo(avatar)</c>, как <c>GrabPoseCoverageTests</c>) или запись под GUID цели; нет — null.
        /// </summary>
        public static UxrGripPoseInfo ExistingEntry(UxrGrabPointInfo point, Target target)
        {
            UxrGripPoseInfo byChain = point.GetGripPoseInfo(target.Avatar);
            if (byChain != null && !ReferenceEquals(byChain, point.DefaultGripPoseInfo)) return byChain;
            return point.GetGripPoseInfo(target.RecordGuid);
        }

        /// <summary>Поза SDK (временная запись): ассет из <c>Assets/ThirdParty/UltimateXR/</c> — GrabClaw, Grab, GrabStandard, Demo*…</summary>
        public static bool IsSdkPose(string posePath) =>
            !string.IsNullOrEmpty(posePath) && posePath.Replace('\\', '/').StartsWith(SdkPoseRoot, StringComparison.OrdinalIgnoreCase);

        private static string PoseDefect(UxrHandPoseAsset pose)
        {
            string left = DescriptorDefect(pose.HandDescriptorLeft);
            string right = DescriptorDefect(pose.HandDescriptorRight);
            return left == null && right == null ? null : $"{(left != null ? "Left — " + left : "")}{(left != null && right != null ? "; " : "")}{(right != null ? "Right — " + right : "")}";
        }

        /// <summary>Snap-точки <c>Orig</c> точки: имя позы <c>_orig</c>, её вид и руки, у которых есть snap.</summary>
        private static bool OrigSource(Transform root, string[] kinds, out string origName, out string pointKind, out List<UxrHandSide> sides, out string problem)
        {
            origName = pointKind = problem = null;
            sides = new List<UxrHandSide>();
            foreach (string kind in kinds)
            {
                foreach (UxrHandSide side in Sides)
                {
                    Transform snap = PackOrigPoses.FindSnap(root, kind, side);
                    if (snap == null) continue;
                    if (origName != null && snap.parent.name != origName)
                    {
                        problem = $"разные позы Orig у рук: {origName} и {snap.parent.name}";
                        return false;
                    }

                    origName = snap.parent.name;
                    pointKind = kind;
                    sides.Add(side);
                }

                if (origName != null) break;
            }

            if (origName == null)
            {
                problem = $"нет snap-точек {PackOrigPoses.SnapFolder}/{string.Join("|", kinds.Select(k => $"*_{k}{PackOrigPoses.Suffix}"))} в префабе";
                return false;
            }

            if (!TryParseOrigName(origName, out _, out _, out _))
            {
                problem = $"имя snap-позы {origName} не вида <Пак>_<Предмет>_<Точка>{PackOrigPoses.Suffix}";
                return false;
            }

            return true;
        }

        // ── Шаблон магазина без _orig ───────────────────────────────────────

        /// <summary>
        /// Точка шаблона в магазине-цели: та же доля габарита меша по каждой оси корня (корень магазина — его рамка в
        /// гнезде оружия): <c>p' = c' + (p − c) ⊙ (s' ⊘ s)</c>, <c>c</c>/<c>s</c> — центр и размер габарита меша шаблона,
        /// <c>c'</c>/<c>s'</c> — цели; нулевой размер оси шаблона — отношение 1.
        /// </summary>
        public static Vector3 TemplatePoint(Vector3 point, Bounds from, Bounds to)
        {
            float Ratio(float a, float b) => Mathf.Abs(a) > 1e-6f ? b / a : 1f;
            Vector3 d = point - from.center;
            return to.center + new Vector3(d.x * Ratio(from.size.x, to.size.x), d.y * Ratio(from.size.y, to.size.y), d.z * Ratio(from.size.z, to.size.z));
        }

        /// <summary>
        /// Габарит меша магазина в осях корня: деталь <c>Mesh</c> (корпус магазина, как snap <c>Orig</c>), без неё — все
        /// меши вне <c>Grabs</c>; нет мешей — null.
        /// </summary>
        public static Bounds? MagazineBounds(Transform root)
        {
            Transform body = root.Find("Mesh");
            Transform grabs = root.Find(GrabsName);
            IEnumerable<MeshFilter> filters = body != null && body.TryGetComponent(out MeshFilter own) && own.sharedMesh != null
                ? new[] { own }
                : root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && (grabs == null || !f.transform.IsChildOf(grabs)));
            Bounds? result = null;
            foreach (MeshFilter filter in filters)
            {
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (result == null) result = new Bounds(local, Vector3.zero);
                    else
                    {
                        Bounds grown = result.Value;
                        grown.Encapsulate(local);
                        result = grown;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Шаблон магазина без <c>_orig</c>: поза — копия <see cref="MagazineTemplatePose" />, align — align записи цели
        /// магазина <see cref="MagazineTemplatePrefab" /> в осях корня, перенесённые <see cref="TemplatePoint" /> по габариту
        /// меша, повороты как у шаблона. Имя позы — пак и предмет по snap <c>Orig</c> Grip оружия этого магазина.
        /// </summary>
        private static bool TemplateSource(Job job, Target target, out string poseName, out AlignSource align, out string problem)
        {
            poseName = null;
            align = null;
            problem = TemplateProblemText(job, target);
            if (problem != null) return false;

            GameObject weapon = job.Weapons.Select(w => w.WeaponPrefab).First(w => w != null && WeaponOrigName(w) != null);
            TryParseOrigName(WeaponOrigName(weapon), out string pack, out string item, out _);
            poseName = GripPoseName(pack, item, MagazinePoint);

            GameObject templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MagazineTemplatePrefab);
            GameObject targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(job.Path);
            UxrGripPoseInfo templateEntry = templateAsset.GetComponent<UxrGrabbableObject>().GetGrabPoint(0).GetGripPoseInfo(target.RecordGuid);
            Bounds from = MagazineBounds(templateAsset.transform).Value, to = MagazineBounds(targetAsset.transform).Value;
            align = new AlignSource { Template = new Dictionary<UxrHandSide, GripLocalPose>(), PointKind = MagazinePoint };
            foreach (UxrHandSide side in Sides)
            {
                Transform node = side == UxrHandSide.Left ? templateEntry.GripAlignTransformHandLeft : templateEntry.GripAlignTransformHandRight;
                GripLocalPose local = GripLocalPose.Of(templateAsset.transform, node);
                align.Template[side] = new GripLocalPose { Position = TemplatePoint(local.Position, from, to), Rotation = local.Rotation };
            }

            align.Note = $"шаблон {Path.GetFileNameWithoutExtension(MagazineTemplatePrefab)}: габарит меша {from.size.ToString("F3")} → {to.size.ToString("F3")}";
            return true;
        }

        /// <summary>Почему шаблон магазина неприменим; null — применим.</summary>
        private static string TemplateProblemText(Job job, Target target)
        {
            GameObject templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MagazineTemplatePrefab);
            if (templateAsset == null) return "нет префаба шаблона " + MagazineTemplatePrefab;
            if (AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(MagazineTemplatePose) == null) return "нет позы шаблона " + MagazineTemplatePose;
            UxrGripPoseInfo entry = templateAsset.TryGetComponent(out UxrGrabbableObject grabbable) && grabbable.GrabPointCount > 0
                ? grabbable.GetGrabPoint(0).GetGripPoseInfo(target.RecordGuid)
                : null;
            if (entry == null || entry.GripAlignTransformHandLeft == null || entry.GripAlignTransformHandRight == null)
                return $"у {MagazineTemplatePrefab} нет записи {target.RecordName} с align обеих рук";
            if (MagazineBounds(templateAsset.transform) == null) return "у шаблона нет меша";
            GameObject targetAsset = AssetDatabase.LoadAssetAtPath<GameObject>(job.Path);
            if (targetAsset == null || MagazineBounds(targetAsset.transform) == null) return "у магазина нет меша";
            if (!job.Weapons.Any(w => w.WeaponPrefab != null && WeaponOrigName(w.WeaponPrefab) != null))
                return "у оружия магазина нет snap Orig Grip — пак и предмет для имени позы не определены";
            return null;
        }

        private static string WeaponOrigName(GameObject weapon)
        {
            Transform snap = PackOrigPoses.FindSnap(weapon.transform, GripPoint, UxrHandSide.Right) ?? PackOrigPoses.FindSnap(weapon.transform, GripPoint, UxrHandSide.Left);
            return snap != null && TryParseOrigName(snap.parent.name, out _, out _, out _) ? snap.parent.name : null;
        }

        /// <summary>Копия ассета позы (оба дескриптора, тип и версия) под новым именем в папке поз цели.</summary>
        private static UxrHandPoseAsset CreatePose(UxrHandPoseAsset source, string folder, string name, string path)
        {
            KinemationWeapon.EnsureFolder(folder);
            UxrHandPoseAsset copy = Object.Instantiate(source);
            copy.name = name;
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        // ── Запись хвата в префаб ───────────────────────────────────────────

        /// <summary>
        /// Запись <c>UxrGripPoseInfo</c> и snap-узлов одной точки хвата по <paramref name="action" />: Create — новая запись;
        /// Replace — значения временной записи цели (поза SDK или пустая) и новые узлы, прежние align удаляются, если на них
        /// больше никто не ссылается; Rebuild — поза уже перезаписана на месте, прежние align записи переставляются (узлы
        /// переиспользуются, fileID и <c>_uxrUniqueId</c> прежние). При отказе исходные байты префаба возвращаются.
        /// </summary>
        private static string WriteRecord(Job job, Target target, UxrHandPoseAsset pose, AlignSource source, GripTargetRig rig, EntryAction action)
        {
            string prefabPath = job.Path;
            int grabPointIndex = job.GrabPoint;
            bool replace = action == EntryAction.Replace, rebuild = action == EntryAction.Rebuild;
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == prefabPath)
                throw new InvalidOperationException("префаб открыт в Prefab Mode — закройте его и повторите");

            byte[] original = File.ReadAllBytes(prefabPath);
            bool touched = false;
            try
            {
                touched = true;
                // Устойчивые байты до записи (как PackOrigPoses): сверка YAML видит только изменения этой записи.
                PackOrigPoses.Converge(prefabPath);
                string baseline = File.ReadAllText(prefabPath);
                bool converged = !File.ReadAllBytes(prefabPath).SequenceEqual(original);

                // Прежние align заменяемой записи: fileID их объектов (для сверки удаления) и ссылки overrides оружия.
                var oldIds = new Dictionary<string, HashSet<long>>();
                var hostReferenced = new Dictionary<string, string>();
                if (replace) CollectOldAligns(job, target, oldIds, hostReferenced);

                var createdPaths = new List<string>();
                var removedPaths = new List<string>();
                var keptNotes = new List<string>();
                var nodePaths = new List<string>();
                string pointName, mirrorNote = null;
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var grabbable = root.GetComponent<UxrGrabbableObject>();
                    UxrGrabPointInfo point = grabbable.GetGrabPoint(grabPointIndex);
                    UxrGripPoseInfo grip = point.GetGripPoseInfo(target.RecordGuid);
                    string gripPose = grip?.HandPose != null ? AssetDatabase.GetAssetPath(grip.HandPose) : null;
                    bool stale = action == EntryAction.Create ? grip != null
                               : replace ? grip == null || !(string.IsNullOrEmpty(gripPose) || IsSdkPose(gripPose))
                               : grip == null || grip.HandPose != pose;
                    if (stale) throw new InvalidOperationException("запись цели изменилась после проверки — повторите команду");
                    Transform oldLeft = grip?.GripAlignTransformHandLeft, oldRight = grip?.GripAlignTransformHandRight;

                    // Snap Orig — до создания Grabs: контейнер Orig ищется среди MeshContainer/Grabs, Grabs и корня.
                    Dictionary<UxrHandSide, Transform> origSnaps = source.Template != null
                        ? new Dictionary<UxrHandSide, Transform>()
                        : source.Sides.ToDictionary(s => s, s => PackOrigPoses.FindSnap(root.transform, source.PointKind, s)
                                                                 ?? throw new InvalidOperationException($"нет snap Orig {source.PointKind} {s}"));

                    pointName = UxrGrabPointIndex.GetIndexDisplayName(grabbable, grabPointIndex);
                    Transform grabs = null, pointRoot = null;
                    Transform PointRoot()
                    {
                        grabs = grabs != null ? grabs : Node(root.transform, GrabsName, root.transform, createdPaths);
                        return pointRoot = pointRoot != null ? pointRoot : Node(Node(grabs, target.RecordName, root.transform, createdPaths), pointName, root.transform, createdPaths);
                    }

                    var nodes = new Dictionary<UxrHandSide, Transform>();
                    foreach (UxrHandSide side in Sides)
                    {
                        // Пересоздание переиспользует align записи; иначе (и если align нет) — узел Grabs/<цель>/<точка>/<рука> Grab.
                        Transform reuse = rebuild ? side == UxrHandSide.Left ? oldLeft : oldRight : null;
                        Transform node = reuse != null ? reuse : Node(PointRoot(), SnapName(side), root.transform, createdPaths);
                        if (Referenced(grabbable, node, action == EntryAction.Create ? null : grip))
                            throw new InvalidOperationException($"узел {GripExtractor.PathOf(node, root.transform)} уже занят другой записью хвата");
                        if (!node.TryGetComponent(out UxrGrabbableObjectSnapTransform _)) node.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();
                        nodes[side] = node;
                    }

                    if (source.Template != null)
                    {
                        // Шаблон: поза align в осях корня предмета.
                        foreach (UxrHandSide side in Sides)
                            nodes[side].SetPositionAndRotation(source.Template[side].WorldPosition(root.transform), source.Template[side].WorldRotation(root.transform));
                    }
                    else
                    {
                        // Руки с snap Orig: align = snap · (рамка ладони → граббер) рига цели в позе.
                        foreach (UxrHandSide side in source.Sides)
                        {
                            Transform snap = origSnaps[side];
                            rig.RestoreBind();
                            rig.Apply(side, side == UxrHandSide.Left ? pose.HandDescriptorLeft : pose.HandDescriptorRight);
                            Quaternion toSnap = snap.rotation * Quaternion.Inverse(rig.HandRotation(side));
                            Transform grabber = rig.Grabber(side);
                            nodes[side].SetPositionAndRotation(snap.position + toSnap * (grabber.position - rig.PalmCenter(side)), toSnap * grabber.rotation);
                        }

                        rig.RestoreBind();

                        // Рука без snap Orig — зеркало другой (правило кнопки «Отзеркалить с другой руки», патч 91): под общим
                        // родителем — в его осях, под разными — в осях корня предмета.
                        foreach (UxrHandSide side in Sides.Where(s => !source.Sides.Contains(s)))
                        {
                            UxrHandSide other = side == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left;
                            TransformExt.MirrorType? fitted = UxrGrabbableObjectSnapTransformEditor.FitMirrorTypeByGrabberPair(
                                rig.Avatar, rig.Grabber(UxrHandSide.Left).GetComponent<UxrGrabber>(), rig.Grabber(UxrHandSide.Right).GetComponent<UxrGrabber>(), out float fitError);
                            TransformExt.MirrorType type = fitted ?? TransformExt.MirrorType.MirrorXZ;
                            Transform from = nodes[other], to = nodes[side];
                            if (from.parent == to.parent)
                            {
                                (Vector3 position, Quaternion rotation) = UxrGrabbableObjectSnapTransformEditor.MirrorSnapLocal(from.localPosition, from.localRotation, type);
                                to.localPosition = position;
                                to.localRotation = rotation;
                            }
                            else
                            {
                                Transform r = root.transform;
                                (Vector3 position, Quaternion rotation) = UxrGrabbableObjectSnapTransformEditor.MirrorSnapLocal(
                                    r.InverseTransformPoint(from.position), Quaternion.Inverse(r.rotation) * from.rotation, type);
                                to.SetPositionAndRotation(r.TransformPoint(position), r.rotation * rotation);
                            }

                            mirrorNote = $"{side} — зеркало {other} ({type}{(fitted.HasValue ? $", подбор по грабберам {fitError:0.0}°" : ", пары грабберов нет")})";
                        }
                    }

                    if (action == EntryAction.Create)
                    {
                        point.CheckAddGripPoseInfo(target.RecordGuid);
                        grip = point.GetGripPoseInfo(target.RecordGuid);
                    }

                    grip.HandPose = pose;
                    grip.PoseBlendValue = PoseBlend;
                    grip.GripAlignTransformHandLeft = nodes[UxrHandSide.Left];
                    grip.GripAlignTransformHandRight = nodes[UxrHandSide.Right];
                    foreach (Transform node in nodes.Values) GripRecordWriter.BindToPrefab(node, prefabPath);
                    nodePaths.AddRange(Sides.Select(s => GripExtractor.PathOf(nodes[s], root.transform)));

                    // Прежние align временной записи: удаляются, только если на них больше никто не ссылается.
                    foreach (Transform old in replace ? new[] { oldLeft, oldRight }.Where(t => t != null).Distinct() : Enumerable.Empty<Transform>())
                    {
                        string oldPath = GripExtractor.PathOf(old, root.transform);
                        if (nodes.Values.Contains(old)) continue;
                        string reason = hostReferenced.TryGetValue(oldPath, out string host) ? "на него ссылаются overrides " + host
                                      : old.childCount > 0 ? "у узла есть дочерние объекты"
                                      : ReferencedAnywhere(root, old) ? "на него ссылаются компоненты префаба"
                                      : null;
                        if (reason != null)
                        {
                            keptNotes.Add($"прежний align {oldPath} оставлен: {reason}");
                            continue;
                        }

                        RemoveWithEmptyParents(old, root.transform, root.transform.Find(GrabsName), removedPaths);
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                    if (!saved) throw new InvalidOperationException("SaveAsPrefabAsset не сохранил префаб");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                PackOrigPoses.Canonicalize(prefabPath);
                var removedIds = new HashSet<long>(removedPaths.Where(oldIds.ContainsKey).SelectMany(p => oldIds[p]));
                VerifyYaml(prefabPath, baseline, target, grabPointIndex, nodePaths, createdPaths, removedPaths, removedIds, pose, action);

                string snaps = source.Template != null ? "обе руки по шаблону" : string.Join(", ", Sides.Select(s => s + (source.Sides.Contains(s) ? " из Orig" : " зеркало")));
                string verb = rebuild ? "пересоздана запись" : replace ? "заменена запись" : "запись";
                return $"{verb} {target.RecordName} точки {grabPointIndex} (blend {PoseBlend}), align {string.Join(" | ", nodePaths)} ({snaps})" +
                       (source.Note != null ? $"; {source.Note}" : "") + (mirrorNote != null ? $"; {mirrorNote}" : "") +
                       (createdPaths.Count > 0 ? $"; новых узлов {createdPaths.Count}" : "") +
                       (removedPaths.Count > 0 ? $"; удалены прежние узлы: {string.Join(", ", removedPaths)}" : "") +
                       (keptNotes.Count > 0 ? "; " + string.Join("; ", keptNotes) : "") +
                       (converged ? "; до записи файл приведён к устойчивому виду Unity (канон PackOrigPoses.Converge)" : "");
            }
            catch
            {
                if (touched)
                {
                    File.WriteAllBytes(prefabPath, original);
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
                }

                throw;
            }
        }

        /// <summary>
        /// fileID объектов прежних align заменяемой записи и их родителей (по пути от корня) — для сверки удаления; узлы,
        /// на которые ссылаются overrides оружия с вложенным магазином, отмечаются (их не удаляем).
        /// </summary>
        private static void CollectOldAligns(Job job, Target target, Dictionary<string, HashSet<long>> ids, Dictionary<string, string> hostReferenced)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(job.Path);
            UxrGripPoseInfo entry = asset.GetComponent<UxrGrabbableObject>().GetGrabPoint(job.GrabPoint).GetGripPoseInfo(target.RecordGuid);
            string guid = AssetDatabase.AssetPathToGUID(job.Path);
            var hostTexts = job.Hosts.Where(File.Exists).ToDictionary(h => h, File.ReadAllText);
            foreach (Transform old in new[] { entry?.GripAlignTransformHandLeft, entry?.GripAlignTransformHandRight }.Where(t => t != null))
            {
                for (Transform t = old; t != null && t != asset.transform; t = t.parent)
                {
                    string path = GripExtractor.PathOf(t, asset.transform);
                    if (ids.ContainsKey(path)) continue;
                    var set = new HashSet<long>();
                    AddFileId(set, t.gameObject);
                    foreach (Component component in t.GetComponents<Component>()) AddFileId(set, component);
                    ids[path] = set;
                    if (t != old) continue;
                    string host = hostTexts.FirstOrDefault(h => set.Any(id => h.Value.Contains($"fileID: {id}, guid: {guid}"))).Key;
                    if (host != null) hostReferenced[path] = host;
                }
            }
        }

        /// <summary>Удалить узел и опустевшие папки над ним (без компонентов кроме Transform), не выше <paramref name="stop" />.</summary>
        private static void RemoveWithEmptyParents(Transform node, Transform root, Transform stop, List<string> removedPaths)
        {
            Transform parent = node.parent;
            removedPaths.Add(GripExtractor.PathOf(node, root));
            Object.DestroyImmediate(node.gameObject);
            while (parent != null && parent != root && parent != stop && parent.childCount == 0 && parent.GetComponents<Component>().Length == 1)
            {
                Transform next = parent.parent;
                removedPaths.Add(GripExtractor.PathOf(parent, root));
                Object.DestroyImmediate(parent.gameObject);
                parent = next;
            }
        }

        /// <summary>Ссылается ли какой-либо компонент префаба (кроме самого узла) на узел, его GameObject или компоненты.</summary>
        private static bool ReferencedAnywhere(GameObject root, Transform node)
        {
            var targets = new HashSet<Object>(node.GetComponents<Component>()) { node.gameObject };
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // Transform родителя ссылается на узел через m_Children — это не ссылка на align.
                if (component == null || component is Transform || component.transform == node) continue;
                SerializedProperty property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                        targets.Contains(property.objectReferenceValue)) return true;
                }
            }

            return false;
        }

        /// <summary>Дочерний узел по имени; нет — новый пустой с локальной позой единицы (как snap-родители инспектора UXR).</summary>
        private static Transform Node(Transform parent, string name, Transform root, List<string> createdPaths)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            var go = new GameObject(name) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            createdPaths.Add(GripExtractor.PathOf(go.transform, root));
            return go.transform;
        }

        /// <param name="except">Заменяемая запись — её ссылки не считаются</param>
        private static bool Referenced(UxrGrabbableObject grabbable, Transform node, UxrGripPoseInfo except)
        {
            for (int i = 0; i < grabbable.GrabPointCount; i++)
            {
                UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                foreach (UxrGripPoseInfo entry in point.AvatarGripPoseEntries.Append(point.DefaultGripPoseInfo))
                {
                    if (entry != null && entry != except && (entry.GripAlignTransformHandLeft == node || entry.GripAlignTransformHandRight == node)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Сверка YAML с устойчивыми байтами до записи: новые или изменённые документы — только объекты snap-узлов
        /// (созданных и узлов рук); удалённые — только объекты удалённых прежних align; дописанные строки — у родителя
        /// новых узлов, вычеркнутые — у родителя удалённых, плюс PrefabInstance варианта; документ
        /// <see cref="UxrGrabbableObject" /> — точно «прежний + блок новой записи» или (замена, пересоздание) «прежний с
        /// новыми значениями блока записи цели» (<see cref="AvatarGripYaml" />). Иначе — исключение со всеми отличиями (файл возвращает вызывающий).
        /// </summary>
        private static void VerifyYaml(string prefabPath, string baseline, Target target, int grabPointIndex, List<string> nodePaths, List<string> createdPaths,
                                       List<string> removedPaths, HashSet<long> removedIds, UxrHandPoseAsset pose, EntryAction action)
        {
            string written = File.ReadAllText(prefabPath);
            if (written == baseline) throw new InvalidOperationException("запись не дошла до файла префаба");

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var free = new HashSet<long>();
            var additive = new HashSet<long>();
            var subtractive = new HashSet<long>();
            foreach (string path in createdPaths.Concat(nodePaths).Distinct())
            {
                Transform node = asset.transform.Find(path) ?? throw new InvalidOperationException($"после записи нет узла {path}");
                AddFileId(free, node.gameObject);
                foreach (Component component in node.GetComponents<Component>()) AddFileId(free, component);
            }

            string Parent(string path) => path.LastIndexOf('/') < 0 ? null : path.Substring(0, path.LastIndexOf('/'));
            foreach (string path in createdPaths.Where(p => Parent(p) == null || !createdPaths.Contains(Parent(p))))
                AddFileId(additive, Parent(path) == null ? asset.transform : asset.transform.Find(Parent(path)));
            foreach (string path in removedPaths.Where(p => Parent(p) == null || !removedPaths.Contains(Parent(p))))
                AddFileId(subtractive, Parent(path) == null ? asset.transform : asset.transform.Find(Parent(path)));

            long grabbableId = FileId(asset.GetComponent<UxrGrabbableObject>(), "UxrGrabbableObject корня");
            long leftId = FileId(asset.transform.Find(nodePaths[0]), nodePaths[0]);
            long rightId = FileId(asset.transform.Find(nodePaths[1]), nodePaths[1]);
            string poseGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pose));

            Dictionary<long, (int Class, string[] Lines)> was = PackOrigPoses.Documents(baseline, out string wasHeader);
            Dictionary<long, (int Class, string[] Lines)> now = PackOrigPoses.Documents(written, out string nowHeader);
            var problems = new List<string>();
            if (wasHeader != nowHeader) problems.Add("заголовок YAML");
            if (!was.ContainsKey(grabbableId) || !now.ContainsKey(grabbableId)) problems.Add($"нет документа UxrGrabbableObject &{grabbableId}");
            foreach (KeyValuePair<long, (int Class, string[] Lines)> doc in was)
            {
                if (!now.TryGetValue(doc.Key, out (int Class, string[] Lines) current))
                {
                    if (!removedIds.Contains(doc.Key)) problems.Add($"удалён !u!{doc.Value.Class} &{doc.Key}");
                    continue;
                }

                if (doc.Key == grabbableId)
                {
                    string grabbableProblem = action != EntryAction.Create
                        ? AvatarGripYaml.ReplaceProblem(doc.Value.Lines, current.Lines, target.RecordGuid, poseGuid, leftId, rightId, grabPointIndex)
                        : AvatarGripYaml.GrabbableProblem(doc.Value.Lines, current.Lines, target.RecordGuid, poseGuid, leftId, rightId, grabPointIndex);
                    if (grabbableProblem != null) problems.Add($"UxrGrabbableObject &{doc.Key}: {grabbableProblem}");
                    continue;
                }

                if (current.Lines.SequenceEqual(doc.Value.Lines) || free.Contains(doc.Key)) continue;
                // Родитель новых узлов: только дописанные строки m_Children (пустой «m_Children: []» раскрывается).
                string[] before = doc.Value.Lines.Select(l => l == "  m_Children: []" && current.Lines.Contains("  m_Children:") ? "  m_Children:" : l).ToArray();
                if ((additive.Contains(doc.Key) || doc.Value.Class == 1001) && PackOrigPoses.IsSubsequence(before, current.Lines)) continue;
                // Родитель удалённых узлов: только вычеркнутые строки m_Children (опустевший — «m_Children: []»).
                string[] after = current.Lines.Select(l => l == "  m_Children: []" && doc.Value.Lines.Contains("  m_Children:") ? "  m_Children:" : l).ToArray();
                if (subtractive.Contains(doc.Key) && PackOrigPoses.IsSubsequence(after, doc.Value.Lines)) continue;
                problems.Add($"изменён !u!{doc.Value.Class} &{doc.Key}: {string.Join("; ", AvatarGripYaml.Differences(doc.Value.Lines, current.Lines).Take(6))}");
            }

            foreach (KeyValuePair<long, (int Class, string[] Lines)> doc in now)
            {
                if (!was.ContainsKey(doc.Key) && !free.Contains(doc.Key) && !additive.Contains(doc.Key))
                    problems.Add($"новый !u!{doc.Value.Class} &{doc.Key} вне snap-узлов");
            }

            if (problems.Count > 0)
                throw new InvalidOperationException("в YAML не только запись хвата — файл возвращён: " + string.Join("; ", problems.Take(8)) +
                                                    (problems.Count > 8 ? $" … ещё {problems.Count - 8}" : ""));
        }

        private static void AddFileId(HashSet<long> ids, Object assetObject)
        {
            if (assetObject != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetObject, out string _, out long id)) ids.Add(id);
        }

        private static long FileId(Object assetObject, string what) =>
            assetObject != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(assetObject, out string _, out long id)
                ? id
                : throw new InvalidOperationException($"нет fileID {what} после записи");

        /// <summary>
        /// Оружие с вложенным магазином: overrides <c>__prefabGuid</c> новых snap-компонентов магазина (их пишет
        /// <see cref="UxrUniqueIdPersister.Normalize" /> — тот же шаг, что постпроцессор после импорта).
        /// </summary>
        private static string NormalizeHosts(Job job, Target target)
        {
            if (!job.Magazine) return "";
            string magazineGuid = AssetDatabase.AssetPathToGUID(job.Path);
            List<long> snapIds = SnapComponentIds(job.Path, job.GrabPoint, target);
            var notes = new List<string>();
            foreach (string host in job.Hosts)
            {
                if (!File.Exists(host) || !File.ReadAllText(host).Contains("guid: " + magazineGuid)) continue;
                string action = UxrUniqueIdPersister.Normalize(host);
                string text = File.ReadAllText(host);
                int overrides = snapIds.Count(id => Regex.IsMatch(text, $@"target: {{fileID: {id}, guid: {magazineGuid}, type: 3}}\r?\n\s+propertyPath: __prefabGuid"));
                notes.Add($"{host}: {action ?? "без изменений"}, overrides __prefabGuid snap-компонентов {overrides}/{snapIds.Count}" +
                          (overrides < snapIds.Count ? " (остальные допишет UxrUniqueIdPersister при импорте/открытии)" : ""));
            }

            return string.Join("; ", notes);
        }

        /// <summary>fileID компонентов <see cref="UxrGrabbableObjectSnapTransform" /> узлов рук записи цели в префабе.</summary>
        private static List<long> SnapComponentIds(string prefabPath, int grabPointIndex, Target target)
        {
            var ids = new HashSet<long>();
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            UxrGrabbableObject grabbable = asset != null ? asset.GetComponent<UxrGrabbableObject>() : null;
            if (grabbable == null) return ids.ToList();
            string pointPath = $"{GrabsName}/{target.RecordName}/{UxrGrabPointIndex.GetIndexDisplayName(grabbable, grabPointIndex)}";
            foreach (UxrHandSide side in Sides)
            {
                Transform node = asset.transform.Find($"{pointPath}/{SnapName(side)}");
                if (node != null && node.TryGetComponent(out UxrGrabbableObjectSnapTransform snap)) AddFileId(ids, snap);
            }

            return ids.ToList();
        }

        // ── Регистрация и отчёт ─────────────────────────────────────────────

        private static string RegisterPoses(Target target, List<UxrHandPoseAsset> poses, List<Row> rows)
        {
            if (poses.Count == 0) return "Новых регистраций нет.";
            try
            {
                HandsPackPoseImporter.RegisterOnPoseBase(target.RegistryPath, poses);
                List<string> warnings = GripPoseAssets.CheckRegistered(target.RegistryPath, poses);
                return $"Зарегистрированы в {target.RegistryPath} ({poses.Count}): {string.Join(", ", poses.Select(p => p.name))}" +
                       (warnings.Count > 0 ? "\n  " + string.Join("\n  ", warnings) : "");
            }
            catch (Exception e)
            {
                var paths = new HashSet<string>(poses.Select(AssetDatabase.GetAssetPath));
                foreach (Row row in rows.Where(r => r.Status == StatusCreated && paths.Contains(r.PosePath)))
                {
                    row.Status = StatusError;
                    row.Detail += "; поза не зарегистрирована: " + e.Message;
                }

                return $"Регистрация в {target.RegistryPath} не выполнена: {e.Message}";
            }
        }

        private static string Format(Target target, List<Row> rows, string registration, List<string> notes, Options options)
        {
            var text = new StringBuilder();
            string Counts(IEnumerable<Row> set) => string.Join(", ", Statuses.Select(s => $"{s} {set.Count(r => r.Status == s)}"));
            text.Append($"{LogTag} Позы рук из паков для {Path.GetFileNameWithoutExtension(target.AvatarPath)}: точек {rows.Count}; {Counts(rows)}\n");
            text.Append($"Режимы: замена временных записей с позами SDK — {(options.ReplaceSdkPoses ? "вкл" : "выкл")}; пересоздание из пака — " +
                        $"{(options.RebuildPoints.Count == 0 ? "выкл" : string.Join(", ", options.RebuildPoints))}\n");
            foreach (IGrouping<string, Row> kind in rows.GroupBy(r => r.Kind))
                text.Append($"  {kind.Key}: {Counts(kind)}\n");
            text.Append(target.Describe()).Append('\n');
            foreach (string status in Statuses)
            {
                List<Row> group = rows.Where(r => r.Status == status).ToList();
                if (group.Count == 0) continue;
                text.Append($"{char.ToUpper(status[0])}{status.Substring(1)} ({group.Count}):\n");
                foreach (Row row in group) text.Append($"  {row.Kind} · {row.Point} · {row.Names} — {row.Prefab}: {row.Detail}\n");
            }

            text.Append(registration);
            if (notes.Count > 0) text.Append("\nПримечания:\n  ").Append(string.Join("\n  ", notes));
            return text.ToString();
        }
    }

    // ── AvatarGripYaml: чистая сверка строк, без Unity ──────────────────────

    /// <summary>
    /// Сверка документа <c>UxrGrabbableObject</c> до и после записи хвата одной точки. Допустимо ровно одно: блок новой
    /// записи (<c>- _avatarPrefabGuid: &lt;цель&gt;</c>, поза, blend, align L/R) последним элементом списка
    /// <c>_avatarGripPoseEntries</c> нужной точки хвата — (а) пустой список <c>_avatarGripPoseEntries: []</c> становится
    /// <c>_avatarGripPoseEntries:</c> с этим блоком или (б) блок дописан в непустой список. Блок снимается, остаток должен
    /// совпасть с прежним документом строка в строку; любое другое отличие (<c>_selectedAvatarForGrips</c>,
    /// <c>__isInPrefab</c>, <c>_uxrUniqueId</c>, прежние записи…) — ошибка с перечнем строк.
    /// </summary>
    public static class AvatarGripYaml
    {
        public const string ListKey = "_avatarGripPoseEntries:";

        /// <summary>null — допустимо; иначе описание всех отличий.</summary>
        /// <param name="grabPointIndex">0 — <c>_grabPoint</c>, i ≥ 1 — элемент i−1 <c>_additionalGrabPoints</c></param>
        public static string GrabbableProblem(string[] was, string[] now, string recordGuid, string poseGuid, long leftId, long rightId, int grabPointIndex)
        {
            string itemHead = "- _avatarPrefabGuid: " + recordGuid;
            int[] heads = Enumerable.Range(0, now.Length).Where(i => now[i].Trim() == itemHead).ToArray();
            int[] wasHeads = Enumerable.Range(0, was.Length).Where(i => was[i].Trim() == itemHead).ToArray();
            if (heads.Length != wasHeads.Length + 1)
                return $"блоков записи {recordGuid}: было {wasHeads.Length}, стало {heads.Length} — ожидался ровно один новый";

            // Новый блок — единственный блок цели в нужной точке хвата; до записи там его не было.
            if (wasHeads.Any(h => GrabPointOf(was, h) == grabPointIndex)) return $"запись {recordGuid} в точке хвата {grabPointIndex} была до записи";
            int[] inPoint = heads.Where(h => GrabPointOf(now, h) == grabPointIndex).ToArray();
            if (inPoint.Length != 1) return $"блоков записи {recordGuid} в точке хвата {grabPointIndex}: {inPoint.Length} — ожидался один";
            var problems = new List<string>();
            int start = inPoint[0];

            int indent = Indent(now[start]);
            int end = BlockEnd(now, start);
            problems.AddRange(BlockProblems(now, start, end, poseGuid, leftId, rightId));

            // Блок — последний элемент списка _avatarGripPoseEntries (элементы на отступе ключа, с «- »).
            int key = ListKeyOf(now, start);
            bool itemAfter = end < now.Length && Indent(now[end]) == indent && now[end].TrimStart().StartsWith("- ", StringComparison.Ordinal);
            if (key < 0) problems.Add("блок записи не в списке " + ListKey);
            if (itemAfter) problems.Add("блок записи не последний в списке " + ListKey);

            // Остаток без блока; список, оставшийся без элементов, возвращается к «[]» (случай а).
            var rest = now.Take(start).Concat(now.Skip(end)).ToList();
            if (key >= 0 && key == start - 1 && !itemAfter) rest[key] += " []";

            problems.AddRange(Differences(was, rest.ToArray()).Select(d => "вне записи " + d));
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>
        /// Замена значений существующей записи цели (временной или при пересоздании из пака): блок записи цели в нужной точке
        /// хвата стоит на прежнем месте и несёт ожидаемые позу, blend и align; всё остальное — строка в строку как было.
        /// null — допустимо; иначе описание всех отличий.
        /// </summary>
        public static string ReplaceProblem(string[] was, string[] now, string recordGuid, string poseGuid, long leftId, long rightId, int grabPointIndex)
        {
            string itemHead = "- _avatarPrefabGuid: " + recordGuid;
            int[] wasIn = Enumerable.Range(0, was.Length).Where(i => was[i].Trim() == itemHead && GrabPointOf(was, i) == grabPointIndex).ToArray();
            int[] nowIn = Enumerable.Range(0, now.Length).Where(i => now[i].Trim() == itemHead && GrabPointOf(now, i) == grabPointIndex).ToArray();
            if (wasIn.Length != 1 || nowIn.Length != 1)
                return $"блоков записи {recordGuid} в точке хвата {grabPointIndex}: было {wasIn.Length}, стало {nowIn.Length} — ожидался один заменяемый";
            int ws = wasIn[0], ns = nowIn[0];
            int we = BlockEnd(was, ws), ne = BlockEnd(now, ns);
            var problems = new List<string>();
            if (ws != ns) problems.Add($"блок записи сдвинулся: строка {ws} → {ns}");
            problems.AddRange(BlockProblems(now, ns, ne, poseGuid, leftId, rightId));
            string[] wasRest = was.Take(ws).Concat(was.Skip(we)).ToArray();
            string[] nowRest = now.Take(ns).Concat(now.Skip(ne)).ToArray();
            problems.AddRange(Differences(wasRest, nowRest).Select(d => "вне записи " + d));
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>Конец блока элемента списка: строки глубже отступа его «- ».</summary>
        private static int BlockEnd(string[] lines, int start)
        {
            int indent = Indent(lines[start]);
            int end = start + 1;
            while (end < lines.Length && Indent(lines[end]) > indent) end++;
            return end;
        }

        /// <summary>Блок записи — ровно поза, blend (число) и align L/R с ожидаемыми значениями.</summary>
        private static List<string> BlockProblems(string[] lines, int start, int end, string poseGuid, long leftId, long rightId)
        {
            var problems = new List<string>();
            string pad = new string(' ', Indent(lines[start]) + 2);
            string[] block = lines.Skip(start).Take(end - start).ToArray();
            string[] expected =
            {
                lines[start],
                pad + "_handPose: {fileID: 11400000, guid: " + poseGuid + ", type: 2}",
                null, // _poseBlendValue — число
                pad + "_gripAlignTransformHandLeft: {fileID: " + leftId + "}",
                pad + "_gripAlignTransformHandRight: {fileID: " + rightId + "}"
            };
            if (block.Length != expected.Length)
            {
                problems.Add($"блок записи — {block.Length} строк вместо {expected.Length}: «{string.Join(" | ", block.Select(l => l.Trim()))}»");
                return problems;
            }

            for (int i = 1; i < expected.Length; i++)
            {
                bool same = expected[i] != null ? block[i] == expected[i] : block[i].StartsWith(pad + "_poseBlendValue: ", StringComparison.Ordinal);
                if (!same) problems.Add($"в блоке записи «{block[i].Trim()}» вместо «{(expected[i] ?? "_poseBlendValue: <число>").Trim()}»");
            }

            return problems;
        }

        /// <summary>Строка ключа <c>_avatarGripPoseEntries:</c>, в чьём списке стоит элемент <paramref name="item" />; нет — −1.</summary>
        private static int ListKeyOf(string[] lines, int item)
        {
            int indent = Indent(lines[item]);
            int key = item - 1;
            while (key >= 0 && (Indent(lines[key]) > indent || Indent(lines[key]) == indent && lines[key].TrimStart().StartsWith("- ", StringComparison.Ordinal))) key--;
            return key >= 0 && Indent(lines[key]) == indent && lines[key].TrimStart() == ListKey ? key : -1;
        }

        /// <summary>Точка хвата строки: 0 — внутри <c>_grabPoint</c>, i ≥ 1 — внутри i-го элемента <c>_additionalGrabPoints</c>; иначе −1.</summary>
        public static int GrabPointOf(string[] lines, int index)
        {
            for (int i = index; i >= 0; i--)
            {
                string trimmed = lines[i].TrimStart();
                if (Indent(lines[i]) == 2 && trimmed == "_grabPoint:") return 0;
                if (Indent(lines[i]) == 2 && trimmed.StartsWith("_additionalGrabPoints:", StringComparison.Ordinal))
                {
                    int item = 0;
                    for (int j = i + 1; j <= index; j++)
                    {
                        if (Indent(lines[j]) == 2 && lines[j].TrimStart().StartsWith("- ", StringComparison.Ordinal)) item++;
                    }

                    return item;
                }

                if (Indent(lines[i]) == 2 && !trimmed.StartsWith("- ", StringComparison.Ordinal) && i != index) return -1;
            }

            return -1;
        }

        private static int Indent(string line) => line.Length - line.TrimStart().Length;

        /// <summary>Отличия двух списков строк по наибольшей общей подпоследовательности: «− было» / «+ стало».</summary>
        public static List<string> Differences(string[] a, string[] b)
        {
            var lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
            {
                for (int j = b.Length - 1; j >= 0; j--) lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }

            var result = new List<string>();
            int x = 0, y = 0;
            while (x < a.Length || y < b.Length)
            {
                if (x < a.Length && y < b.Length && a[x] == b[y])
                {
                    x++;
                    y++;
                }
                else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
                {
                    result.Add($"+ «{b[y++].Trim()}»");
                }
                else
                {
                    result.Add($"− «{a[x++].Trim()}»");
                }
            }

            return result;
        }
    }
}
