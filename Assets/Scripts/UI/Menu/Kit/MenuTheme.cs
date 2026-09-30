using System;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>Роль цвета графики меню (<see cref="MenuThemed"/>). Литералы цвета в меню запрещены.</summary>
    public enum MenuColorRole
    {
        Surface = 0,
        SurfaceRaised = 1,
        SurfaceHover = 2,
        Accent = 3,
        TextPrimary = 4,
        TextSecondary = 5,
        TextOnAccent = 6,
        Danger = 7,
        Success = 8,
        Disabled = 9,
        Divider = 10,
    }

    /// <summary>Роль текста: размер и начертание берутся из темы.</summary>
    public enum MenuTextRole
    {
        Title = 0,
        Body = 1,
        Caption = 2,
        Button = 3,
        TableHeader = 4,
    }

    /// <summary>Роль кнопки набора (<see cref="KitButton"/>): одна роль — один вид.</summary>
    public enum MenuButtonRole
    {
        Secondary = 0,
        Primary = 1,
        Danger = 2,
        Back = 3,
        Tab = 4,
    }

    /// <summary>Визуальное состояние кнопки.</summary>
    public enum MenuButtonState
    {
        Normal = 0,
        Hover = 1,
        Pressed = 2,
        Disabled = 3,
        Selected = 4,
    }

    /// <summary>Цвета одного состояния кнопки: заливка и текст.</summary>
    [Serializable]
    public struct MenuButtonColors
    {
        public Color Fill;
        public Color Text;

        public MenuButtonColors(Color fill, Color text)
        {
            Fill = fill;
            Text = text;
        }
    }

    /// <summary>
    /// Дизайн-система меню планшета: цвета по ролям, типографика, размеры. Единственное место, где
    /// они заданы, — префабы набора и код берут значения только отсюда (T-32). Грузится из
    /// <c>Resources/MenuTheme.asset</c>; без ассета — значения по умолчанию из кода, те же, что в ассете.
    /// Контраст проверяет <c>MenuThemeTests</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "MenuTheme", menuName = "VR Battlegrounds/UI/Menu Theme")]
    public class MenuTheme : ScriptableObject
    {
        private static MenuTheme s_instance;

        public static MenuTheme Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<MenuTheme>(nameof(MenuTheme));
                if (s_instance == null)
                {
                    GameLog.UI.Warning("[MenuTheme] Нет Resources/MenuTheme.asset — тема по умолчанию из кода.");
                    s_instance = CreateInstance<MenuTheme>();
                }
                return s_instance;
            }
        }

        [Header("Цвета")]
        public Color Surface = Hex("15161C");
        public Color SurfaceRaised = Hex("2A2D38");
        public Color SurfaceHover = Hex("3A3E4C");
        public Color Accent = Hex("FFAF00");
        public Color AccentPressed = Hex("F0A500");
        public Color TextPrimary = Hex("FFFFFF");
        public Color TextSecondary = Hex("C9CCD6");
        public Color TextOnAccent = Hex("15161C");
        public Color Danger = Hex("7A2620");
        public Color DangerHover = Hex("9A3028");
        public Color Success = Hex("4FD17A");
        public Color DisabledFill = Hex("1E2027");
        public Color DisabledText = Hex("8A8E9A");
        public Color Divider = Hex("30333F");

        [Header("Типографика, px канваса (1 px ≈ 0,34 мм)")]
        public float TitleSize = 60f;
        public float BodySize = 46f;
        public float CaptionSize = 40f;
        public float ButtonSize = 44f;
        public float ButtonMinSize = 32f;
        public float TableHeaderSize = 38f;

        [Header("Шрифт")]
        [Tooltip("Жирное начертание для заголовков и кнопок (настоящий Bold чётче искусственного). Пусто — шрифт по умолчанию TMP.")]
        public TMPro.TMP_FontAsset BoldFont;

        [Header("Графика")]
        [Tooltip("9-slice спрайт скруглённой заливки кнопок (встроенный UISprite). Пусто — прямоугольник.")]
        public Sprite RoundedSprite;

        [Header("Звук")]
        [Tooltip("Клик кнопки меню (KitButton) — подтверждение нажатия лазером. Пусто — без звука (MenuSoundTests красный).")]
        public AudioClip ClickSound;
        [Range(0f, 1f)] public float ClickVolume = 0.6f;

        [Header("Размеры, px канваса")]
        [Tooltip("Отступ каркаса от края канваса.")]
        public float SafeMargin = 24f;
        [Tooltip("Ширина левой навигационной колонки: разделы сверху, Назад/Закрыть внизу.")]
        public float RailWidth = 380f;
        public float RailItemHeight = 88f;
        [Tooltip("Полоса главного действия внизу области контента (только у экранов с действием).")]
        public float ActionStripHeight = 104f;
        public float PrimaryMinWidth = 360f;
        public float ZoneGap = 12f;
        public float RowHeight = 88f;
        public float TableRowHeight = 64f;
        public float ButtonMinWidth = 160f;
        public float Spacing = 16f;
        public float ScrollbarWidth = 20f;

        public Color Color(MenuColorRole role)
        {
            switch (role)
            {
                case MenuColorRole.Surface: return Surface;
                case MenuColorRole.SurfaceRaised: return SurfaceRaised;
                case MenuColorRole.SurfaceHover: return SurfaceHover;
                case MenuColorRole.Accent: return Accent;
                case MenuColorRole.TextPrimary: return TextPrimary;
                case MenuColorRole.TextSecondary: return TextSecondary;
                case MenuColorRole.TextOnAccent: return TextOnAccent;
                case MenuColorRole.Danger: return Danger;
                case MenuColorRole.Success: return Success;
                case MenuColorRole.Disabled: return DisabledText;
                case MenuColorRole.Divider: return Divider;
                default: return TextPrimary;
            }
        }

        public float FontSize(MenuTextRole role)
        {
            switch (role)
            {
                case MenuTextRole.Title: return TitleSize;
                case MenuTextRole.Caption: return CaptionSize;
                case MenuTextRole.Button: return ButtonSize;
                case MenuTextRole.TableHeader: return TableHeaderSize;
                default: return BodySize;
            }
        }

        public bool IsBold(MenuTextRole role) => role == MenuTextRole.Title || role == MenuTextRole.Button || role == MenuTextRole.TableHeader;

        /// <summary>
        /// Цвета кнопки по роли и состоянию. Доступная кнопка — всегда яркий текст
        /// (<see cref="TextPrimary"/> или <see cref="TextOnAccent"/>); выбранная — заливка акцентом,
        /// а не вид недоступной.
        /// </summary>
        public MenuButtonColors Button(MenuButtonRole role, MenuButtonState state)
        {
            if (state == MenuButtonState.Disabled) return new MenuButtonColors(DisabledFill, DisabledText);
            if (state == MenuButtonState.Selected) return new MenuButtonColors(Accent, TextOnAccent);

            switch (role)
            {
                case MenuButtonRole.Primary:
                    return state == MenuButtonState.Pressed ? new MenuButtonColors(AccentPressed, TextOnAccent)
                         : state == MenuButtonState.Hover ? new MenuButtonColors(Lighten(Accent, 0.15f), TextOnAccent)
                         : new MenuButtonColors(Accent, TextOnAccent);

                case MenuButtonRole.Danger:
                    return state == MenuButtonState.Normal ? new MenuButtonColors(Danger, TextPrimary)
                         : new MenuButtonColors(DangerHover, TextPrimary);

                default:
                    return state == MenuButtonState.Pressed ? new MenuButtonColors(AccentPressed, TextOnAccent)
                         : state == MenuButtonState.Hover ? new MenuButtonColors(SurfaceHover, TextPrimary)
                         : new MenuButtonColors(SurfaceRaised, TextPrimary);
            }
        }

        private static Color Lighten(Color c, float t) => UnityEngine.Color.Lerp(c, UnityEngine.Color.white, t);

        public static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out Color c);
            return c;
        }
    }

    /// <summary>Контраст по WCAG 2.x: (L1 + 0,05) / (L2 + 0,05), L — относительная яркость sRGB.</summary>
    public static class ColorContrast
    {
        public static float Ratio(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        public static float Luminance(Color c) => 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

        private static float Channel(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
