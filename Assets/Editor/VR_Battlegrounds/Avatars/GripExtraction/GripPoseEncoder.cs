using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Поза пальцев на риге цели и её проверки. Кодирование <see cref="GripPoseEncoding.TargetRigWorld" /> переносит
    /// рабочий драйвер reports/cyborg-grips/tools/pose-retarget.cs.txt: исходная поза кисти → мировой путь
    /// (<c>UpdateHandUsingDescriptor</c>) → curl доводки → <c>new UxrHandDescriptor(avatar, side)</c> — с пястными, если
    /// они есть у цели. Тогда рантайм-путь SDK (<c>UxrRuntimeFingerDescriptor</c>) совпадает с редакторным.
    /// </summary>
    public static class GripPoseEncoder
    {
        public static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "little" };

        /// <param name="curlDeg">Curl доводки по пальцам (thumb..little), градусы; правило SDK CurlFinger. null — без curl.</param>
        public static UxrHandDescriptor Encode(GripTargetRig rig, UxrHandSide side, UxrHandDescriptor source, GripPoseEncoding encoding,
                                               IReadOnlyDictionary<string, float> curlDeg)
        {
            bool curl = curlDeg != null && curlDeg.Values.Any(v => Mathf.Abs(v) > 1e-4f);
            if (encoding == GripPoseEncoding.AsSource)
            {
                if (curl)
                {
                    throw new ArgumentException("Curl пальцев пишется только кодированием на риге цели (encoding=rig).");
                }

                return source;
            }

            rig.RestoreBind();
            try
            {
                rig.Apply(side, source);
                if (curl)
                {
                    ApplyCurl(rig, side, curlDeg);
                }

                return new UxrHandDescriptor(rig.Avatar, side);
            }
            finally
            {
                rig.RestoreBind();
            }
        }

        /// <summary>Curl на костях рига — то же правило, что предпросмотр правки Hand Pose Fit (<see cref="FitFingerChain.ApplyCurl" />).</summary>
        public static void ApplyCurl(GripTargetRig rig, UxrHandSide side, IReadOnlyDictionary<string, float> curlDeg)
        {
            UxrAvatarHand         hand    = rig.Hand(side);
            UxrUniversalLocalAxes axes    = rig.Avatar.AvatarRigInfo.GetArmInfo(side).FingerUniversalLocalAxes;
            UxrAvatarFinger[]     fingers = { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };
            foreach (KeyValuePair<string, float> pair in curlDeg)
            {
                int index = Array.IndexOf(FingerNames, pair.Key);
                if (index < 0)
                {
                    throw new ArgumentException($"Неизвестный палец: {pair.Key} (есть {string.Join(", ", FingerNames)}).");
                }

                if (float.IsNaN(pair.Value) || Mathf.Abs(pair.Value) > 120f)
                {
                    throw new ArgumentException($"Curl пальца {pair.Key} вне [-120; 120]°.");
                }

                FitFingerChain.ApplyCurl(fingers[index], pair.Key, axes, pair.Value);
            }
        }

        /// <summary>
        /// Рантайм-путь против мирового (как pose-retarget): максимум угла между мировыми поворотами костей пальцев,
        /// градусы. Ноль — игра покажет ту же позу, что редактор.
        /// </summary>
        public static float RuntimeVsWorldDeg(GripTargetRig rig, UxrHandSide side, UxrHandPoseAsset asset)
        {
            List<Transform> bones = rig.FingerBones(side);
            rig.RestoreBind();
            rig.Apply(side, Descriptor(asset, side));
            Quaternion[] world = bones.Select(b => b.rotation).ToArray();

            rig.RestoreBind();
            UxrBlendPoseType blend   = asset.PoseType == UxrHandPoseType.Fixed ? UxrBlendPoseType.None : UxrBlendPoseType.OpenGrip;
            var              runtime = new UxrRuntimeHandDescriptor(rig.Avatar, side, asset, asset.PoseType, blend);
            UxrAvatarRig.UpdateHandUsingRuntimeDescriptor(rig.Avatar, side, runtime);
            float max = MaxAngle(world, bones);
            rig.RestoreBind();
            return max;
        }

        /// <summary>Форма: мировые повороты костей пальцев двух дескрипторов (оба мировым путём), градусы.</summary>
        public static float ShapeDeg(GripTargetRig rig, UxrHandSide side, UxrHandDescriptor a, UxrHandDescriptor b)
        {
            List<Transform> bones = rig.FingerBones(side);
            rig.RestoreBind();
            rig.Apply(side, a);
            Quaternion[] world = bones.Select(t => t.rotation).ToArray();

            rig.RestoreBind();
            rig.Apply(side, b);
            float max = MaxAngle(world, bones);
            rig.RestoreBind();
            return max;
        }

        /// <summary>Дескриптор руки ассета: Fixed — единственный, Blend — открытый.</summary>
        public static UxrHandDescriptor Descriptor(UxrHandPoseAsset asset, UxrHandSide side) =>
            asset.PoseType == UxrHandPoseType.Fixed ? asset.GetHandDescriptor(side) : asset.GetHandDescriptor(side, UxrBlendPoseType.OpenGrip);

        /// <summary>Пястная информация у index, middle, ring, little.</summary>
        public static bool[] MetacarpalInfo(UxrHandDescriptor descriptor) =>
            new[] { descriptor.Index.HasMetacarpalInfo, descriptor.Middle.HasMetacarpalInfo, descriptor.Ring.HasMetacarpalInfo, descriptor.Little.HasMetacarpalInfo };

        private static float MaxAngle(Quaternion[] world, List<Transform> bones)
        {
            float max = 0f;
            for (int i = 0; i < bones.Count; i++)
            {
                max = Mathf.Max(max, Quaternion.Angle(world[i], bones[i].rotation));
            }

            return max;
        }
    }
}
