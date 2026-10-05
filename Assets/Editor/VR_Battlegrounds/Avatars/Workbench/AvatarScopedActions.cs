using System;
using System.Linq;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.EditorTools;
using VrBattlegrounds.EditorTools.VersionControl;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Узкие действия над явно переданным префабом. Selection не является входом writer.</summary>
    internal static class AvatarScopedActions
    {
        internal static string[] Inputs(AvatarEditorContext target, params string[] extra) =>
            target.PrefabChain.Concat(new[] { target.AssetPath, AssetDatabase.GetAssetPath(target.Data) })
                .Concat(extra).Where(p => !string.IsNullOrEmpty(p))
                .SelectMany(p => p.StartsWith("Assets/", StringComparison.Ordinal) && AssetDatabase.LoadMainAssetAtPath(p)
                    ? AssetDatabase.GetDependencies(p, true) : new[] { p }).Distinct().ToArray();

        internal static string SnapshotPath(string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return path;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null) throw new InvalidOperationException("Не разрешён package dependency: " + path);
            return System.IO.Path.Combine(package.resolvedPath, path.Substring(package.assetPath.Length).TrimStart('/')).Replace('\\', '/');
        }

        internal static string EditPrefab(string path, Func<UxrAvatar, string> edit)
        {
            RequireOwnPrefab(path);
            var root = PrefabUtility.LoadPrefabContents(path);
            string result;
            try
            {
                var avatar = root.GetComponent<UxrAvatar>();
                if (!avatar) throw new InvalidOperationException("На корне prefab отсутствует UxrAvatar.");
                result = edit(avatar);
                SetCanonicalAssetId(root, path);
                if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new InvalidOperationException("Prefab не сохранён: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            NormalizeAndVerify(path);
            return result + "\n" + path;
        }

        internal static string Fingertips(string path) => EditPrefab(path, avatar =>
        {
            if (!AvatarFingertipSetup.CanSetup(avatar)) throw new InvalidOperationException("Кости указательных пальцев не найдены; prefab не сохранён.");
            AvatarFingertipSetup.Setup(avatar);
            return "UI fingertips настроены для выбранного аватара.";
        });

        internal static string Renderers(string path) => EditPrefab(path, avatar =>
        {
            // Виртуальная SDK-кисть не принадлежит видимому телу; её исключает тот же typed backend.
            return FixAvatarRenderers.Setup(avatar);
        });

        internal static string Pockets(string path) => EditPrefab(path, avatar => AvatarPocketSetup.Setup(avatar.gameObject));
        internal static string Icon(AvatarData data) => AvatarIconRenderer.Render(data);

        internal static void RequireOwnPrefab(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Writer работает только с собственными prefab в Assets/Prefabs. Создайте собственный вариант vendor-ассета.");
        }
        internal static void NormalizeAndVerify(string path)
        {
            NetworkAssetIdNormalizer.Normalize(new[] { path }, false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab && prefab.GetComponent<Mirror.NetworkIdentity>() && NetworkAssetIdNormalizer.ReadOnDisk(path) !=
                Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path))))
                throw new InvalidOperationException("Не подтверждён канонический Mirror assetId: " + path);
        }
        internal static void SetCanonicalAssetId(GameObject root, string path)
        {
            var identity = root.GetComponent<Mirror.NetworkIdentity>();
            if (!identity) return;
            var network = new SerializedObject(identity);
            network.FindProperty("_assetId").longValue = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
            network.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
