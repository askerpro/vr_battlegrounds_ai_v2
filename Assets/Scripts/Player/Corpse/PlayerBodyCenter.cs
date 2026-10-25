using UltimateXR.Avatar;
using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Где тело игрока (T-35): от него толкает взрыв, разлетается снаряжение и падает труп.
    ///
    /// <para>
    /// <b>Не корень аватара.</b> При физической ходьбе корень стоит на месте калибровки или последнего
    /// телепорта, по залу идёт голова, тело за ней строит IK — в бою корень и тело расходятся на метры
    /// (так же было у ботов, <c>BotSenses.FeetOf</c>). Центр тела — таз модели, с которой снимается поза
    /// трупа (<see cref="CorpseSource.ModelRoot"/>), или таз рига UltimateXR. Без таза — под головой на
    /// высоте <see cref="FallbackHeight"/> над полом аватара; без головы — корень + <see cref="FallbackHeight"/>.
    /// </para>
    /// </summary>
    public static class PlayerBodyCenter
    {
        /// <summary>Высота центра тела над полом, когда таза нет.</summary>
        public const float FallbackHeight = 1f;

        /// <summary>Центр тела игрока в мире.</summary>
        public static Vector3 Of(PlayerController player)
        {
            if (player == null) return Vector3.zero;

            Transform hips = HipsOf(player);
            if (hips != null) return hips.position;

            UxrAvatar avatar = player.GetComponent<UxrAvatar>();
            if (avatar != null && avatar.CameraTransform != null)
                return avatar.CameraFloorPosition + Vector3.up * FallbackHeight;

            return player.transform.position + Vector3.up * FallbackHeight;
        }

        /// <summary>Таз модели игрока; null — модели или гуманоидного таза нет (призрак, тест).</summary>
        public static Transform HipsOf(PlayerController player)
        {
            if (player == null) return null;

            CorpseSource source = player.GetComponent<CorpseSource>();
            Animator animator = source != null && source.ModelRoot != null ? source.ModelRoot.GetComponent<Animator>() : null;
            if (animator != null && animator.isHuman)
            {
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hips != null) return hips;
            }

            UxrAvatar avatar = player.GetComponent<UxrAvatar>();
            return avatar != null && avatar.AvatarRig != null ? avatar.AvatarRig.Hips : null;
        }

        /// <summary>Голова игрока (камера аватара, иначе кость головы рига); false — головы нет.</summary>
        public static bool TryGetHead(PlayerController player, out Vector3 head)
        {
            head = Vector3.zero;
            if (player == null) return false;

            UxrAvatar avatar = player.GetComponent<UxrAvatar>();
            if (avatar == null) return false;

            if (avatar.CameraTransform != null)
            {
                head = avatar.CameraPosition;
                return true;
            }

            Transform bone = avatar.AvatarRig != null && avatar.AvatarRig.Head != null ? avatar.AvatarRig.Head.Head : null;
            if (bone == null) return false;
            head = bone.position;
            return true;
        }
    }
}
