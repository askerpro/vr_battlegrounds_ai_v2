using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Game-owned материал комплекта MX68 (URP Lit) из материала пака <c>M_MX68_03</c>. В отличие от
    /// <c>KinemationMaterials.Convert</c> не правит .meta текстур пака: цвет и нормаль копируются в 1024 PNG рядом с
    /// комплектом, металличность и шероховатость упаковываются в одну карту так же, как там. Пишет только
    /// <see cref="MaterialPath"/> и <see cref="TextureFolder"/>; вызывает его <see cref="SightKitBuilder"/>.
    /// </summary>
    public static class IronKitMaterials
    {
        public const string SourceMaterialPath = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/MX68/Materials/M_MX68_03.mat";
        public const string MaterialPath = SightKitBuilder.ArtFolder + "/M_MX68_IronSights.mat";
        public const string TextureFolder = SightKitBuilder.ArtFolder + "/Textures";
        public const int TextureSize = 1024;
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string BaseColorPath = TextureFolder + "/T_MX68_IronSights_BaseColor.png";
        private const string NormalPath = TextureFolder + "/T_MX68_IronSights_Normal.png";
        private const string MetallicSmoothnessPath = TextureFolder + "/T_MX68_IronSights_MetallicSmoothness.png";

        public static string[] OutputPaths() => new[] { MaterialPath, BaseColorPath, NormalPath, MetallicSmoothnessPath };

        /// <summary>Read-only проверка входа: материал пака и его карты на месте.</summary>
        public static string Preflight()
        {
            Material src = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
            if (src == null) throw new InvalidOperationException("Нет материала пака MX68: " + SourceMaterialPath);
            if (Tex(src, "_BC") == null) throw new InvalidOperationException("У материала пака MX68 нет карты цвета _BC");
            if (Shader.Find(LitShader) == null) throw new InvalidOperationException("Нет шейдера " + LitShader);
            if (File.Exists(MaterialPath) && AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
                throw new InvalidOperationException("Путь материала комплекта занят ассетом другого типа: " + MaterialPath);
            return "MX68: " + src.name + " → " + MaterialPath;
        }

        /// <summary>Создаёт или обновляет материал и карты; повторный запуск переписывает те же ассеты (GUID сохраняются).</summary>
        public static Material Ensure()
        {
            Preflight();
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Texture2D existingBase = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath);
            if (existing != null && existing.shader != null && existing.shader.name == LitShader && existingBase != null
                && existing.GetTexture("_BaseMap") == existingBase)
                return existing; // Уже собран: карты пака не перечитываются, ассеты не переписываются.
            Material src = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
            SightKitBuilder.EnsureFolder(TextureFolder);

            Texture2D baseColor = WriteTexture(BaseColorPath, Tex(src, "_BC"), Kind.Color, src);
            Texture2D normal = Tex(src, "_Normal") != null ? WriteTexture(NormalPath, Tex(src, "_Normal"), Kind.Normal, src) : null;
            Texture2D packed = Tex(src, "_Metallic") != null || Tex(src, "_Roughness") != null
                ? WriteTexture(MetallicSmoothnessPath, null, Kind.MetallicSmoothness, src) : null;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find(LitShader)) { name = Path.GetFileNameWithoutExtension(MaterialPath) };
            else mat.shader = Shader.Find(LitShader);
            Color tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.white;
            mat.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            mat.SetTexture("_BaseMap", baseColor);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (packed != null)
            {
                mat.SetTexture("_MetallicGlossMap", packed);
                mat.SetFloat("_Metallic", 1f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetFloat("_Metallic", .5f);
                mat.SetFloat("_Smoothness", .4f);
            }
            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(mat, MaterialPath);
            else EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        private enum Kind { Color, Normal, MetallicSmoothness }

        private static Texture Tex(Material m, string property) => m.HasProperty(property) ? m.GetTexture(property) : null;

        private static Texture2D WriteTexture(string path, Texture source, Kind kind, Material src)
        {
            Color[] pixels;
            bool linear = kind != Kind.Color;
            switch (kind)
            {
                case Kind.Color:
                    pixels = Read(source, srgb: true);
                    break;
                case Kind.Normal:
                    pixels = Read(source, srgb: false);
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        // Как UnpackNormalmapRGorAG: x = r·a (DXT5nm хранит x в alpha, BC5/RGB — в red), z восстанавливается.
                        Color p = pixels[i];
                        float x = p.r * p.a * 2f - 1f, y = p.g * 2f - 1f;
                        float z = Mathf.Sqrt(Mathf.Clamp01(1f - x * x - y * y));
                        pixels[i] = new Color(x * .5f + .5f, y * .5f + .5f, z * .5f + .5f, 1f);
                    }
                    break;
                default:
                    pixels = PackMetallicSmoothness(src);
                    break;
            }
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, linear);
            try
            {
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = kind == Kind.Color;
            importer.alphaSource = kind == Kind.MetallicSmoothness ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.maxTextureSize = TextureSize;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = TextureSize,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 50
            });
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>R — металличность, A — гладкость; диапазоны пака (_MetallicRemapping/_RoughnessRemapping).</summary>
        private static Color[] PackMetallicSmoothness(Material src)
        {
            Vector2 mRemap = Remap(src, "_MetallicRemapping"), rRemap = Remap(src, "_RoughnessRemapping");
            Color[] m = Read(Tex(src, "_Metallic"), srgb: false), r = Read(Tex(src, "_Roughness"), srgb: false);
            var pixels = new Color[TextureSize * TextureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                float metal = Mathf.Lerp(mRemap.x, mRemap.y, m != null ? m[i].r : 0f);
                float rough = Mathf.Lerp(rRemap.x, rRemap.y, r != null ? r[i].r : 1f);
                pixels[i] = new Color(metal, metal, metal, 1f - rough);
            }
            return pixels;
        }

        private static Vector2 Remap(Material m, string property)
        {
            if (!m.HasProperty(property)) return new Vector2(0f, 1f);
            Color c = m.GetColor(property);
            return new Vector2(c.r, c.g);
        }

        /// <summary>Пиксели карты пака в <see cref="TextureSize"/> через рендер-текстуру, без флага Read/Write у TGA.</summary>
        private static Color[] Read(Texture texture, bool srgb)
        {
            if (texture == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32,
                                                          srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                read = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, !srgb);
                read.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
                read.Apply();
                return read.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
            }
        }
    }
}
