using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Кнопка набора. Цвета заливки и текста по роли и состоянию — только из темы
    /// (<see cref="MenuTheme.Button"/>): доступная кнопка всегда с ярким текстом, выбранная (вкладка)
    /// — залита акцентом. Текст — <see cref="TextMeshProUGUI"/> проектного шрифта, авторазмер от
    /// <see cref="MenuTheme.ButtonMinSize"/> до <see cref="MenuTheme.ButtonSize"/> с многоточием —
    /// длинная подпись не вылезает за кнопку.
    /// </summary>
    public class KitButton : Button
    {
        [SerializeField] private MenuButtonRole _role;
        [SerializeField] private Image _fill;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Image _image;
        [SerializeField] private bool _selected;

        public MenuButtonRole Role => _role;
        public TMP_Text Label => _label;
        public Image Picture => _image;

        public string Text
        {
            get => _label != null ? _label.text : "";
            set
            {
                if (_label == null || _label.text == value) return;
                _label.text = value;
                FitWidth();
            }
        }

        /// <summary>Выбранная вкладка / выбранный вариант. Не путать с недоступной: у той свой вид.</summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                ApplyColors(currentSelectionState);
            }
        }

        /// <summary>Связывает части кнопки (вызывает <see cref="MenuKit"/> при сборке).</summary>
        public void Setup(MenuButtonRole role, Image fill, TMP_Text label, Image picture)
        {
            _role = role;
            _fill = fill;
            _label = label;
            _image = picture;
            transition = Transition.None;
            targetGraphic = fill;
            FitWidth();
            ApplyColors(SelectionState.Normal);
        }

        public void SetRole(MenuButtonRole role)
        {
            _role = role;
            ApplyColors(currentSelectionState);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            ApplyColors(currentSelectionState);
        }

        protected override void DoStateTransition(SelectionState state, bool instant) => ApplyColors(state);

        // Клик — звуком (MenuClickSound): нажатие лазером в VR иначе не подтверждено ничем, кроме цвета.
        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
                MenuClickSound.Play(this);
            base.OnPointerClick(eventData);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (IsActive() && IsInteractable()) MenuClickSound.Play(this);
            base.OnSubmit(eventData);
        }

        private void ApplyColors(SelectionState state)
        {
            MenuTheme theme = MenuTheme.Instance;
            if (theme == null) return;

            MenuButtonColors c = theme.Button(_role, ToMenuState(state));
            if (_fill != null) _fill.color = c.Fill;
            if (_label != null) _label.color = c.Text;
        }

        private MenuButtonState ToMenuState(SelectionState state)
        {
            if (!IsInteractable()) return MenuButtonState.Disabled;
            if (_selected) return MenuButtonState.Selected;
            switch (state)
            {
                case SelectionState.Highlighted: return MenuButtonState.Hover;
                case SelectionState.Pressed: return MenuButtonState.Pressed;
                // Selected у Unity — фокус после клика; в VR он «залипал» бы подсветкой.
                default: return MenuButtonState.Normal;
            }
        }

        /// <summary>Предпочтительная ширина — по тексту, не уже минимума темы. Сжимать её вправе раскладка.</summary>
        private void FitWidth()
        {
            var element = GetComponent<LayoutElement>();
            if (element == null || _label == null) return;

            MenuTheme theme = MenuTheme.Instance;
            float textWidth = _label.GetPreferredValues(_label.text, 10000f, 1000f).x;
            element.preferredWidth = Mathf.Max(theme.ButtonMinWidth, textWidth + 2f * theme.Spacing + 16f);
        }
    }
}
