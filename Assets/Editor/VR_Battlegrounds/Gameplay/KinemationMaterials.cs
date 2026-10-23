using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Материалы оружия KINEMATION под Quest (T-39): URP Lit вместо Shader Graph пака.
    ///
    /// <para>
    /// Шейдер пака <c>Shader_TacticalShooterWeapon</c> читает шесть карт (цвет, нормаль, металличность,
    /// шероховатость отдельно, эмиссия, альфа), материалы сделаны под HDRP. URP Lit — три карты: цвет, нормаль
    /// и упакованная <c>_MetallicGlossMap</c> (R — металличность, A — гладкость). Упакованная карта
    /// генерируется из двух карт пака так же, как их считает шейдер пака: металличность = lerp(x, y, карта),
    /// гладкость = lerp(x, y, 1 − карта шероховатости) (<c>_MetallicRemapping</c>, <c>_RoughnessRemapping</c>),
    /// карты — в том виде, в каком их видит шейдер (импорт пака sRGB, значит линеаризованные), 1024.
    /// Эмиссия и альфа не берутся: у оружия пака <c>_EmissionStrength</c> = 0, поверхность непрозрачная.
    /// </para>
    ///
    /// <para>
    /// Уже созданные материалы <see cref="Convert" /> не пересобирает; карты металличности и гладкости
    /// всех материалов <see cref="MaterialsFolder" /> пересчитывает <see cref="RepackAll" /> (те же PNG, GUID сохраняются).
    /// </para>
    ///
    /// <para>
    /// Цвет и нормаль — текстуры пака; для Android им ставится переопределение 1024 ASTC 6×6 (правка только
    /// <c>.meta</c> пака, сам TGA не меняется). Материалы пака не трогаются.
    /// </para>
    /// </summary>
    public static class KinemationMaterials
    {
        public const int TextureSize = 1024;
        public const string Shader = "Universal Render Pipeline/Lit";
        public const string MaterialsFolder = KinemationWeapon.ArtRoot + "/Materials";
        private const string PackWeapons = KinemationWeapon.Pack + "Meshes/Weapons";

        public static Material Convert(Material src, string folder)
        {
            KinemationWeapon.EnsureFolder(folder);
            KinemationWeapon.EnsureFolder($"{folder}/Textures");
            string path = $"{folder}/{src.name}.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.shader != null && mat.shader.name == Shader && mat.GetTexture("_BaseMap") != null) return mat;

            bool create = mat == null;
            if (create) mat = new Material(UnityEngine.Shader.Find(Shader)) { name = src.name };
            else mat.shader = UnityEngine.Shader.Find(Shader);

            Texture baseColor = Tex(src, "_BC");
            Texture normal = Tex(src, "_Normal");
            Texture metallic = Tex(src, "_Metallic");
            Texture roughness = Tex(src, "_Roughness");

            Color tint = src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.white;
            mat.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            mat.SetTexture("_BaseMap", baseColor);
            LimitForAndroid(baseColor);

            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
                LimitForAndroid(normal);
            }

            Texture2D packed = PackMetallicSmoothness(src, metallic, roughness, $"{folder}/Textures/{src.name}_MetallicSmoothness.png");
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
                mat.SetFloat("_Metallic", 0.5f);
                mat.SetFloat("_Smoothness", 0.4f);
            }

            mat.SetFloat("_Surface", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.enableInstancing = true;

            if (create) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Пересчитать <c>_MetallicGlossMap</c> всех материалов <see cref="MaterialsFolder" /> из одноимённых материалов
        /// пака. Материал без пары в паке пропускается; материал и его остальные карты не меняются.
        /// </summary>
        [MenuItem("Tools/VR Battlegrounds/Weapons/KINEMATION: перепаковать металличность и гладкость")]
        public static void RepackAll()
        {
            int done = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Material src = PackMaterial(mat.name);
                if (src == null)
                {
                    GameLog.WeaponSystem.Warning($"KINEMATION: у {path} нет материала пака, карта не пересчитана");
                    continue;
                }

                Texture2D packed = PackMetallicSmoothness(src, Tex(src, "_Metallic"), Tex(src, "_Roughness"),
                                                          $"{MaterialsFolder}/Textures/{mat.name}_MetallicSmoothness.png");
                if (packed == null) continue;
                mat.SetTexture("_MetallicGlossMap", packed);
                EditorUtility.SetDirty(mat);
                done++;
            }
            AssetDatabase.SaveAssets();
            GameLog.WeaponSystem.Info($"KINEMATION: карты металличности и гладкости пересчитаны — {done}");
        }

        private static Material PackMaterial(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:Material {name}", new[] { PackWeapons }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name) return m;
            }
            return null;
        }

        private static Texture Tex(Material m, string property) => m.HasProperty(property) ? m.GetTexture(property) : null;

        /// <summary>Remap пака: <c>(x, y)</c> — значение при карте 0 и 1.</summary>
        private static Vector2 Remap(Material m, string property, Vector2 fallback)
        {
            if (!m.HasProperty(property)) return fallback;
            Color c = m.GetColor(property);
            return new Vector2(c.r, c.g);
        }

        /// <summary>
        /// Пиксель <c>_MetallicGlossMap</c> (R — металличность, A — гладкость) из значений карт пака, как их считает
        /// шейдер пака: диапазон гладкости — к инвертированной карте, а не инверсия диапазона шероховатости.
        /// </summary>
        public static Color PackPixel(float metallicMap, float roughnessMap, Vector2 metallicRemap, Vector2 roughnessRemap)
        {
            float metal = Mathf.Lerp(metallicRemap.x, metallicRemap.y, metallicMap);
            float smooth = Mathf.Lerp(roughnessRemap.x, roughnessRemap.y, 1f - roughnessMap);
            return new Color(metal, metal, metal, smooth);
        }

        private static Texture2D PackMetallicSmoothness(Material src, Texture metallic, Texture roughness, string path)
        {
            if (metallic == null && roughness == null) return null;

            Vector2 mRemap = Remap(src, "_MetallicRemapping", new Vector2(0f, 1f));
            Vector2 rRemap = Remap(src, "_RoughnessRemapping", new Vector2(0f, 1f));
            Color[] m = Read(metallic);
            Color[] r = Read(roughness);

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true);
            var pixels = new Color[TextureSize * TextureSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = PackPixel(m != null ? m[i].r : 0f, r != null ? r[i].r : 1f, mRemap, rRemap);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = TextureSize;
            importer.SetPlatformTextureSettings(AndroidSettings());
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Пиксели карты пака в <see cref="TextureSize" /> такими, какими их выбирает шейдер (sRGB-импорт уже
        /// линеаризован выборкой): через линейную рендер-текстуру, без флага Read/Write у TGA.
        /// </summary>
        private static Color[] Read(Texture texture)
        {
            if (texture == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32,
                                                          RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
                read.Apply();
                Color[] pixels = read.GetPixels();
                Object.DestroyImmediate(read);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Android: не больше 1024, ASTC 6×6 (Quest).</summary>
        public static void LimitForAndroid(Texture texture)
        {
            if (texture == null) return;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer == null) return;
            TextureImporterPlatformSettings current = importer.GetPlatformTextureSettings("Android");
            if (current.overridden && current.maxTextureSize == TextureSize && current.format == TextureImporterFormat.ASTC_6x6) return;
            importer.SetPlatformTextureSettings(AndroidSettings());
            importer.SaveAndReimport();
        }

        private static TextureImporterPlatformSettings AndroidSettings() => new TextureImporterPlatformSettings
        {
            name = "Android",
            overridden = true,
            maxTextureSize = TextureSize,
            format = TextureImporterFormat.ASTC_6x6,
            compressionQuality = 50
        };
    }
}
