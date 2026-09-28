using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Позы хвата сняты с рук пака Hands Weapons Animations, а не взяты из пресетов SDK.
    ///
    /// <para>
    /// Поза UltimateXR хранит повороты фаланг в универсальных осях ладони, поэтому одна поза
    /// годится любому аватару: SDK пересчитывает её под скелет в рантайме. Тест сам ставит кадр
    /// клипа на руки пака, накладывает сохранённую позу на аватар и сравнивает сгибы суставов —
    /// независимо от импортёра <c>HandsPackPoseImporter</c>.
    /// </para>
    ///
    /// <para>
    /// Большой палец сверяется грубее: SDK берёт для него оси указательного, и основание большого
    /// пальца на чужом скелете расходится на 10–15° (<c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c>).
    /// </para>
    ///
    /// <para>Новая поза из пака — добавь кейс в <see cref="Cases" />.</para>
    /// </summary>
    public class HandsPackHandPoseTests
    {
        private const string Pack       = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/";
        private const string PoseFolder = "Assets/Art/HandPoses/HandsPack/";
        private const string PoseBase   = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";

        private const float FingerTolerance = 5f;
        private const float ThumbTolerance  = 20f;
        private const float GripPositionTolerance = 0.005f;
        private const float GripAngleTolerance    = 3f;

        // Корпус оружия: меш пака, его кость, и тот же меш в префабе.
        private const string BodyMesh = "Base_mesh";
        private const string BodyBone = "Bn_Base";
        private const string BodyPath = "MeshContainer/Base";

        private static readonly string[] Avatars = { "Assets/Prefabs/Player/MEF_Base_Avatar.prefab" };

        public sealed class Case
        {
            public string      Pose;
            public string      Folder;
            public string      Mesh;
            public string      Clip;
            public float       Time;
            public UxrHandSide PackSide; // рука пака, с которой снята поза
            public string      Weapon;
            public int         GrabPoint;

            public override string ToString() => Pose;
        }

        public static IEnumerable<Case> Cases()
        {
            yield return new Case
            {
                Pose = "HandsPack_Gun_Grip", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Right, Weapon = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab", GrabPoint = 0
            };
            yield return new Case
            {
                Pose = "HandsPack_Gun_Support", Folder = "Hands_Gun", Mesh = "Gun_Mesh", Clip = "Aiming_Idle", Time = 0f,
                PackSide = UxrHandSide.Left, Weapon = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab", GrabPoint = 1
            };
        }

        public static IEnumerable<TestCaseData> PoseOnAvatarCases() =>
            from c in Cases()
            from a in Avatars
            select new TestCaseData(c, a).SetName($"{{m}}({c.Pose}, {System.IO.Path.GetFileNameWithoutExtension(a)})");

        [TestCaseSource(nameof(PoseOnAvatarCases))]
        public void Поза_повторяет_кадр_пака_на_обеих_руках(Case c, string avatarPath)
        {
            UxrHandPoseAsset pose = LoadPose(c);
            float[,] expected = PackBends(c);

            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath));
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                UxrAvatar avatar = instance.GetComponent<UxrAvatar>();

                // Рука пака и зеркальная ей: зеркало не меняет углов сгиба.
                foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    UxrHandDescriptor descriptor = side == UxrHandSide.Left ? pose.HandDescriptorLeft : pose.HandDescriptorRight;
                    UxrAvatarRig.UpdateHandUsingDescriptor(avatar, side, descriptor);
                    AssertBends(expected, Bends(avatar.GetHand(side)), $"{c.Pose}, {side}");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [TestCaseSource(nameof(Cases))]
        public void Поза_доступна_всем_игровым_аватарам(Case c)
        {
            UxrAvatar poseBase = AssetDatabase.LoadAssetAtPath<GameObject>(PoseBase).GetComponent<UxrAvatar>();

            Assert.That(poseBase.GetAllHandPoses().Select(p => p.name), Contains.Item(c.Pose),
                        "Поза не зарегистрирована на PlayerBase_NonSdkHands — аватары-наследники её не найдут по имени.");
        }

        [TestCaseSource(nameof(PoseOnAvatarCases))]
        public void Точка_хвата_оружия_использует_позу_пака(Case c, string avatarPath)
        {
            UxrAvatar avatar = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath).GetComponent<UxrAvatar>();
            UxrGrabbableObject grabbable = AssetDatabase.LoadAssetAtPath<GameObject>(c.Weapon).GetComponent<UxrGrabbableObject>();

            UxrGripPoseInfo grip = grabbable.GetGrabPoint(c.GrabPoint).GetGripPoseInfo(avatar);

            Assert.That(grip?.HandPose, Is.Not.Null, "У аватара нет позы для этой точки хвата.");
            Assert.That(grip.HandPose.name, Is.EqualTo(c.Pose));
        }

        /// <summary>
        /// Хват ставит оружие в ладонь так же, как в кадре пака: SDK совмещает трансформ выравнивания
        /// с граббером руки, и корпус оказывается там же относительно ладони (в универсальных осях
        /// кисти), что и корпус пака, — с поправкой на масштаб префаба. Вторая рука — зеркало.
        /// </summary>
        [TestCaseSource(nameof(PoseOnAvatarCases))]
        public void Оружие_лежит_в_ладони_как_в_паке(Case c, string avatarPath)
        {
            Matrix4x4 packBody = PackBodyInHand(c, out float packScale);

            GameObject weapon   = AssetDatabase.LoadAssetAtPath<GameObject>(c.Weapon);
            Transform  body     = weapon.transform.Find(BodyPath);
            float      rootScale = weapon.transform.localScale.x;
            Quaternion bodyRot  = Quaternion.Inverse(weapon.transform.rotation) * body.rotation;
            Vector3    bodyPos  = weapon.transform.InverseTransformPoint(body.position);
            float      k        = rootScale * body.lossyScale.x / weapon.transform.lossyScale.x / packScale;

            UxrAvatar prefabAvatar = AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath).GetComponent<UxrAvatar>();
            UxrGripPoseInfo grip = weapon.GetComponent<UxrGrabbableObject>().GetGrabPoint(c.GrabPoint).GetGripPoseInfo(prefabAvatar);

            var instance = (GameObject)Object.Instantiate(prefabAvatar.gameObject);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                UxrAvatar avatar = instance.GetComponent<UxrAvatar>();
                var errors = new List<string>();

                foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    Transform align = side == UxrHandSide.Left ? grip.GripAlignTransformHandLeft : grip.GripAlignTransformHandRight;
                    if (align == null)
                    {
                        errors.Add($"{side}: нет трансформа выравнивания");
                        continue;
                    }

                    UxrGrabber grabber = instance.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == side);
                    Quaternion alignRot = Quaternion.Inverse(weapon.transform.rotation) * align.rotation;
                    Vector3    alignPos = weapon.transform.InverseTransformPoint(align.position);

                    // Snap ObjectToHand: трансформ выравнивания совпадает с граббером.
                    Quaternion rootRot = grabber.transform.rotation * Quaternion.Inverse(alignRot);
                    Vector3    rootPos = grabber.transform.position - rootRot * (alignPos * rootScale);
                    Quaternion worldBodyRot = rootRot * bodyRot;
                    Vector3    worldBodyPos = rootPos + rootRot * (bodyPos * rootScale);

                    Transform  wrist = avatar.GetHand(side).Wrist;
                    Quaternion hand  = avatar.AvatarRigInfo.GetArmInfo(side).HandUniversalLocalAxes.UniversalRotation;
                    Vector3    actualPos = Quaternion.Inverse(hand) * (worldBodyPos - wrist.position);
                    Quaternion actualRot = Quaternion.Inverse(hand) * worldBodyRot;

                    Vector3    expectedPos = (Vector3)packBody.GetColumn(3) * k;
                    Quaternion expectedRot = packBody.rotation;
                    if (side != c.PackSide)
                    {
                        // Зеркало по оси «вправо» кисти и по боковой оси оружия (X корпуса).
                        expectedPos.x = -expectedPos.x;
                        expectedRot   = new Quaternion(expectedRot.x, -expectedRot.y, -expectedRot.z, expectedRot.w);
                    }

                    float distance = Vector3.Distance(actualPos, expectedPos);
                    float angle    = Quaternion.Angle(actualRot, expectedRot);
                    if (distance > GripPositionTolerance || angle > GripAngleTolerance)
                    {
                        errors.Add($"{side}: корпус смещён на {distance * 1000f:F0} мм и повёрнут на {angle:F0}° относительно кадра пака");
                    }
                }

                Assert.That(errors, Is.Empty, $"{c.Pose}: оружие не в ладони как в паке\n{string.Join("\n", errors)}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Корпус оружия пака в универсальных осях ладони на кадре клипа; масштаб корпуса пака — в <paramref name="scale" />.</summary>
        private static Matrix4x4 PackBodyInHand(Case c, out float scale)
        {
            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}{c.Folder}/Mesh/{c.Mesh}.FBX"));
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                UxrAvatarHand hand = PackHand(instance, c.PackSide);
                PackHandAxes(hand, c.PackSide, out Quaternion wristToUniversal);

                LoadClip(c).SampleAnimation(instance, c.Time);

                SkinnedMeshRenderer bodyRenderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.sharedMesh.name == BodyMesh);
                int bone = System.Array.FindIndex(bodyRenderer.bones, b => b.name == BodyBone);
                Matrix4x4 body = bodyRenderer.bones[bone].localToWorldMatrix * bodyRenderer.sharedMesh.bindposes[bone];

                Matrix4x4 handFrame = Matrix4x4.TRS(hand.Wrist.position, hand.Wrist.rotation * wristToUniversal, Vector3.one);
                Matrix4x4 relative  = handFrame.inverse * body;
                scale = relative.lossyScale.x;
                return Matrix4x4.TRS(relative.GetColumn(3), relative.rotation, Vector3.one);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static AnimationClip LoadClip(Case c) =>
            AssetDatabase.LoadAllAssetsAtPath($"{Pack}{c.Folder}/Animations/{c.Folder}@{c.Clip}.FBX")
                         .OfType<AnimationClip>().First(a => !a.name.StartsWith("__"));

        private static UxrAvatarHand PackHand(GameObject instance, UxrHandSide side)
        {
            Dictionary<string, Transform> bones = instance.GetComponentsInChildren<Transform>(true)
                                                          .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            string s = side == UxrHandSide.Left ? "L" : "R";

            var hand = new UxrAvatarHand { Wrist = bones[$"Character001{s}ArmPalm"] };
            UxrAvatarFinger[] fingers = Fingers(hand);
            for (int f = 0; f < fingers.Length; f++)
            {
                fingers[f].SetupFingerBones(Enumerable.Range(1, 3).Select(n => bones[$"Character001{s}ArmDigit{f + 1}{n}"]).ToList());
            }

            return hand;
        }

        /// <summary>
        /// Универсальные оси ладони по bind-позе — как <c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c>:
        /// «вправо» — от указательного к среднему со знаком стороны (округлено до оси), «вперёд» — к
        /// середине между средним и безымянным. Возвращает поворот ладони; поправку запястье → оси — в out.
        /// </summary>
        private static Quaternion PackHandAxes(UxrAvatarHand hand, UxrHandSide side, out Quaternion wristToUniversal)
        {
            Transform wrist   = hand.Wrist;
            Vector3   across  = wrist.InverseTransformDirection(hand.Index.Proximal.position - hand.Middle.Proximal.position);
            Vector3   right   = ClosestAxis(across) * (side == UxrHandSide.Left ? 1f : -1f);
            Vector3   forward = wrist.InverseTransformDirection((Vector3.Lerp(hand.Ring.Proximal.position, hand.Middle.Proximal.position, 0.5f) - wrist.position).normalized);
            Vector3   up      = Vector3.Cross(forward, right).normalized;

            wristToUniversal = Quaternion.LookRotation(forward, up);
            return wrist.rotation * wristToUniversal;
        }

        private static Vector3 ClosestAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (a.y >= a.z) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        private static UxrHandPoseAsset LoadPose(Case c)
        {
            var pose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>($"{PoseFolder}{c.Pose}.asset");
            Assert.That(pose, Is.Not.Null, $"Нет ассета позы {PoseFolder}{c.Pose}.asset");
            Assert.That(pose.PoseType, Is.EqualTo(UxrHandPoseType.Fixed));
            return pose;
        }

        /// <summary>Сгибы суставов руки пака на кадре клипа.</summary>
        private static float[,] PackBends(Case c)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}{c.Folder}/Mesh/{c.Mesh}.FBX");
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath($"{Pack}{c.Folder}/Animations/{c.Folder}@{c.Clip}.FBX")
                                              .OfType<AnimationClip>().First(a => !a.name.StartsWith("__"));

            var instance = (GameObject)Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                clip.SampleAnimation(instance, c.Time);
                Dictionary<string, Transform> bones = instance.GetComponentsInChildren<Transform>(true)
                                                              .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                string s = c.PackSide == UxrHandSide.Left ? "L" : "R";

                var hand = new UxrAvatarHand { Wrist = bones[$"Character001{s}ArmPalm"] };
                UxrAvatarFinger[] fingers = Fingers(hand);
                for (int f = 0; f < fingers.Length; f++)
                {
                    fingers[f].SetupFingerBones(Enumerable.Range(1, 3).Select(n => bones[$"Character001{s}ArmDigit{f + 1}{n}"]).ToList());
                }

                return Bends(hand);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// [палец, сустав]: угол в пястно-фаланговом (запястье→основная фаланга) и межфаланговом
        /// суставах. Считается по позициям костей, поэтому не зависит от осей рига.
        /// </summary>
        private static float[,] Bends(UxrAvatarHand hand)
        {
            UxrAvatarFinger[] fingers = Fingers(hand);
            var bends = new float[fingers.Length, 2];

            for (int f = 0; f < fingers.Length; f++)
            {
                UxrAvatarFinger finger = fingers[f];
                Vector3 toProximal     = finger.Proximal.position - hand.Wrist.position;
                Vector3 proximal       = finger.Intermediate.position - finger.Proximal.position;
                Vector3 intermediate   = finger.Distal.position - finger.Intermediate.position;

                bends[f, 0] = Vector3.Angle(toProximal, proximal);
                bends[f, 1] = Vector3.Angle(proximal, intermediate);
            }

            return bends;
        }

        private static UxrAvatarFinger[] Fingers(UxrAvatarHand hand) => new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };

        private static void AssertBends(float[,] expected, float[,] actual, string context)
        {
            string[] names = { "большой", "указательный", "средний", "безымянный", "мизинец" };
            var errors = new List<string>();

            for (int f = 0; f < names.Length; f++)
            {
                float tolerance = f == 0 ? ThumbTolerance : FingerTolerance;
                for (int j = 0; j < 2; j++)
                {
                    if (Mathf.Abs(expected[f, j] - actual[f, j]) > tolerance)
                    {
                        errors.Add($"{names[f]} сустав {j}: пак {expected[f, j]:F0}°, аватар {actual[f, j]:F0}°");
                    }
                }
            }

            Assert.That(errors, Is.Empty, $"{context}: сгибы расходятся с кадром пака больше допуска\n{string.Join("\n", errors)}");
        }
    }
}
