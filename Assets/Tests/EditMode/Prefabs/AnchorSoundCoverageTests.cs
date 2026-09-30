using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Любое «положить в якорь» и «снять с якоря» звучит (решение пользователя): у каждого
    /// <see cref="UxrGrabbableObjectAnchor" /> — <see cref="AnchorSound" /> со звуком вставки (clip источника) и
    /// доставания (<see cref="AnchorSound.TakeOutClip" />). В VR это подтверждение действия: руку не видно у кармана,
    /// на стене арсенала слот за решёткой.
    ///
    /// <para>
    /// Класс ошибки: звук требовали точечно — у гнезда магазина (<c>WeaponFeedbackTests</c>) и у карманов аватара
    /// (<c>AvatarLoadoutTests</c>), и каждый новый якорь (слот стены, полка, жетон) появлялся молчаливым. Тест идёт по
    /// всем якорям префабов проекта и сцен сборки.
    /// </para>
    /// </summary>
    public class AnchorSoundCoverageTests
    {
        private static readonly string[] PrefabRoots = { "Assets/Prefabs" };

        [Test]
        public void У_каждого_якоря_префабов_есть_звуки()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                {
                    checks++;
                    Check(anchor, $"{path} :: {PathOf(anchor.transform, root.transform)}", failures);
                }
            }

            Assert.Greater(checks, 0, "Контроль: якорей не найдено.");
            Assert.IsEmpty(failures, $"Якоря без звука ({failures.Count}):\n" + string.Join("\n", failures));
        }

        [Test]
        public void У_каждого_якоря_сцен_есть_звуки()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(buildScene.path);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
                    {
                        checks++;
                        Check(anchor, $"{buildScene.path} :: {PathOf(anchor.transform, null)}", failures);
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.Greater(checks, 0, "Контроль: якорей в сценах сборки не найдено.");
            Assert.IsEmpty(failures, $"Якоря сцен без звука ({failures.Count}):\n" + string.Join("\n", failures));
        }

        private static void Check(UxrGrabbableObjectAnchor anchor, string where, List<string> failures)
        {
            AnchorSound sound = anchor.GetComponent<AnchorSound>();
            if (sound == null)
            {
                failures.Add($"{where}: нет AnchorSound");
                return;
            }

            if (sound.Source == null || sound.Source.clip == null) failures.Add($"{where}: нет звука вставки (источник или его clip)");
            if (sound.TakeOutClip == null) failures.Add($"{where}: нет звука доставания (Take Out Clip)");
        }

        private static string PathOf(Transform t, Transform stop)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != stop; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
