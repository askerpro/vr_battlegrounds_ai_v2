using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Ставит трансформы выравнивания хвата так, чтобы оружие легло в ладонь аватара как в кадре пака.
    ///
    /// <para>
    /// SDK при хвате (snap ObjectToHand, PositionAndRotation) совмещает трансформ выравнивания с
    /// <see cref="UxrGrabber" /> руки. Значит, трансформ — это граббер, пересчитанный в систему корня
    /// оружия, когда корпус стоит относительно ладони (универсальные оси кисти) там же, где корпус пака.
    /// Сдвиг масштабируется отношением размеров корпуса префаба и пака (пак крупнее реального оружия ~×1.4).
    /// Вторая рука — зеркало по оси «вправо» кисти и по боковой оси оружия (X корпуса).
    /// </para>
    /// </summary>
    public static class HandsPackGripAligner
    {
        /// <param name="weaponRoot">Корень префаба оружия (содержимое, открытое на редактирование)</param>
        /// <param name="body">Корпус оружия в префабе — тот же меш, что корпус пака</param>
        /// <param name="sample">Кадр пака</param>
        /// <param name="packSide">Рука пака, с которой снят кадр</param>
        /// <param name="avatarPrefab">Аватар, для которого считается хват</param>
        /// <param name="side">Рука аватара</param>
        /// <param name="align">Трансформ выравнивания; его мировая поза перезаписывается</param>
        public static void Place(Transform weaponRoot, Transform body, HandsPackPoseExtractor.Sample sample, UxrHandSide packSide,
                                 GameObject avatarPrefab, UxrHandSide side, Transform align)
        {
            float      rootScale = weaponRoot.lossyScale.x;
            Vector3    bodyPos   = weaponRoot.InverseTransformPoint(body.position);
            Quaternion bodyRot   = Quaternion.Inverse(weaponRoot.rotation) * body.rotation;
            float      k         = body.lossyScale.x / sample.BodyScale;

            Vector3    handBodyPos = sample.BodyPosition * k;
            Quaternion handBodyRot = sample.BodyRotation;
            if (side != packSide)
            {
                handBodyPos.x = -handBodyPos.x;
                handBodyRot   = new Quaternion(handBodyRot.x, -handBodyRot.y, -handBodyRot.z, handBodyRot.w);
            }

            var instance = Object.Instantiate(avatarPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                UxrAvatar  avatar  = instance.GetComponent<UxrAvatar>();
                Transform  wrist   = avatar.GetHand(side).Wrist;
                Quaternion hand    = avatar.AvatarRigInfo.GetArmInfo(side).HandUniversalLocalAxes.UniversalRotation;
                Transform  grabber = instance.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == side).transform;

                // Корень оружия, когда корпус лежит в ладони как в паке.
                Quaternion worldBodyRot = hand * handBodyRot;
                Vector3    worldBodyPos = wrist.position + hand * handBodyPos;
                Quaternion rootRot      = worldBodyRot * Quaternion.Inverse(bodyRot);
                Vector3    rootPos      = worldBodyPos - rootRot * (bodyPos * rootScale);

                // Граббер в системе этого корня → та же поза относительно настоящего корня.
                Vector3    localPos = Quaternion.Inverse(rootRot) * (grabber.position - rootPos) / rootScale;
                Quaternion localRot = Quaternion.Inverse(rootRot) * grabber.rotation;
                align.SetPositionAndRotation(weaponRoot.TransformPoint(localPos), weaponRoot.rotation * localRot);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
