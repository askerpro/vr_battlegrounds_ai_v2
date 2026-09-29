using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Правила дизайн-системы планшета (T-32) на всех планшетах из <see cref="MenuPrefabRegistry"/>
    /// и на всех их экранах — новый экран проверяется без правки теста. Ловят весь класс ошибок
    /// аудита <c>Docs/audit/ui-menu-audit-2026-09.md</c>: серый и неклипаемый 3D-текст (R1),
    /// экран без каркаса и «Назад» где попало (R2), перелив без скролла (R3).
    /// </summary>
    public class MenuDesignRulesTests
    {
        private const string RegistryPath = "Assets/Data/UI/Menu/MenuPrefabRegistry.asset";
        public const string ScreenBasePath = "Assets/Prefabs/UI/Menu/Screens/Screen_Base.prefab";

        public static IEnumerable<GameObject> Tablets()
        {
            var registry = AssetDatabase.LoadAssetAtPath<MenuPrefabRegistry>(RegistryPath);
            Assert.IsNotNull(registry, $"Нет {RegistryPath}.");

            var result = new HashSet<GameObject>();
            foreach (RoleMenuConfig config in registry.RoleConfigurations)
            {
                if (config.OfflineMenuPrefab != null) result.Add(config.OfflineMenuPrefab);
                if (config.LobbyMenuPrefab != null) result.Add(config.LobbyMenuPrefab);
                if (config.DefaultGameMenuPrefab != null) result.Add(config.DefaultGameMenuPrefab);
                foreach (GameModeMenuMapping m in config.SpecificGameModeMenus)
                    if (m.MenuPrefab != null) result.Add(m.MenuPrefab);
            }
            Assert.IsNotEmpty(result, "Контроль: в реестре есть планшеты.");
            return result;
        }

        private static MenuView View(GameObject tablet)
        {
            var view = tablet.GetComponent<MenuView>();
            Assert.IsNotNull(view, $"{tablet.name}: нет MenuView.");
            Assert.IsNotNull(view.MenuRoot, $"{tablet.name}: MenuView.MenuRoot не назначен.");
            return view;
        }

        private static MenuFrame Frame(GameObject tablet)
        {
            MenuView view = View(tablet);
            Assert.IsNotNull(view.Frame, $"{tablet.name}: нет каркаса (MenuView.Frame) — экраны раскладывают себя сами.");
            Assert.IsTrue(view.Frame.IsBuilt, $"{tablet.name}: каркас не собран (Tools/VR Battlegrounds/UI/Rebuild Menu Frame).");
            return view.Frame;
        }

        // ── Каркас ───────────────────────────────────────────────────────

        [Test]
        public void У_планшета_есть_каркас_и_все_экраны_в_его_скролле()
        {
            foreach (GameObject tablet in Tablets())
            {
                MenuFrame frame = Frame(tablet);
                foreach (MenuScreen screen in tablet.GetComponentsInChildren<MenuScreen>(true))
                    Assert.IsTrue(screen.transform.parent == frame.Screens,
                        $"{tablet.name}/{screen.name}: экран вне прокручиваемой области каркаса (Frame → Viewport → Screens).");

                Assert.IsNotNull(frame.Viewport.GetComponent<RectMask2D>(), "У области контента нет маски — содержимое вылезет.");
                Assert.IsTrue(frame.Body.content == frame.Screens, "Скролл двигает не контейнер экранов.");
                Assert.IsTrue(frame.Body.viewport == frame.Viewport, "Вьюпорт скролла — не маскированная область.");
            }
        }

        [Test]
        public void Экран_это_вариант_Screen_Base()
        {
            foreach (GameObject tablet in Tablets())
            foreach (MenuScreen screen in tablet.GetComponentsInChildren<MenuScreen>(true))
                Assert.IsTrue(SourceChain(screen.gameObject).Contains(ScreenBasePath),
                    $"{tablet.name}/{screen.name}: экран не вариант Screen_Base.prefab. Цепочка: {string.Join(" ← ", SourceChain(screen.gameObject))}");
        }

        [Test]
        public void На_объекте_один_MenuScreen()
        {
            foreach (GameObject tablet in Tablets())
            foreach (MenuScreen screen in tablet.GetComponentsInChildren<MenuScreen>(true))
                Assert.AreEqual(1, screen.GetComponents<MenuScreen>().Length,
                    $"{tablet.name}/{screen.name}: несколько MenuScreen на одном объекте — контроллер возьмёт первый.");
        }

        [Test]
        public void Экраны_не_содержат_навигацию()
        {
            foreach (GameObject tablet in Tablets())
            {
                MenuFrame frame = Frame(tablet);
                foreach (KitButton button in tablet.GetComponentsInChildren<KitButton>(true))
                {
                    if (button.Role != MenuButtonRole.Back && button.Role != MenuButtonRole.Tab) continue;
                    Assert.IsTrue(button.transform.IsChildOf(frame.Rail),
                        $"{tablet.name}/{button.name}: кнопка «{button.Role}» вне левой колонки.");
                }
            }
        }

        [Test]
        public void На_планшете_нет_потерянных_скриптов()
        {
            foreach (GameObject tablet in Tablets())
            foreach (Transform t in tablet.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                    $"{tablet.name}: {Path(t)} — потерянный скрипт (удалённый компонент старого меню).");
        }

        // ── Текст и кнопки ───────────────────────────────────────────────

        [Test]
        public void Нет_3D_TextMeshPro_на_планшете()
        {
            foreach (GameObject tablet in Tablets())
            {
                string[] offenders = View(tablet).MenuRoot.GetComponentsInChildren<TextMeshPro>(true).Select(t => Path(t.transform)).ToArray();
                Assert.IsEmpty(offenders, $"{tablet.name}: 3D-текст в канвасе — серый (фон рисуется поверх) и не клипается масками.");
            }
        }

        [Test]
        public void Шрифт_только_проектный()
        {
            foreach (GameObject tablet in Tablets())
            foreach (TMP_Text text in View(tablet).MenuRoot.GetComponentsInChildren<TMP_Text>(true))
                Assert.IsTrue(text.font != null && text.font.name.StartsWith("RobotoCondensed"),
                    $"{tablet.name}: {Path(text.transform)} — шрифт {(text.font != null ? text.font.name : "null")}, нужен Roboto Condensed (кириллица).");
        }

        [Test]
        public void Кнопки_только_из_набора()
        {
            foreach (GameObject tablet in Tablets())
            foreach (Button button in View(tablet).MenuRoot.GetComponentsInChildren<Button>(true))
                Assert.IsInstanceOf<KitButton>(button, $"{tablet.name}: {Path(button.transform)} — самодельная кнопка, нужна MenuKit.Button/Tile.");
        }

        [Test]
        public void Цвет_графики_только_из_темы()
        {
            foreach (GameObject tablet in Tablets())
            foreach (Graphic graphic in View(tablet).MenuRoot.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.GetComponent<MenuThemed>() != null) continue;
                if (graphic.GetComponentInParent<KitButton>(true) != null) continue;      // красит KitButton
                if (graphic.GetComponentInParent<TMP_InputField>(true) != null) continue; // поле ввода набора
                if (graphic.color.a == 0f) continue;                                      // невидимая цель луча
                if (graphic.transform == View(tablet).MenuRoot.transform) continue;
                Assert.Fail($"{tablet.name}: {Path(graphic.transform)} — цвет литералом, нужен MenuThemed.");
            }
        }

        [Test]
        public void Текст_не_переливается_за_свою_рамку()
        {
            foreach (GameObject tablet in Tablets())
            foreach (TMP_Text text in View(tablet).MenuRoot.GetComponentsInChildren<TMP_Text>(true))
                Assert.AreNotEqual(TextOverflowModes.Overflow, text.overflowMode,
                    $"{tablet.name}: {Path(text.transform)} — overflowMode=Overflow, длинный текст вылезет за рамку.");
        }

        // ── Служебное ────────────────────────────────────────────────────

        /// <summary>Пути префабов-источников объекта: экземпляр → его префаб → база варианта → …</summary>
        public static List<string> SourceChain(GameObject go)
        {
            var chain = new List<string>();
            Object current = go;
            for (int i = 0; i < 8; i++)
            {
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(current);
                if (source == null) break;
                chain.Add(AssetDatabase.GetAssetPath(source));
                current = source;
            }
            return chain;
        }

        public static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }

    /// <summary>
    /// Вмещаемость (M1, M2 аудита): каждый экран каждого планшета набивается большим содержимым
    /// (60+ строк, широкие строки из многих кнопок, сетка, таблица, длинные подписи, оба действия),
    /// раскладка считается, и ни одна видимая графика не выходит за планшет: служебные зоны — в
    /// безопасной зоне канваса, содержимое — по ширине в пределах области контента (по высоте его
    /// клипает маска и прокручивает скролл).
    /// </summary>
    public class MenuContainmentTests
    {
        private const float Tolerance = 1f;

        [Test]
        public void Стресс_наполнение_не_вылезает_за_планшет()
        {
            foreach (GameObject tabletPrefab in MenuDesignRulesTests.Tablets())
            {
                Scene scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var tablet = (GameObject)PrefabUtility.InstantiatePrefab(tabletPrefab, scene);
                    tablet.SetActive(true);
                    MenuView view = tablet.GetComponent<MenuView>();
                    Assert.IsNotNull(view.Frame, $"{tabletPrefab.name}: нет каркаса MenuFrame — проверять нечего, экраны раскладывают себя сами.");
                    MenuFrame frame = view.Frame;
                    var canvas = (RectTransform)view.MenuRoot.transform;
                    MenuScreen[] screens = tablet.GetComponentsInChildren<MenuScreen>(true);
                    Assert.IsNotEmpty(screens);

                    foreach (MenuScreen screen in screens)
                    {
                        foreach (MenuScreen other in screens) other.gameObject.SetActive(other == screen);
                        Stuff(screen.Content, frame);
                        Relayout(canvas);
                        AssertContained(tabletPrefab.name + "/" + screen.name, canvas, frame);
                        MenuKit.Clear(screen.Content);
                        frame.ClearActions();
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        /// <summary>
        /// Превью каждого экрана (тот же код, что в игре, на образцовых данных): ничего не вылезает,
        /// и у каждого символа каждого текста есть глиф в шрифте — иначе на планшете пустой квадрат.
        /// </summary>
        [Test]
        public void Превью_экранов_вмещается_и_без_пустых_квадратов()
        {
            foreach (GameObject tabletPrefab in MenuDesignRulesTests.Tablets())
            {
                Scene scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var tablet = (GameObject)PrefabUtility.InstantiatePrefab(tabletPrefab, scene);
                    tablet.SetActive(true);
                    MenuView view = tablet.GetComponent<MenuView>();
                    Assert.IsNotNull(view.Frame, $"{tabletPrefab.name}: нет каркаса.");
                    var canvas = (RectTransform)view.MenuRoot.transform;
                    MenuScreen[] screens = tablet.GetComponentsInChildren<MenuScreen>(true);

                    foreach (MenuScreen screen in screens)
                    {
                        foreach (MenuScreen other in screens) other.gameObject.SetActive(other == screen);
                        view.Frame.ClearActions();
                        screen.BuildPreview();
                        Relayout(canvas);

                        string where = tabletPrefab.name + "/" + screen.name + " (превью)";
                        Assert.Greater(screen.Content.childCount, 0, $"{where}: превью пустое — экрану нечего показать в редакторе.");
                        AssertContained(where, canvas, view.Frame);
                        AssertGlyphs(where, canvas);
                        MenuKit.Clear(screen.Content);
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }

        private static void AssertGlyphs(string where, RectTransform canvas)
        {
            var missing = new List<string>();
            foreach (TMPro.TMP_Text text in canvas.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (text.font == null || string.IsNullOrEmpty(text.text)) continue;
                string plain = System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "");
                foreach (char c in plain)
                {
                    if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
                    if (!text.font.HasCharacter(c, searchFallbacks: true, tryAddCharacter: false))
                        missing.Add($"«{c}» в {MenuDesignRulesTests.Path(text.transform)}: «{text.text}»");
                }
            }
            Assert.IsEmpty(missing, $"{where}: нет глифов:\n" + string.Join("\n", missing.Distinct().Take(10)));
        }

        private const string Long ="Очень длинная подпись, которую никто не рассчитывал увидеть на планшете целиком — с ником ИгрокСОченьДлиннымНикомИКомандойПовстанцы";

        private static void Stuff(RectTransform content, MenuFrame frame)
        {
            MenuKit.Title(content, Long);
            MenuKit.Section(content, Long);
            for (int i = 0; i < 30; i++)
            {
                RectTransform row = MenuKit.Row(content);
                MenuKit.Label(row, Long);
                MenuKit.Input(row, Long, 32);
                for (int b = 0; b < 5; b++) MenuKit.Button(row, "Кнопка с длинной подписью " + b, null);
            }
            MenuKit.TableRow(content, new[] { "Игрок", "HP", "Убийства", "Смерти", "Ассисты", "Пинг", "Команда" }, null, header: true);
            for (int i = 0; i < 20; i++)
                MenuKit.TableRow(content, new[] { Long, "100", "12", "3", "4", "25 мс", "Повстанцы" }, new[] { 4f, 1f, 1f, 1f, 1f, 1f, 2f });
            RectTransform grid = MenuKit.Grid(content, 3, 0.6f);
            for (int i = 0; i < 12; i++) MenuKit.Tile(grid, Long, null, null);
            MenuKit.EmptyState(content, Long + " " + Long);

            frame.SetPrimary("Начать серию с очень длинным названием", null);
            frame.SetSecondary("Очистить очередь карт полностью", null);
        }

        private static void Relayout(RectTransform canvas)
        {
            for (int i = 0; i < 3; i++)
            {
                Canvas.ForceUpdateCanvases();
                foreach (LayoutGroup group in canvas.GetComponentsInChildren<LayoutGroup>(false))
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
            }
            Canvas.ForceUpdateCanvases();
        }

        private static void AssertContained(string where, RectTransform canvas, MenuFrame frame)
        {
            MenuTheme theme = MenuTheme.Instance;
            Rect safe = canvas.rect;
            safe.xMin += theme.SafeMargin - Tolerance;
            safe.yMin += theme.SafeMargin - Tolerance;
            safe.xMax -= theme.SafeMargin - Tolerance;
            safe.yMax -= theme.SafeMargin - Tolerance;
            Rect viewport = LocalRect(frame.Viewport, canvas);

            var failures = new List<string>();
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (graphic.transform == canvas || graphic.transform == frame.transform) continue;
                if (!graphic.enabled || graphic.color.a == 0f) continue;
                Rect r = LocalRect(graphic.rectTransform, canvas);
                if (r.width <= 0.01f || r.height <= 0.01f) continue;

                if (graphic.transform.IsChildOf(frame.Viewport))
                {
                    if (r.xMin < viewport.xMin - Tolerance || r.xMax > viewport.xMax + Tolerance)
                        failures.Add($"шире области контента: {MenuDesignRulesTests.Path(graphic.transform)} x=[{r.xMin:0}; {r.xMax:0}], область [{viewport.xMin:0}; {viewport.xMax:0}]");
                }
                else if (!safe.Contains(r.min) || !safe.Contains(r.max))
                {
                    failures.Add($"вне безопасной зоны: {MenuDesignRulesTests.Path(graphic.transform)} {r}");
                }
            }

            Assert.IsEmpty(failures, $"{where}:\n" + string.Join("\n", failures.Take(15)));
        }

        private static Rect LocalRect(RectTransform rt, RectTransform space)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 c in corners)
            {
                Vector3 p = space.InverseTransformPoint(c);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
