using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VRBattlegrounds.Editor
{
    public static class CodReaperMaterialConverter
    {
        private const string MenuRoot = "Tools/VR Battlegrounds/Third Party/CodReaper/";
        private const string MaterialRootFolder = "Assets/ThirdParty/CodReaper";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        [MenuItem(MenuRoot + "Convert All Materials To URP Lit", false, 10)]
        public static void ConvertMaterialsToUrpLit()
        {
            ConvertMaterialsInFolders(new[] { MaterialRootFolder });
        }

        [MenuItem("Assets/CodReaper/Convert Materials To URP Lit", false, 20)]
        public static void ConvertMaterialsInSelectedFolders()
        {
            string[] folders = GetSelectedFolders();
            ConvertMaterialsInFolders(folders);
        }

        [MenuItem("Assets/CodReaper/Convert Materials To URP Lit", true)]
        public static bool ValidateConvertMaterialsInSelectedFolders()
        {
            return GetSelectedFolders().Length > 0;
        }

        private static string[] GetSelectedFolders()
        {
            return Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(AssetDatabase.IsValidFolder)
                .ToArray();
        }

        private static void ConvertMaterialsInFolders(string[] searchFolders)
        {
            if (searchFolders == null || searchFolders.Length == 0) return;

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", searchFolders);
            if (materialGuids.Length == 0)
            {
                Debug.LogWarning("[CodReaper Materials] No materials found in selected folders.");
                return;
            }

            Shader urpLitShader = Shader.Find(UrpLitShaderName);
            if (urpLitShader == null)
            {
                Debug.LogError($"[CodReaper Materials] Shader not found: {UrpLitShaderName}. Make sure URP is installed and active.");
                return;
            }

            int convertedCount = 0;
            int normalImportCount = 0;
            int missingCount = 0;
            StringBuilder report = new StringBuilder();

            try
            {
                for (int i = 0; i < materialGuids.Length; i++)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                    EditorUtility.DisplayProgressBar(
                        "CodReaper Material Conversion",
                        materialPath,
                        (float)i / materialGuids.Length);

                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        missingCount++;
                        continue;
                    }

                    MaterialSnapshot snapshot = MaterialSnapshot.From(material);

                    Undo.RecordObject(material, "Convert CodReaper Material To URP Lit");
                    material.shader = urpLitShader;
                    ApplyUrpLitProperties(material, snapshot);

                    if (snapshot.NormalMap != null && EnsureNormalMapImporter(snapshot.NormalMap))
                        normalImportCount++;

                    EditorUtility.SetDirty(material);
                    convertedCount++;

                    if (convertedCount <= 12)
                    {
                        report.AppendLine(
                            $"- {material.name}: base={TextureName(snapshot.BaseMap)}, normal={TextureName(snapshot.NormalMap)}, ao={TextureName(snapshot.OcclusionMap)}, metallic={TextureName(snapshot.MetallicMap)}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (convertedCount > 12)
                report.AppendLine($"- ...and {convertedCount - 12} more materials.");

            Debug.Log(
                $"[CodReaper Materials] Converted {convertedCount} materials to URP Lit. " +
                $"Updated {normalImportCount} normal texture importers. Missing materials: {missingCount}.\n" +
                report);
        }

        private static void ApplyUrpLitProperties(Material material, MaterialSnapshot snapshot)
        {
            SetFloatIfPresent(material, "_WorkflowMode", 1f);
            SetFloatIfPresent(material, "_Surface", 0f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(material, "_AlphaClip", snapshot.AlphaClip);
            SetFloatIfPresent(material, "_Cutoff", snapshot.Cutoff);
            SetFloatIfPresent(material, "_Cull", snapshot.Cull);
            SetFloatIfPresent(material, "_ReceiveShadows", snapshot.ReceiveShadows);

            SetColorIfPresent(material, "_BaseColor", snapshot.BaseColor);
            SetColorIfPresent(material, "_Color", snapshot.BaseColor);

            SetTextureIfPresent(material, "_BaseMap", snapshot.BaseMap);
            SetTextureIfPresent(material, "_BumpMap", snapshot.NormalMap);
            SetTextureIfPresent(material, "_OcclusionMap", snapshot.OcclusionMap);
            SetTextureIfPresent(material, "_MetallicGlossMap", snapshot.MetallicMap);

            SetFloatIfPresent(material, "_BumpScale", snapshot.BumpScale);
            SetFloatIfPresent(material, "_OcclusionStrength", snapshot.OcclusionStrength);
            SetFloatIfPresent(material, "_Metallic", snapshot.MetallicValue);
            SetFloatIfPresent(material, "_Smoothness", snapshot.Smoothness);

            if (snapshot.EmissionEnabled || snapshot.EmissionMap != null)
            {
                SetTextureIfPresent(material, "_EmissionMap", snapshot.EmissionMap);
                SetColorIfPresent(material, "_EmissionColor", snapshot.EmissionColor);
            }
            else
            {
                SetTextureIfPresent(material, "_EmissionMap", null);
                SetColorIfPresent(material, "_EmissionColor", Color.black);
            }

            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = -1;

            SetKeyword(material, "_NORMALMAP", snapshot.NormalMap != null);
            SetKeyword(material, "_OCCLUSIONMAP", snapshot.OcclusionMap != null);
            SetKeyword(material, "_METALLICSPECGLOSSMAP", snapshot.MetallicMap != null);
            SetKeyword(material, "_EMISSION", snapshot.EmissionEnabled || snapshot.EmissionMap != null);
        }

        private static bool EnsureNormalMapImporter(Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return false;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.textureType == TextureImporterType.NormalMap)
                return false;

            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
            return true;
        }

        private static Texture GetTextureIfPresent(Material material, string propertyName)
        {
            return HasShaderProperty(material, propertyName, ShaderUtil.ShaderPropertyType.TexEnv)
                ? material.GetTexture(propertyName)
                : null;
        }

        private static float GetFloatIfPresent(Material material, string propertyName, float fallback)
        {
            return HasShaderProperty(material, propertyName, ShaderUtil.ShaderPropertyType.Float) ||
                HasShaderProperty(material, propertyName, ShaderUtil.ShaderPropertyType.Range)
                    ? material.GetFloat(propertyName)
                    : fallback;
        }

        private static Color GetColorIfPresent(Material material, string propertyName, Color fallback)
        {
            return HasShaderProperty(material, propertyName, ShaderUtil.ShaderPropertyType.Color)
                ? material.GetColor(propertyName)
                : fallback;
        }

        private static void SetTextureIfPresent(Material material, string propertyName, Texture texture)
        {
            if (material.HasProperty(propertyName))
                material.SetTexture(propertyName, texture);
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetColorIfPresent(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        private static string TextureName(Texture texture)
        {
            return texture != null ? texture.name : "none";
        }

        private static bool HasShaderProperty(Material material, string propertyName, ShaderUtil.ShaderPropertyType propertyType)
        {
            if (material == null || material.shader == null)
                return false;

            int propertyCount = ShaderUtil.GetPropertyCount(material.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyName(material.shader, i) == propertyName &&
                    ShaderUtil.GetPropertyType(material.shader, i) == propertyType)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct MaterialSnapshot
        {
            public readonly Texture BaseMap;
            public readonly Texture NormalMap;
            public readonly Texture OcclusionMap;
            public readonly Texture MetallicMap;
            public readonly Texture EmissionMap;
            public readonly Color BaseColor;
            public readonly Color EmissionColor;
            public readonly float AlphaClip;
            public readonly float BumpScale;
            public readonly float Cull;
            public readonly float Cutoff;
            public readonly float MetallicValue;
            public readonly float OcclusionStrength;
            public readonly float ReceiveShadows;
            public readonly float Smoothness;
            public readonly bool EmissionEnabled;

            private MaterialSnapshot(
                Texture baseMap,
                Texture normalMap,
                Texture occlusionMap,
                Texture metallicMap,
                Texture emissionMap,
                Color baseColor,
                Color emissionColor,
                float alphaClip,
                float bumpScale,
                float cull,
                float cutoff,
                float metallicValue,
                float occlusionStrength,
                float receiveShadows,
                float smoothness,
                bool emissionEnabled)
            {
                BaseMap = baseMap;
                NormalMap = normalMap;
                OcclusionMap = occlusionMap;
                MetallicMap = metallicMap;
                EmissionMap = emissionMap;
                BaseColor = baseColor;
                EmissionColor = emissionColor;
                AlphaClip = alphaClip;
                BumpScale = bumpScale;
                Cull = cull;
                Cutoff = cutoff;
                MetallicValue = metallicValue;
                OcclusionStrength = occlusionStrength;
                ReceiveShadows = receiveShadows;
                Smoothness = smoothness;
                EmissionEnabled = emissionEnabled;
            }

            public static MaterialSnapshot From(Material material)
            {
                Texture baseMap = GetTextureIfPresent(material, "_BaseMap");
                Texture normalMap = GetTextureIfPresent(material, "_BumpMap");
                Texture occlusionMap = GetTextureIfPresent(material, "_AO") ?? GetTextureIfPresent(material, "_OcclusionMap");
                Texture metallicMap = GetTextureIfPresent(material, "_Metallic") ?? GetTextureIfPresent(material, "_MetallicGlossMap");
                Texture emissionMap = GetTextureIfPresent(material, "_EmissionMap");

                Color baseColor = GetColorIfPresent(
                    material,
                    "_BaseColor",
                    GetColorIfPresent(material, "_Color", Color.white));

                Color emissionColor = GetColorIfPresent(material, "_EmissionColor", Color.black);
                float smoothness = GetFloatIfPresent(
                    material,
                    "_SmoothMultiplier",
                    GetFloatIfPresent(material, "_Smoothness", 0.5f));

                return new MaterialSnapshot(
                    baseMap,
                    normalMap,
                    occlusionMap,
                    metallicMap,
                    emissionMap,
                    baseColor,
                    emissionColor,
                    GetFloatIfPresent(material, "_AlphaClip", 0f),
                    GetFloatIfPresent(material, "_BumpScale", 1f),
                    GetFloatIfPresent(material, "_Cull", 2f),
                    GetFloatIfPresent(material, "_Cutoff", 0.5f),
                    GetFloatIfPresent(material, "_Metallic", 0f),
                    GetFloatIfPresent(material, "_OcclusionStrength", 1f),
                    GetFloatIfPresent(material, "_ReceiveShadows", 1f),
                    Mathf.Clamp01(smoothness),
                    GetFloatIfPresent(material, "_Emission", 0f) > 0f);
            }
        }
    }
}
