using System;
using System.IO;
using UnityEditor;
using UnityEngine;

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
        /// <summary>Дескриптор источника как есть (вторая рука — Mirrored), как у прежнего импортёра паков.</summary>
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
    /// и как называть align. Встроенные профили — <see cref="CyborgHands" /> (по умолчанию) и <see cref="MefCyborgHands" />.
    /// Профиль <c>MEF</c> (прежний импортёр паков: MEF_Base_Avatar, база поз PlayerBase_NonSdkHands, опора — запястье)
    /// выведен вместе с аватаром хвата MEF (решение пользователя 2026-10-23).
    /// </summary>
    public sealed class GripTargetProfile
    {
        /// <summary>Имя выведенного профиля MEF — только для понятного отказа в <see cref="Get" />.</summary>
        public const string RetiredMefName     = "MEF";
        public const string MefCyborgHandsName = "MEF_cyborg_hands";
        public const string CyborgHandsName    = "CyborgHands";

        public const string CyborgBaseGuid              = "71b74b0d08444f49bbb7aa2828d0890a";
        public const string CyborgAvatarGuid            = "8e0e06323e4754f479a9f19788554652";
        public const string MefCyborgPlayerGuid         = "2aa0ab6714e2413f9d829637b62df0c0";
        public const string CanonicalCyborgHandsBasePath = "Assets/Prefabs/Player/Avatars/CyborgHands/Base/CyborgHandsBase.prefab";
        public const string LegacyMefCyborgBasePath     = "Assets/Prefabs/Player/Experimental/MEF_cyborg_hands/MEF_cyborg_hands_Base.prefab";
        public const string CyborgAvatarPath            = "Assets/Prefabs/Player/Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab";
        public const string MefCyborgPlayerPath         = "Assets/Prefabs/Player/Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player.prefab";
        public const string MefCyborgPlayerBlackPath    = "Assets/Prefabs/Player/Avatars/CyborgHands/MEF/MEF_cyborg_hands_Player_Black.prefab";
        public const string MefCyborgModelPath          = "Assets/Models/Avatars/MEF_CyborgHands/MEF_CyborgHands.fbx";

        private const string CyborgPoseFolder = "Assets/Prefabs/Player/HandPoses/CyborgHands";

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
                PoseFolder           = CyborgPoseFolder,
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
                PoseFolder           = CyborgPoseFolder,
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
            if (string.Equals(name, RetiredMefName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"target: профиль «{RetiredMefName}» выведен 2026-10-23 (записи хвата MEF_Base_Avatar не пишутся) — " +
                                            $"есть {CyborgHandsName}, {MefCyborgHandsName}.");
            if (string.Equals(name, MefCyborgHandsName, StringComparison.OrdinalIgnoreCase)) return MefCyborgHands();
            if (string.Equals(name, CyborgHandsName, StringComparison.OrdinalIgnoreCase)) return CyborgHands();
            throw new ArgumentException($"target: неизвестный профиль «{name}» (есть {CyborgHandsName}, {MefCyborgHandsName}).");
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
}

