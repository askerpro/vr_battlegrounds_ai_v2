using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// На картах и в лобби играет фоновый эмбиент (пользователь: «в игре тихо как в гробу»): зацикленный, стартует
    /// сам, не объёмный (фон, а не точка в мире). Offline — стартовый экран без мира, эмбиент не требуется.
    /// </summary>
    public class AmbienceTests
    {
        private static readonly string[] WithoutWorld = { "Assets/Scenes/Offline.unity" };

        [Test]
        public void В_каждой_сцене_мира_есть_эмбиент()
        {
            var failures = new List<string>();
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                if (WithoutWorld.Contains(buildScene.path)) continue;
                Scene scene = EditorSceneManager.OpenPreviewScene(buildScene.path);
                try
                {
                    bool found = scene.GetRootGameObjects()
                                      .SelectMany(r => r.GetComponentsInChildren<AudioSource>(true))
                                      .Any(s => s.clip != null && s.loop && s.playOnAwake && s.spatialBlend < 0.5f && s.gameObject.activeInHierarchy);
                    if (!found) failures.Add($"{buildScene.path}: нет зацикленного фонового звука (loop, Play On Awake, 2D)");
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
