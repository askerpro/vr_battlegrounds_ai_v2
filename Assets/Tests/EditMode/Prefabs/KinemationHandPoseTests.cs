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
    /// Позы хвата стволов KINEMATION (T-39) сняты с рук пака: кадр клипа <c>A_FP_*_Idle</c> на скелете UE5-манекена
    /// (<c>SKM_Operator</c>), оружие на кости <c>ik_hand_gun</c> с поворотом <c>weaponRotationOffset</c> из настроек
    /// ствола. Тест сам ставит кадр и сравнивает с ним позу на аватаре и место оружия в ладони — как
    /// <see cref="HandsPackHandPoseTests" /> для пака Hands, независимо от импортёра.
    /// </summary>
    public class KinemationHandPoseTests
    {
        private const string Pack       = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/";
        private const string Character  = Pack + "Meshes/Character/Operator/SKM_Operator.fbx";
        private const string PoseFolder = "Assets/Art/HandPoses/Kinemation/";
        private const string PoseBase   = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";

        private const float FingerTolerance = 5f;
        private const float ThumbTolerance  = 20f;
        private const float GripPositionTolerance = 0.005f;
        private const float GripAngleTolerance    = 3f;

        private static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

        public sealed class Case
        {
            public string      Pose;
            public string      AnimFolder;
            public string      Clip;
            public string      PackPrefab;
            public string      RestClip;   // клип покоя оружия (A_W_*_Idle)
            public UxrHandSide PackSide;
            public string      Weapon;
            public int         GrabPoint;
            public string      BodyBone = "Body";
            public string      BodyPath = "MeshContainer/Base";

            public override string ToString() => Pose;
        }

        private static IEnumerable<Case> Pair(string name, string animFolder, string clip, string packPrefab, string restClip)
        {
            string weapon = $"Assets/Prefabs/Weapons/{name}/{name}.prefab";
            yield return new Case { Pose = $"Kinemation_{name}_Grip", AnimFolder = animFolder, Clip = clip, PackPrefab = packPrefab, RestClip = restClip,
                                    PackSide = UxrHandSide.Right, Weapon = weapon, GrabPoint = 0 };
            yield return new Case { Pose = $"Kinemation_{name}_Support", AnimFolder = animFolder, Clip = clip, PackPrefab = packPrefab, RestClip = restClip,
                                    PackSide = UxrHandSide.Left, Weapon = weapon, GrabPoint = 1 };
        }

        public static IEnumerable<Case> Cases() =>
            Pair("SRM12", "SRM-12", "A_FP_SRM-12_Idle_Pose", "W_Oryx_SRM-12", "A_W_SRM-12_Idle")
                .Concat(Pair("Mk14", "Mk14EBR", "A_FP_Mk14EBR_Idle", "W_Mk14EBR", null))
                .Concat(Pair("AK105", "AK105", "A_FP_AK105_Idle", "W_AK105", "A_W_AK105_Idle"))
                .Concat(Pair("MKR9", "MKR9", "A_FP_MKR9_Idle", "W_MKR9", "A_W_MKR9_Idle"))
                .Concat(Pair("Viper", "WK-11_Viper", "A_FP_WK-11_Viper_Idle_Pose", "W_WK-11_Viper", "A_W_WK-11_Viper_Idle"))
                .Concat(Pair("R08", "R08", "A_FP_R08_Idle", "W_R08", "A_W_R08_Idle"))
                .Concat(Pair("Herrington", "Herrington_11-87", "A_FP_Herrington_11-87_Idle", "W_Herrington_11-87_Police", "A_W_Herrington_11-87_Idle"));

        private static IEnumerable<string> Avatars =>
            RegisteredAvatars.Prefabs()
                             .Where(p => InheritsFrom(p, PoseBase))
                             .Select(AssetDatabase.GetAssetPath)
                             .OrderBy(p => p);

        private static bool InheritsFrom(GameObject prefab, string basePath)
        {
            for (GameObject current = prefab; current != null; current = PrefabUtility.GetCorrespondingObjectFromSource(current))
            {
                if (AssetDatabase.GetAssetPath(current) == basePath) return true;
            }
            return false;
        }

        public static IEnumerable<TestCaseData> PoseOnAvatarCases() =>
            from c in Cases()
            from a in Avatars
            select new TestCaseData(c, a).SetName($"{{m}}({c.Pose}, {System.IO.Path.GetFileNameWithoutExtension(a)})");

        [TestCaseSource(nameof(PoseOnAvatarCases))]
        public void Поза_повторяет_кадр_пака_на_обеих_руках(Case c, string avatarPath)
        {
            var pose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>($"{PoseFolder}{c.Pose}.asset");
            Assert.That(pose, Is.Not.Null, $"Нет ассета позы {PoseFolder}{c.Pose}.asset");
            float[,] expected = PackBends(c);

            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath));
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                UxrAvatar avatar = instance.GetComponent<UxrAvatar>();
                foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
                {
                    UxrAvatarRig.UpdateHandUsingDescriptor(avatar, side, side == UxrHandSide.Left ? pose.HandDescriptorLeft : pose.HandDescriptorRight);
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
            var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(c.Weapon);
            Assert.IsNotNull(weapon, $"Нет префаба {c.Weapon}");
            UxrGripPoseInfo grip = weapon.GetComponent<UxrGrabbableObject>().GetGrabPoint(c.GrabPoint).GetGripPoseInfo(avatar);

            Assert.That(grip?.HandPose, Is.Not.Null, "У аватара нет позы для этой точки хвата.");
            Assert.That(grip.HandPose.name, Is.EqualTo(c.Pose));
        }

        /// <summary>Хват ставит корпус оружия относительно ладони (универсальные оси кисти) так же, как в кадре пака.</summary>
        [TestCaseSource(nameof(PoseOnAvatarCases))]
        public void Оружие_лежит_в_ладони_как_в_паке(Case c, string avatarPath)
        {
            Matrix4x4 packBody = PackBodyInHand(c);

            GameObject weapon   = AssetDatabase.LoadAssetAtPath<GameObject>(c.Weapon);
            Assert.IsNotNull(weapon, $"Нет префаба {c.Weapon}");
            Transform  body     = weapon.transform.Find(c.BodyPath);
            float      rootScale = weapon.transform.localScale.x;
            Quaternion bodyRot  = Quaternion.Inverse(weapon.transform.rotation) * body.rotation;
            Vector3    bodyPos  = weapon.transform.InverseTransformPoint(body.position);

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
                    if (align == null) { errors.Add($"{side}: нет трансформа выравнивания"); continue; }

                    UxrGrabber grabber = instance.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == side);
                    Quaternion alignRot = Quaternion.Inverse(weapon.transform.rotation) * align.rotation;
                    Vector3    alignPos = weapon.transform.InverseTransformPoint(align.position);
                    Quaternion rootRot = grabber.transform.rotation * Quaternion.Inverse(alignRot);
                    Vector3    rootPos = grabber.transform.position - rootRot * (alignPos * rootScale);
                    Quaternion worldBodyRot = rootRot * bodyRot;
                    Vector3    worldBodyPos = rootPos + rootRot * (bodyPos * rootScale);

                    Transform  wrist = avatar.GetHand(side).Wrist;
                    Quaternion hand  = avatar.AvatarRigInfo.GetArmInfo(side).HandUniversalLocalAxes.UniversalRotation;
                    Vector3    actualPos = Quaternion.Inverse(hand) * (worldBodyPos - wrist.position);
                    Quaternion actualRot = Quaternion.Inverse(hand) * worldBodyRot;

                    Vector3    expectedPos = packBody.GetColumn(3);
                    Quaternion expectedRot = packBody.rotation;
                    if (side != c.PackSide)
                    {
                        expectedPos.x = -expectedPos.x;
                        expectedRot   = new Quaternion(expectedRot.x, -expectedRot.y, -expectedRot.z, expectedRot.w);
                    }

                    float distance = Vector3.Distance(actualPos, expectedPos);
                    float angle    = Quaternion.Angle(actualRot, expectedRot);
                    if (distance > GripPositionTolerance || angle > GripAngleTolerance)
                        errors.Add($"{side}: корпус смещён на {distance * 1000f:F0} мм и повёрнут на {angle:F0}° относительно кадра пака");
                }

                Assert.That(errors, Is.Empty, $"{c.Pose}: оружие не в ладони как в паке\n{string.Join("\n", errors)}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ── Кадр пака ───────────────────────────────────────────────────────────

        /// <summary>Корпус оружия (кость <see cref="Case.BodyBone" />, без масштаба) в универсальных осях ладони на кадре клипа.</summary>
        private static Matrix4x4 PackBodyInHand(Case c)
        {
            var character = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Character));
            character.hideFlags = HideFlags.HideAndDontSave;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}Prefabs/Weapons/{c.PackPrefab}.prefab");
            var weapon = Object.Instantiate(prefab);
            weapon.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Dictionary<string, Transform> bones = Bones(character);
                UxrAvatarHand hand = PackHand(bones, c.PackSide);
                Quaternion wristToUniversal = PackHandAxes(hand, c.PackSide);

                LoadClip(c).SampleAnimation(character, 0f);
                weapon.transform.SetParent(bones["ik_hand_gun"], false);
                weapon.transform.localRotation = RotationOffset(prefab);
                if (c.RestClip != null)
                    AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}Animations/{c.AnimFolder}/Weapon" })
                                 .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                                 .OfType<AnimationClip>().First(a => a.name == c.RestClip)
                                 .SampleAnimation(weapon.GetComponentInChildren<Animator>(true).gameObject, 0f);

                Transform body = weapon.GetComponentsInChildren<Transform>(true).First(t => t.name == c.BodyBone);
                Matrix4x4 handFrame = Matrix4x4.TRS(hand.Wrist.position, hand.Wrist.rotation * wristToUniversal, Vector3.one);
                return handFrame.inverse * Matrix4x4.TRS(body.position, body.rotation, Vector3.one);
            }
            finally
            {
                Object.DestroyImmediate(weapon);
                Object.DestroyImmediate(character);
            }
        }

        /// <summary><c>weaponRotationOffset</c> настроек ствола пака — сериализацией (код пака в чужой сборке).</summary>
        private static Quaternion RotationOffset(GameObject prefab)
        {
            foreach (MonoBehaviour mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                SerializedProperty settings = new SerializedObject(mb).FindProperty("tacWeaponSettings");
                if (settings?.objectReferenceValue == null) continue;
                return new SerializedObject(settings.objectReferenceValue).FindProperty("weaponRotationOffset").quaternionValue;
            }
            Assert.Fail($"{prefab.name}: нет настроек ствола");
            return Quaternion.identity;
        }

        private static AnimationClip LoadClip(Case c) =>
            AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}Animations/{c.AnimFolder}/Character" })
                         .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                         .OfType<AnimationClip>().First(a => a.name == c.Clip);

        private static Dictionary<string, Transform> Bones(GameObject instance) =>
            instance.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());

        private static UxrAvatarHand PackHand(Dictionary<string, Transform> bones, UxrHandSide side)
        {
            string s = side == UxrHandSide.Left ? "l" : "r";
            var hand = new UxrAvatarHand { Wrist = bones[$"hand_{s}"] };
            UxrAvatarFinger[] fingers = Fingers(hand);
            for (int f = 0; f < fingers.Length; f++)
                fingers[f].SetupFingerBones(Enumerable.Range(1, 3).Select(n => bones[$"{FingerNames[f]}_0{n}_{s}"]).ToList());
            return hand;
        }

        /// <summary>Поправка запястье → универсальные оси ладони, по текущей позе (как <c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c>).</summary>
        private static Quaternion PackHandAxes(UxrAvatarHand hand, UxrHandSide side)
        {
            Transform wrist   = hand.Wrist;
            Vector3   across  = wrist.InverseTransformDirection(hand.Index.Proximal.position - hand.Middle.Proximal.position);
            Vector3   right   = ClosestAxis(across) * (side == UxrHandSide.Left ? 1f : -1f);
            Vector3   forward = wrist.InverseTransformDirection((Vector3.Lerp(hand.Ring.Proximal.position, hand.Middle.Proximal.position, 0.5f) - wrist.position).normalized);
            Vector3   up      = Vector3.Cross(forward, right).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        private static Vector3 ClosestAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0, 0);
            if (a.y >= a.z) return new Vector3(0, Mathf.Sign(v.y), 0);
            return new Vector3(0, 0, Mathf.Sign(v.z));
        }

        private static float[,] PackBends(Case c)
        {
            var instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Character));
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                LoadClip(c).SampleAnimation(instance, 0f);
                return Bends(PackHand(Bones(instance), c.PackSide));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>[палец, сустав]: сгиб в пястно-фаланговом и межфаланговом суставах, по позициям костей.</summary>
        private static float[,] Bends(UxrAvatarHand hand)
        {
            UxrAvatarFinger[] fingers = Fingers(hand);
            var bends = new float[fingers.Length, 2];
            for (int f = 0; f < fingers.Length; f++)
            {
                UxrAvatarFinger finger = fingers[f];
                Vector3 toProximal   = finger.Proximal.position - hand.Wrist.position;
                Vector3 proximal     = finger.Intermediate.position - finger.Proximal.position;
                Vector3 intermediate = finger.Distal.position - finger.Intermediate.position;
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
                    if (Mathf.Abs(expected[f, j] - actual[f, j]) > tolerance)
                        errors.Add($"{names[f]} сустав {j}: пак {expected[f, j]:F0}°, аватар {actual[f, j]:F0}°");
            }
            Assert.That(errors, Is.Empty, $"{context}: сгибы расходятся с кадром пака больше допуска\n{string.Join("\n", errors)}");
        }
    }
}
