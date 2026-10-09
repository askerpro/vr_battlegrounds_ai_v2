using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UnityEngine;
using UnityEditor;

namespace VrBattlegrounds.Tests.Prefabs
{
    public class AvatarRendererOwnershipTests
    {
        private GameObject _root;
        private UxrAvatar _avatar;

        [SetUp]
        public void CreateInactiveAvatar()
        {
            _root = new GameObject("RendererOwnershipControl");
            _root.SetActive(false);
            _avatar = _root.AddComponent<UxrAvatar>();
        }

        [TearDown]
        public void DestroyControl()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        private Renderer Skin(string name, Transform parent = null)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent != null ? parent : _root.transform, false);
            return child.AddComponent<SkinnedMeshRenderer>();
        }

        private void RunSetup()
        {
            // Сборщик находится в editor-сборке, недоступной прямой ссылкой asmdef тестов.
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("FixAvatarRenderers", false)).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "Нет штатного FixAvatarRenderers");
            MethodInfo method = type.GetMethod("Setup", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { _avatar });
        }

        [Test]
        public void ExplicitRendererListIsNotExpandedByUnrelatedChildren()
        {
            Renderer body = Skin("PermanentBody");
            var serialized = new SerializedObject(_avatar);
            var list = serialized.FindProperty("_avatarRenderers");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = body;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Renderer auxiliary = Skin("Ghost");
            var watch = new GameObject("Watch");
            watch.transform.SetParent(_root.transform, false);
            watch.AddComponent<MeshRenderer>();

            RunSetup();
            CollectionAssert.AreEqual(new[] { body }, _avatar.AvatarRenderers);
            UnityEngine.Object.DestroyImmediate(auxiliary.gameObject);
            int depth = UxrStateSyncImplementer.SyncCallDepth;
            Assert.DoesNotThrow(() => _avatar.RenderMode = UxrAvatarRenderModes.Avatar);
            Assert.That(UxrStateSyncImplementer.SyncCallDepth, Is.EqualTo(depth));
        }

        [Test]
        public void CyborgKeepsAcceptedBodyAfterSetupAndLegacyGhostRemoval()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Player/PlayerControllersCyborgAvatar.prefab");
            Assert.That(prefab, Is.Not.Null);
            var holder = new GameObject("InactiveCyborgRegression");
            holder.SetActive(false);
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, holder.transform);
                UxrAvatar cyborg = instance.GetComponent<UxrAvatar>();
                Renderer[] configured = cyborg.AvatarRenderers.ToArray();
                Assert.That(configured.Length, Is.EqualTo(15));
                Assert.That(configured.All(r => r != null && r is SkinnedMeshRenderer), Is.True);
                Transform legacyGhost = instance.transform.Find("Cyborg/Ghost");
                Assert.That(legacyGhost, Is.Not.Null);
                Assert.That(configured.Any(r => r.transform.IsChildOf(legacyGhost)), Is.False);
                _avatar = cyborg;
                RunSetup();
                CollectionAssert.AreEqual(configured, cyborg.AvatarRenderers);
                UnityEngine.Object.DestroyImmediate(legacyGhost.gameObject);
                int depth = UxrStateSyncImplementer.SyncCallDepth;
                Assert.DoesNotThrow(() => cyborg.RenderMode = UxrAvatarRenderModes.Avatar);
                Assert.That(UxrStateSyncImplementer.SyncCallDepth, Is.EqualTo(depth));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void EditorOnlySubtreeIsExcludedAndItsRemovalKeepsRenderModeValid()
        {
            Renderer body = Skin("PermanentBody");
            var temporary = new GameObject("TemporaryAuthoringCopy");
            temporary.transform.SetParent(_root.transform, false);
            temporary.tag = "EditorOnly";
            temporary.SetActive(false);
            Renderer auxiliary = Skin("CopiedBody", temporary.transform);

            RunSetup();
            Assert.That(_avatar.AvatarRenderers, Has.Member(body));
            Assert.That(_avatar.AvatarRenderers, Has.No.Member(auxiliary),
                "В постоянный список попала геометрия, которая не принадлежит игровому аватару");

            UnityEngine.Object.DestroyImmediate(temporary);
            Assert.That(_avatar.AvatarRenderers.All(r => r != null), Is.True,
                "Удаление авторской копии оставило мёртвые ссылки, как у Cyborg/Ghost");
            int depth = UxrStateSyncImplementer.SyncCallDepth;
            Assert.DoesNotThrow(() => _avatar.RenderMode = UxrAvatarRenderModes.Avatar);
            Assert.That(UxrStateSyncImplementer.SyncCallDepth, Is.EqualTo(depth),
                "Смена render mode оставила незавершённый общий BeginSync");
        }

        [Test]
        public void PermanentInactiveGeometryAndObjectNamedGhostAreIncluded()
        {
            Renderer namedGhost = Skin("Ghost");
            Renderer inactiveLod = Skin("InactivePermanentLod");
            inactiveLod.gameObject.SetActive(false);

            RunSetup();
            CollectionAssert.AreEquivalent(new[] { namedGhost, inactiveLod }, _avatar.AvatarRenderers,
                "Имя Ghost или неактивность не означают временную геометрию");
        }

        [Test]
        public void InactiveHandIntegrationIsExcluded()
        {
            Renderer body = Skin("PermanentBody");
            var hand = new GameObject("InactiveControllerModel");
            hand.transform.SetParent(_root.transform, false);
            hand.SetActive(false);
            hand.AddComponent<UxrHandIntegration>();
            Renderer controller = Skin("ControllerSkin", hand.transform);

            RunSetup();
            Assert.That(_avatar.AvatarRenderers, Has.Member(body));
            Assert.That(_avatar.AvatarRenderers, Has.No.Member(controller));
        }

        [Test]
        public void NestedAvatarOwnsItsRenderers()
        {
            Renderer body = Skin("PermanentBody");
            var nested = new GameObject("SeparateAvatar");
            nested.transform.SetParent(_root.transform, false);
            nested.AddComponent<UxrAvatar>();
            Renderer nestedBody = Skin("OtherBody", nested.transform);

            RunSetup();
            Assert.That(_avatar.AvatarRenderers, Has.Member(body));
            Assert.That(_avatar.AvatarRenderers, Has.No.Member(nestedBody),
                "Рендерер соседнего аватара получил второго владельца");
        }
    }
}
