using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Ассеты поз Grip Extraction: создание (Fixed, обе руки), запись на месте, проверка регистрации в базе поз.</summary>
    public static class GripPoseAssets
    {
        /// <summary>Поза Fixed в памяти — как <c>HandsPackPoseImporter.CreatePoseAsset</c> до записи.</summary>
        public static UxrHandPoseAsset CreateFixed(string name, UxrHandDescriptor left, UxrHandDescriptor right)
        {
            var asset = ScriptableObject.CreateInstance<UxrHandPoseAsset>();
            asset.name                = name;
            asset.Version             = UxrHandPoseAsset.CurrentVersion;
            asset.PoseType            = UxrHandPoseType.Fixed;
            asset.HandDescriptorLeft  = left;
            asset.HandDescriptorRight = right;
            return asset;
        }

        /// <summary>Временная поза для замеров; уничтожается вызывающим.</summary>
        public static UxrHandPoseAsset CreateTransient(string name, UxrHandDescriptor left, UxrHandDescriptor right)
        {
            UxrHandPoseAsset asset = CreateFixed(name, left, right);
            asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }

        /// <summary>
        /// <c>&lt;folder&gt;/&lt;name&gt;.asset</c>: существующий ассет перезаписывается на месте (<c>CopySerialized</c>) — GUID и
        /// ссылки из баз поз и записей хвата сохраняются (как прежний импортёр).
        /// </summary>
        public static UxrHandPoseAsset Write(string folder, string name, UxrHandDescriptor left, UxrHandDescriptor right)
        {
            UxrHandPoseAsset asset = CreateFixed(name, left, right);
            EnsureFolder(folder);
            string path     = $"{folder}/{name}.asset";
            var    existing = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(asset);
                return existing;
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// Проверка без записи: каждая поза должна быть в <c>_handPoses</c> базы (SDK ищет позу по имени). Возвращает
        /// предупреждения: поза не зарегистрирована или под тем же именем зарегистрирован другой ассет.
        /// </summary>
        public static List<string> CheckRegistered(string registryPath, IEnumerable<UxrHandPoseAsset> poses)
        {
            var        warnings = new List<string>();
            GameObject root     = AssetDatabase.LoadAssetAtPath<GameObject>(registryPath);
            UxrAvatar  avatar   = root != null ? root.GetComponent<UxrAvatar>() : null;
            if (avatar == null)
            {
                warnings.Add($"База поз {registryPath}: нет UxrAvatar на корне — регистрация не проверена.");
                return warnings;
            }

            SerializedProperty list = new SerializedObject(avatar).FindProperty("_handPoses");
            List<Object> registered = Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue).ToList();
            foreach (UxrHandPoseAsset pose in poses)
            {
                if (registered.Contains(pose)) continue;
                Object sameName = registered.FirstOrDefault(r => r != null && r.name == pose.name);
                warnings.Add(sameName != null
                                 ? $"Поза {pose.name}: в базе {registryPath} под этим именем другой ассет {AssetDatabase.GetAssetPath(sameName)}."
                                 : $"Поза {pose.name}: не зарегистрирована в {registryPath} — SDK её по имени не найдёт (registerPoses=true или ручная регистрация).");
            }

            return warnings;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int    slash  = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
