using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Кисть призрака (SmallHands из UltimateXR) повторяет позу кисти аватара с чужим скелетом:
    /// направление кисти и каждой фаланги совпадает. Аватары — из реестра.
    /// </summary>
    public class GhostHandTests
    {
        private const float MaxAngle = 15f;
        private const string Folder = "Assets/Prefabs/Player/Ghost/";

        private static GhostHand LoadHand(string side) => AssetDatabase.LoadAssetAtPath<GhostHand>(Folder + "GhostHand" + side + ".prefab");

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static IEnumerable<TestCaseData> RegisteredAvatars()
        {
            foreach (AvatarData data in AssetDatabase.FindAssets("t:AvatarRegistry")
                                                     .Select(AssetDatabase.GUIDToAssetPath)
                                                     .Select(AssetDatabase.LoadAssetAtPath<AvatarRegistry>)
                                                     .Where(r => r != null)
                                                     .SelectMany(r => r.avatars))
            {
                if (data == null || data.prefab == null) continue;
                yield return new TestCaseData(AssetDatabase.GetAssetPath(data.prefab)).SetName($"{{m}}({data.prefab.name})");
            }
        }

        [Test]
        public void Префабы_кистей_призрака_собраны()
        {
            foreach (string side in new[] { "Left", "Right" })
            {
                GhostHand hand = LoadHand(side);
                Assert.IsNotNull(hand, $"Нет {Folder}GhostHand{side} — Tools/VR Battlegrounds/Avatars/Build Ghost Parts.");
                Assert.IsNotNull(hand.Wrist);
                Assert.IsNotNull(hand.Renderer);
                Assert.AreEqual("GhostMaterial", hand.Renderer.sharedMaterial.name, "Кисть призрака не в материале призрака.");
            }
        }

        /// <summary>
        /// Призрак — префаб: его смотрят и правят руками. Все части на месте и в материале
        /// призрака (иначе цвет команды и прозрачность не лягут).
        /// </summary>
        [Test]
        public void Префаб_призрака_собран()
        {
            var model = AssetDatabase.LoadAssetAtPath<GhostModel>(Folder + "GhostBody.prefab");
            Assert.IsNotNull(model, $"Нет {Folder}GhostBody.prefab — Tools/VR Battlegrounds/Avatars/Build Ghost Parts.");
            Assert.IsNotNull(model.Head, "Нет шлема.");
            Assert.IsNotNull(model.Torso, "Нет корпуса.");
            Assert.IsNotNull(model.LeftHand, "Нет левой кисти.");
            Assert.IsNotNull(model.RightHand, "Нет правой кисти.");
            Assert.AreEqual(UxrHandSide.Left, model.LeftHand.Side);
            Assert.AreEqual(UxrHandSide.Right, model.RightHand.Side);

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual("GhostMaterial", r.sharedMaterial != null ? r.sharedMaterial.name : null, $"{r.name}: не в материале призрака.");
            }
            Assert.IsEmpty(model.GetComponentsInChildren<Collider>(true), "У призрака коллайдер — он остановит пули.");
        }

        [TestCaseSource(nameof(RegisteredAvatars))]
        public void Кисть_призрака_повторяет_позу_аватара(string path)
        {
            var avatarGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            _created.Add(avatarGo);
            var avatar = avatarGo.GetComponent<UxrAvatar>();

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                UxrAvatarHand source = avatar.GetHand(side);
                UxrAvatarArmInfo info = avatar.AvatarRigInfo.GetArmInfo(side);

                // Поза, которой нет в привязке: кисть повёрнута, пальцы согнуты.
                source.Wrist.Rotate(new Vector3(30f, -50f, 20f), Space.World);
                foreach (UxrAvatarFinger finger in new[] { source.Index, source.Middle, source.Ring, source.Little })
                {
                    finger.Proximal.Rotate(info.FingerUniversalLocalAxes.LocalRight, 60f, Space.Self);
                    finger.Intermediate.Rotate(info.FingerUniversalLocalAxes.LocalRight, 70f, Space.Self);
                }

                GhostHand ghost = Object.Instantiate(LoadHand(side.ToString()));
                _created.Add(ghost.gameObject);
                ghost.Follow(source, info);

                AssertSame(avatar.name, side, "кисть", source.Wrist, source.Middle.Distal, ghost.Wrist, ghost.Middle.Distal);
                AssertSame(avatar.name, side, "ладонь поперёк", source.Index.Proximal, source.Little.Proximal, ghost.Index.Proximal, ghost.Little.Proximal);
                AssertSame(avatar.name, side, "указательный", source.Index.Intermediate, source.Index.Distal, ghost.Index.Intermediate, ghost.Index.Distal);
                AssertSame(avatar.name, side, "большой", source.Thumb.Proximal, source.Thumb.Distal, ghost.Thumb.Proximal, ghost.Thumb.Distal);
            }
        }

        private static void AssertSame(string avatar, UxrHandSide side, string what, Transform a, Transform b, Transform ga, Transform gb)
        {
            float angle = Vector3.Angle(b.position - a.position, gb.position - ga.position);
            Assert.Less(angle, MaxAngle, $"{avatar} {side}: {what} призрака отклонён на {angle:F0}° от позы аватара.");
        }
    }
}
