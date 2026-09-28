using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Связка Legs Animator ↔ UltimateXR (<c>LegsAnimatorUxrBridge</c>, <see cref="LegsGrounding"/>).
    ///
    /// <para>
    /// Главное, что стережётся, — таз. У ригов аватаров нет анимации, и Legs Animator каждый кадр
    /// брал сдвинутый собой же таз за исходный: поправка копилась, при выключении/включении
    /// плагина запекалась (+14 см за раз), а UltimateXR, держа голову у камеры, вдавливал шею в
    /// плечи. Мост обязан каждый кадр возвращать таз в позу префаба.
    /// </para>
    /// </summary>
    public class LegsGroundingTests
    {
        // ── Поза кости ──────────────────────────────────────────────────────

        [Test]
        public void Поза_кости_возвращается_как_была()
        {
            var bone = new GameObject("bone").transform;
            try
            {
                bone.SetLocalPositionAndRotation(new Vector3(0f, 0.019f, 1.053f), Quaternion.Euler(-90f, 0f, 0f));
                BoneLocalPose pose = BoneLocalPose.Capture(bone);

                bone.SetLocalPositionAndRotation(new Vector3(0f, 0.05f, 1.47f), Quaternion.Euler(-70f, 10f, 0f));
                pose.ApplyTo(bone);

                Assert.That(Vector3.Distance(bone.localPosition, new Vector3(0f, 0.019f, 1.053f)), Is.LessThan(1e-6f));
                Assert.That(Quaternion.Angle(bone.localRotation, Quaternion.Euler(-90f, 0f, 0f)), Is.LessThan(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(bone.gameObject);
            }
        }

        [Test]
        public void Незахваченная_поза_помечена_невалидной()
        {
            Assert.IsFalse(default(BoneLocalPose).IsValid, "поза по умолчанию обнулила бы таз");
            Assert.IsTrue(new BoneLocalPose(Vector3.zero, Quaternion.identity).IsValid);
        }

        /// <summary>
        /// Регрессия: после перекомпиляции в Play Mode несериализуемая поза обнулялась, а ссылка
        /// на кость выживала — мост ставил таз в (0, 0, 0). Unity сохраняет приватное поле при
        /// перезагрузке домена, только если тип сериализуем; проверяем тем же сериализатором.
        /// </summary>
        [Test]
        public void Поза_кости_переживает_сериализацию()
        {
            var pose = new BoneLocalPose(new Vector3(0f, 0.019f, 1.053f), Quaternion.Euler(75.7f, 180f, 180f));
            var copy = JsonUtility.FromJson<PoseHolder>(JsonUtility.ToJson(new PoseHolder { pose = pose })).pose;

            Assert.That(Vector3.Distance(copy.Position, pose.Position), Is.LessThan(1e-6f));
            Assert.That(Quaternion.Angle(copy.Rotation, pose.Rotation), Is.LessThan(1e-3f));
        }

        [System.Serializable]
        private class PoseHolder
        {
            public BoneLocalPose pose;
        }

        // ── Корень ног ──────────────────────────────────────────────────────

        [Test]
        public void Корень_ног_на_полу_под_опорой_тела()
        {
            // Dummy Forward висит на 1,6 м ниже пола — так бывает, когда камера лежит на полу.
            Pose anchor = LegsGrounding.RootAnchor(new Vector3(1f, -1.6f, 2f), Vector3.forward,
                new Vector3(0f, 0.1f, 0f), Vector3.up, Vector3.forward);

            Assert.That(Vector3.Distance(anchor.position, new Vector3(1f, 0.1f, 2f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void Корень_ног_повёрнут_только_вокруг_вертикали()
        {
            Vector3 tilted = Quaternion.Euler(30f, 40f, 0f) * Vector3.forward;
            Pose anchor = LegsGrounding.RootAnchor(Vector3.zero, tilted, Vector3.zero, Vector3.up, Vector3.forward);

            Assert.That(Vector3.Angle(anchor.rotation * Vector3.up, Vector3.up), Is.LessThan(1e-3f));
            Assert.That(anchor.rotation.eulerAngles.y, Is.EqualTo(40f).Within(1e-2f));
        }

        [Test]
        public void Вертикальный_взгляд_берёт_запасное_направление()
        {
            Pose anchor = LegsGrounding.RootAnchor(Vector3.zero, Vector3.down, Vector3.zero, Vector3.up, Vector3.right);

            Assert.That(Vector3.Angle(anchor.rotation * Vector3.forward, Vector3.right), Is.LessThan(1e-3f));
        }

        [Test]
        public void Опора_ступни_поднята_на_подошву()
        {
            Vector3 contact = LegsGrounding.SoleContactPoint(new Vector3(1f, -0.01f, 2f), Vector3.up, 0.15f);

            Assert.That(Vector3.Distance(contact, new Vector3(1f, 0.14f, 2f)), Is.LessThan(1e-6f));
        }

        // ── Мост на настоящих аватарах ──────────────────────────────────────

        private static IEnumerable<TestCaseData> AvatarsWithLegs()
        {
            IEnumerable<AvatarData> avatars = AssetDatabase.FindAssets("t:AvatarRegistry")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                .Select(AssetDatabase.LoadAssetAtPath<AvatarRegistry>)
                .Where(r => r != null)
                .SelectMany(r => r.avatars);

            foreach (AvatarData data in avatars)
            {
                if (data == null || data.prefab == null) continue;
                if (FindBridge(data.prefab) == null) continue;

                yield return new TestCaseData(AssetDatabase.GetAssetPath(data.prefab)).SetName($"{{m}}({data.prefab.name})");
            }
        }

        /// <summary>
        /// Регрессия «голова вдавлена в плечи»: сдвинутый таз (так его оставляет Legs Animator
        /// после выключения/включения) мост возвращает в позу префаба. Сборки моста тестам
        /// недоступны — вызовы по имени.
        /// </summary>
        [TestCaseSource(nameof(AvatarsWithLegs))]
        public void Мост_возвращает_таз_в_позу_префаба(string path)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                Component bridge = FindBridge(root);
                Component legs = bridge.GetComponents<Component>().First(c => c != null && c.GetType().Name == "LegsAnimator");
                var hips = (Transform)legs.GetType().GetField("Hips").GetValue(legs);
                Assert.IsNotNull(hips, $"{root.name}: у LegsAnimator не задан Hips");

                Vector3 bindPosition = hips.localPosition;
                Quaternion bindRotation = hips.localRotation;

                // Awake в редакторе не вызывается — зовём его сами.
                bridge.GetType().GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                      .Invoke(bridge, null);

                hips.localPosition = bindPosition + new Vector3(0f, 0.03f, 0.14f);
                hips.localRotation = bindRotation * Quaternion.Euler(12f, 0f, 0f);

                bridge.GetType().GetMethod("RestoreHipsPose").Invoke(bridge, null);

                Assert.That(Vector3.Distance(hips.localPosition, bindPosition), Is.LessThan(1e-6f),
                    $"{root.name}: таз не вернулся — {hips.localPosition:F3}, в префабе {bindPosition:F3}");
                Assert.That(Quaternion.Angle(hips.localRotation, bindRotation), Is.LessThan(1e-3f),
                    $"{root.name}: поворот таза не вернулся");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static Component FindBridge(GameObject root)
        {
            return root.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "LegsAnimatorUxrBridge");
        }
    }
}
