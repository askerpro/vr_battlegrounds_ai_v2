using System.IO;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    ///     Цветной вариант аватара: <c>Optimized_MEF_Player</c> → <c>Optimized_MEF_Player_Black</c>, и настройка
    ///     шейдера формы у исходного аватара.
    ///
    ///     <para>
    ///         <b>Цвет красит шейдер, а не запечённая текстура</b> (с 2026-10-01). Тело рисуется шейдером
    ///         <c>VR Battlegrounds/Team Uniform Lit</c> — копия URP Lit, которая по маске
    ///         <c>MEF_Optimized_TeamMask.png</c> (R — одежда, G — экипировка, B — каска и очки; генерирует конвейер
    ///         <c>Tools/mef-avatar</c>) перекрашивает albedo: яркость исходного пикселя, нормированная на медиану канала, ×
    ///         цвет канала, плюс доля исходного пикселя (износ). Альфа цвета — сила перекраски, 0 — родной цвет.
    ///         В игре цвета задаёт команда игрока (<see cref="TeamUniformColors" />: <c>TeamData.mainColor</c> /
    ///         <c>additionalColor</c>); значения материала — цвета варианта вне игры (меню, иконка, труп).
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
        private const string Mask = Folder + "MEF_Optimized_TeamMask.png";
        private const string SourceData = "Assets/Data/Player/Avatars/OptimizedMEF.asset";

        /// <summary>
        ///     Медианы яркости (sRGB) исходного albedo по каналам маски — нормировка формулы перекраски.
        ///     Пересчитывать при пересборке модели (<c>Tools/mef-avatar</c>); значения 2026-10-01.
        /// </summary>
        public static readonly Vector4 Medians = new Vector4(0.4414f, 0.3030f, 0.5362f, 0f);

        /// <summary>
        ///     Чёрная (угольная) тактическая форма. Синий вариант (<c>0.16, 0.22, 0.40</c>) читался как рабочая
        ///     спецовка; чистый чёрный ниже ~0.1 съедает складки — в шлеме остаётся только силуэт.
        /// </summary>
        public static readonly Color Black = new Color(0.13f, 0.13f, 0.13f, 1f);

        /// <summary>
        ///     Экипировка к чёрной форме — холодный серый (Wolf Grey). Выбран из Ranger Green / Wolf Grey / OD Green /
        ///     Coyote: песочная экипировка на чёрном сливалась с командой Повстанцев.
        /// </summary>
        public static readonly Color WolfGrey = new Color(0.30f, 0.31f, 0.32f, 1f);

        /// <summary>
        ///     Доля исходного пикселя (приведённого к яркости цвета) поверх заливки: сохраняет потёртости и разнотон
        ///     ткани. Без неё форма — ровная заливка одного оттенка.
        /// </summary>
        public const float Wear = 0.25f;

        [MenuItem("Tools/VR Battlegrounds/Avatars/Team Color Variant/Build Optimized MEF Black")]
        private static void BuildBlack()
        {
            // Одежда чёрная, экипировка Wolf Grey, каска и очки чёрные — голова сразу отличает от песочных (2026-10-01).
            Build("Black", "US Marine (чёрный)", Black, WolfGrey, Black);
        }

        /// <summary>
        ///     Переводит исходный аватар на шейдер формы и строит (или перестраивает) материал, вариант и AvatarData.
        ///     <paramref name="main" /> — одежда, <paramref name="additional" /> — экипировка, <paramref name="helmet" /> —
        ///     каска и очки; альфа — сила.
        /// </summary>
        public static string Build(string suffix, string displayName, Color main, Color additional, Color helmet)
        {
            string materialPath = Folder + $"MEF_Optimized_{suffix}.mat";
            string prefabPath = $"Assets/Prefabs/Player/Optimized_MEF_Player_{suffix}.prefab";
            string dataPath = $"Assets/Data/Player/Avatars/OptimizedMEF_{suffix}.asset";

            SetupSource();
            Material material = CreateMaterial(materialPath, main, additional, helmet);
            GameObject variant = CreateVariant(prefabPath, material);
            CreateData(dataPath, displayName, variant);

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[TeamColorVariant] Готово: {prefabPath}, материал {materialPath}, данные {dataPath}.");
            return prefabPath;
        }

        // ── Шейдер формы ────────────────────────────────────────────────────

        /// <summary>
        ///     Исходный материал — на шейдере формы без перекраски (родной цвет); исходный аватар — с
        ///     <see cref="TeamUniformColors" /> (варианты наследуют).
        /// </summary>
        private static void SetupSource()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterial);
            SetupShader(source, Color.clear, Color.clear, Color.clear);

            GameObject root = PrefabUtility.LoadPrefabContents(SourcePrefab);
            try
            {
                if (root.GetComponent<TeamUniformColors>() != null) return;
                root.AddComponent<TeamUniformColors>();
                PrefabUtility.SaveAsPrefabAsset(root, SourcePrefab);
                GameLog.Debug.Info($"[TeamColorVariant] {SourcePrefab}: добавлен TeamUniformColors.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Шейдер формы (свойства URP Lit сохраняются — имена те же), маска, медианы и цвета каналов.</summary>
        private static void SetupShader(Material material, Color main, Color additional, Color helmet)
        {
            Shader shader = Shader.Find(TeamUniformColors.ShaderName);
            if (material.shader != shader) material.shader = shader;
            material.SetTexture("_TeamMask", AssetDatabase.LoadAssetAtPath<Texture2D>(Mask));
            material.SetVector("_TeamMedians", Medians);
            material.SetFloat("_TeamWear", Wear);
            material.SetColor("_TeamMainColor", main);
            material.SetColor("_TeamAdditionalColor", additional);
            material.SetColor("_TeamHelmetColor", helmet);
            EditorUtility.SetDirty(material);
        }

        // ── Материал ────────────────────────────────────────────────────────

        private static Material CreateMaterial(string path, Color main, Color additional, Color helmet)
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
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            SetupShader(material, main, additional, helmet);
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
            // Иконку исходника — только новому варианту: при перестройке своя иконка варианта не затирается.
            if (data.icon == null) data.icon = source != null ? source.icon : null;
            data.prefab = prefab;
            EditorUtility.SetDirty(data);
        }
    }
}
