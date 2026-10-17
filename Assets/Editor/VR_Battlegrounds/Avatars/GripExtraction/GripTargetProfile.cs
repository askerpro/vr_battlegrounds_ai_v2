using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Avatars.Workbench;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Опора посадки предмета в кисть цели.</summary>
    public enum GripAnchor
    {
        /// <summary>Прежний конвейер: предмет относительно запястья, подобие вокруг запястья (HandsPackGripAligner).</summary>
        Wrist,

        /// <summary>Центр ладони (SDK GetPalmCenter) и универсальные оси кисти источника совпадают с теми же у цели.</summary>
        PalmCenter
    }

    /// <summary>Как поза пальцев попадает в ассет цели.</summary>
    public enum GripPoseEncoding
    {
        /// <summary>Дескриптор источника как есть (вторая рука — Mirrored): прежний импортёр, профиль MEF.</summary>
        AsSource,

        /// <summary>
        /// Мировой путь на риге цели (<c>UxrAvatarRig.UpdateHandUsingDescriptor</c>) из исходной позы, затем
        /// <c>new UxrHandDescriptor(avatar, side)</c> — поза получает пястные, если они есть у цели
        /// (reports/cyborg-grips/pose-transfer-diagnosis.md).
        /// </summary>
        TargetRigWorld
    }

    /// <summary>
    /// Профиль кисти цели: на каком риге сажать и кодировать позу, по какому GUID писать запись хвата, куда класть позы
    /// и как называть align. Встроенные профили — <see cref="Mef" />, <see cref="MefCyborgHands" /> и <see cref="CyborgHands" />.
    /// </summary>
    public sealed class GripTargetProfile
    {
        public const string MefName            = "MEF";
        public const string MefCyborgHandsName = "MEF_cyborg_hands";
        public const string CyborgHandsName    = "CyborgHands";

        public const string SdkHandsBaseGuid            = "a58abb6b878ca5b40b79c14fae6f5645";
        public const string CyborgBaseGuid              = "71b74b0d08444f49bbb7aa2828d0890a";
        public const string CyborgAvatarGuid            = "8e0e06323e4754f479a9f19788554652";
        public const string MefCyborgPlayerGuid         = "2aa0ab6714e2413f9d829637b62df0c0";
        public const string SdkHandsBasePath            = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        public const string CanonicalCyborgHandsBasePath = "Assets/Prefabs/Player/CyborgHandsBase.prefab";
        public const string LegacyMefCyborgBasePath     = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Base.prefab";
        public const string CyborgAvatarPath            = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        public const string MefCyborgPlayerPath         = "Assets/Prefabs/Player/MEF_cyborg_hands_Player.prefab";
        public const string MefCyborgPlayerBlackPath    = "Assets/Prefabs/Player/MEF_cyborg_hands_Player_Black.prefab";
        public const string MefCyborgModelPath          = "Assets/Prefabs/Player/MEF_cyborg_hands/Model/MEF_CyborgHands.fbx";

        private const string CyborgFolder = "Assets/Prefabs/Player/MEF_cyborg_hands";

        public string Name;

        /// <summary>Префаб аватара, на котором считаются посадка (запястье, центр ладони, граббер) и кодирование позы.</summary>
        public string RigPrefabPath;

        /// <summary>Префаб, чей GUID — ключ записи хвата (<c>UxrGripPoseInfo.AvatarPrefabGuid</c>).</summary>
        public string RecordAvatarPath;

        /// <summary>Ожидаемый GUID записи (сторож), null — без проверки.</summary>
        public string ExpectedRecordGuid;

        /// <summary>База поз (<c>_handPoses</c>), где SDK ищет позу по имени.</summary>
        public string PoseRegistryPath;

        /// <summary>Папка поз; null — папка рецепта (как прежний импортёр).</summary>
        public string PoseFolder;

        /// <summary>Имя папки аватара в align: <c>Grabs/&lt;источник&gt;/&lt;поза&gt;/&lt;AlignAvatarName&gt;/Left|Right</c>.</summary>
        public string AlignAvatarName;

        public GripAnchor       Anchor;
        public GripPoseEncoding Encoding;

        /// <summary>Apply дописывает новые позы в <see cref="PoseRegistryPath" /> (иначе только проверка и предупреждения).</summary>
        public bool RegisterPoses;

        /// <summary>Apply удаляет прежние align записи, если на них больше никто не ссылается (как прежний импортёр).</summary>
        public bool RemoveReplacedAligns;

        /// <summary>Пальцы 2–5 рига должны иметь пястные (иначе — ошибка профиля).</summary>
        public bool RequireMetacarpals;

        public string PoseFolderFor(GripSourcePoint point) => PoseFolder ?? point.LegacyPoseFolder;

        public string PosePathFor(GripSourcePoint point) => $"{PoseFolderFor(point)}/{point.Pose}.asset";

        /// <summary>Находит актуальный путь к базе CyborgHandsBase по её неизменному GUID.</summary>
        public static string ResolveCyborgBasePath()
        {
            string byGuid = AssetDatabase.GUIDToAssetPath(CyborgBaseGuid);
            if (!string.IsNullOrEmpty(byGuid)) return byGuid;
            if (File.Exists(CanonicalCyborgHandsBasePath)) return CanonicalCyborgHandsBasePath;
            return LegacyMefCyborgBasePath;
        }

        /// <summary>Прежний импортёр паков: MEF_Base_Avatar, база поз PlayerBase_NonSdkHands, опора — запястье.</summary>
        public static GripTargetProfile Mef()
        {
            string avatar = HandsPackPoseImporter.GripAvatars[0];
            return new GripTargetProfile
            {
                Name                 = MefName,
                RigPrefabPath        = avatar,
                RecordAvatarPath     = avatar,
                PoseRegistryPath     = HandsPackPoseImporter.PoseBase,
                PoseFolder           = null,
                AlignAvatarName      = Path.GetFileNameWithoutExtension(avatar),
                Anchor               = GripAnchor.Wrist,
                Encoding             = GripPoseEncoding.AsSource,
                RegisterPoses        = true,
                RemoveReplacedAligns = true,
                RequireMetacarpals   = false
            };
        }

        /// <summary>
        /// Кисть Cyborg: геометрия и грабберы — с пястными, записи — по GUID CyborgHandsBase, позы — папка HandPoses,
        /// опора — центр ладони.
        /// </summary>
        public static GripTargetProfile MefCyborgHands()
        {
            string basePath = ResolveCyborgBasePath();
            return new GripTargetProfile
            {
                Name                 = MefCyborgHandsName,
                RigPrefabPath        = MefCyborgPlayerPath,
                RecordAvatarPath     = basePath,
                ExpectedRecordGuid   = CyborgBaseGuid,
                PoseRegistryPath     = basePath,
                PoseFolder           = $"{CyborgFolder}/HandPoses",
                AlignAvatarName      = MefCyborgHandsName,
                Anchor               = GripAnchor.PalmCenter,
                Encoding             = GripPoseEncoding.TargetRigWorld,
                RegisterPoses        = false,
                RemoveReplacedAligns = false,
                RequireMetacarpals   = true
            };
        }

        /// <summary>
        /// Единый профиль общей базы CyborgHandsBase для всех аватаров с пересаженными руками киборга
        /// (MEF_cyborg_hands_Player, MEF_cyborg_hands_Player_Black и др.).
        /// </summary>
        public static GripTargetProfile CyborgHands()
        {
            string basePath = ResolveCyborgBasePath();
            return new GripTargetProfile
            {
                Name                 = CyborgHandsName,
                RigPrefabPath        = MefCyborgPlayerPath,
                RecordAvatarPath     = basePath,
                ExpectedRecordGuid   = CyborgBaseGuid,
                PoseRegistryPath     = basePath,
                PoseFolder           = $"{CyborgFolder}/HandPoses",
                AlignAvatarName      = MefCyborgHandsName,
                Anchor               = GripAnchor.PalmCenter,
                Encoding             = GripPoseEncoding.TargetRigWorld,
                RegisterPoses        = true,
                RemoveReplacedAligns = false,
                RequireMetacarpals   = true
            };
        }

        public static GripTargetProfile Get(string name)
        {
            if (string.Equals(name, MefName, StringComparison.OrdinalIgnoreCase)) return Mef();
            if (string.Equals(name, MefCyborgHandsName, StringComparison.OrdinalIgnoreCase)) return MefCyborgHands();
            if (string.Equals(name, CyborgHandsName, StringComparison.OrdinalIgnoreCase)) return CyborgHands();
            throw new ArgumentException($"target: неизвестный профиль «{name}» (есть {MefName}, {MefCyborgHandsName}, {CyborgHandsName}).");
        }

        /// <summary>Копия с переопределениями запроса (null — как в профиле).</summary>
        public GripTargetProfile With(GripAnchor? anchor, GripPoseEncoding? encoding, bool? registerPoses, bool? removeReplacedAligns)
        {
            var copy = (GripTargetProfile)MemberwiseClone();
            copy.Anchor               = anchor ?? Anchor;
            copy.Encoding             = encoding ?? Encoding;
            copy.RegisterPoses        = registerPoses ?? RegisterPoses;
            copy.RemoveReplacedAligns = removeReplacedAligns ?? RemoveReplacedAligns;
            return copy;
        }

        /// <summary>GUID записи хвата; сверяется с <see cref="ExpectedRecordGuid" />.</summary>
        public string ResolveRecordGuid()
        {
            string guid = AssetDatabase.AssetPathToGUID(RecordAvatarPath);
            if (string.IsNullOrEmpty(guid))
            {
                throw new ArgumentException($"{Name}: нет префаба записи {RecordAvatarPath}");
            }

            if (ExpectedRecordGuid != null && guid != ExpectedRecordGuid)
            {
                throw new ArgumentException($"{Name}: GUID {RecordAvatarPath} = {guid}, ожидался {ExpectedRecordGuid}");
            }

            return guid;
        }
    }

    /// <summary>
    /// Перестройка и проверка иерархии префабов с кистями киборга и вывод MEF_cyborg_hands из Experimental в основные аватары.
    /// </summary>
    public static class CyborgHandsPrefabTree
    {
        private const string UnusedFolder = "Assets/Prefabs/Player/Unused";
        private const string ManagersPrefabPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";

        [MenuItem("VR Battlegrounds/Avatars/Черновики/Cyborg Hands/Validate CyborgHandsBase Prefab Tree")]
        public static void ValidateFromMenu()
        {
            string summary = ValidateTree();
            GameLog.Player.Info(summary);
        }

        [MenuItem("VR Battlegrounds/Avatars/Черновики/Cyborg Hands/Rebuild CyborgHandsBase Prefab Tree")]
        public static void RebuildFromMenu()
        {
            string summary = RebuildTree();
            GameLog.Player.Info(summary);
        }

        /// <summary>
        /// Выполняет миграцию дерева префабов CyborgHandsBase, вывод MEF_cyborg_hands в основные аватары,
        /// сборку чёрного варианта и перенос старых MEF в папку Unused с сохранением GUID.
        /// </summary>
        public static string RebuildTree()
        {
            PromoteExperimentalAndArchiveUnused();
            string basePath = EnsureCanonicalBasePath();
            ReplaceNativeMefGlovesWithCyborgHandsInBase(basePath);
            ConfigureBaseRenderersAndParent(basePath);
            EnsureMefCyborgPlayerVariant(GripTargetProfile.MefCyborgPlayerPath, basePath);
            ConfigureMainAvatarsRegistryTeamsAndCorpses();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ValidateTree();
        }

        /// <summary>
        /// Проверяет цепочку наследования префабов, наличие пястных костей, отсутствие родных перчаток MEF в базе и число поз.
        /// </summary>
        public static string ValidateTree()
        {
            var sb = new StringBuilder();
            string basePath = GripTargetProfile.ResolveCyborgBasePath();
            sb.AppendLine($"[CyborgHandsPrefabTree] Base path: {basePath} (GUID={AssetDatabase.AssetPathToGUID(basePath)})");

            AppendAvatarLine(sb, "CyborgHandsBase", basePath);
            AppendAvatarLine(sb, "PlayerControllersCyborgAvatar", GripTargetProfile.CyborgAvatarPath);
            AppendAvatarLine(sb, "MEF_cyborg_hands_Player", GripTargetProfile.MefCyborgPlayerPath);
            AppendAvatarLine(sb, "MEF_cyborg_hands_Player_Black", GripTargetProfile.MefCyborgPlayerBlackPath);
            return sb.ToString();
        }

        private static void PromoteExperimentalAndArchiveUnused()
        {
            if (!AssetDatabase.IsValidFolder(UnusedFolder))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs/Player", "Unused");
            }

            MoveAssetIfPresent(
                "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Player.prefab",
                GripTargetProfile.MefCyborgPlayerPath);
            MoveAssetIfPresent(
                "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands",
                "Assets/Prefabs/Player/MEF_cyborg_hands");

            MoveAssetIfPresent("Assets/Prefabs/Player/Optimized_MEF_Player.prefab", $"{UnusedFolder}/Optimized_MEF_Player.prefab");
            MoveAssetIfPresent("Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab", $"{UnusedFolder}/Optimized_MEF_Player_Black.prefab");
            MoveAssetIfPresent("Assets/Prefabs/Player/MEF_Base_Avatar.prefab", $"{UnusedFolder}/MEF_Base_Avatar.prefab");
            MoveAssetIfPresent("Assets/Prefabs/Player/Experimental/MEF_sdk_hands", $"{UnusedFolder}/MEF_sdk_hands");
            MoveAssetIfPresent("Assets/Prefabs/Player/Experimental/Optimized_MEF_Player_VRIK.prefab", $"{UnusedFolder}/Optimized_MEF_Player_VRIK.prefab");
            MoveAssetIfPresent("Assets/Data/Player/Avatars/MEF_cyborg_hands.asset", $"{UnusedFolder}/MEF_cyborg_hands.asset");
            MoveAssetIfPresent("Assets/Data/Player/Avatars/MEF_sdk_hands.asset", $"{UnusedFolder}/MEF_sdk_hands.asset");

            if (AssetDatabase.IsValidFolder("Assets/Prefabs/Player/Experimental"))
            {
                string[] remaining = AssetDatabase.FindAssets("", new[] { "Assets/Prefabs/Player/Experimental" });
                if (remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset("Assets/Prefabs/Player/Experimental");
                }
            }
        }

        private static void MoveAssetIfPresent(string sourcePath, string destPath)
        {
            if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase)) return;
            bool sourceExists = File.Exists(sourcePath) || Directory.Exists(sourcePath) || AssetDatabase.IsValidFolder(sourcePath);
            bool destExists = File.Exists(destPath) || Directory.Exists(destPath) || AssetDatabase.IsValidFolder(destPath);
            if (!sourceExists || destExists) return;

            string error = AssetDatabase.MoveAsset(sourcePath, destPath);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"Не удалось переместить {sourcePath} -> {destPath}: {error}");
            }
        }

        private static void AppendAvatarLine(StringBuilder sb, string label, string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                sb.AppendLine($"  - {label}: НЕ НАЙДЕН ({path})");
                return;
            }

            var avatar = go.GetComponent<UxrAvatar>();
            if (avatar == null)
            {
                sb.AppendLine($"  - {label}: нет UxrAvatar ({path})");
                return;
            }

            string parentName = avatar.ParentAvatarPrefab != null ? avatar.ParentAvatarPrefab.name : "null";
            string parentGuid = avatar.ParentAvatarPrefab != null
                ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(avatar.ParentAvatarPrefab.gameObject))
                : "null";
            bool hasLeftMetacarpal = avatar.LeftHand != null && avatar.LeftHand.Index.Metacarpal != null;
            bool hasRightMetacarpal = avatar.RightHand != null && avatar.RightHand.Index.Metacarpal != null;
            bool hasNativeMefGloves = false;
            int activeBodyRenderers = 0;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                if (smr.sharedMesh.name.IndexOf("Hands_gloves", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasNativeMefGloves = true;
                }

                bool isHandRenderer =
                    smr.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    smr.name.IndexOf("Glove", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isHandRenderer && smr.enabled && IsActiveWithinPrefab(smr.transform, go.transform))
                {
                    activeBodyRenderers++;
                }
            }

            int poseCount = 0;
            foreach (var _ in avatar.GetAllHandPoses()) poseCount++;

            sb.AppendLine(
                $"  - {label}: GUID={avatar.PrefabGuid}, Parent={parentName} ({parentGuid}), " +
                $"Metacarpals=L:{hasLeftMetacarpal}/R:{hasRightMetacarpal}, NativeMefGloves={hasNativeMefGloves}, ActiveBodyMeshes={activeBodyRenderers}, Poses={poseCount}");
        }

        private static bool IsActiveWithinPrefab(Transform current, Transform root)
        {
            while (current != null)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) break;
                current = current.parent;
            }

            return true;
        }

        private static string EnsureCanonicalBasePath()
        {
            string currentPath = GripTargetProfile.ResolveCyborgBasePath();
            if (string.Equals(currentPath, GripTargetProfile.CanonicalCyborgHandsBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return currentPath;
            }

            if (File.Exists(currentPath) && !File.Exists(GripTargetProfile.CanonicalCyborgHandsBasePath))
            {
                string moveError = AssetDatabase.MoveAsset(currentPath, GripTargetProfile.CanonicalCyborgHandsBasePath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    throw new InvalidOperationException($"Не удалось переместить {currentPath} -> {GripTargetProfile.CanonicalCyborgHandsBasePath}: {moveError}");
                }

                return GripTargetProfile.CanonicalCyborgHandsBasePath;
            }

            return currentPath;
        }

        private static void ReplaceNativeMefGlovesWithCyborgHandsInBase(string basePath)
        {
            var playerRoot = PrefabUtility.LoadPrefabContents(GripTargetProfile.MefCyborgPlayerPath);
            try
            {
                var removedGameObjects = PrefabUtility.GetRemovedGameObjects(playerRoot);
                if (removedGameObjects != null && removedGameObjects.Count > 0)
                {
                    foreach (var removed in removedGameObjects)
                    {
                        removed.Apply(basePath, InteractionMode.AutomatedAction);
                    }
                }

                var addedGameObjects = PrefabUtility.GetAddedGameObjects(playerRoot);
                if (addedGameObjects != null && addedGameObjects.Count > 0)
                {
                    foreach (var added in addedGameObjects)
                    {
                        if (added?.instanceGameObject == null) continue;
                        string goName = added.instanceGameObject.name;
                        string parentName = added.instanceGameObject.transform.parent != null
                            ? added.instanceGameObject.transform.parent.name
                            : string.Empty;
                        bool isHandOrGlove =
                            parentName.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Glove", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Thumb", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Index", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Little", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            goName.IndexOf("Metacarpal", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isHandOrGlove)
                        {
                            added.Apply(basePath, InteractionMode.AutomatedAction);
                        }
                    }
                }

                var avatar = playerRoot.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    var so = new SerializedObject(avatar);
                    foreach (string propPath in new[]
                    {
                        "_rig._leftArm._hand",
                        "_rig._rightArm._hand",
                        "_prefabGrabberRenderers"
                    })
                    {
                        var prop = so.FindProperty(propPath);
                        if (prop != null)
                        {
                            PrefabUtility.ApplyPropertyOverride(prop, basePath, InteractionMode.AutomatedAction);
                        }
                    }
                }

                foreach (var grabber in playerRoot.GetComponentsInChildren<UxrGrabber>(true))
                {
                    if (grabber == null) continue;
                    var grabberSo = new SerializedObject(grabber);
                    var rendererProp = grabberSo.FindProperty("_handRenderer");
                    if (rendererProp != null)
                    {
                        PrefabUtility.ApplyPropertyOverride(rendererProp, basePath, InteractionMode.AutomatedAction);
                    }
                }

                AvatarMaintenanceTools.SavePrefab(playerRoot, GripTargetProfile.MefCyborgPlayerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
        }

        /// <summary>
        /// Сохраняет наследование <c>CyborgHandsBase -&gt; PlayerBase_SdkHands</c> и скрывает меши тела в базовом префабе,
        /// оставляя видимыми в самой базе только кисти киборга на гуманоидном риге.
        /// </summary>
        private static void ConfigureBaseRenderersAndParent(string basePath)
        {
            var sdkHandsBaseGo = AssetDatabase.LoadAssetAtPath<GameObject>(GripTargetProfile.SdkHandsBasePath);
            var baseRoot = PrefabUtility.LoadPrefabContents(basePath);
            try
            {
                baseRoot.name = Path.GetFileNameWithoutExtension(basePath);
                var avatar = baseRoot.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    var so = new SerializedObject(avatar);
                    var parentProp = so.FindProperty("_parentPrefab");
                    if (parentProp != null && sdkHandsBaseGo != null)
                    {
                        parentProp.objectReferenceValue = sdkHandsBaseGo;
                    }

                    var guidProp = so.FindProperty("__prefabGuid") ?? so.FindProperty("_prefabGuid");
                    if (guidProp != null)
                    {
                        guidProp.stringValue = GripTargetProfile.CyborgBaseGuid;
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                foreach (var smr in baseRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr == null) continue;
                    bool isHandRenderer =
                        smr.name.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        smr.name.IndexOf("Glove", StringComparison.OrdinalIgnoreCase) >= 0;
                    smr.enabled = isHandRenderer;
                }

                AvatarMaintenanceTools.SavePrefab(baseRoot, basePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(baseRoot);
            }

            var savedBaseGo = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            var savedBaseAvatar = savedBaseGo != null ? savedBaseGo.GetComponent<UxrAvatar>() : null;
            if (savedBaseAvatar != null && sdkHandsBaseGo != null)
            {
                var savedSo = new SerializedObject(savedBaseAvatar);
                var parentProp = savedSo.FindProperty("_parentPrefab");
                if (parentProp != null) parentProp.objectReferenceValue = sdkHandsBaseGo;
                var guidProp = savedSo.FindProperty("__prefabGuid") ?? savedSo.FindProperty("_prefabGuid");
                if (guidProp != null) guidProp.stringValue = GripTargetProfile.CyborgBaseGuid;
                savedSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(savedBaseGo);
            }
        }

        private static void EnsureMefCyborgPlayerVariant(string childPath, string basePath)
        {
            var baseGo = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (baseGo == null) return;

            string childGuid = AssetDatabase.AssetPathToGUID(childPath);
            var root = PrefabUtility.LoadPrefabContents(childPath);
            try
            {
                var mefRig = root.transform.Find("MEF_Rig");
                if (mefRig != null)
                {
                    mefRig.gameObject.SetActive(true);
                }

                var body0 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Body_LOD0");
                var body1 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Body_LOD1");
                var body2 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Body_LOD2");
                var head0 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Head_LOD0");
                var head1 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Head_LOD1");
                var head2 = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_Optimized_Head_LOD2");
                var gloveL = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_CyborgHands_Glove_L");
                var gloveR = FindByName<SkinnedMeshRenderer>(root.transform, "MEF_CyborgHands_Glove_R");

                var allSkins = new[] { body0, body1, body2, head0, head1, head2, gloveL, gloveR }
                    .Where(r => r != null)
                    .ToArray();
                foreach (var smr in allSkins)
                {
                    smr.gameObject.SetActive(true);
                    smr.enabled = true;
                }

                var lodGroup = root.GetComponentInChildren<LODGroup>(true);
                if (lodGroup != null && body0 != null && head0 != null && gloveL != null && gloveR != null)
                {
                    LOD[] lods = lodGroup.GetLODs();
                    if (lods.Length >= 3)
                    {
                        lods[0].renderers = new Renderer[] { body0, head0, gloveL, gloveR };
                        lods[1].renderers = new Renderer[] { body1 ?? body0, head1 ?? head0, gloveL, gloveR };
                        lods[2].renderers = new Renderer[] { body2 ?? body1 ?? body0, head2 ?? head1 ?? head0, gloveL, gloveR };
                        lodGroup.SetLODs(lods);
                    }
                }

                var avatar = root.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    var so = new SerializedObject(avatar);
                    var parentProp = so.FindProperty("_parentPrefab");
                    if (parentProp != null) parentProp.objectReferenceValue = baseGo;
                    var guidProp = so.FindProperty("__prefabGuid") ?? so.FindProperty("_prefabGuid");
                    if (guidProp != null && !string.IsNullOrEmpty(childGuid)) guidProp.stringValue = childGuid;

                    var renderersProp = so.FindProperty("_avatarRenderers");
                    if (renderersProp != null && allSkins.Length > 0)
                    {
                        renderersProp.arraySize = allSkins.Length;
                        for (int i = 0; i < allSkins.Length; i++)
                        {
                            renderersProp.GetArrayElementAtIndex(i).objectReferenceValue = allSkins[i];
                        }
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                AvatarMaintenanceTools.SavePrefab(root, childPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var savedChildGo = AssetDatabase.LoadAssetAtPath<GameObject>(childPath);
            var savedAvatar = savedChildGo != null ? savedChildGo.GetComponent<UxrAvatar>() : null;
            if (savedAvatar != null)
            {
                var savedSo = new SerializedObject(savedAvatar);
                var parentProp = savedSo.FindProperty("_parentPrefab");
                if (parentProp != null) parentProp.objectReferenceValue = baseGo;
                var guidProp = savedSo.FindProperty("__prefabGuid") ?? savedSo.FindProperty("_prefabGuid");
                if (guidProp != null && !string.IsNullOrEmpty(childGuid)) guidProp.stringValue = childGuid;
                savedSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(savedChildGo);
            }
        }

        private static void ConfigureMainAvatarsRegistryTeamsAndCorpses()
        {
            var mefPlayerGo = AssetDatabase.LoadAssetAtPath<GameObject>(GripTargetProfile.MefCyborgPlayerPath);
            var optimizedMefData = AssetDatabase.LoadAssetAtPath<AvatarData>("Assets/Data/Player/Avatars/OptimizedMEF.asset");
            if (optimizedMefData != null && mefPlayerGo != null)
            {
                optimizedMefData.prefab = mefPlayerGo;
                EditorUtility.SetDirty(optimizedMefData);
            }

            TeamColorVariantBuilder.BuildBlack();

            var optimizedMefBlackData = AssetDatabase.LoadAssetAtPath<AvatarData>("Assets/Data/Player/Avatars/OptimizedMEF_Black.asset");
            var cyborgData = AssetDatabase.LoadAssetAtPath<AvatarData>("Assets/Data/Player/Avatars/Cyborg.asset");

            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>("Assets/Data/Player/Avatars/AvatarsRegistry.asset");
            if (registry != null)
            {
                registry.avatars = new[] { optimizedMefData, optimizedMefBlackData, cyborgData }
                    .Where(a => a != null)
                    .ToArray();
                EditorUtility.SetDirty(registry);
            }

            var terroristsTeam = AssetDatabase.LoadAssetAtPath<TeamData>("Assets/Data/Teams/Terrorists_Team.asset");
            if (terroristsTeam != null && optimizedMefData != null)
            {
                terroristsTeam.avatars = new List<AvatarData> { optimizedMefData };
                EditorUtility.SetDirty(terroristsTeam);
            }

            var ctTeam = AssetDatabase.LoadAssetAtPath<TeamData>("Assets/Data/Teams/CounterTerrorists_Team.asset");
            if (ctTeam != null && optimizedMefBlackData != null)
            {
                ctTeam.avatars = new List<AvatarData> { optimizedMefBlackData };
                EditorUtility.SetDirty(ctTeam);
            }

            CorpseBuilder.Build();

            MoveAssetIfPresent("Assets/Prefabs/Player/Corpses/Optimized_MEF_Player_Corpse.prefab", $"{UnusedFolder}/Optimized_MEF_Player_Corpse.prefab");
            MoveAssetIfPresent("Assets/Prefabs/Player/Corpses/Optimized_MEF_Player_Black_Corpse.prefab", $"{UnusedFolder}/Optimized_MEF_Player_Black_Corpse.prefab");

            UpdateManagersSpawnPrefabs(registry);
        }

        private static void UpdateManagersSpawnPrefabs(AvatarRegistry registry)
        {
            if (registry == null || !File.Exists(ManagersPrefabPath)) return;

            var unusedPrefabs = new[]
            {
                $"{UnusedFolder}/Optimized_MEF_Player.prefab",
                $"{UnusedFolder}/Optimized_MEF_Player_Black.prefab",
                $"{UnusedFolder}/MEF_Base_Avatar.prefab",
                $"{UnusedFolder}/MEF_sdk_hands/MEF_sdk_hands_Player.prefab",
                $"{UnusedFolder}/Optimized_MEF_Player_VRIK.prefab"
            }
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(go => go != null)
            .ToHashSet();

            var root = PrefabUtility.LoadPrefabContents(ManagersPrefabPath);
            try
            {
                var manager = root.GetComponentInChildren<NetworkManager>(true);
                if (manager != null && manager.spawnPrefabs != null)
                {
                    manager.spawnPrefabs.RemoveAll(p => p == null || unusedPrefabs.Contains(p));
                    foreach (var prefab in AvatarRegistryTools.Prefabs(registry))
                    {
                        if (!manager.spawnPrefabs.Contains(prefab))
                        {
                            manager.spawnPrefabs.Add(prefab);
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, ManagersPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T FindByName<T>(Transform root, string targetName) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(targetName)) return null;
            foreach (var c in root.GetComponentsInChildren<T>(true))
            {
                if (c != null && string.Equals(c.name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
        }
    }
}

