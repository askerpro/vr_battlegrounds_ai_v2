using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Компактные мировые позы аватара бота для сверки «сервер ↔ клиент» на одной серверной метке выстрела (этап
    /// pf-network): корень, камера, запястья (<c>Avatar.Left/RightHandBone</c>), локальные позы запястий относительно
    /// родителя (предплечья) и список NetworkTransform тела. Поза — массив [px, py, pz, qx, qy, qz, qw]. Только чтение.
    /// </summary>
    public static class BotStandNetPoses
    {
        public static float[] Pose(Transform transform)
        {
            if (transform == null) return null;
            Vector3 p = transform.position;
            Quaternion q = transform.rotation;
            return new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
        }

        public static float[] Pose(Vector3 p, Quaternion q) => new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w };

        public static float[] LocalPose(Transform transform)
        {
            if (transform == null) return null;
            Vector3 p = transform.localPosition;
            Quaternion q = transform.localRotation;
            return new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
        }

        /// <summary>Корень, камера, оба запястья (мир) и запястья относительно родителя (предплечье/твист).</summary>
        public static Dictionary<string, object> Rig(UxrAvatar avatar)
        {
            if (avatar == null) return null;
            Transform left = avatar.GetHandBone(UxrHandSide.Left);
            Transform right = avatar.GetHandBone(UxrHandSide.Right);
            return new Dictionary<string, object>
            {
                ["root"] = Pose(avatar.transform),
                ["camera"] = Pose(avatar.CameraTransform),
                ["wristL"] = Pose(left),
                ["wristR"] = Pose(right),
                ["wristLLocal"] = LocalPose(left),
                ["wristRLocal"] = LocalPose(right)
            };
        }

        /// <summary>
        /// NetworkTransform тела: цель, пространство, направление, кратность отправки; и чем являются кисти аватара
        /// (<c>GetHandBone</c>, <c>GetHand().Wrist</c>, родитель) — чтобы видеть, синхронизируется ли именно кость,
        /// которую пишет драйвер.
        /// </summary>
        public static Dictionary<string, object> Inventory(UxrAvatar avatar)
        {
            if (avatar == null) return null;
            var transforms = new List<object>();
            foreach (NetworkTransformBase nt in avatar.GetComponentsInChildren<NetworkTransformBase>(true))
            {
                if (nt == null) continue;
                Transform target = nt.target != null ? nt.target : nt.transform;
                transforms.Add(new Dictionary<string, object>
                {
                    ["type"] = nt.GetType().Name, ["host"] = nt.gameObject.name, ["target"] = target.name,
                    ["targetActive"] = target.gameObject.activeInHierarchy, ["enabled"] = nt.enabled,
                    ["coordinateSpace"] = nt.coordinateSpace.ToString(), ["syncDirection"] = nt.syncDirection.ToString(),
                    ["sendIntervalMultiplier"] = nt.sendIntervalMultiplier, ["timelineOffset"] = nt.timelineOffset,
                    ["isLeftHandBone"] = target == avatar.GetHandBone(UxrHandSide.Left),
                    ["isRightHandBone"] = target == avatar.GetHandBone(UxrHandSide.Right),
                    ["isCamera"] = target == avatar.CameraTransform, ["isRoot"] = target == avatar.transform
                });
            }
            return new Dictionary<string, object>
            {
                ["mode"] = avatar.AvatarMode.ToString(),
                ["networkTransforms"] = transforms,
                ["hands"] = new Dictionary<string, object> { ["left"] = Hand(avatar, UxrHandSide.Left), ["right"] = Hand(avatar, UxrHandSide.Right) }
            };
        }

        private static Dictionary<string, object> Hand(UxrAvatar avatar, UxrHandSide side)
        {
            Transform bone = avatar.GetHandBone(side);
            UxrAvatarHand hand = avatar.GetHand(side);
            UxrAvatarArm arm = avatar.GetArm(side);
            return new Dictionary<string, object>
            {
                ["handBone"] = bone != null ? bone.name : null,
                ["handBoneParent"] = bone != null && bone.parent != null ? bone.parent.name : null,
                ["wrist"] = hand != null && hand.Wrist != null ? hand.Wrist.name : null,
                ["forearm"] = arm != null && arm.Forearm != null ? arm.Forearm.name : null
            };
        }

        /// <summary>Расстояние между позициями двух поз, мм, и угол между поворотами, градусы.</summary>
        public static void Delta(float[] a, float[] b, out float mm, out float degrees)
        {
            mm = degrees = float.NaN;
            if (a == null || b == null) return;
            mm = (new Vector3(a[0], a[1], a[2]) - new Vector3(b[0], b[1], b[2])).magnitude * 1000f;
            degrees = Quaternion.Angle(new Quaternion(a[3], a[4], a[5], a[6]), new Quaternion(b[3], b[4], b[5], b[6]));
        }
    }
}
