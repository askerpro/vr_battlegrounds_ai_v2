using System.IO;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    ///     Цветной вариант аватара по маске команды: <c>Optimized_MEF_Player</c> → <c>Optimized_MEF_Player_Blue</c>.
    ///
    ///     <para>
    ///         <b>Цвет запекается в текстуру, а не красится шейдером.</b> У модели есть маска формы
    ///         (<c>MEF_Optimized_TeamMask.png</c>: белое — ткань формы, чёрное — кожа, снаряжение, железо).
    ///         URP Lit маску не читает, а свой шейдер на Quest — лишняя стоимость и риск сборки. Поэтому
    ///         инструмент считает новую albedo: в маске — яркость исходной ткани, умноженная на цвет,
    ///         вне маски — исходный пиксель. Материал — копия исходного с новой текстурой, тот же URP Lit.
    ///     </para>
    ///
    ///     <para>
    ///         <b>Вариант — вариант префаба Unity</b> от исходного аватара, переопределены только материалы
    ///         тел. <c>UxrAvatar._parentPrefab</c> указывает на исходный: позы кисти и записи хвата оружия
    ///         приходят по цепочке (<c>Optimized → MEF</c>), отдельно их прописывать не нужно.
    ///         Регистрация (реестр, команды, <c>spawnPrefabs</c>) — отдельно, см. <c>Docs/README.md</c>.
    ///     </para>
    /// </summary>
    internal static class TeamColorVariantBuilder
    {
        private const string Folder = "Assets/Models/Avatars/MEF_Optimized/";
        private const string SourcePrefab = "Assets/Prefabs/Player/Optimized_MEF_Player.prefab";
        private const string SourceMaterial = Folder + "MEF_Optimized.mat";
        private const string SourceAlbedo = Folder + "MEF_Optimized_Albedo.png";
        private const string Mask = Folder + "MEF_Optimized_TeamMask.png";
        private const string SourceData = "Assets/Data/Player/Avatars/OptimizedMEF.asset";

        /// <summary>Тёмно-синяя форма. Цвет — множитель яркости ткани, нормированной на её медиану.</summary>
        public static readonly Color Blue = new Color(0.16f, 0.22f, 0.40f);

        [MenuItem("Tools/VR Battlegrounds/Avatars/Team Color Variant/Build Optimized MEF Blue")]
        private static void BuildBlue()
        {
            Build("Blue", "US Marine (синий)", Blue);
        }

        /// <summary>Строит (или перестраивает) текстуру, материал, вариант и AvatarData. Возвращает путь варианта.</summary>
        public static string Build(string suffix, string displayName, Color color)
        {
            string albedoPath = Folder + $"MEF_Optimized_Albedo_{suffix}.png";
            string materialPath = Folder + $"MEF_Optimized_{suffix}.mat";
            string prefabPath = $"Assets/Prefabs/Player/Optimized_MEF_Player_{suffix}.prefab";
            string dataPath = $"Assets/Data/Player/Avatars/OptimizedMEF_{suffix}.asset";

            Texture2D albedo = BakeAlbedo(albedoPath, color);
            Material material = CreateMaterial(materialPath, albedo);
            GameObject variant = CreateVariant(prefabPath, material);
            CreateData(dataPath, displayName, variant);

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[TeamColorVariant] Готово: {prefabPath}, материал {materialPath}, данные {dataPath}.");
            return prefabPath;
        }

        // ── Текстура ────────────────────────────────────────────────────────

        private static Texture2D BakeAlbedo(string path, Color color)
        {
            Texture2D source = ReadPixels(SourceAlbedo, linear: false);
            Texture2D mask = ReadPixels(Mask, linear: true);

            Color[] pixels = source.GetPixels();
            int width = source.width, height = source.height;
            float[] weights = new float[pixels.Length];

            // Маска меньше albedo — берём билинейно.
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                weights[i] = mask.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height).r;
            }

            float median = MedianLuminance(pixels, weights);

            for (int i = 0; i < pixels.Length; i++)
            {
                float w = weights[i];
                if (w <= 0.001f) continue;

                Color p = pixels[i];
                float luminance = p.r * 0.299f + p.g * 0.587f + p.b * 0.114f;
                float k = luminance / Mathf.Max(0.01f, median);
                var tinted = new Color(Mathf.Clamp01(color.r * k), Mathf.Clamp01(color.g * k), Mathf.Clamp01(color.b * k), p.a);
                pixels[i] = Color.Lerp(p, tinted, w);
            }

            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels(pixels);
            File.WriteAllBytes(path, result.EncodeToPNG());

            Object.DestroyImmediate(source);
            Object.DestroyImmediate(mask);
            Object.DestroyImmediate(result);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            CopyImportSettings(SourceAlbedo, path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Пиксели файла как есть, мимо импорта: исходные текстуры не должны становиться Read/Write.</summary>
        private static Texture2D ReadPixels(string path, bool linear)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        private static float MedianLuminance(Color[] pixels, float[] weights)
        {
            var values = new System.Collections.Generic.List<float>();
            for (int i = 0; i < pixels.Length; i += 7)
            {
                if (weights[i] < 0.5f) continue;
                Color p = pixels[i];
                values.Add(p.r * 0.299f + p.g * 0.587f + p.b * 0.114f);
            }

            if (values.Count == 0) return 0.35f;
            values.Sort();
            return values[values.Count / 2];
        }

        private static void CopyImportSettings(string fromPath, string toPath)
        {
            var from = (TextureImporter)AssetImporter.GetAtPath(fromPath);
            var to = (TextureImporter)AssetImporter.GetAtPath(toPath);

            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);

            foreach (string platform in new[] { "Standalone", "Android" })
            {
                TextureImporterPlatformSettings platformSettings = from.GetPlatformTextureSettings(platform);
                platformSettings.name = platform;
                to.SetPlatformTextureSettings(platformSettings);
            }

            to.SaveAndReimport();
        }

        // ── Материал ────────────────────────────────────────────────────────

        private static Material CreateMaterial(string path, Texture2D albedo)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterial);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
            }

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ── Префаб ──────────────────────────────────────────────────────────

        private static GameObject CreateVariant(string path, Material material)
        {
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterial);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
                try
                {
                    instance.name = Path.GetFileNameWithoutExtension(path);
                    PrefabUtility.SaveAsPrefabAsset(instance, path);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int replaced = 0;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != sourceMaterial) continue;
                        materials[i] = material;
                        changed = true;
                        replaced++;
                    }

                    if (changed) renderer.sharedMaterials = materials;
                }

                // Цепочка UltimateXR: без неё позы кисти и хвата оружия ищутся не там.
                var avatar = new SerializedObject(root.GetComponent<UxrAvatar>());
                avatar.FindProperty("_parentPrefab").objectReferenceValue = sourcePrefab;
                avatar.FindProperty("_prefabGuid").stringValue = AssetDatabase.AssetPathToGUID(path);
                avatar.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                GameLog.Debug.Info($"[TeamColorVariant] {path}: заменено материалов {replaced}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void CreateData(string path, string displayName, GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<AvatarData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<AvatarData>();
                AssetDatabase.CreateAsset(data, path);
            }

            var source = AssetDatabase.LoadAssetAtPath<AvatarData>(SourceData);
            data.displayName = displayName;
            data.icon = source != null ? source.icon : null;
            data.prefab = prefab;
            EditorUtility.SetDirty(data);
        }
    }
}
