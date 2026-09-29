using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Фабрика элементов меню (T-32). Экраны строят содержимое только через неё: размеры, цвета и
    /// шрифт берутся из <see cref="MenuTheme"/>, поэтому экран не может собрать серую кнопку или
    /// строку шире планшета. Всё — UGUI и <see cref="TextMeshProUGUI"/> (3D-текст в канвасе не
    /// сортируется с фоном и не клипается — аудит R1).
    ///
    /// <para>
    /// Родитель элементов — содержимое экрана (<see cref="MenuScreen.Content"/>) или строка. Содержимое
    /// экрана — вертикальный столбец во всю ширину области контента; растёт только вниз, в скролл.
    /// </para>
    /// </summary>
    public static class MenuKit
    {
        private static MenuTheme T => MenuTheme.Instance;

        // ── Текст ────────────────────────────────────────────────────────

        public static TMP_Text Title(Transform parent, string text) =>
            Label(parent, text, MenuTextRole.Title, MenuColorRole.TextPrimary);

        /// <summary>Заголовок группы строк (приглушённый).</summary>
        public static TMP_Text Section(Transform parent, string text) =>
            Label(parent, text, MenuTextRole.Caption, MenuColorRole.TextSecondary);

        /// <summary>«Здесь пусто, потому что…» — вместо пустого экрана.</summary>
        public static TMP_Text EmptyState(Transform parent, string text)
        {
            TMP_Text label = Label(parent, text, MenuTextRole.Body, MenuColorRole.TextSecondary);
            label.alignment = TextAlignmentOptions.Center;
            return label;
        }

        /// <summary>
        /// Текст, растягивается по ширине родителя и переносится по словам. В строке забирает
        /// свободное место и при нехватке сжимается с многоточием, а не выталкивает кнопки.
        /// </summary>
        public static TMP_Text Label(Transform parent, string text, MenuTextRole role = MenuTextRole.Body,
                                     MenuColorRole color = MenuColorRole.TextPrimary)
        {
            GameObject go = New("Label", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            go.AddComponent<MenuThemed>().Set(color, role);

            var element = go.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minWidth = 0f;
            return tmp;
        }

        // ── Раскладка ────────────────────────────────────────────────────

        /// <summary>
        /// Горизонтальная строка фиксированной высоты. Ширину детей раздаёт раскладка: не хватает
        /// места — кнопки сжимаются до минимума, текст — с многоточием.
        /// </summary>
        public static RectTransform Row(Transform parent, float height = -1f)
        {
            GameObject go = New("Row", parent);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = T.Spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height > 0f ? height : T.RowHeight;
            return (RectTransform)go.transform;
        }

        /// <summary>Сетка плиток: <paramref name="columns"/> колонок во всю ширину, высота ячейки = ширина × <paramref name="aspect"/>.</summary>
        public static RectTransform Grid(Transform parent, int columns, float aspect)
        {
            GameObject go = New("Grid", parent);
            go.AddComponent<KitGrid>().Setup(columns, aspect, T.Spacing);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return (RectTransform)go.transform;
        }

        /// <summary>Строка таблицы: ячейки делят ширину по весам. Заголовок — приглушённый.</summary>
        public static RectTransform TableRow(Transform parent, IList<string> cells, IList<float> weights, bool header = false)
        {
            RectTransform row = Row(parent, T.TableRowHeight);
            for (int i = 0; i < cells.Count; i++)
            {
                TMP_Text cell = Label(row, cells[i], header ? MenuTextRole.TableHeader : MenuTextRole.Body,
                                      header ? MenuColorRole.TextSecondary : MenuColorRole.TextPrimary);
                cell.textWrappingMode = TextWrappingModes.NoWrap;
                var element = cell.GetComponent<LayoutElement>();
                element.flexibleWidth = weights != null && i < weights.Count ? weights[i] : 1f;
                element.preferredWidth = 0f;
                if (i > 0) cell.alignment = TextAlignmentOptions.Midline;
            }
            return row;
        }

        // ── Кнопки ───────────────────────────────────────────────────────

        public static KitButton Button(Transform parent, string text, UnityAction onClick,
                                       MenuButtonRole role = MenuButtonRole.Secondary)
        {
            GameObject go = New("Btn_" + text, parent);
            Image fill = go.AddComponent<Image>();
            fill.sprite = T.RoundedSprite;
            fill.type = T.RoundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;

            var element = go.AddComponent<LayoutElement>();
            element.minWidth = T.ButtonMinWidth;
            element.minHeight = element.preferredHeight = T.RowHeight;

            TMP_Text label = ButtonLabel(go.transform, text, TextAlignmentOptions.Center);

            var button = go.AddComponent<KitButton>();
            button.Setup(role, fill, label, null);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Плитка с картинкой и подписью снизу (карта, команда, скин). Кладётся в <see cref="Grid"/>.</summary>
        public static KitButton Tile(Transform parent, string text, Sprite picture, UnityAction onClick)
        {
            GameObject go = New("Tile_" + text, parent);
            Image fill = go.AddComponent<Image>();
            fill.sprite = T.RoundedSprite;
            fill.type = T.RoundedSprite != null ? Image.Type.Sliced : Image.Type.Simple;

            // Картинка — в маске, чтобы превью любого размера не вылезало за плитку.
            GameObject mask = New("Picture_Mask", go.transform);
            Stretch(mask.transform, 8f, 8f, 8f, T.RowHeight);
            mask.AddComponent<RectMask2D>();

            GameObject pic = New("Picture", mask.transform);
            Stretch(pic.transform, 0f, 0f, 0f, 0f);
            Image image = pic.AddComponent<Image>();
            image.sprite = picture;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = picture != null;

            GameObject caption = New("Caption", go.transform);
            var rt = (RectTransform)caption.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(-2f * T.Spacing, T.RowHeight);
            rt.anchoredPosition = Vector2.zero;
            TMP_Text label = ButtonLabel(caption.transform, text, TextAlignmentOptions.Center, stretch: true);

            var button = go.AddComponent<KitButton>();
            button.Setup(MenuButtonRole.Secondary, fill, label, image);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        // ── Поле ввода ───────────────────────────────────────────────────

        public static TMP_InputField Input(Transform parent, string text, int characterLimit)
        {
            GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = "Input";
            go.transform.SetParent(parent, false);

            var field = go.GetComponent<TMP_InputField>();
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.pointSize = T.BodySize;
            field.text = text;

            Image background = go.GetComponent<Image>();
            if (background != null) background.color = T.SurfaceRaised;
            foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                bool placeholder = field.placeholder == t;
                t.gameObject.AddComponent<MenuThemed>().Set(placeholder ? MenuColorRole.TextSecondary : MenuColorRole.TextPrimary, MenuTextRole.Body);
            }

            var element = go.AddComponent<LayoutElement>();
            element.minWidth = 240f;
            element.preferredWidth = 420f;
            element.flexibleWidth = 0.5f;
            element.minHeight = element.preferredHeight = T.RowHeight;
            return field;
        }

        // ── Служебное ────────────────────────────────────────────────────

        /// <summary>Удалить всё содержимое контейнера (перестроение экрана по данным).</summary>
        public static void Clear(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                GameObject child = container.GetChild(i).gameObject;
                // Отвязать сразу: Destroy отложен до конца кадра, а раскладка считается раньше.
                child.transform.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(child);
                else Object.DestroyImmediate(child);
            }
        }

        public static GameObject New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        public static void Stretch(Transform t, float left, float right, float top, float bottom)
        {
            var rt = (RectTransform)t;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static TMP_Text ButtonLabel(Transform parent, string text, TextAlignmentOptions alignment, bool stretch = true)
        {
            GameObject go = New("Label", parent);
            Stretch(go.transform, T.Spacing, T.Spacing, 0f, 0f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = T.ButtonSize;
            if (T.BoldFont != null) tmp.font = T.BoldFont;
            tmp.fontStyle = T.BoldFont != null ? FontStyles.Normal : FontStyles.Bold;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = T.ButtonMinSize;
            tmp.fontSizeMax = T.ButtonSize;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
