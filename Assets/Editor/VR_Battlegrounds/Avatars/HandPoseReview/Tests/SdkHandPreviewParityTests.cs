using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview.Tests
{
    /// <summary>Превью SDK обязано совпадать с runtime-позой на целевом риге, независимо от исходных матриц пака.</summary>
    public class SdkHandPreviewParityTests
    {
        private const string Mef = "Assets/Prefabs/Player/Optimized_MEF_Player.prefab";
        private const string Cyborg = "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [TestCase(Mef, "Kinemation_AK105_Grip", UxrHandSide.Left, 0f)]
        [TestCase(Mef, "Kinemation_AK105_Grip", UxrHandSide.Right, 0f)]
        [TestCase(Mef, "Kinemation_AK105_Support", UxrHandSide.Left, 0f)]
        [TestCase(Mef, "Kinemation_AK105_Support", UxrHandSide.Right, 0f)]
        [TestCase(Cyborg, "Default", UxrHandSide.Left, 0f)]
        [TestCase(Cyborg, "Default", UxrHandSide.Right, 0f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Left, 0f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Left, .5f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Left, 1f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Right, 0f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Right, .5f)]
        [TestCase(Cyborg, "DemoGun", UxrHandSide.Right, 1f)]
        public void PreviewMatchesRuntime(string path, string poseName, UxrHandSide side, float blend)
        {
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<UxrAvatar>();
            var pose = avatar.GetHandPose(poseName);
            var grabber = avatar.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == side);
            var source = (SkinnedMeshRenderer)grabber.HandRenderer;
            string beforePose = EditorJsonUtility.ToJson(pose);
            string beforeAvatar = EditorJsonUtility.ToJson(avatar);
            bool poseDirty = EditorUtility.IsDirty(pose);
            bool avatarDirty = EditorUtility.IsDirty(avatar);
            var bones = avatar.GetComponentsInChildren<Transform>(true)
                .Select(t => new { t, p = t.localPosition, r = t.localRotation, s = t.localScale }).ToArray();
            int undo = Undo.GetCurrentGroup();
            var selected = Selection.instanceIDs.ToArray();
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            var dirty = scenes.Select(s => s.isDirty).ToArray();
            int previewScenes = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            UxrPreviewHandGripMesh sdk = null;
            try
            {
                using (var fixture = new RuntimeFixture(avatar, side))
                {
                    fixture.Apply(pose, side, blend);
                    sdk = UxrPreviewHandGripMesh.BuildForAvatar(avatar, pose, side, blend);
                    Assert.That(sdk, Is.Not.Null);
                    Assert.That(sdk.IsValid, Is.True);
                    AssertGeometry(sdk.UnityMesh, fixture, ExtractIndices(source, avatar.GetHand(side).Wrist));
                }
            }
            finally
            {
                if (sdk?.UnityMesh) Object.DestroyImmediate(sdk.UnityMesh);
            }

            Assert.That(EditorJsonUtility.ToJson(pose), Is.EqualTo(beforePose));
            Assert.That(EditorJsonUtility.ToJson(avatar), Is.EqualTo(beforeAvatar));
            Assert.That(EditorUtility.IsDirty(pose), Is.EqualTo(poseDirty));
            Assert.That(EditorUtility.IsDirty(avatar), Is.EqualTo(avatarDirty));
            Assert.That(bones.All(b => b.t.localPosition == b.p && b.t.localRotation == b.r && b.t.localScale == b.s), Is.True);
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undo));
            Assert.That(Selection.instanceIDs, Is.EqualTo(selected));
            Assert.That(scenes.Select((s, i) => s.isDirty == dirty[i]).All(v => v), Is.True);
            Assert.That(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount, Is.EqualTo(previewScenes));
        }

        [TestCase(1.7f, 1.7f, 1.7f, 0f)]
        [TestCase(.7f, 1.4f, 1.2f, 67f)]
        public void ScaledRigAndBlendShapeMatchRuntime(float x, float y, float z, float blendShape)
        {
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(Mef).GetComponent<UxrAvatar>();
            var pose = avatar.GetHandPose("Kinemation_AK105_Grip");
            var grabber = avatar.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == UxrHandSide.Right);
            var source = (SkinnedMeshRenderer)grabber.HandRenderer;
            UxrPreviewHandGripMesh sdk = null;
            Mesh modifiedMesh = null;
            try
            {
                using (var fixture = new RuntimeFixture(avatar, UxrHandSide.Right))
                {
                    fixture.Root.localScale = Vector3.Scale(fixture.Root.localScale, new Vector3(x, y, z));
                    fixture.Root.localRotation = Quaternion.Euler(13, 27, -8) * fixture.Root.localRotation;
                    modifiedMesh = Object.Instantiate(source.sharedMesh);
                    modifiedMesh.hideFlags = HideFlags.HideAndDontSave;
                    int shape = modifiedMesh.blendShapeCount;
                    var delta = Enumerable.Repeat(new Vector3(.002f, -.001f, .003f), modifiedMesh.vertexCount).ToArray();
                    var zero = new Vector3[modifiedMesh.vertexCount];
                    modifiedMesh.AddBlendShapeFrame("Parity fixture", 100, delta, zero, zero);
                    fixture.Skin.sharedMesh = modifiedMesh;
                    fixture.Skin.SetBlendShapeWeight(shape, blendShape);
                    fixture.Apply(pose, UxrHandSide.Right, 0);

                    sdk = UxrPreviewHandGripMesh.BuildForAvatar(avatar, pose, UxrHandSide.Right);
                    // Подменяется только собственный transient preview: ни prefab, ни SDK component не активируются.
                    typeof(UxrPreviewHandGripMesh).GetField("_skin", Private).SetValue(sdk, fixture.Skin);
                    typeof(UxrPreviewHandGripMesh).GetField("_hand", Private).SetValue(sdk, fixture.Hand);
                    typeof(UxrPreviewHandGripMesh).GetField("_sourceMesh", Private).SetValue(sdk, modifiedMesh);
                    typeof(UxrPreviewHandGripMesh).GetMethod("ComputePose", Private)
                        .Invoke(sdk, new object[] { fixture.Grabber, false, 0f });
                    AssertGeometry(sdk.UnityMesh, fixture, ExtractIndices(source, avatar.GetHand(UxrHandSide.Right).Wrist));
                }
            }
            finally
            {
                if (sdk?.UnityMesh) Object.DestroyImmediate(sdk.UnityMesh);
                if (modifiedMesh) Object.DestroyImmediate(modifiedMesh);
            }
        }

        [Test]
        public void RefreshPreservesMeshIdentityForBothGrabPoints()
        {
            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(Mef).GetComponent<UxrAvatar>();
            var weapon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/AK105/AK105.prefab")
                .GetComponent<UxrGrabbableObject>();
            var sdk = UxrPreviewHandGripMesh.Build(weapon, avatar, 0, UxrHandSide.Right);
            Assert.That(sdk, Is.Not.Null);
            var mesh = sdk.UnityMesh;
            try
            {
                Assert.That(sdk.Refresh(weapon, avatar, 1, UxrHandSide.Left, true), Is.True);
                Assert.That(sdk.UnityMesh, Is.SameAs(mesh));
                using (var fixture = new RuntimeFixture(avatar, UxrHandSide.Left))
                {
                    fixture.Apply(avatar.GetHandPose("Kinemation_AK105_Support"), UxrHandSide.Left, 0);
                    var source = avatar.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == UxrHandSide.Left);
                    AssertGeometry(mesh, fixture, ExtractIndices((SkinnedMeshRenderer)source.HandRenderer, avatar.GetHand(UxrHandSide.Left).Wrist));
                }
            }
            finally { if (mesh) Object.DestroyImmediate(mesh); }
        }

        private static void AssertGeometry(Mesh actual, RuntimeFixture reference, int[] indices)
        {
            var vertices = reference.Baked.vertices;
            var normals = reference.Baked.normals;
            var rigid = Matrix4x4.TRS(reference.Grabber.position, reference.Grabber.rotation, Vector3.one);
            var matrix = rigid.inverse * reference.Skin.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var actualVertices = actual.vertices;
            var actualNormals = actual.normals;
            Assert.That(actualVertices.Length, Is.EqualTo(indices.Length));
            float maximum = 0, normalError = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                maximum = Mathf.Max(maximum, Vector3.Distance(actualVertices[i], matrix.MultiplyPoint3x4(vertices[indices[i]])));
                normalError = Mathf.Max(normalError, Vector3.Distance(actualNormals[i], normalMatrix.MultiplyVector(normals[indices[i]]).normalized));
            }
            Assert.That(maximum, Is.LessThan(.00001f), "Допуск 0,01 мм для всех hand vertices");
            Assert.That(normalError, Is.LessThan(.0001f), "Нормали должны соответствовать runtime bake");
        }

        // Независимая проверка правила SDK extraction: mapping не читается из проверяемого класса.
        private static int[] ExtractIndices(SkinnedMeshRenderer skin, Transform wrist)
        {
            var weights = skin.sharedMesh.boneWeights;
            var hand = skin.bones.Select(t => t && (t == wrist || t.IsChildOf(wrist))).ToArray();
            float Weight(int index)
            {
                var w = weights[index];
                return (hand[w.boneIndex0] ? w.weight0 : 0) + (hand[w.boneIndex1] ? w.weight1 : 0)
                    + (hand[w.boneIndex2] ? w.weight2 : 0) + (hand[w.boneIndex3] ? w.weight3 : 0);
            }
            var used = new HashSet<int>();
            var result = new List<int>();
            for (int sub = 0; sub < skin.sharedMesh.subMeshCount; sub++)
            {
                var triangles = skin.sharedMesh.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                    if (Weight(triangles[i]) + Weight(triangles[i + 1]) + Weight(triangles[i + 2]) > UxrConstants.Geometry.SignificantBoneWeight)
                        for (int j = 0; j < 3; j++) if (used.Add(triangles[i + j])) result.Add(triangles[i + j]);
            }
            return result.ToArray();
        }

        private sealed class RuntimeFixture : IDisposable
        {
            private readonly PreviewRenderUtility _preview = new PreviewRenderUtility();
            private readonly UxrAvatarRigInfo _rigInfo;
            public Transform Root { get; }
            public Transform Grabber { get; }
            public SkinnedMeshRenderer Skin { get; }
            public UxrAvatarHand Hand { get; }
            public Mesh Baked { get; private set; }

            public RuntimeFixture(UxrAvatar avatar, UxrHandSide side)
            {
                try
                {
                    _rigInfo = (UxrAvatarRigInfo)typeof(UxrAvatar).GetField("_rigInfo", Private).GetValue(avatar);
                    var map = new Dictionary<Transform, Transform>();
                    Transform Copy(Transform source, Transform parent)
                    {
                        var go = new GameObject("Runtime parity bone") { hideFlags = HideFlags.HideAndDontSave };
                        var node = go.transform;
                        if (!parent) _preview.AddSingleGO(go);
                        node.SetParent(parent, false);
                        node.localPosition = source.localPosition;
                        node.localRotation = source.localRotation;
                        node.localScale = source.localScale;
                        map.Add(source, node);
                        foreach (Transform child in source) Copy(child, node);
                        return node;
                    }
                    Root = Copy(avatar.transform, null);
                    Transform Resolve(Transform t) => t ? map[t] : null;
                    UxrAvatarFinger Finger(UxrAvatarFinger f) => new UxrAvatarFinger
                    {
                        Metacarpal = Resolve(f.Metacarpal), Proximal = Resolve(f.Proximal),
                        Intermediate = Resolve(f.Intermediate), Distal = Resolve(f.Distal)
                    };
                    var hand = avatar.GetHand(side);
                    Hand = new UxrAvatarHand { Wrist = Resolve(hand.Wrist), Thumb = Finger(hand.Thumb), Index = Finger(hand.Index),
                        Middle = Finger(hand.Middle), Ring = Finger(hand.Ring), Little = Finger(hand.Little) };
                    var grabber = avatar.GetComponentsInChildren<UxrGrabber>(true).First(g => g.Side == side);
                    Grabber = Resolve(grabber.transform);
                    var source = (SkinnedMeshRenderer)grabber.HandRenderer;
                    Skin = Resolve(source.transform).gameObject.AddComponent<SkinnedMeshRenderer>();
                    Skin.enabled = false;
                    Skin.sharedMesh = source.sharedMesh;
                    Skin.bones = source.bones.Select(Resolve).ToArray();
                    Skin.rootBone = Resolve(source.rootBone);
                    Skin.quality = source.quality;
                    for (int i = 0; i < source.sharedMesh.blendShapeCount; i++) Skin.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
                }
                catch { _preview.Cleanup(); throw; }
            }

            public void Apply(UxrHandPoseAsset pose, UxrHandSide side, float blend)
            {
                var arm = _rigInfo.GetArmInfo(side);
                if (pose.PoseType == UxrHandPoseType.Fixed)
                    UxrAvatarRig.UpdateHandUsingDescriptor(Hand, side == UxrHandSide.Left ? pose.HandDescriptorLeft : pose.HandDescriptorRight,
                        arm.HandUniversalLocalAxes, arm.FingerUniversalLocalAxes);
                else
                    UxrAvatarRig.UpdateHandUsingDescriptor(Hand, side == UxrHandSide.Left ? pose.HandDescriptorOpenLeft : pose.HandDescriptorOpenRight,
                        side == UxrHandSide.Left ? pose.HandDescriptorClosedLeft : pose.HandDescriptorClosedRight, blend,
                        arm.HandUniversalLocalAxes, arm.FingerUniversalLocalAxes);
                if (Baked) Object.DestroyImmediate(Baked);
                Baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                Skin.BakeMesh(Baked, false);
            }

            public void Dispose()
            {
                if (Baked) Object.DestroyImmediate(Baked);
                _preview.Cleanup();
            }
        }
    }
}
