// Standalone-проверка отдельного проекта каталога; в игровую Assets не устанавливать.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class CatalogImportValidation
{
    [Serializable] private class Entry { public string path, guid; }
    [Serializable] private class Snapshot { public Entry[] pack; }
    [Serializable] private class Report
    {
        public bool passed, compilationFailed, renderPipelineAvailable;
        public int expectedAssets, sceneAssets, previewRoots, previewRenderers, previewMissingScripts;
        public string unityVersion, previewScene;
        public string[] errors;
    }

    public static void Run()
    {
        var errors = new List<string>();
        var result = new Report { unityVersion = Application.unityVersion,
            compilationFailed = EditorUtility.scriptCompilationFailed,
            renderPipelineAvailable = GraphicsSettings.defaultRenderPipeline != null };
        try
        {
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText("Migration/retention-unity.json"));
            result.expectedAssets = snapshot.pack.Length;
            foreach (var item in snapshot.pack)
            {
                if (!File.Exists(item.path) || AssetDatabase.AssetPathToGUID(item.path) != item.guid)
                    errors.Add("Файл/GUID: " + item.path);
                if (item.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(item.path) == null)
                        errors.Add("SceneAsset: " + item.path);
                    else result.sceneAssets++;
                }
            }
            result.previewScene = "Assets/env_packs/RPG_FPS_game_assets_industrial/Map_v1.unity";
            var scene = EditorSceneManager.OpenPreviewScene(result.previewScene);
            try
            {
                var roots = scene.GetRootGameObjects();
                result.previewRoots = roots.Length;
                result.previewRenderers = roots.Sum(r => r.GetComponentsInChildren<Renderer>(true).Length);
                result.previewMissingScripts = roots.Sum(r => r.GetComponentsInChildren<Transform>(true)
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
                if (result.previewRoots == 0 || result.previewRenderers == 0)
                    errors.Add("Демо Industrial не содержит отображаемой геометрии");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); }
        if (result.compilationFailed) errors.Add("Ошибки компиляции");
        if (!result.renderPipelineAvailable) errors.Add("URP не назначен");
        result.errors = errors.ToArray();
        result.passed = errors.Count == 0;
        Directory.CreateDirectory("Migration");
        File.WriteAllText("Migration/import-verification.json", JsonUtility.ToJson(result, true));
        EditorApplication.Exit(result.passed ? 0 : 1);
    }
}
