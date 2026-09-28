using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Core.Math;
using UltimateXR.Extensions.Unity.Math;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Снимает позу кисти UltimateXR с рук пака Hands Weapons Animations (риг 3ds Max CAT:
    /// <c>Character001{R,L}ArmPalm</c> → <c>…ArmDigit{палец}{фаланга}</c>, три фаланги, без пястных костей).
    ///
    /// <para>
    /// Результат нейтрален к скелету: <see cref="UxrHandDescriptor" /> хранит повороты фаланг в
    /// универсальных осях ладони, и SDK пересчитывает его под скелет каждого аватара в рантайме
    /// (<c>UxrRuntimeFingerDescriptor</c>). Оси рига пака считаются по bind-позе той же математикой,
    /// что <c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c> (там она приватная).
    /// </para>
    /// </summary>
    public static class HandsPackPoseExtractor
    {
        /// <summary>Кадр пака для одной руки.</summary>
        public sealed class Sample
        {
            /// <summary>Поза пальцев.</summary>
            public UxrHandDescriptor Hand;

            /// <summary>Корпус оружия в универсальных осях ладони (запястье — начало координат), без масштаба.</summary>
            public Vector3 BodyPosition;
            public Quaternion BodyRotation;

            /// <summary>Масштаб корпуса пака в тех же осях — делитель для перевода в масштаб префаба.</summary>
            public float BodyScale;
        }

        /// <param name="model">Меш рук пака (<c>Modelas/&lt;папка&gt;/Mesh/*.FBX</c>)</param>
        /// <param name="clip">Клип пака; его кадр задаёт позу</param>
        /// <param name="time">Время кадра, с</param>
        /// <param name="side">Рука пака, с которой снимается поза</param>
        /// <param name="bodyMesh">Меш корпуса оружия в паке</param>
        /// <param name="bodyBone">Кость, к которой привязан корпус</param>
        public static Sample Extract(GameObject model, AnimationClip clip, float time, UxrHandSide side, string bodyMesh, string bodyBone)
        {
            var instance = Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                Dictionary<string, Transform> bones = instance.GetComponentsInChildren<Transform>(true)
                                                              .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                UxrAvatarArm arm = BuildArm(bones, side);

                // Оси — по bind-позе, до кадра клипа: так же SDK считает их у аватара.
                SolveAxes(arm.Hand, side, out UxrUniversalLocalAxes handAxes, out UxrUniversalLocalAxes fingerAxes);

                clip.SampleAnimation(instance, time);

                // Место корпуса — кость × bindpose (трансформ рендерера с ним расходится).
                SkinnedMeshRenderer body = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh && r.sharedMesh.name == bodyMesh)
                                           ?? throw new KeyNotFoundException($"В модели пака нет меша {bodyMesh}");
                int bone = System.Array.FindIndex(body.bones, b => b.name == bodyBone);
                if (bone < 0)
                {
                    throw new KeyNotFoundException($"Меш {bodyMesh} не привязан к кости {bodyBone}");
                }

                Matrix4x4 hand     = Matrix4x4.TRS(arm.Hand.Wrist.position, handAxes.UniversalRotation, Vector3.one);
                Matrix4x4 relative = hand.inverse * body.bones[bone].localToWorldMatrix * body.sharedMesh.bindposes[bone];

                return new Sample
                {
                    Hand         = new UxrHandDescriptor(arm, handAxes, fingerAxes),
                    BodyPosition = relative.GetColumn(3),
                    BodyRotation = relative.rotation,
                    BodyScale    = relative.lossyScale.x
                };
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static UxrAvatarArm BuildArm(Dictionary<string, Transform> bones, UxrHandSide side)
        {
            string s   = side == UxrHandSide.Left ? "L" : "R";
            var    arm = new UxrAvatarArm();
            arm.Hand.Wrist = Bone(bones, $"Character001{s}ArmPalm");

            // Digit1x — большой палец … Digit5x — мизинец.
            UxrAvatarFinger[] fingers = { arm.Hand.Thumb, arm.Hand.Index, arm.Hand.Middle, arm.Hand.Ring, arm.Hand.Little };
            for (int f = 0; f < fingers.Length; f++)
            {
                fingers[f].SetupFingerBones(Enumerable.Range(1, 3).Select(n => Bone(bones, $"Character001{s}ArmDigit{f + 1}{n}")).ToList());
            }

            return arm;
        }

        private static Transform Bone(Dictionary<string, Transform> bones, string name)
        {
            if (!bones.TryGetValue(name, out Transform bone))
            {
                throw new KeyNotFoundException($"В модели пака нет кости {name} — это не риг CAT пака Hands Weapons Animations.");
            }

            return bone;
        }

        private static void SolveAxes(UxrAvatarHand hand, UxrHandSide side, out UxrUniversalLocalAxes handAxes, out UxrUniversalLocalAxes fingerAxes)
        {
            Transform indexProximal  = hand.Index.Proximal;
            Transform indexDistal    = hand.Index.Distal;
            Transform middleProximal = hand.Middle.Proximal;
            Transform ringProximal   = hand.Ring.Proximal;
            float     sign           = side == UxrHandSide.Left ? 1.0f : -1.0f;

            Vector3 handRight   = hand.Wrist.InverseTransformDirection(indexProximal.position - middleProximal.position).GetClosestAxis() * sign;
            Vector3 handForward = hand.Wrist.InverseTransformDirection((Vector3.Lerp(ringProximal.position, middleProximal.position, 0.5f) - hand.Wrist.position).normalized);
            Vector3 handUp      = Vector3.Cross(handForward, handRight).normalized;
            handRight = Vector3.Cross(handUp, handForward).normalized;
            handAxes  = UxrUniversalLocalAxes.FromAxes(hand.Wrist, handRight, handUp, handForward);

            Vector3 fingerRight   = indexProximal.InverseTransformDirection(indexProximal.position - middleProximal.position).GetClosestAxis() * sign;
            Vector3 fingerForward = indexProximal.InverseTransformDirection(indexDistal.position - indexProximal.position).GetClosestAxis();
            Vector3 fingerUp      = Vector3.Cross(fingerForward, fingerRight);
            fingerAxes = UxrUniversalLocalAxes.FromAxes(indexProximal, fingerRight, fingerUp, fingerForward);
        }
    }
}
