using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

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
        public const string CanonicalCyborgHandsBasePath = "Assets/Prefabs/Player/CyborgHandsBase.prefab";
        public const string LegacyMefCyborgBasePath     = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Base.prefab";
        public const string CyborgAvatarPath            = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        public const string MefCyborgPlayerPath         = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Player.prefab";
        public const string MefCyborgModelPath          = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/Model/MEF_CyborgHands.fbx";

        private const string CyborgFolder = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands";

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
        /// (Cyborg_Player, MEF_cyborg_hands_Player и др.).
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
    /// Перестройка и проверка иерархии префабов с кистями киборга по целевой схеме:
    /// <c>PlayerBase_SdkHands -&gt; CyborgHandsBase -&gt; { PlayerControllersCyborgAvatar, MEF_cyborg_hands_Player }</c>.
    /// </summary>
    public static class CyborgHandsPrefabTree
    {
        private const string BaseStandaloneRigName = "CyborgHands_Rig";

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
        /// Выполняет миграцию дерева префабов CyborgHandsBase с сохранением GUID всех существующих аватаров.
        /// </summary>
        public static string RebuildTree()
        {
            string basePath = EnsureCanonicalBasePath();
            ReplaceNativeMefGlovesWithCyborgHandsInBase(basePath);
            StripBodyRenderersAndDetachParentInBase(basePath);
            EnsureChildVariantUsesBase(GripTargetProfile.MefCyborgPlayerPath, basePath, clearHandPosesOverride: false);
            RebaseCyborgAvatarToCyborgHandsBase(GripTargetProfile.CyborgAvatarPath, basePath);
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
            return sb.ToString();
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

                PrefabUtility.SaveAsPrefabAsset(playerRoot, GripTargetProfile.MefCyborgPlayerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(playerRoot);
            }
        }

        /// <summary>
        /// Делает <c>CyborgHandsBase</c> корневым префабом ветки киборг-рук (<c>_parentPrefab = null</c> без <c>PlayerBase_SdkHands</c>)
        /// и скрывает меши тела в базовом префабе, оставляя видимыми только кисти киборга на гуманоидном риге.
        /// </summary>
        private static void StripBodyRenderersAndDetachParentInBase(string basePath)
        {
            var baseRoot = PrefabUtility.LoadPrefabContents(basePath);
            try
            {
                var avatar = baseRoot.GetComponent<UxrAvatar>();
                if (avatar != null)
                {
                    var so = new SerializedObject(avatar);
                    var parentProp = so.FindProperty("_parentPrefab");
                    if (parentProp != null)
                    {
                        parentProp.objectReferenceValue = null;
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
                    if (!isHandRenderer)
                    {
                        smr.enabled = false;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(baseRoot, basePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(baseRoot);
            }
        }

        private static void SetGrabberRendererEntry(SerializedProperty element, UxrGrabber grabber, Renderer handRenderer)
        {
            if (element == null) return;
            var grabberProp = element.FindPropertyRelative("_grabber");
            var rendererProp = element.FindPropertyRelative("_handRenderer");
            if (grabberProp != null) grabberProp.objectReferenceValue = grabber;
            if (rendererProp != null) rendererProp.objectReferenceValue = handRenderer;
        }

        private static UxrGrabber EnsureGrabber(Transform wrist, string grabberName, UxrHandSide side, Renderer handRenderer, UxrAvatar donorAvatar, bool isLeft)
        {
            if (wrist == null) return null;
            var grabberTransform = wrist.Find(grabberName);
            if (grabberTransform == null)
            {
                var go = new GameObject(grabberName);
                grabberTransform = go.transform;
                grabberTransform.SetParent(wrist, false);
            }

            UxrGrabber donorGrabber = null;
            foreach (var g in donorAvatar.GetComponentsInChildren<UxrGrabber>(true))
            {
                if (g != null && g.Side == side)
                {
                    donorGrabber = g;
                    break;
                }
            }

            if (donorGrabber != null)
            {
                grabberTransform.localPosition = donorGrabber.transform.localPosition;
                grabberTransform.localRotation = donorGrabber.transform.localRotation;
                grabberTransform.localScale = donorGrabber.transform.localScale;
            }

            var grabber = grabberTransform.GetComponent<UxrGrabber>();
            if (grabber == null) grabber = grabberTransform.gameObject.AddComponent<UxrGrabber>();

            var so = new SerializedObject(grabber);
            var sideProp = so.FindProperty("_side");
            if (sideProp != null) sideProp.enumValueIndex = (int)side;
            var rendererProp = so.FindProperty("_handRenderer");
            if (rendererProp != null) rendererProp.objectReferenceValue = handRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            return grabber;
        }

        private static void MapHandProperty(SerializedObject targetSo, SerializedObject donorSo, string prefix, Transform targetRigRoot)
        {
            MapBoneRef(targetSo, donorSo, $"{prefix}._wrist", targetRigRoot);
            foreach (string finger in new[] { "_thumb", "_index", "_middle", "_ring", "_little" })
            {
                MapBoneRef(targetSo, donorSo, $"{prefix}.{finger}._metacarpal", targetRigRoot);
                MapBoneRef(targetSo, donorSo, $"{prefix}.{finger}._proximal", targetRigRoot);
                MapBoneRef(targetSo, donorSo, $"{prefix}.{finger}._intermediate", targetRigRoot);
                MapBoneRef(targetSo, donorSo, $"{prefix}.{finger}._distal", targetRigRoot);
            }
        }

        private static void MapBoneRef(SerializedObject targetSo, SerializedObject donorSo, string propertyPath, Transform targetRigRoot)
        {
            var donorProp = donorSo.FindProperty(propertyPath);
            var targetProp = targetSo.FindProperty(propertyPath);
            if (donorProp == null || targetProp == null) return;

            var donorTransform = donorProp.objectReferenceValue as Transform;
            targetProp.objectReferenceValue = donorTransform != null
                ? FindTransformByName(targetRigRoot, donorTransform.name)
                : null;
        }

        private static void CopyVector3Property(SerializedObject targetSo, SerializedObject donorSo, string propertyPath)
        {
            var donorProp = donorSo.FindProperty(propertyPath);
            var targetProp = targetSo.FindProperty(propertyPath);
            if (donorProp != null && targetProp != null)
            {
                targetProp.vector3Value = donorProp.vector3Value;
            }
        }

        private static void RebaseCyborgAvatarToCyborgHandsBase(string cyborgAvatarPath, string basePath)
        {
            string text = File.ReadAllText(cyborgAvatarPath, Encoding.UTF8);
            string updated = Regex.Replace(
                text,
                @"(\b(?:m_TransformParent|m_SourcePrefab):\s*\{fileID:\s*100100000,\s*guid:\s*)" + GripTargetProfile.SdkHandsBaseGuid + @"(\b)",
                "${1}" + GripTargetProfile.CyborgBaseGuid + "${2}");
            if (!string.Equals(text, updated, StringComparison.Ordinal))
            {
                File.WriteAllText(cyborgAvatarPath, updated.Replace("\r\n", "\n"), new UTF8Encoding(false));
                AssetDatabase.ImportAsset(cyborgAvatarPath, ImportAssetOptions.ForceSynchronousImport);
            }

            EnsureChildVariantUsesBase(cyborgAvatarPath, basePath, clearHandPosesOverride: true);
        }

        private static void EnsureChildVariantUsesBase(string childPath, string basePath, bool clearHandPosesOverride)
        {
            var baseGo = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            var baseAvatar = baseGo != null ? baseGo.GetComponent<UxrAvatar>() : null;
            if (baseAvatar == null) return;

            string childGuid = AssetDatabase.AssetPathToGUID(childPath);
            var root = PrefabUtility.LoadPrefabContents(childPath);
            try
            {
                var inheritedHandsRig = root.transform.Find(BaseStandaloneRigName);
                if (inheritedHandsRig != null)
                {
                    inheritedHandsRig.gameObject.SetActive(false);
                }

                bool hasOwnCyborgRig = root.transform.Find("Cyborg") != null;
                var mefRig = root.transform.Find("MEF_Rig");
                if (hasOwnCyborgRig && mefRig != null)
                {
                    mefRig.gameObject.SetActive(false);
                }
                else if (!hasOwnCyborgRig && mefRig != null)
                {
                    mefRig.gameObject.SetActive(true);
                    foreach (var smr in mefRig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (smr == null || smr.sharedMesh == null) continue;
                        if (smr.sharedMesh.name.IndexOf("Hands_gloves", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        smr.enabled = true;
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

                    if (clearHandPosesOverride)
                    {
                        var posesProp = so.FindProperty("_handPoses");
                        if (posesProp != null) PrefabUtility.RevertPropertyOverride(posesProp, InteractionMode.AutomatedAction);
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, childPath);
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
                EditorUtility.SetDirty(savedAvatar);
            }
        }

        private static Transform FindTransformByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && string.Equals(t.name, targetName, StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return null;
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

