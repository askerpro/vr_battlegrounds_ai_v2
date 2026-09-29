using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.EditorTools.UI
{
    /// <summary>
    /// Сборка планшета из дизайн-системы (T-32) — генерация вместо ручной настройки:
    /// <list type="number">
    /// <item>тема <c>Resources/MenuTheme.asset</c> (если нет) со скруглённым спрайтом;</item>
    /// <item><c>Screen_Base.prefab</c> — база всех экранов (столбец содержимого);</item>
    /// <item>каркас <see cref="MenuFrame"/> в <c>Tablet_Base.prefab</c> — его наследуют все планшеты;</item>
    /// <item>в каждый планшет реестра кладутся <b>все</b> экраны из <see cref="ScreensFolder"/> —
    /// правило «префаб экрана в папке экранов = экран на планшете»; остальное под канвасом удаляется.</item>
    /// </list>
    /// Экраны не переносятся, а кладутся заново: каркас пересобирается с новыми id объектов, и
    /// экземпляры, добавленные в вариант под старые объекты, Unity теряет. Повторный запуск безопасен.
    /// Новый экран — <see cref="CreateScreen"/> (скил <c>/add-menu-screen</c>).
    /// </summary>
    public static class MenuKitBuilder
    {
        public const string ThemePath = "Assets/Resources/MenuTheme.asset";
        public const string ScreensFolder = "Assets/Prefabs/UI/Menu/Screens";
        public const string ScreenBasePath = ScreensFolder + "/Screen_Base.prefab";
        public const string TabletBasePath = "Assets/Prefabs/UI/Menu/Tablet/Tablet_Base.prefab";
        public const string RegistryPath = "Assets/Data/UI/Menu/MenuPrefabRegistry.asset";

        [MenuItem("Tools/VR Battlegrounds/UI/Rebuild Menu Frame")]
        public static void RebuildAll()
        {
            EnsureTheme();
            EnsureScreenBase();
            BuildTabletFrame(TabletBasePath);
            foreach (string tablet in TabletPaths()) PlaceScreens(tablet);

            AssetDatabase.SaveAssets();
            GameLog.UI.Info("[MenuKitBuilder] Каркас меню пересобран, экраны разложены.");
        }

        // ── Тема ─────────────────────────────────────────────────────────

        public static MenuTheme EnsureTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<MenuTheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<MenuTheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }

            if (theme.BoldFont == null)
            {
                theme.BoldFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Art/Fonts/RobotoCondensed/RobotoCondensed-Bold SDF.asset");
                EditorUtility.SetDirty(theme);
            }

            if (theme.RoundedSprite == null)
            {
                theme.RoundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                EditorUtility.SetDirty(theme);
            }
            return theme;
        }

        // ── База экрана ──────────────────────────────────────────────────

        /// <summary>Создать базу, если нет; есть — поправить на месте (пересоздание сменило бы id корня и порвало все варианты).</summary>
        public static void EnsureScreenBase()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ScreenBasePath) == null)
            {
                var go = new GameObject("Screen_Base", typeof(RectTransform));
                try
                {
                    MenuScreen.ApplyContentLayout(go);
                    PrefabUtility.SaveAsPrefabAsset(go, ScreenBasePath);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ScreenBasePath);
            try
            {
                MenuScreen.ApplyContentLayout(root);
                PrefabUtility.SaveAsPrefabAsset(root, ScreenBasePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Каркас ───────────────────────────────────────────────────────

        public static void BuildTabletFrame(string tabletPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(tabletPath);
            try
            {
                MenuView view = root.GetComponent<MenuView>();
                Transform menuRoot = view.MenuRoot.transform;

                Transform frameTransform = menuRoot.Find("Frame");
                if (frameTransform == null)
                {
                    frameTransform = MenuKit.New("Frame", menuRoot).transform;
                    frameTransform.SetAsFirstSibling();
                }

                MenuFrame frame = frameTransform.GetComponent<MenuFrame>();
                if (frame == null) frame = frameTransform.gameObject.AddComponent<MenuFrame>();
                frame.Build();
                frame.ClearActions();

                view.Frame = frame;
                view.DefaultScreen = MenuScreenType.Main;

                // Фон канваса закрыт каркасом; красим его темой на случай зазора (был ярко-красный).
                Image background = view.MenuRoot.GetComponent<Image>();
                if (background != null)
                {
                    MenuThemed themed = background.GetComponent<MenuThemed>();
                    if (themed == null) themed = background.gameObject.AddComponent<MenuThemed>();
                    themed.Set(MenuColorRole.Surface, MenuTextRole.Body);
                }

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                PrefabUtility.SaveAsPrefabAsset(root, tabletPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Экраны ───────────────────────────────────────────────────────

        /// <summary>Префабы экранов: варианты Screen_Base со скриптом экрана из <see cref="ScreensFolder"/>.</summary>
        public static List<GameObject> ScreenPrefabs()
        {
            var result = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ScreensFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == ScreenBasePath) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<MenuScreen>() != null) result.Add(prefab);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>Все экраны — в прокручиваемую область каркаса, выключены; прочее под канвасом — прочь.</summary>
        public static void PlaceScreens(string tabletPath)
        {
            GameObject tablet = PrefabUtility.LoadPrefabContents(tabletPath);
            try
            {
                MenuView view = tablet.GetComponent<MenuView>();
                MenuFrame frame = view.Frame;
                if (frame == null || frame.Screens == null)
                    throw new InvalidOperationException($"{tabletPath}: нет каркаса — сначала BuildTabletFrame.");

                // Каркас — только из базы: переопределения в варианте (например, записанные, пока
                // скрипт был потерян) обнуляли бы ссылки каркаса.
                foreach (Component c in frame.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c.transform.IsChildOf(frame.Screens) && c.transform != frame.Screens) continue;
                    if (PrefabUtility.IsPartOfPrefabInstance(c) && PrefabUtility.HasPrefabInstanceAnyOverrides(PrefabUtility.GetOutermostPrefabInstanceRoot(c), false))
                        PrefabUtility.RevertObjectOverride(c, InteractionMode.AutomatedAction);
                }

                Transform menuRoot = view.MenuRoot.transform;
                for (int i = menuRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = menuRoot.GetChild(i);
                    if (child != frame.transform) Object.DestroyImmediate(child.gameObject);
                }
                for (int i = frame.Screens.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(frame.Screens.GetChild(i).gameObject);

                // Экраны прошлой сборки, осиротевшие при пересборке каркаса базы: Unity переносит
                // добавленные в вариант объекты в корень, когда их родитель в базе пересоздан.
                foreach (MenuScreen orphan in tablet.GetComponentsInChildren<MenuScreen>(true))
                    if (orphan != null) Object.DestroyImmediate(orphan.gameObject);

                foreach (GameObject prefab in ScreenPrefabs())
                {
                    var screen = (GameObject)PrefabUtility.InstantiatePrefab(prefab, frame.Screens);
                    screen.SetActive(false);
                }

                foreach (Transform t in tablet.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

                PrefabUtility.SaveAsPrefabAsset(tablet, tabletPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(tablet);
            }
        }

        /// <summary>
        /// Новый экран: вариант Screen_Base в <see cref="ScreensFolder"/> со скриптом
        /// <typeparamref name="T"/> и типом <paramref name="type"/>; затем каркас и планшеты пересобираются.
        /// </summary>
        public static GameObject CreateScreen<T>(string prefabName, MenuScreenType type) where T : MenuScreen
        {
            string path = $"{ScreensFolder}/{prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                throw new InvalidOperationException($"{path} уже есть.");

            var screenBase = AssetDatabase.LoadAssetAtPath<GameObject>(ScreenBasePath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(screenBase);
            try
            {
                instance.name = prefabName;
                var screen = instance.AddComponent<T>();
                var so = new SerializedObject(screen);
                so.FindProperty("_screenType").enumValueIndex = Array.IndexOf(Enum.GetValues(typeof(MenuScreenType)), type);
                so.ApplyModifiedPropertiesWithoutUndo();
                instance.SetActive(false);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
                RebuildAll();
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        public static IEnumerable<string> TabletPaths()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MenuPrefabRegistry>(RegistryPath);
            var result = new HashSet<string>();
            if (registry == null) return result;
            foreach (RoleMenuConfig c in registry.RoleConfigurations)
            {
                foreach (GameObject p in new[] { c.OfflineMenuPrefab, c.LobbyMenuPrefab, c.DefaultGameMenuPrefab })
                    if (p != null) result.Add(AssetDatabase.GetAssetPath(p));
                foreach (GameModeMenuMapping m in c.SpecificGameModeMenus)
                    if (m.MenuPrefab != null) result.Add(AssetDatabase.GetAssetPath(m.MenuPrefab));
            }
            return result;
        }
    }
}
