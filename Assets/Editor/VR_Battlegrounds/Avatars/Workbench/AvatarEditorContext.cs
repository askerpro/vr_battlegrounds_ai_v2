using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    public enum AvatarEditorContextKind { Prefab, Instance, PrefabStage, Model }

    /// <summary>Явная цель инструмента; разрешение не создаёт объекты и не меняет выделение.</summary>
    public sealed class AvatarEditorContext
    {
        public UnityEngine.Object Selected;
        public GameObject Root;
        public UxrAvatar Avatar;
        public AvatarData Data;
        public AvatarData[] DataCandidates = Array.Empty<AvatarData>();
        public AvatarEditorContextKind Kind;
        public string AssetPath, AssetGuid;
        public string[] PrefabChain = Array.Empty<string>();
        public string ObjectId, SceneId;
        public bool IsAsset => Kind == AvatarEditorContextKind.Prefab || Kind == AvatarEditorContextKind.Model;
        public bool IsValid => Root && Root.GetEntityId().ToString() == ObjectId &&
            (IsAsset || Root.scene.IsValid() && Root.scene.handle.ToString() == SceneId);
        public string Identity => AssetGuid + ":" + ObjectId + ":" + SceneId;
    }

    public static class AvatarEditorContextResolver
    {
        public static bool TryResolve(UnityEngine.Object selected, out AvatarEditorContext context, out string reason)
        {
            context = null;
            reason = "Выберите AvatarData, аватар, его дочерний объект или исходную модель.";
            if (!selected) return false;
            AvatarData data = selected as AvatarData;
            GameObject root = data ? data.prefab : selected as GameObject;
            if (selected is Component component) root = component.gameObject;
            if (!root) { if (data) reason = "У AvatarData не назначен префаб."; return false; }

            UxrAvatar avatar = root.GetComponentInParent<UxrAvatar>(true);
            if (!avatar)
            {
                var candidates = root.GetComponentsInChildren<UxrAvatar>(true);
                if (candidates.Length > 1) { reason = "В объекте несколько аватаров: выберите корень конкретного аватара."; return false; }
                avatar = candidates.SingleOrDefault();
            }
            if (avatar) root = avatar.gameObject;
            bool persistent = EditorUtility.IsPersistent(root);
            string path = persistent ? AssetDatabase.GetAssetPath(root) :
                AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(root));
            bool model = !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter;
            if (!avatar && !model) { reason = "У выбранного объекта нет UxrAvatar; это также не импортированная модель."; return false; }
            if (model && persistent && !avatar) root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var kind = persistent ? (model ? AvatarEditorContextKind.Model : AvatarEditorContextKind.Prefab) :
                stage != null && stage.scene == root.scene ? AvatarEditorContextKind.PrefabStage : AvatarEditorContextKind.Instance;
            if (kind == AvatarEditorContextKind.PrefabStage) path = stage.assetPath;
            var dataCandidates = FindData(path);
            context = new AvatarEditorContext {
                Selected = selected, Root = root, Avatar = avatar, Data = data ? data : dataCandidates.Length == 1 ? dataCandidates[0] : null,
                DataCandidates = dataCandidates, Kind = kind, AssetPath = path ?? "", AssetGuid = AssetDatabase.AssetPathToGUID(path ?? ""),
                ObjectId = root.GetEntityId().ToString(), SceneId = persistent ? "" : root.scene.handle.ToString(), PrefabChain = Chain(path).ToArray()
            };
            reason = null;
            return true;
        }

        public static IEnumerable<string> Chain(string path)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(path) && seen.Add(path))
            {
                yield return path;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) yield break;
                var parent = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                path = parent ? AssetDatabase.GetAssetPath(parent) : null;
            }
        }

        static AvatarData[] FindData(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath)) return Array.Empty<AvatarData>();
            return AssetDatabase.FindAssets("t:AvatarData", new[] { "Assets/Data", "Assets/Resources" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AvatarData>)
                .Where(d => d && d.prefab && AssetDatabase.GetAssetPath(d.prefab) == prefabPath).ToArray();
        }
    }
}
