using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Параметры расчёта точки: профиль цели и доводка по рукам.</summary>
    public sealed class GripExtractionOptions
    {
        public GripTargetProfile Profile;
        public string            RecordGuid;
        public float             NormalOffsetMm;
        public float             MaxNormalOffsetMm = 15f;

        public Dictionary<string, float>                     NormalOffsetMmByHand;
        public Dictionary<string, Dictionary<string, float>> CurlDegByHand;

        /// <summary>Хук шага 2 (сдвиг по нормали): сейчас — значение запроса; автоподбор по Hand Pose Fit — позже (GripFitCheck).</summary>
        public float NormalOffsetFor(string handKey) =>
            NormalOffsetMmByHand != null && NormalOffsetMmByHand.TryGetValue(handKey, out float mm) ? mm : NormalOffsetMm;

        /// <summary>Хук шага 4 (curl пальцев): значения запроса, например подсказки HandPoseBendSolver прошлой итерации.</summary>
        public Dictionary<string, float> CurlFor(string handKey) =>
            CurlDegByHand != null && CurlDegByHand.TryGetValue(handKey, out Dictionary<string, float> curl) ? curl : null;
    }

    /// <summary>
    /// Расчёт хвата одной точки на риге цели — только чтение: префаб предмета открывается через <c>LoadPrefabContents</c>
    /// и выгружается без сохранения. Шаги: (1) кодирование позы обеих рук; (2) поза на риге (центр ладони — как в игре);
    /// (3) посадка по опоре профиля и сдвиг по нормали; (4) сверки с прежней формулой, существующими align и позой;
    /// (5) рантайм против редактора. Запись — <see cref="GripRecordWriter" />.
    /// </summary>
    public static class GripExtractor
    {
        public static GripExtractionResult Extract(IGripSource source, GripSourcePoint point, GripTargetRig rig, GripExtractionOptions options)
        {
            GripTargetProfile profile = options.Profile;
            var result = new GripExtractionResult
            {
                Pose       = point.Pose,
                SourceKind = point.SourceKind,
                SourceSide = point.SourceSide.ToString(),
                Weapon     = point.Weapon,
                BodyPath   = point.BodyPath,
                GrabPoint  = point.GrabPoint,
                PosePath   = profile.PosePathFor(point),
                Point      = point
            };

            GripSample sample       = source.Sample(point);
            var        existingPose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(result.PosePath);
            result.ExistingPose = existingPose != null;

            // (1) Поза обеих рук: ассет позы хранит обе, рантайм-проверке нужен целый ассет.
            var descriptors = new Dictionary<UxrHandSide, UxrHandDescriptor>();
            foreach (UxrHandSide side in GripTargetRig.Sides)
            {
                descriptors[side] = GripPoseEncoder.Encode(rig, side, sample.DescriptorFor(side), profile.Encoding, options.CurlFor(point.HandKey(side)));
            }

            UxrHandPoseAsset transient = GripPoseAssets.CreateTransient(point.Pose, descriptors[UxrHandSide.Left], descriptors[UxrHandSide.Right]);
            GameObject       root      = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(point.Weapon);
                Transform body = root.transform.Find(point.BodyPath) ?? throw new KeyNotFoundException($"{point.Weapon}: нет корпуса {point.BodyPath}");
                if (!root.TryGetComponent(out UxrGrabbableObject grabbable))
                {
                    throw new KeyNotFoundException($"{point.Weapon}: нет UxrGrabbableObject на корне");
                }

                UxrGrabPointInfo grabPoint = grabbable.GetGrabPoint(point.GrabPoint) ?? throw new KeyNotFoundException($"{point.Weapon}: нет точки хвата {point.GrabPoint}");
                UxrGripPoseInfo  record    = grabPoint.GetGripPoseInfo(options.RecordGuid);

                foreach (UxrHandSide side in GripTargetRig.Sides)
                {
                    result.Hands.Add(ExtractHand(point, sample, side, descriptors[side], rig, options, root.transform, body, record, transient, existingPose, out float k));
                    result.K = GripMath.Round(k);
                }
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
                UnityEngine.Object.DestroyImmediate(transient);
                rig.RestoreBind();
            }

            return result;
        }

        private static GripHandResult ExtractHand(GripSourcePoint point, GripSample sample, UxrHandSide side, UxrHandDescriptor descriptor, GripTargetRig rig,
                                                  GripExtractionOptions options, Transform root, Transform body, UxrGripPoseInfo record,
                                                  UxrHandPoseAsset transient, UxrHandPoseAsset existingPose, out float k)
        {
            GripTargetProfile profile = options.Profile;
            string            key     = point.HandKey(side);
            float             offset  = options.NormalOffsetFor(key);
            float             clamped = Mathf.Clamp(offset, -options.MaxNormalOffsetMm, options.MaxNormalOffsetMm);
            var hand = new GripHandResult
            {
                Side                = side.ToString(),
                Anchor              = profile.Anchor.ToString(),
                NormalOffsetMm      = GripMath.Round(clamped),
                NormalOffsetClamped = !Mathf.Approximately(offset, clamped),
                CurlDeg             = options.CurlFor(key),
                Descriptor          = descriptor,
                MetacarpalInfo      = GripPoseEncoder.MetacarpalInfo(descriptor)
            };

            // (2) Поза на риге: центр ладони считается по суставам в позе (пястные могут повернуться).
            rig.RestoreBind();
            rig.Apply(side, descriptor);

            // (3) Посадка.
            var placement = new GripPlacement(root, body, sample, side, rig);
            k = placement.K;
            float meters = clamped * 0.001f;
            hand.Align              = placement.Compute(profile.Anchor, meters);
            hand.AlignLocalPosition = GripMath.Round(hand.Align.Position);
            hand.AlignLocalRotation = GripMath.Round(hand.Align.Rotation);
            hand.AlignLocalEuler    = GripMath.Round(hand.Align.Rotation.eulerAngles);
            hand.Palm               = placement.Evaluate(hand.Align, meters);

            // (4) Сверки: прежняя формула на той же цели, align с именем профиля, align текущей записи.
            hand.VsLegacyWrist = placement.Delta(hand.Align, placement.Compute(GripAnchor.Wrist, 0f), "anchor=wrist, s=0");
            hand.ExistingNamed = Existing(placement, root, hand.Align, FindNamedAlign(root, point, profile, side));
            Transform recordAlign = record == null ? null : side == UxrHandSide.Left ? record.GripAlignTransformHandLeft : record.GripAlignTransformHandRight;
            hand.ExistingRecord = Existing(placement, root, hand.Align, recordAlign);
            if (profile.Anchor == GripAnchor.Wrist && sample.Legacy != null && Mathf.Approximately(clamped, 0f))
            {
                hand.VsHandsPackAligner = AlignerDelta(placement, root, body, sample, side, rig.PrefabPath, hand.Align);
            }

            // (5) Поза: рантайм против редактора, форма против источника, против существующего ассета.
            rig.RestoreBind();
            hand.RuntimeVsWorldDeg = GripMath.Round(GripPoseEncoder.RuntimeVsWorldDeg(rig, side, transient));
            hand.ShapeDeg          = GripMath.Round(GripPoseEncoder.ShapeDeg(rig, side, sample.DescriptorFor(side), descriptor));
            if (existingPose != null)
            {
                hand.PoseVsExistingDeg = GripMath.Round(GripPoseEncoder.ShapeDeg(rig, side, GripPoseEncoder.Descriptor(existingPose, side), descriptor));
            }

            return hand;
        }

        /// <summary><c>Grabs/&lt;источник&gt;/&lt;поза&gt;/&lt;аватар&gt;/&lt;сторона&gt;</c> без создания (та же база, что HandsPackPoseImporter.AlignTransform).</summary>
        public static Transform FindNamedAlign(Transform root, GripSourcePoint point, GripTargetProfile profile, UxrHandSide side)
        {
            Transform parent = root.Find("MeshContainer/Grabs") ?? root;
            return parent.Find($"{point.AlignSource}/{point.Pose}/{profile.AlignAvatarName}/{side}");
        }

        public static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static GripExistingAlign Existing(GripPlacement placement, Transform root, GripLocalPose align, Transform existing)
        {
            if (existing == null) return null;
            GripLocalPose pose  = GripLocalPose.Of(root, existing);
            GripDelta     delta = placement.Delta(align, pose);
            return new GripExistingAlign { Path = PathOf(existing, root), DeltaMm = delta.Mm, DeltaDeg = delta.Deg, Palm = placement.Evaluate(pose, 0f) };
        }

        /// <summary>R0: прямой вызов прежнего HandsPackGripAligner.Place на временном трансформе той же копии префаба.</summary>
        private static GripDelta AlignerDelta(GripPlacement placement, Transform root, Transform body, GripSample sample, UxrHandSide side, string rigPath, GripLocalPose align)
        {
            var probe = new GameObject("GripExtraction_AlignerProbe").transform;
            try
            {
                probe.SetParent(root, false);
                HandsPackGripAligner.Place(root, body, sample.Legacy, sample.Side, AssetDatabase.LoadAssetAtPath<GameObject>(rigPath), side, probe);
                return placement.Delta(align, GripLocalPose.Of(root, probe), "HandsPackGripAligner.Place");
            }
            catch (Exception e)
            {
                return new GripDelta { Path = "HandsPackGripAligner.Place", Error = e.Message, Mm = double.NaN, Deg = double.NaN };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
        }
    }
}
