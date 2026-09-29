using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.EditorTools.UI
{
    /// <summary>
    /// Превью экранов планшета в редакторе (T-32). В инспекторе <see cref="MenuView"/> — выбор экрана,
    /// «Показать превью» и «Очистить»: экран строит содержимое тем же кодом, что в игре, из образцовых
    /// данных (<see cref="MenuScreen.BuildPreview"/>). Созданное помечается «не сохранять», а перед
    /// сохранением префаба превью снимается само — в префаб оно не попадает.
    /// </summary>
    [CustomEditor(typeof(MenuView))]
    public class MenuViewEditor : UnityEditor.Editor
    {
        private int _selected;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var view = (MenuView)target;
            if (view.Frame == null || view.MenuRoot == null) return;

            MenuScreen[] screens = view.MenuRoot.GetComponentsInChildren<MenuScreen>(true);
            if (screens.Length == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Превью экрана (не сохраняется в префаб)", EditorStyles.boldLabel);
            string[] names = screens.Select(s => $"{s.ScreenType} — {s.name}").ToArray();
            _selected = Mathf.Clamp(EditorGUILayout.Popup("Экран", _selected, names), 0, screens.Length - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Показать превью")) MenuPreview.Show(view, screens[_selected]);
                if (GUILayout.Button("Очистить")) MenuPreview.Clear(view);
            }
        }
    }

    public static class MenuPreview
    {
        [InitializeOnLoadMethod]
        private static void Hook()
        {
            // Превью в префаб не сохраняется: перед сохранением снимаем его со всех планшетов стадии.
            PrefabStage.prefabSaving += root =>
            {
                MenuView view = root.GetComponent<MenuView>();
                if (view != null) Clear(view);
            };
        }

        public static void Show(MenuView view, MenuScreen screen)
        {
            Clear(view);
            foreach (MenuScreen s in view.MenuRoot.GetComponentsInChildren<MenuScreen>(true))
                s.gameObject.SetActive(s == screen);

            // Колонка разделов в игре строится контроллером; в превью — здесь, все разделы видны.
            view.Frame.BuildTabs(MenuView.DefaultTabs(), _ => { });
            view.Frame.RefreshTabs(true, true, screen.ScreenType);
            view.Frame.SetBack(true, null);
            foreach (Transform t in view.Frame.Rail.GetComponentsInChildren<Transform>(true))
                if (t.parent != null && t.parent.name == "Tabs") t.gameObject.hideFlags = HideFlags.DontSave;

            view.Frame.ClearActions();
            screen.BuildPreview();

            foreach (Transform t in screen.Content.GetComponentsInChildren<Transform>(true))
                if (t != screen.Content) t.gameObject.hideFlags = HideFlags.DontSave;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)view.MenuRoot.transform);
            view.Frame.ScrollToTop();
            SceneView.RepaintAll();
        }

        public static void Clear(MenuView view)
        {
            if (view.MenuRoot == null) return;
            foreach (MenuScreen s in view.MenuRoot.GetComponentsInChildren<MenuScreen>(true))
            {
                MenuKit.Clear(s.Content);
                s.gameObject.SetActive(false);
            }
            if (view.Frame != null && view.Frame.IsBuilt)
            {
                view.Frame.ClearActions();
                view.Frame.BuildTabs(null, null);
            }
        }

        // ── Снимки ───────────────────────────────────────────────────────

        public const string SnapshotsFolder = "Temp/MenuSnapshots";

        [MenuItem("Tools/VR Battlegrounds/UI/Render Menu Snapshots")]
        public static void RenderSnapshotsMenu()
        {
            string dir = RenderSnapshots(SnapshotsFolder);
            EditorUtility.RevealInFinder(dir);
        }

        /// <summary>
        /// Снимки превью каждого экрана каждого планшета реестра — PNG в <paramref name="folder"/>.
        /// Камера смотрит на канвас в упор, как игрок на планшет. Для самопроверки раскладки агентом.
        /// </summary>
        public static string RenderSnapshots(string folder, int width = 1600, int height = 1000)
        {
            string dir = Path.GetFullPath(folder);
            Directory.CreateDirectory(dir);

            foreach (string tabletPath in MenuKitBuilder.TabletPaths())
            {
                Scene scene = EditorSceneManager.NewPreviewScene();
                var rt = new RenderTexture(width, height, 24);
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(tabletPath);
                    var tablet = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    tablet.SetActive(true);
                    MenuView view = tablet.GetComponent<MenuView>();
                    var canvas = (RectTransform)view.MenuRoot.transform;

                    var camGo = new GameObject("SnapshotCamera");
                    SceneManager.MoveGameObjectToScene(camGo, scene);
                    var cam = camGo.AddComponent<Camera>();
                    cam.scene = scene;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.35f, 0.4f, 0.45f);
                    cam.fieldOfView = 36f;
                    cam.nearClipPlane = 0.01f;
                    cam.transform.position = canvas.position - canvas.forward * 1.05f;
                    cam.transform.rotation = Quaternion.LookRotation(canvas.forward, canvas.up);
                    cam.targetTexture = rt;

                    foreach (MenuScreen screen in tablet.GetComponentsInChildren<MenuScreen>(true))
                    {
                        Show(view, screen);
                        for (int i = 0; i < 3; i++)
                        {
                            Canvas.ForceUpdateCanvases();
                            LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
                        }
                        view.Frame.ScrollToTop();
                        cam.Render();

                        RenderTexture.active = rt;
                        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(dir, $"{prefab.name}__{screen.name}.png"), tex.EncodeToPNG());
                        Object.DestroyImmediate(tex);
                    }
                    cam.targetTexture = null;
                }
                finally
                {
                    Object.DestroyImmediate(rt);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            GameLog.UI.Info($"[MenuPreview] Снимки меню: {dir}");
            return dir;
        }
    }
}
