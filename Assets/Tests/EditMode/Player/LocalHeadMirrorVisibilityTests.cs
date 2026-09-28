using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Голова своего аватара: не видна своей камере (иначе перед глазами текстуры лица изнутри),
    /// но видна зеркалу. UltimateXR прячет её выключением объектов из
    /// <c>UxrMirrorAvatar → Local Disabled Game Objects</c>, и выключенный объект не рисует
    /// никто — в зеркале игрок был без головы. <see cref="LocalHeadMirrorVisibility"/> включает
    /// их обратно на слое <see cref="LocalHeadMirrorVisibility.LayerName"/>, который своя
    /// камера не рисует, а камера зеркала (<c>UxrPlanarReflectionUrp</c>, маска «всё, кроме
    /// Water») рисует.
    /// </summary>
    public class LocalHeadMirrorVisibilityTests
    {
        // Слой Water камера зеркала вычёркивает всегда (UxrPlanarReflectionUrp: ~(1 << 4)).
        private const int ReflectionExcludedLayer = 4;

        private static IEnumerable<TestCaseData> AvatarsWithLocalHead()
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

                UxrMirrorAvatar net = data.prefab.GetComponent<UxrMirrorAvatar>();
                if (net == null || net.LocalDisabledGameObjects == null || net.LocalDisabledGameObjects.Count == 0) continue;

                yield return new TestCaseData(AssetDatabase.GetAssetPath(data.prefab)).SetName($"{{m}}({data.prefab.name})");
            }
        }

        private Scene _scene;

        [SetUp]
        public void SetUp()
        {
            _scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(_scene);
        }

        [Test]
        public void Слой_головы_есть_и_зеркало_его_рисует()
        {
            int layer = LayerMask.NameToLayer(LocalHeadMirrorVisibility.LayerName);

            Assert.GreaterOrEqual(layer, 0,
                $"Нет слоя '{LocalHeadMirrorVisibility.LayerName}' в Project Settings → Tags and Layers");
            Assert.AreNotEqual(ReflectionExcludedLayer, layer, "Камера зеркала слой Water не рисует");
        }

        [TestCaseSource(nameof(AvatarsWithLocalHead))]
        public void Своя_голова_видна_зеркалу_но_не_своей_камере(string path)
        {
            UxrAvatar avatar = Spawn(path, out LocalHeadMirrorVisibility visibility, out List<GameObject> head);
            int layer = LayerMask.NameToLayer(LocalHeadMirrorVisibility.LayerName);

            // Так их оставляет UxrMirrorAvatar.InitializeNetworkAvatar для своего аватара.
            head.ForEach(o => o.SetActive(false));

            visibility.ShowToMirrorsOnly();

            var problems = new List<string>();
            foreach (GameObject o in head)
            {
                if (!o.activeSelf) problems.Add($"'{o.name}' выключен — зеркало его не нарисует");

                foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.gameObject.layer != layer) problems.Add($"'{r.name}' на слое {LayerMask.LayerToName(r.gameObject.layer)}");
                }
            }

            if ((avatar.CameraComponent.cullingMask & (1 << layer)) != 0)
                problems.Add("своя камера рисует слой головы — лицо изнутри перед глазами");

            Assert.IsEmpty(problems, $"{avatar.name}:\n{string.Join("\n", problems)}");
        }

        [TestCaseSource(nameof(AvatarsWithLocalHead))]
        public void Чужому_аватару_голова_возвращается_как_была(string path)
        {
            UxrAvatar avatar = Spawn(path, out LocalHeadMirrorVisibility visibility, out List<GameObject> head);
            Dictionary<GameObject, int> original = head.SelectMany(o => o.GetComponentsInChildren<Transform>(true))
                                                       .Select(t => t.gameObject).Distinct()
                                                       .ToDictionary(g => g, g => g.layer);
            int mask = avatar.CameraComponent.cullingMask;

            visibility.ShowToMirrorsOnly();
            visibility.Restore();

            var problems = original.Where(p => p.Key.layer != p.Value)
                                   .Select(p => $"'{p.Key.name}': слой {p.Key.layer}, был {p.Value}")
                                   .ToList();

            if (head.Any(o => !o.activeSelf)) problems.Add("голова чужого аватара выключена");
            if (avatar.CameraComponent.cullingMask != mask) problems.Add("маска камеры не восстановлена");

            Assert.IsEmpty(problems, $"{avatar.name}:\n{string.Join("\n", problems)}");
        }

        private UxrAvatar Spawn(string path, out LocalHeadMirrorVisibility visibility, out List<GameObject> head)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);

            UxrAvatar avatar = root.GetComponent<UxrAvatar>();
            Assert.IsNotNull(avatar.CameraComponent, $"{prefab.name}: нет камеры");

            head = root.GetComponent<UxrMirrorAvatar>().LocalDisabledGameObjects.Where(o => o != null).ToList();
            visibility = root.AddComponent<LocalHeadMirrorVisibility>();
            return avatar;
        }
    }
}
