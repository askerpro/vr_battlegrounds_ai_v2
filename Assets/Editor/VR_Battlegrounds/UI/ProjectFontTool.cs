using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>
    /// Шрифт проекта для UI: сборка статического SDF-ассета из .ttf и применение к префабам.
    ///
    /// <para>
    /// Почему статический атлас. Динамический шрифт дорисовывает глифы на лету и при этом
    /// переписывает свой .asset в редакторе — файл вечно висит изменённым в git. Именно
    /// поэтому стандартный <c>LiberationSans SDF - Fallback</c> однажды выкинули из репозитория,
    /// и вместе с ним пропала кириллица во всём меню (UI-01). Статический атлас запекается
    /// один раз этой утилитой и дальше не меняется.
    /// </para>
    ///
    /// <para>
    /// Цена статического атласа — фиксированный набор символов (<see cref="CharacterSet"/>).
    /// Нужен новый символ — добавить его в набор и пересобрать. Пропуск ловит
    /// <c>UiFontCoverageTests</c>.
    /// </para>
    ///
    /// Вызов:
    ///   — Tools/VR Battlegrounds/UI/Шрифт — пересобрать атласы
    ///   — Tools/VR Battlegrounds/UI/Шрифт — применить к UI-префабам
    ///   — из кода и агентом: ProjectFontTool.Rebuild(), ProjectFontTool.ApplyToUiPrefabs()
    /// </summary>
    public static class ProjectFontTool
    {
        public const string ResultMarker = "[ProjectFontTool]";

        private const string FontFolder = "Assets/Art/Fonts/RobotoCondensed";
        private const string RegularTtf = FontFolder + "/RobotoCondensed-Regular.ttf";
        private const string BoldTtf = FontFolder + "/RobotoCondensed-Bold.ttf";
        public const string RegularAssetPath = FontFolder + "/RobotoCondensed-Regular SDF.asset";
        public const string BoldAssetPath = FontFolder + "/RobotoCondensed-Bold SDF.asset";

        /// <summary>Папки, в которых утилита переназначает шрифт.</summary>
        private static readonly string[] UiPrefabRoots = { "Assets/Prefabs/UI" };

        // Параметры атласа: 80 pt при паддинге 8 дают чёткие края на планшете в шлеме,
        // ~330 глифов помещаются в один атлас 2048² (Alpha8, 4 МБ на начертание).
        private const int SamplingPointSize = 80;
        private const int AtlasPadding = 8;
        private const int AtlasSize = 2048;

        /// <summary>Индекс «Bold (700)» в таблице начертаний TMP_FontAsset.fontWeightTable.</summary>
        private const int BoldWeightIndex = 7;

        /// <summary>Какие символы запекаются в атлас.</summary>
        public static string CharacterSet
        {
            get
            {
                var sb = new StringBuilder();
                AppendRange(sb, 0x0020, 0x007E); // ASCII
                AppendRange(sb, 0x00A0, 0x00FF); // Latin-1: « » ° × · и т.п.
                AppendRange(sb, 0x0400, 0x045F); // кириллица, включая Ё/ё
                // Типографика: – — ‘ ’ ‚ “ ” „ • … ‰ ₽ № ™ −.
                // Стрелок ←↑→↓ в Roboto Condensed нет — в UI писать «<-» / «->».
                sb.Append("–—‘’‚“”„•…");
                sb.Append("‰₽№™−");
                return sb.ToString();
            }
        }

        [MenuItem("Tools/VR Battlegrounds/UI/Шрифт — пересобрать атласы")]
        public static void Rebuild()
        {
            var regular = BuildStaticFontAsset(RegularTtf, RegularAssetPath);
            var bold = BuildStaticFontAsset(BoldTtf, BoldAssetPath);
            if (regular == null || bold == null) return;

            // Жирный стиль у текста на Regular берёт настоящий Bold, а не утолщение шейдером.
            // Массив структур: элемент правится на месте, сеттер не нужен.
            regular.fontWeightTable[BoldWeightIndex].regularTypeface = bold;
            EditorUtility.SetDirty(regular);

            SetAsTmpDefault(regular);
            AssetDatabase.SaveAssets();
            Debug.Log($"{ResultMarker} Атласы пересобраны, {regular.name} — шрифт TMP по умолчанию");
        }

        [MenuItem("Tools/VR Battlegrounds/UI/Шрифт — применить к UI-префабам")]
        public static void ApplyToUiPrefabs()
        {
            var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RegularAssetPath);
            if (regular == null)
            {
                Debug.LogError($"{ResultMarker} Нет {RegularAssetPath} — сначала «пересобрать атласы»");
                return;
            }

            int changedTexts = 0;
            var changedPrefabs = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", UiPrefabRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // Открытие префаба на редактирование пересохраняет его целиком, и Unity
                // дописывает новые поля сериализации — лишний шум в git. Поэтому трогаем
                // только префабы, где действительно есть чужой шрифт.
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset.GetComponentsInChildren<TMP_Text>(true).Any(t => NeedsFont(t, regular))) continue;

                int changedHere = 0;

                using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    foreach (var text in scope.prefabContentsRoot.GetComponentsInChildren<TMP_Text>(true))
                    {
                        // Тексты вложенных префабов правятся в их собственном файле — иначе
                        // во внешнем префабе появятся лишние override'ы.
                        if (PrefabUtility.IsPartOfPrefabInstance(text)) continue;
                        if (ApplyFont(text, regular)) changedHere++;
                    }
                }

                if (changedHere > 0)
                {
                    changedTexts += changedHere;
                    changedPrefabs.Add(path);
                }
            }

            Debug.Log($"{ResultMarker} Шрифт применён: текстов {changedTexts}, префабов {changedPrefabs.Count}\n" +
                      string.Join("\n", changedPrefabs));
        }

        /// <summary>
        /// Переводит текст на шрифт проекта. Прежний «жирный» шрифт (LATO-BOLD и т.п.)
        /// превращается в стиль Bold, чтобы толщина надписи сохранилась.
        /// </summary>
        private static bool NeedsFont(TMP_Text text, TMP_FontAsset regular)
        {
            return text.font != regular && !PrefabUtility.IsPartOfPrefabInstance(text);
        }

        private static bool ApplyFont(TMP_Text text, TMP_FontAsset regular)
        {
            if (text.font == regular) return false;

            bool wasBoldFont = text.font != null && text.font.name.ToUpperInvariant().Contains("BOLD");

            text.font = regular;
            // Материал старого шрифта ссылается на чужой атлас — сбрасываем на родной.
            text.fontSharedMaterial = regular.material;
            if (wasBoldFont) text.fontStyle |= FontStyles.Bold;

            EditorUtility.SetDirty(text);
            return true;
        }

        private static TMP_FontAsset BuildStaticFontAsset(string ttfPath, string assetPath)
        {
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (sourceFont == null)
            {
                Debug.LogError($"{ResultMarker} Нет исходного шрифта {ttfPath}");
                return null;
            }

            // Ассет пересобирается на месте, а не пересоздаётся: GUID сохраняется,
            // и ссылки из префабов и TMP Settings не рвутся.
            var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (fontAsset == null)
            {
                fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont, SamplingPointSize, AtlasPadding,
                    GlyphRenderMode.SDFAA, AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, false);
                fontAsset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

                AssetDatabase.CreateAsset(fontAsset, assetPath);
                var atlas = fontAsset.atlasTexture;
                atlas.name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, fontAsset);
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            else
            {
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fontAsset.ClearFontAssetData(true);
            }

            fontAsset.TryAddCharacters(CharacterSet, out string missing, false);
            // В файле шрифта может просто не быть какого-то значка — это не ошибка сборки,
            // но о нём надо знать: такой символ в UI нарисуется квадратом.
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"{ResultMarker} В {sourceFont.name} нет символов: " +
                                 string.Join(" ", missing.Select(c => $"U+{(int)c:X4}")));

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fontAsset);
            EditorUtility.SetDirty(fontAsset.atlasTexture);
            return fontAsset;
        }

        /// <summary>
        /// Шрифт проекта — дефолт TMP (его получают новые тексты) и глобальный fallback
        /// (страховка для текстов, которые ещё сидят на старых латинских шрифтах).
        /// </summary>
        private static void SetAsTmpDefault(TMP_FontAsset font)
        {
            var settings = TMP_Settings.instance;
            var so = new SerializedObject(settings);
            so.FindProperty("m_defaultFontAsset").objectReferenceValue = font;

            var fallbacks = so.FindProperty("m_fallbackFontAssets");
            bool present = Enumerable.Range(0, fallbacks.arraySize)
                .Any(i => fallbacks.GetArrayElementAtIndex(i).objectReferenceValue == font);
            if (!present)
            {
                fallbacks.InsertArrayElementAtIndex(fallbacks.arraySize);
                fallbacks.GetArrayElementAtIndex(fallbacks.arraySize - 1).objectReferenceValue = font;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static void AppendRange(StringBuilder sb, int from, int to)
        {
            for (int c = from; c <= to; c++) sb.Append((char)c);
        }
    }
}
