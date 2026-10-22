using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>
    /// Рамка кисти для данных ладони в профиле (схема 2): начало — кость запястья, оси — универсальные оси кисти
    /// UltimateXR (<see cref="UxrAvatarArmInfo.HandUniversalLocalAxes"/>: +X — вправо по аватару у обеих рук,
    /// +Y — тыл кисти, +Z — от запястья к середине между основаниями среднего и безымянного пальцев), единицы —
    /// метры аватара (масштаб корня аватара, без масштаба костей). SDK выводит эти оси из положения костей пальцев,
    /// поэтому рамка одинаково лежит на кисти любого рига, как бы ни была повёрнута кость запястья (у Controllers —
    /// на 80° / 100° относительно MEF). Единственная пара преобразований «рамка кисти ↔ мир» для точки ладони P1,
    /// разметки, зеркала и точки на коже (<see cref="PointToWorld"/>/<see cref="PointFromWorld"/> и их варианты
    /// для смещений, направлений и поворотов).
    /// </summary>
    internal readonly struct GripHandFrame
    {
        public readonly Vector3 Origin;
        /// <summary>Поворот «рамка кисти → мир».</summary>
        public readonly Quaternion Rotation;
        /// <summary>Метры аватара → мир (масштаб корня аватара).</summary>
        public readonly float Scale;

        GripHandFrame(Vector3 origin, Quaternion rotation, float scale)
        {
            Origin = origin;
            Rotation = rotation;
            Scale = scale;
        }

        /// <summary>Рамка кисти руки аватара в текущей позе; false — нет запястья или универсальных осей кисти.</summary>
        public static bool TryGet(UxrAvatar avatar, UxrHandSide side, out GripHandFrame frame, out string error)
        {
            frame = default;
            error = null;
            UxrAvatarHand hand = avatar != null ? avatar.GetHand(side) : null;
            if (hand == null || hand.Wrist == null)
            {
                error = "нет кости запястья";
                return false;
            }

            UxrAvatarArmInfo arm = avatar.AvatarRigInfo != null ? avatar.AvatarRigInfo.GetArmInfo(side) : null;
            UxrUniversalLocalAxes axes = arm != null ? arm.HandUniversalLocalAxes : null;
            if (axes == null)
            {
                error = "нет HandUniversalLocalAxes (AvatarRigInfo не вычислен)";
                return false;
            }

            Vector3 forward = axes.LocalForward;
            Vector3 up = axes.LocalUp;
            if (forward.sqrMagnitude < 1e-12f || Vector3.Cross(forward, up).sqrMagnitude < 1e-12f)
            {
                error = "вырожденные HandUniversalLocalAxes";
                return false;
            }

            float scale = Mathf.Abs(avatar.transform.lossyScale.x);
            frame = new GripHandFrame(hand.Wrist.position, hand.Wrist.rotation * Quaternion.LookRotation(forward, up), scale > 1e-6f ? scale : 1f);
            return true;
        }

        public Vector3 PointToWorld(Vector3 pointHand) => Origin + VectorToWorld(pointHand);
        public Vector3 PointFromWorld(Vector3 pointWorld) => VectorFromWorld(pointWorld - Origin);
        public Vector3 VectorToWorld(Vector3 vectorHand) => Rotation * (vectorHand * Scale);
        public Vector3 VectorFromWorld(Vector3 vectorWorld) => Quaternion.Inverse(Rotation) * vectorWorld / Scale;
        public Vector3 DirToWorld(Vector3 directionHand) => Rotation * directionHand;
        public Vector3 DirFromWorld(Vector3 directionWorld) => Quaternion.Inverse(Rotation) * directionWorld;
        public Quaternion RotationToWorld(Quaternion rotationHand) => Rotation * rotationHand;
        public Quaternion RotationFromWorld(Quaternion rotationWorld) => Quaternion.Inverse(Rotation) * rotationWorld;

        /// <summary>Отражение поперёк кисти: у обеих рук +X рамки — вправо по аватару, поэтому меняет знак только x.</summary>
        public static Vector3 Mirror(Vector3 vectorHand) => new Vector3(-vectorHand.x, vectorHand.y, vectorHand.z);
    }

    /// <summary>
    /// Поиск, создание и запись профилей <see cref="GripHandProbeProfile"/>.
    /// Ассет создаётся только по явному действию пользователя («Сохранить», «Сбросить», «Отзеркалить»).
    /// </summary>
    internal static class GripHandProbeProfiles
    {
        public const string ProfilesFolder = "Assets/Editor/VR_Battlegrounds/Avatars/GripAuthoring/Analysis/Profiles";
        const string ParentFolder = "Assets/Editor/VR_Battlegrounds/Avatars/GripAuthoring/Analysis";

        static Dictionary<string, GripHandProbeProfile> _byGuid;
        static int _chainAvatarId;
        static List<KeyValuePair<string, string>> _chain;
        // Есть ли у звена цепочки собственные позы (владелец поз — база кистей, а не общий PlayerBase)
        static List<bool> _chainOwnsPoses;

        static GripHandProbeProfiles()
        {
            EditorApplication.projectChanged += Refresh;
        }

        public static void Refresh()
        {
            _byGuid = null;
            _chain = null;
        }

        static Dictionary<string, GripHandProbeProfile> Profiles
        {
            get
            {
                if (_byGuid != null)
                {
                    return _byGuid;
                }

                _byGuid = new Dictionary<string, GripHandProbeProfile>(StringComparer.Ordinal);
                foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GripHandProbeProfile)))
                {
                    var profile = AssetDatabase.LoadAssetAtPath<GripHandProbeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                    if (profile != null && !string.IsNullOrEmpty(profile.AvatarPrefabGuid) && !_byGuid.ContainsKey(profile.AvatarPrefabGuid))
                    {
                        _byGuid.Add(profile.AvatarPrefabGuid, profile);
                    }
                }

                return _byGuid;
            }
        }

        /// <summary>Цепочка (GUID prefab, имя) от аватара вверх по ParentAvatarPrefab; последний элемент — базовый аватар.</summary>
        static List<KeyValuePair<string, string>> Chain(UxrAvatar avatar)
        {
            int id = avatar.GetHashCode();
            if (_chain != null && _chainAvatarId == id)
            {
                return _chain;
            }

            var chain = new List<KeyValuePair<string, string>>();
            var ownsPoses = new List<bool>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int guard = 0;
            foreach (UxrAvatar link in avatar.GetAvatarChain())
            {
                if (link == null || ++guard > 32) break;
                string guid = link.PrefabGuid;
                if (string.IsNullOrEmpty(guid) && EditorUtility.IsPersistent(link))
                {
                    guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(link));
                }

                if (string.IsNullOrEmpty(guid) || !seen.Add(guid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = string.IsNullOrEmpty(path) ? link.name : Path.GetFileNameWithoutExtension(path);
                chain.Add(new KeyValuePair<string, string>(guid, name));
                ownsPoses.Add(link.GetHandPoses().Any());
            }

            _chain = chain;
            _chainOwnsPoses = ownsPoses;
            _chainAvatarId = id;
            return chain;
        }

        /// <summary>Первый профиль вверх по цепочке prefab аватара; null — разметки нет.</summary>
        public static GripHandProbeProfile Find(UxrAvatar avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            Dictionary<string, GripHandProbeProfile> profiles = Profiles;
            foreach (KeyValuePair<string, string> link in Chain(avatar))
            {
                if (profiles.TryGetValue(link.Key, out GripHandProbeProfile profile) && profile != null)
                {
                    return profile;
                }
            }

            return null;
        }

        /// <summary>
        /// Аватар, к которому привязывается новый профиль, и путь будущего ассета: первый вверх по цепочке
        /// с собственными позами (владелец поз, база кистей); иначе ближайший родитель; корень — в крайнем случае.
        /// </summary>
        public static bool TryGetNewProfileTarget(UxrAvatar avatar, out string guid, out string avatarName, out string assetPath)
        {
            guid = avatarName = assetPath = null;
            if (avatar == null)
            {
                return false;
            }

            List<KeyValuePair<string, string>> chain = Chain(avatar);
            if (chain.Count == 0)
            {
                return false;
            }

            int index = _chainOwnsPoses.IndexOf(true);
            if (index < 0)
            {
                index = chain.Count > 1 ? 1 : 0;
            }

            KeyValuePair<string, string> target = chain[index];
            guid = target.Key;
            avatarName = target.Value;
            assetPath = ProfilesFolder + "/" + SafeFileName(avatarName) + "_HandProbes.asset";
            return true;
        }

        static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }

        /// <summary>Существующий профиль цепочки или новый ассет у базового аватара.</summary>
        static GripHandProbeProfile FindOrCreate(UxrAvatar avatar, out string error)
        {
            error = null;
            GripHandProbeProfile profile = Find(avatar);
            if (profile != null)
            {
                return profile;
            }

            if (!TryGetNewProfileTarget(avatar, out string guid, out string avatarName, out string assetPath))
            {
                error = "У аватара нет GUID prefab: профиль не к чему привязать.";
                return null;
            }

            if (!AssetDatabase.IsValidFolder(ProfilesFolder))
            {
                AssetDatabase.CreateFolder(ParentFolder, "Profiles");
            }

            profile = ScriptableObject.CreateInstance<GripHandProbeProfile>();
            profile.Initialize(guid, avatarName);
            AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            AssetDatabase.SaveAssets();
            Refresh();
            return profile;
        }

        static void Commit(GripHandProbeProfile profile)
        {
            profile.BumpRevision();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        /// <summary>Сохраняет ручную рамку ладони руки в рамке кисти <see cref="GripHandFrame"/> (Undo на профиле).</summary>
        public static GripHandProbeProfile SavePalm(UxrAvatar avatar, UxrHandSide side, Vector3 positionHand, Quaternion rotationHand, out string error)
        {
            GripHandProbeProfile profile = FindOrCreate(avatar, out error);
            if (profile == null)
            {
                return null;
            }

            Undo.RecordObject(profile, "Разметка центра ладони");
            GripPalmProbe palm = profile.Get(side).Palm;
            palm.ManuallySet = true;
            palm.LegacyWristFrame = false;
            palm.PositionHand = positionHand;
            palm.RotationHand = GripPalmProbe.FrameFrom(rotationHand * Vector3.up, rotationHand * Vector3.forward);
            Commit(profile);
            return profile;
        }

        /// <summary>Записывает t колец фаланг руки (Undo на профиле); создаёт профиль у владельца поз при первом изменении.</summary>
        public static GripHandProbeProfile SaveRings(UxrAvatar avatar, UxrHandSide side, float[] rings, string undoName, out string error)
        {
            if (rings == null || rings.Length != GripHandProbeSide.RingCount)
            {
                error = "Нужно 15 значений t.";
                return null;
            }

            GripHandProbeProfile profile = FindOrCreate(avatar, out error);
            if (profile == null)
            {
                return null;
            }

            Undo.RecordObject(profile, undoName);
            GripHandProbeSide data = profile.Get(side);
            data.RingT = new float[GripHandProbeSide.RingCount];
            for (int i = 0; i < GripHandProbeSide.RingCount; i++)
            {
                data.RingT[i] = GripHandProbeSide.ClampRingT(rings[i]);
            }

            Commit(profile);
            return profile;
        }

        /// <summary>Записывает радиусы колец фаланг руки, мм (0 — по умолчанию), с Undo; создаёт профиль у владельца поз.</summary>
        public static GripHandProbeProfile SaveRingRadii(UxrAvatar avatar, UxrHandSide side, float[] radiiMm, string undoName, out string error)
        {
            if (radiiMm == null || radiiMm.Length != GripHandProbeSide.RingCount)
            {
                error = "Нужно 15 значений радиуса.";
                return null;
            }

            GripHandProbeProfile profile = FindOrCreate(avatar, out error);
            if (profile == null)
            {
                return null;
            }

            Undo.RecordObject(profile, undoName);
            GripHandProbeSide data = profile.Get(side);
            data.RingRadiusMm = new float[GripHandProbeSide.RingCount];
            for (int i = 0; i < GripHandProbeSide.RingCount; i++)
            {
                data.RingRadiusMm[i] = radiiMm[i] > 0f ? GripHandProbeSide.ClampRingRadiusMm(radiiMm[i]) : 0f;
            }

            Commit(profile);
            return profile;
        }

        static int RingIndex(int finger, int phalanx)
        {
            if (finger < 0 || finger > 4 || phalanx < 0 || phalanx > 2)
            {
                throw new ArgumentOutOfRangeException(nameof(finger), "Палец 0..4 (большой…мизинец), фаланга 0..2 (прокс., средн., дист.).");
            }

            return finger * 3 + phalanx;
        }

        static int FingerIndex(UxrFingerType finger)
        {
            switch (finger)
            {
                case UxrFingerType.Thumb: return 0;
                case UxrFingerType.Index: return 1;
                case UxrFingerType.Middle: return 2;
                case UxrFingerType.Ring: return 3;
                case UxrFingerType.Little: return 4;
                default: throw new ArgumentOutOfRangeException(nameof(finger), finger, null);
            }
        }

        /// <summary>
        /// Радиус кольца фаланги в метрах с учётом профиля кисти (поиск вверх по цепочке prefab) и умолчаний 6,8 / 5,8 / 5,0 мм.
        /// <paramref name="finger"/>: 0 большой, 1 указательный, 2 средний, 3 безымянный, 4 мизинец;
        /// <paramref name="phalanx"/>: 0 прокс., 1 средн., 2 дист.
        /// </summary>
        internal static float GetRingRadiusMeters(UxrAvatar avatar, UxrHandSide side, int finger, int phalanx)
        {
            int index = RingIndex(finger, phalanx);
            GripHandProbeProfile profile = avatar != null ? Find(avatar) : null;
            float mm = profile != null ? profile.Get(side).GetRingRadiusMm(index) : GripHandProbeSide.DefaultRingRadiusMm(index);
            return mm * 0.001f;
        }

        internal static float GetRingRadiusMeters(UxrAvatar avatar, UxrHandSide side, UxrFingerType finger, int phalanx) =>
            GetRingRadiusMeters(avatar, side, FingerIndex(finger), phalanx);

        /// <summary>
        /// Доля длины сегмента (0..1) для кольца фаланги с учётом профиля кисти; по умолчанию 0,5 (середина).
        /// Индексы — как у <see cref="GetRingRadiusMeters(UxrAvatar, UxrHandSide, int, int)"/>.
        /// </summary>
        internal static float GetRingT(UxrAvatar avatar, UxrHandSide side, int finger, int phalanx)
        {
            int index = RingIndex(finger, phalanx);
            GripHandProbeProfile profile = avatar != null ? Find(avatar) : null;
            return profile != null ? profile.Get(side).GetRingT(index) : GripHandProbeSide.DefaultRingT;
        }

        internal static float GetRingT(UxrAvatar avatar, UxrHandSide side, UxrFingerType finger, int phalanx) =>
            GetRingT(avatar, side, FingerIndex(finger), phalanx);

        /// <summary>Записывает наклон (°, x — pitch, y — yaw) и азимут точки касания (°) колец руки с Undo.</summary>
        public static GripHandProbeProfile SaveRingOrientation(UxrAvatar avatar, UxrHandSide side, Vector2[] tiltDeg, float[] azimuthDeg, string undoName, out string error)
        {
            if (tiltDeg == null || tiltDeg.Length != GripHandProbeSide.RingCount || azimuthDeg == null || azimuthDeg.Length != GripHandProbeSide.RingCount)
            {
                error = "Нужно 15 значений наклона и азимута.";
                return null;
            }

            GripHandProbeProfile profile = FindOrCreate(avatar, out error);
            if (profile == null)
            {
                return null;
            }

            Undo.RecordObject(profile, undoName);
            GripHandProbeSide data = profile.Get(side);
            data.RingTiltDeg = new Vector2[GripHandProbeSide.RingCount];
            data.RingContactAzimuthDeg = new float[GripHandProbeSide.RingCount];
            for (int i = 0; i < GripHandProbeSide.RingCount; i++)
            {
                data.RingTiltDeg[i] = GripHandProbeSide.ClampRingTilt(tiltDeg[i]);
                data.RingContactAzimuthDeg[i] = GripHandProbeSide.ClampRingAzimuth(azimuthDeg[i]);
            }

            Commit(profile);
            return profile;
        }

        /// <summary>Записывает смещения центров колец, мм (x — LocalRight, y — LocalUp), с Undo.</summary>
        public static GripHandProbeProfile SaveRingOffsets(UxrAvatar avatar, UxrHandSide side, Vector2[] offsetsMm, string undoName, out string error)
        {
            if (offsetsMm == null || offsetsMm.Length != GripHandProbeSide.RingCount)
            {
                error = "Нужно 15 значений смещения.";
                return null;
            }

            GripHandProbeProfile profile = FindOrCreate(avatar, out error);
            if (profile == null)
            {
                return null;
            }

            Undo.RecordObject(profile, undoName);
            GripHandProbeSide data = profile.Get(side);
            data.RingOffsetMm = new Vector2[GripHandProbeSide.RingCount];
            for (int i = 0; i < GripHandProbeSide.RingCount; i++)
            {
                data.RingOffsetMm[i] = GripHandProbeSide.ClampRingOffset(offsetsMm[i]);
            }

            Commit(profile);
            return profile;
        }

        /// <summary>
        /// Оси смещения центра кольца в мире: x — LocalRight кости, y — LocalUp, обе ортогонализованы к оси фаланги
        /// (y ещё и к x). false — вырожденная кость.
        /// </summary>
        internal static bool GetRingOffsetAxes(Transform bone, UxrUniversalLocalAxes fingerAxes, Vector3 boneStart, Vector3 boneEnd,
            out Vector3 xAxis, out Vector3 yAxis)
        {
            xAxis = yAxis = Vector3.zero;
            Vector3 axis = boneEnd - boneStart;
            if (bone == null || fingerAxes == null || axis.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            axis.Normalize();
            xAxis = Vector3.ProjectOnPlane(bone.TransformDirection(fingerAxes.LocalRight), axis);
            if (xAxis.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            xAxis.Normalize();
            yAxis = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(bone.TransformDirection(fingerAxes.LocalUp), axis), xAxis);
            if (yAxis.sqrMagnitude < 1e-12f)
            {
                yAxis = Vector3.Cross(axis, xAxis);
            }

            yAxis.Normalize();
            return true;
        }

        /// <summary>Рамка кольца без смещения центра (прежняя сигнатура; эквивалент offsetMm = 0).</summary>
        internal static bool ComputeRingFrame(Transform bone, UxrUniversalLocalAxes fingerAxes, Vector3 boneStart, Vector3 boneEnd,
            float t, Vector2 tiltDeg, float azimuthDeg,
            out Vector3 center, out Vector3 normal, out Vector3 contactDir) =>
            ComputeRingFrame(bone, fingerAxes, boneStart, boneEnd, t, tiltDeg, azimuthDeg, Vector2.zero, out center, out normal, out contactDir);

        /// <summary>
        /// Рамка кольца фаланги в мире по явным параметрам (общая для анализа, отрисовки и «Обхватить»).
        /// Центр = точка A = Lerp(boneStart, boneEnd, t) на линии фаланги + смещение offsetMm (мм) по осям
        /// <see cref="GetRingOffsetAxes"/>; наклон и азимут применяются вокруг смещённого центра.
        /// Базис кости: ось сегмента A = boneEnd − boneStart; ладонное направление P = −LocalUp кости, ортогонализованное к A.
        /// Наклон Q = AngleAxis(tilt.x, LocalRight кости) · AngleAxis(tilt.y, LocalUp кости) (оси в мире);
        /// нормаль плоскости кольца = Q·A; направление на точку касания = AngleAxis(azimuth, нормаль) · (Q·P).
        /// Центр — Lerp(boneStart, boneEnd, t). false — вырожденная кость.
        /// </summary>
        internal static bool ComputeRingFrame(Transform bone, UxrUniversalLocalAxes fingerAxes, Vector3 boneStart, Vector3 boneEnd,
            float t, Vector2 tiltDeg, float azimuthDeg, Vector2 offsetMm,
            out Vector3 center, out Vector3 normal, out Vector3 contactDir)
        {
            center = Vector3.Lerp(boneStart, boneEnd, t);
            if (offsetMm != Vector2.zero && GetRingOffsetAxes(bone, fingerAxes, boneStart, boneEnd, out Vector3 xAxis, out Vector3 yAxis))
            {
                center += (xAxis * offsetMm.x + yAxis * offsetMm.y) * 0.001f;
            }

            normal = contactDir = Vector3.zero;
            Vector3 axis = boneEnd - boneStart;
            if (bone == null || fingerAxes == null || axis.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            axis.Normalize();
            Vector3 palmar = Vector3.ProjectOnPlane(-bone.TransformVector(fingerAxes.LocalUp), axis);
            if (palmar.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            palmar.Normalize();
            Vector3 right = bone.TransformDirection(fingerAxes.LocalRight).normalized;
            Vector3 up = bone.TransformDirection(fingerAxes.LocalUp).normalized;
            Quaternion tilt = Quaternion.AngleAxis(tiltDeg.x, right) * Quaternion.AngleAxis(tiltDeg.y, up);
            normal = (tilt * axis).normalized;
            Vector3 tiltedPalmar = Vector3.ProjectOnPlane(tilt * palmar, normal).normalized;
            contactDir = (Quaternion.AngleAxis(azimuthDeg, normal) * tiltedPalmar).normalized;
            return true;
        }

        /// <summary>
        /// Рамка кольца фаланги в мире с учётом профиля кисти (t, смещение центра, радиус, наклон, азимут;
        /// умолчания — середина, без смещения, 6,8/5,8/5,0 мм, 0°). center уже включает смещение.
        /// finger: 0 большой … 4 мизинец; phalanx: 0 прокс., 1 средн., 2 дист.
        /// bone — кость фаланги (Proximal/Intermediate/Distal); boneStart/boneEnd — начало и конец сегмента в мире
        /// (для дистальной — кончик пальца). FingerUniversalLocalAxes берутся у аватара. radius — в метрах.
        /// </summary>
        internal static bool GetRingFrame(UxrAvatar avatar, UxrHandSide side, int finger, int phalanx,
            Transform bone, Vector3 boneStart, Vector3 boneEnd,
            out Vector3 center, out Vector3 normal, out Vector3 contactDir, out float radius)
        {
            int index = RingIndex(finger, phalanx);
            GripHandProbeProfile profile = avatar != null ? Find(avatar) : null;
            GripHandProbeSide data = profile != null ? profile.Get(side) : null;
            float t = data != null ? data.GetRingT(index) : GripHandProbeSide.DefaultRingT;
            radius = (data != null ? data.GetRingRadiusMm(index) : GripHandProbeSide.DefaultRingRadiusMm(index)) * 0.001f;
            Vector2 tilt = data != null ? data.GetRingTiltDeg(index) : Vector2.zero;
            float azimuth = data != null ? data.GetRingAzimuthDeg(index) : 0f;
            Vector2 offset = data != null ? data.GetRingOffsetMm(index) : Vector2.zero;
            UxrAvatarArmInfo arm = avatar != null && avatar.AvatarRigInfo != null ? avatar.AvatarRigInfo.GetArmInfo(side) : null;
            return ComputeRingFrame(bone, arm != null ? arm.FingerUniversalLocalAxes : null, boneStart, boneEnd, t, tilt, azimuth, offset,
                out center, out normal, out contactDir);
        }

        internal static bool GetRingFrame(UxrAvatar avatar, UxrHandSide side, UxrFingerType finger, int phalanx,
            Transform bone, Vector3 boneStart, Vector3 boneEnd,
            out Vector3 center, out Vector3 normal, out Vector3 contactDir, out float radius) =>
            GetRingFrame(avatar, side, FingerIndex(finger), phalanx, bone, boneStart, boneEnd, out center, out normal, out contactDir, out radius);

        /// <summary>
        /// Наклон и азимут, при которых направление на точку касания совпадает с targetDir (yaw = 0):
        /// pitch вокруг LocalRight переводит ладонное направление в проекцию цели на плоскость, перпендикулярную LocalRight;
        /// азимут докручивает вокруг нормали кольца к компоненте вдоль LocalRight. Цель тогда лежит в плоскости кольца.
        /// </summary>
        internal static bool SolveRingOrientation(Transform bone, UxrUniversalLocalAxes fingerAxes, Vector3 boneStart, Vector3 boneEnd,
            Vector3 targetDir, out Vector2 tiltDeg, out float azimuthDeg)
        {
            tiltDeg = Vector2.zero;
            azimuthDeg = 0f;
            Vector3 axis = boneEnd - boneStart;
            if (bone == null || fingerAxes == null || axis.sqrMagnitude < 1e-12f || targetDir.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            axis.Normalize();
            Vector3 palmar = Vector3.ProjectOnPlane(-bone.TransformVector(fingerAxes.LocalUp), axis).normalized;
            Vector3 right = bone.TransformDirection(fingerAxes.LocalRight).normalized;
            Vector3 target = targetDir.normalized;
            Vector3 targetNoRight = Vector3.ProjectOnPlane(target, right);
            float pitch = targetNoRight.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(palmar, targetNoRight, right) : 0f;
            pitch = Mathf.Clamp(pitch, -GripHandProbeSide.MaxRingTiltDeg, GripHandProbeSide.MaxRingTiltDeg);
            Quaternion tilt = Quaternion.AngleAxis(pitch, right);
            Vector3 normal = (tilt * axis).normalized;
            Vector3 tiltedPalmar = Vector3.ProjectOnPlane(tilt * palmar, normal).normalized;
            Vector3 targetInPlane = Vector3.ProjectOnPlane(target, normal);
            float azimuth = targetInPlane.sqrMagnitude > 1e-8f ? Vector3.SignedAngle(tiltedPalmar, targetInPlane, normal) : 0f;
            tiltDeg = new Vector2(pitch, 0f);
            azimuthDeg = GripHandProbeSide.ClampRingAzimuth(azimuth);
            return true;
        }

        /// <summary>Снимает ручную разметку руки (и пометку старого формата): анализ вернётся к авто (кожа).</summary>
        public static bool ResetPalm(UxrAvatar avatar, UxrHandSide side)
        {
            GripHandProbeProfile profile = Find(avatar);
            if (profile == null || !profile.Get(side).Palm.ManuallySet)
            {
                return false;
            }

            Undo.RecordObject(profile, "Сброс центра ладони на авто");
            profile.Get(side).Palm.ManuallySet = false;
            profile.Get(side).Palm.LegacyWristFrame = false;
            Commit(profile);
            return true;
        }

        /// <summary>
        /// Отражение рамки ладони на другую руку в рамке кисти <see cref="GripHandFrame"/>: компонента поперёк ладони
        /// (x) меняет знак, вдоль пальцев (z) и по нормали тыла (y) сохраняется. Отражаются позиция, нормаль и «вперёд»;
        /// рамка собирается заново. Риг не нужен: оси кисти обеих рук согласованы SDK.
        /// </summary>
        public static void Mirror(Vector3 positionHand, Quaternion rotationHand, out Vector3 mirroredPosition, out Quaternion mirroredRotation)
        {
            mirroredPosition = GripHandFrame.Mirror(positionHand);
            mirroredRotation = GripPalmProbe.FrameFrom(GripHandFrame.Mirror(rotationHand * Vector3.up), GripHandFrame.Mirror(rotationHand * Vector3.forward));
        }

        /// <summary>Записывает отражённую рамку ладони на другую руку (Undo на профиле).</summary>
        public static GripHandProbeProfile MirrorPalm(UxrAvatar avatar, UxrHandSide fromSide, Vector3 positionHand, Quaternion rotationHand, out string error)
        {
            Mirror(positionHand, rotationHand, out Vector3 position, out Quaternion rotation);
            UxrHandSide toSide = fromSide == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left;
            return SavePalm(avatar, toSide, position, rotation, out error);
        }
    }
}
