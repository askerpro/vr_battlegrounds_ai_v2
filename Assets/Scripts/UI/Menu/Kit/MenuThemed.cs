using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Цвет графики (и размер текста) по роли из <see cref="MenuTheme"/>. Литералы цвета в меню не
    /// ставятся: <c>MenuDesignRulesTests</c> требует этот компонент на каждой графике, кроме кнопок
    /// набора (их красит <see cref="KitButton"/>) и картинок-превью.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Graphic))]
    public class MenuThemed : MonoBehaviour
    {
        [SerializeField] private MenuColorRole _color = MenuColorRole.TextPrimary;
        [Tooltip("Для текста — размер и начертание из темы.")]
        [SerializeField] private MenuTextRole _text = MenuTextRole.Body;

        public MenuColorRole ColorRole => _color;

        public void Set(MenuColorRole color, MenuTextRole text)
        {
            _color = color;
            _text = text;
            Apply();
        }

        private void OnEnable() => Apply();

#if UNITY_EDITOR
        private void OnValidate() => Apply();
#endif

        public void Apply()
        {
            MenuTheme theme = MenuTheme.Instance;
            var graphic = GetComponent<Graphic>();
            if (graphic == null || theme == null) return;

            graphic.color = theme.Color(_color);

            if (graphic is TMP_Text tmp)
            {
                tmp.fontSize = theme.FontSize(_text);
                bool bold = theme.IsBold(_text);
                if (bold && theme.BoldFont != null) tmp.font = theme.BoldFont;
                tmp.fontStyle = bold && theme.BoldFont == null ? FontStyles.Bold : FontStyles.Normal;
            }
        }
    }
}
