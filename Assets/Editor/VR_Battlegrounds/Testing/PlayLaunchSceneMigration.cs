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
        private const string AvatarStandsFolder = "Assets/Scenes/Stands/Avatar";
        private const string ArsenalReviewScene = "Assets/Scenes/Stands/Arsenal/CommonArsenalReview.unity";

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
                // Прежний охват (бывшие Scenes/Dev и Scenes/Tools): стенды аватара и обзор арсенала.
                // Пустая ArsenalLayoutAuthoring в той же папке Stands/Arsenal разметке не подлежит.
                var paths = AssetDatabase.IsValidFolder(AvatarStandsFolder)
                    ? AssetDatabase.FindAssets("t:Scene", new[] { AvatarStandsFolder }).Select(AssetDatabase.GUIDToAssetPath).ToList()
                    : new List<string>();
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ArsenalReviewScene) != null) paths.Add(ArsenalReviewScene);
                    foreach (string path in paths)
                    {
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
