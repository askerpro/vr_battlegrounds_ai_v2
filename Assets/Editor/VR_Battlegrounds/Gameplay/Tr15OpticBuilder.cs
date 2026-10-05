using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Только линза XPS2 у TR15: source alpha выделяет её в отдельный прозрачный submesh.</summary>
    public static class Tr15OpticBuilder
    {
        public const string WeaponPath = "Assets/Prefabs/Weapons/TR15/TR15.prefab";
        public const string OpticName = "SM_Attach_AR15_XPS2";
        public const string MeshPath = "Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_XPS2_Optic.asset";
        public const string MaterialPath = "Assets/Art/Weapons/Kinemation/TR15/Materials/TR15_XPS2_Lens.mat";
        public const string TexturePath = "Assets/Art/Weapons/Kinemation/TR15/Materials/TR15_XPS2_Lens.png";
        private const string RawMeshPath = "Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_SM_Attach_AR15_XPS2.asset";
        private const string HousingMaterialPath = "Assets/Art/Weapons/Kinemation/Materials/M_TR15_Attachments.mat";
        private const string SourceTextures = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/TR15/Textures/";

        public static string[] InputPaths() => new[] { WeaponPath, RawMeshPath, HousingMaterialPath,
            SourceTextures + "T_TR15_Attachments_BaseColor.TGA", SourceTextures + "T_TR15_Attachments_Alpha.TGA",
            "Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader" };
        public static string[] OutputPaths() => new[] { WeaponPath, MeshPath, MaterialPath, TexturePath };

        /// <summary>Read-only: проверяет binding и маску до первого writer, не импортирует источник.</summary>
        public static string Preflight()
        {
            ReadInputs(out _, out _, out _, out _, out _);
            return "TR15 XPS2: housing=1648, lens=52; vertices/channels/triangle union сохраняются";
        }

        private static void ReadInputs(out Mesh raw, out Texture2D baseTexture, out Shader shader,
                                       out List<int> housing, out List<int> lens)
        {
            raw = AssetDatabase.LoadAssetAtPath<Mesh>(RawMeshPath);
            baseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTextures + "T_TR15_Attachments_BaseColor.TGA");
            var maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTextures + "T_TR15_Attachments_Alpha.TGA");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
            if (raw == null || baseTexture == null || maskTexture == null || shader == null ||
                asset == null)
                throw new InvalidOperationException("TR15 XPS2: отсутствует исходный меш, alpha/color или URP Unlit");
            if (!raw.isReadable || raw.subMeshCount != 1 || raw.blendShapeCount != 0)
                throw new InvalidOperationException("TR15 XPS2: требуется неизменённый game-owned исходный меш с одним submesh");
            if ((File.Exists(MeshPath) && AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) == null) ||
                (File.Exists(MaterialPath) && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null) ||
                (File.Exists(TexturePath) && AssetImporter.GetAtPath(TexturePath) is not TextureImporter))
                throw new InvalidOperationException("TR15 XPS2: output path занят чужим типом ассета или неимпортированным файлом");
            ValidateBinding(asset);
            Color32[] mask = Read(maskTexture, maskTexture.width, maskTexture.height);
            Vector2[] uv = raw.uv;
            int[] triangles = raw.GetTriangles(0);
            if (uv.Length != raw.vertexCount || triangles.Length % 3 != 0)
                throw new InvalidOperationException("TR15 XPS2: отсутствуют полные UV/треугольники исходного меша");
            housing = new List<int>();
            lens = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 at = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3f;
                int x = Mathf.RoundToInt(Mathf.Clamp01(at.x) * (maskTexture.width - 1));
                int y = Mathf.RoundToInt(Mathf.Clamp01(at.y) * (maskTexture.height - 1));
                var target = mask[y * maskTexture.width + x].r < 128 ? lens : housing;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            // У подтверждённого source ровно 52 треугольника окна и 1648 корпуса. Изменённый пак требует нового аудита.
            if (lens.Count != 52 * 3 || housing.Count != 1648 * 3)
                throw new InvalidOperationException($"TR15 XPS2: изменённая геометрия/UV, housing={housing.Count / 3}, lens={lens.Count / 3}");
        }

        private static MeshFilter ValidateBinding(GameObject root)
        {
            MeshFilter filter = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == OpticName);
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterials.Length < 1 || renderer.sharedMaterials[0] == null ||
                (AssetDatabase.GetAssetPath(filter.sharedMesh) != RawMeshPath && AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshPath) ||
                AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]) != HousingMaterialPath)
                throw new InvalidOperationException("TR15 XPS2: чужой mesh/material binding; калибровка отказывается его перезаписывать");
            return filter;
        }

        public static void Apply()
        {
            WeaponModelPreviewScope.CheckEditor();
            ReadInputs(out Mesh raw, out Texture2D baseTexture, out Shader shader, out List<int> housing, out List<int> lens);
            var root = PrefabUtility.LoadPrefabContents(WeaponPath);
            try
            {
                MeshFilter filter = ValidateBinding(root);
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                Material housingMaterial = renderer.sharedMaterials[0];
                KinemationWeapon.EnsureFolder("Assets/Art/Weapons/Kinemation/TR15/Materials");
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                bool create = saved == null;
                if (create) saved = new Mesh();
                try
                {
                    UpdateMesh(saved, raw, housing, lens);
                    if (create) AssetDatabase.CreateAsset(saved, MeshPath);
                    else EditorUtility.SetDirty(saved);
                    AssetDatabase.SaveAssetIfDirty(saved);
                    filter.sharedMesh = saved;
                }
                catch { if (create && !EditorUtility.IsPersistent(saved)) UnityEngine.Object.DestroyImmediate(saved); throw; }
                Texture2D lensTexture = BuildTexture(baseTexture);
                Material lensMaterial = BuildMaterial(shader, lensTexture);
                renderer.sharedMaterials = new[] { housingMaterial, lensMaterial };
                // Collider geometry остаётся прежней: объединение двух render submeshes точно равно исходному мешу.
                if (PrefabUtility.SaveAsPrefabAsset(root, WeaponPath) == null)
                    throw new InvalidOperationException("TR15 XPS2: сохранение префаба не удалось");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void UpdateMesh(Mesh target, Mesh source, List<int> housing, List<int> lens)
        {
            // CopySerialized может оставить старый GPU buffer. Mesh API обновляет его, сохраняя сам asset/GUID.
            // Копируются сырые vertex streams: normals/tangents/colors/все UV не теряют формат или размерность.
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            {
                target.Clear(false);
                target.indexFormat = source.indexFormat;
                target.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    var bytes = data[0].GetVertexData<byte>(stream);
                    target.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream);
                }
                target.bindposes = source.bindposes;
                target.subMeshCount = 2;
                target.SetTriangles(housing, 0, false);
                target.SetTriangles(lens, 1, false);
                target.bounds = source.bounds;
                target.name = "TR15_XPS2_Optic";
                target.UploadMeshData(false);
            }
        }

        private static Texture2D BuildTexture(Texture2D source)
        {
            const int size = KinemationMaterials.TextureSize;
            Color32[] pixels = Read(source, size, size);
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                // Красная метка уже нарисована автором внутри UV линзы; она остаётся видимой без непрозрачного фона.
                bool reticle = p.r > 50 && p.r > p.g * 1.35f && p.r > p.b * 1.35f;
                p.a = reticle ? (byte)255 : (byte)15;
                pixels[i] = p;
            }
            var generated = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            byte[] png;
            try { generated.SetPixels32(pixels); generated.Apply(); png = generated.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(generated); }
            if (!File.Exists(TexturePath) || !File.ReadAllBytes(TexturePath).SequenceEqual(png))
            {
                File.WriteAllBytes(TexturePath, png);
                AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            bool changed = !importer.sRGBTexture || importer.alphaSource != TextureImporterAlphaSource.FromInput ||
                           !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != size;
            if (changed)
            {
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }
            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            KinemationMaterials.LimitForAndroid(saved); // Только новый game-owned importer; source TGA не меняется.
            return saved;
        }

        private static Material BuildMaterial(Shader shader, Texture texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool create = material == null;
            if (create) material = new Material(shader) { name = "TR15_XPS2_Lens" };
            material.shader = shader;
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f); material.SetFloat("_AlphaClip", 0f); material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(material, MaterialPath);
            else EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Color32[] Read(Texture source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D read = null;
            try
            {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                read = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0); read.Apply();
                return read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
