using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Одноразовая принятая разметка standalone fixtures. Не вызывается при каждом Play.</summary>
    public static class PlayLaunchSceneMigration
    {
        [Serializable] public sealed class Result { public bool Passed; public int Marked; public string[] Paths; public string[] Skipped; }
        public static Result ApplyStandaloneMarkers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Migration requires idle editor.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++) if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Migration refuses dirty scenes.");
            var original = EditorSceneManager.GetSceneManagerSetup();
            var changed = new List<string>(); var skipped = new List<string>();
            try
            {
                var folders = new[] { "Assets/Scenes/Dev", "Assets/Scenes/Tools" }.Where(AssetDatabase.IsValidFolder).ToArray();
                if (folders.Length > 0)
                    foreach (string guid in AssetDatabase.FindAssets("t:Scene", folders))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                        var roots = scene.GetRootGameObjects();
                        if (roots.Any(r => r.GetComponentInChildren<MapRoot>(true) != null || r.GetComponentInChildren<GameNetworkDiscovery>(true) != null))
                        { skipped.Add(path); continue; }
                        if (roots.Any(r => r.GetComponentInChildren<StandaloneSceneMarker>(true) != null)) continue;
                        new GameObject("__StandaloneSceneMarker").AddComponent<StandaloneSceneMarker>();
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("SceneSaveFailed:" + path);
                        changed.Add(path);
                    }
                return new Result { Passed = true, Marked = changed.Count, Paths = changed.ToArray(), Skipped = skipped.ToArray() };
            }
            finally
            {
                if (original.Length > 0 && original.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(original);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
