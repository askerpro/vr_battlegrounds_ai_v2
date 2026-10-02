using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>Проверяет сохранённую расстановку; не пересобирает ниши и не исправляет карту.</summary>
    public sealed class ArsenalMapGeometryTests
    {
        public static IEnumerable<TestCaseData> Scenes()
        {
            yield return new TestCaseData("Assets/Scenes/Lobby.unity", 4);
            yield return new TestCaseData("Assets/Scenes/Tools/CommonArsenalReview.unity", 2);
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/Maps" }))
                yield return new TestCaseData(AssetDatabase.GUIDToAssetPath(guid), 8);
        }

        [TestCaseSource(nameof(Scenes))]
        public void WalletAndDogTagAreVisibleAndEquipmentPathIsClear(string path, int expectedStations)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var stations = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<ArsenalWallController>(true)).ToArray();
                Assert.That(stations.Length, Is.EqualTo(expectedStations),
                    $"{path}: нарушен контракт стенда; пустая/неполная карта не считается успешной проверкой.");
                Assert.That(stations.All(s => s.GetComponent<ArsenalEquipmentPoses>() != null), Is.True,
                    $"{path}: станция без маркеров движения.");
                foreach (var station in stations)
                {
                    var poses = station.GetComponent<ArsenalEquipmentPoses>().Targets;
                    Assert.That(poses.Length, Is.EqualTo(2), $"{path}/{station.name}: нужны обе группы оборудования.");
                    Assert.That(poses.All(p => p.Target != null && p.ClosedPose != null && p.OpenPose != null), Is.True,
                        $"{path}/{station.name}: неполная конфигурация поз.");
                }
                var before = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
                var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true))
                    .ToDictionary(c => c, c => c.enabled);
                var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                    .ToDictionary(r => r, r => r.enabled);
                var active = before.Keys.ToDictionary(t => t.gameObject, t => t.gameObject.activeSelf);
                var errors = ArsenalVisibilityGeometryAudit.Audit(scene);
                errors.AddRange(ArsenalMotionGeometryAudit.Audit(scene));
                foreach (var pair in before)
                    Assert.That((pair.Key.localPosition, pair.Key.localRotation, pair.Key.localScale), Is.EqualTo(pair.Value),
                        $"{path}/{pair.Key.name}: проверка изменила авторскую расстановку.");
                foreach (var pair in colliders)
                    Assert.That(pair.Key.enabled, Is.EqualTo(pair.Value), $"{path}/{pair.Key.name}: изменён коллайдер.");
                foreach (var pair in renderers)
                    Assert.That(pair.Key.enabled, Is.EqualTo(pair.Value), $"{path}/{pair.Key.name}: изменён renderer.");
                foreach (var pair in active)
                    Assert.That(pair.Key.activeSelf, Is.EqualTo(pair.Value), $"{path}/{pair.Key.name}: изменена активность.");
                Assert.That(scene.isDirty, Is.False, "Проверка не должна изменять или сохранять карту.");
                Assert.That(errors, Is.Empty, string.Join("\n", errors));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                var after = EditorSceneManager.GetSceneManagerSetup();
                Assert.That(after.Select(s => (s.path, s.isLoaded, s.isActive)),
                    Is.EqualTo(setup.Select(s => (s.path, s.isLoaded, s.isActive))),
                    "Проверка не должна переключать рабочие сцены редактора.");
            }
        }
    }
}
