using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Player.Avatars;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Облегчённая отрисовка чужих аватаров: <see cref="RemoteAvatarRenderPolicy"/> решает,
    /// <see cref="RemoteAvatarRenderSnapshot"/> применяет и откатывает. Сам
    /// <see cref="RemoteAvatarRenderOptimizer"/> (опрос режима в <c>LateUpdate</c>) в EditMode
    /// не выполняется — проверяется его логика перехода.
    /// </summary>
    public class RemoteAvatarRenderOptimizerTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        [Test]
        public void Облегчается_только_удалённый_аватар()
        {
            Assert.IsTrue(RemoteAvatarRenderPolicy.ShouldOptimize(UxrAvatarMode.UpdateExternally));
            Assert.IsFalse(RemoteAvatarRenderPolicy.ShouldOptimize(UxrAvatarMode.Local),
                           "Свой аватар трогать нельзя: UltimateXR сам двигает его кости.");
        }

        [Test]
        public void Скрытые_детали_узнаются_только_по_точному_имени()
        {
            Assert.IsTrue(RemoteAvatarRenderPolicy.IsHiddenUnderGear("SK_BusinessLady_teeth_upper"));
            Assert.IsFalse(RemoteAvatarRenderPolicy.IsHiddenUnderGear("sk_businesslady_teeth_upper"));
            Assert.IsFalse(RemoteAvatarRenderPolicy.IsHiddenUnderGear("SK_BusinessLady_Body"));
            Assert.IsFalse(RemoteAvatarRenderPolicy.IsHiddenUnderGear(null));
        }

        [Test]
        public void Запас_bounds_добавляется_с_каждой_стороны()
        {
            var original = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 0.5f));
            Bounds padded = RemoteAvatarRenderPolicy.PadBounds(original);
            float pad = RemoteAvatarRenderPolicy.BoundsPaddingMeters;

            AssertNear(original.center, padded.center);
            AssertNear(original.min - Vector3.one * pad, padded.min);
            AssertNear(original.max + Vector3.one * pad, padded.max);
        }

        [Test]
        public void Применение_и_откат_возвращают_префабные_настройки()
        {
            var bodyBounds = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.4f));
            SkinnedMeshRenderer body = CreateSkin("SK_BusinessLady_Body", bodyBounds);
            SkinnedMeshRenderer teeth = CreateSkin("SK_BusinessLady_teeth_upper", new Bounds(Vector3.zero, Vector3.one * 0.05f));
            MeshRenderer helmet = CreateRenderer<MeshRenderer>("Helmet");
            helmet.shadowCastingMode = ShadowCastingMode.TwoSided;
            body.receiveShadows = true;

            RemoteAvatarRenderSnapshot snapshot = RemoteAvatarRenderSnapshot.Capture(new Renderer[] { body, teeth, helmet });
            Assert.AreEqual(3, snapshot.RendererCount);
            Assert.AreEqual(2, snapshot.SkinCount);
            Assert.AreEqual(1, snapshot.HiddenCount);

            snapshot.ApplyRemote();
            snapshot.ApplyRemote(); // повтор не должен наращивать запас bounds

            Assert.IsTrue(snapshot.IsApplied);
            Assert.AreEqual(ShadowCastingMode.Off, body.shadowCastingMode);
            Assert.AreEqual(ShadowCastingMode.Off, helmet.shadowCastingMode);
            Assert.IsTrue(body.receiveShadows, "receiveShadows политика не трогает.");
            Assert.IsFalse(body.updateWhenOffscreen);
            Assert.IsFalse(body.skinnedMotionVectors);
            AssertNear(RemoteAvatarRenderPolicy.PadBounds(bodyBounds), body.localBounds);
            Assert.IsTrue(teeth.forceRenderingOff, "Зубы под маской должны гаснуть.");
            Assert.IsFalse(body.forceRenderingOff, "Тело гасить нельзя.");

            snapshot.Restore();

            Assert.IsFalse(snapshot.IsApplied);
            Assert.AreEqual(ShadowCastingMode.On, body.shadowCastingMode);
            Assert.AreEqual(ShadowCastingMode.TwoSided, helmet.shadowCastingMode);
            Assert.IsTrue(body.updateWhenOffscreen);
            Assert.IsTrue(body.skinnedMotionVectors);
            AssertNear(bodyBounds, body.localBounds);
            Assert.IsFalse(teeth.forceRenderingOff);
        }

        [Test]
        public void Снимок_переживает_уничтоженный_рендерер()
        {
            SkinnedMeshRenderer body = CreateSkin("Body", new Bounds(Vector3.zero, Vector3.one));
            SkinnedMeshRenderer gone = CreateSkin("Gone", new Bounds(Vector3.zero, Vector3.one));

            RemoteAvatarRenderSnapshot snapshot = RemoteAvatarRenderSnapshot.Capture(new Renderer[] { body, gone });
            Object.DestroyImmediate(gone.gameObject);

            Assert.DoesNotThrow(snapshot.ApplyRemote);
            Assert.DoesNotThrow(snapshot.Restore);
        }

        public static IEnumerable<string> RegisteredAvatarPaths() =>
            VrBattlegrounds.Tests.Prefabs.RegisteredAvatars.Prefabs().Select(AssetDatabase.GetAssetPath).OrderBy(p => p);

        /// <summary>
        /// Скрытые детали узнаются по имени рендерера. Если у зарегистрированного аватара есть объект
        /// с таким именем, он обязан быть среди рендереров тела — иначе список молча не сработает
        /// (деталь переехала, сменила тип рендерера). Конкретных аватаров тест не знает.
        /// </summary>
        [TestCaseSource(nameof(RegisteredAvatarPaths))]
        public void Скрытые_детали_аватара_есть_среди_рендереров_тела(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"Не найден префаб {path}.");

            HashSet<string> bodyNames = new HashSet<string>(
                RemoteAvatarRenderPolicy.CollectBodyRenderers(prefab.transform).Select(r => r.name));
            HashSet<string> allNames = new HashSet<string>(
                prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name));

            string[] missing = RemoteAvatarRenderPolicy.HiddenUnderGearRendererNames
                                                       .Where(n => allNames.Contains(n) && !bodyNames.Contains(n))
                                                       .ToArray();

            Assert.IsEmpty(missing,
                           $"В {path} детали есть, но не среди рендереров тела: {string.Join(", ", missing)}. " +
                           "Поправьте RemoteAvatarRenderPolicy.HiddenUnderGearRendererNames.");
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), 1e-4f, $"Ожидалось {expected}, получено {actual}.");
        }

        private static void AssertNear(Bounds expected, Bounds actual)
        {
            AssertNear(expected.center, actual.center);
            AssertNear(expected.extents, actual.extents);
        }

        private SkinnedMeshRenderer CreateSkin(string name, Bounds localBounds)
        {
            SkinnedMeshRenderer skin = CreateRenderer<SkinnedMeshRenderer>(name);
            skin.updateWhenOffscreen = true;
            skin.skinnedMotionVectors = true;
            skin.shadowCastingMode = ShadowCastingMode.On;
            skin.localBounds = localBounds;
            return skin;
        }

        private T CreateRenderer<T>(string name) where T : Renderer
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<T>();
        }
    }
}
