using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран планшета. Экран — только содержимое: лежит в прокручиваемой области каркаса
    /// (<see cref="MenuFrame.Screens"/>), строит строки через <see cref="MenuKit"/> в
    /// <see cref="Content"/>. Навигация («Назад/Закрыть», разделы) — у каркаса, главное действие —
    /// <see cref="SetPrimary"/> (всегда справа внизу). Префаб экрана — вариант
    /// <c>Screen_Base.prefab</c> (T-32, проверяет <c>MenuDesignRulesTests</c>).
    /// </summary>
    public class MenuScreen : MonoBehaviour
    {
        [SerializeField] private MenuScreenType _screenType;

        public MenuScreenType ScreenType => _screenType;

        public UnityEvent OnShow;
        public UnityEvent OnHide;

        [System.NonSerialized] private float _screenRefreshTimer;

        /// <summary>Содержимое экрана: вертикальный столбец во всю ширину области контента.</summary>
        public RectTransform Content => (RectTransform)transform;

        /// <summary>Каркас планшета, в котором лежит экран (в превью редактора контроллера нет — ищем по иерархии).</summary>
        protected MenuFrame Frame => GetComponentInParent<MenuFrame>(true);

        public virtual void Show()
        {
            gameObject.SetActive(true);
            _screenRefreshTimer = 0f;
            OnShow?.Invoke();
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
            OnHide?.Invoke();
        }

        /// <summary>
        /// «Назад» внутри экрана (например, этап «скин» → этап «команда»). <c>true</c> — экран
        /// обработал сам, стек навигации не трогается.
        /// </summary>
        public virtual bool HandleBack() => false;

        /// <summary>Есть ли у экрана свой шаг назад — тогда на корне раздела каркас пишет «Назад», а не «Закрыть».</summary>
        public virtual bool HasInnerBack => false;

        /// <summary>
        /// Превью в редакторе: построить содержимое тем же кодом, что в игре, но из образцовых данных
        /// (фейковые игроки, карточки, таблицы). Вызывает <c>MenuPreview</c> (инспектор планшета) и
        /// инструмент снимков; созданное помечается «не сохранять» и в префаб не попадает.
        /// По умолчанию — обычный <see cref="Show"/>: экрану, чьи данные есть и в редакторе (реестры),
        /// своё превью не нужно.
        /// </summary>
        public virtual void BuildPreview() => Show();

        // ── Для наследников ──────────────────────────────────────────────

        /// <summary>Главное действие экрана — справа внизу области контента.</summary>
        protected void SetPrimary(string label, UnityAction onClick, bool interactable = true,
                                  MenuButtonRole role = MenuButtonRole.Primary)
        {
            if (Frame != null) Frame.SetPrimary(label, onClick, interactable, role);
        }

        protected void SetSecondary(string label, UnityAction onClick, bool interactable = true)
        {
            if (Frame != null) Frame.SetSecondary(label, onClick, interactable);
        }

        protected void SetPrimaryInteractable(bool interactable)
        {
            if (Frame != null) Frame.SetPrimaryInteractable(interactable);
        }

        protected void SetSecondaryInteractable(bool interactable)
        {
            if (Frame != null) Frame.SetSecondaryInteractable(interactable);
        }

        protected void ClearActions()
        {
            if (Frame != null) Frame.ClearActions();
        }

        /// <summary>Вложенный экран (не глубже двух уровней).</summary>
        protected static void Push(MenuScreenType screen)
        {
            if (MenuController.Instance != null) MenuController.Instance.Push(screen);
        }

        /// <summary>Каркасу пересчитать надпись «Назад/Закрыть» (сменился внутренний шаг экрана).</summary>
        protected static void RefreshNavigation()
        {
            if (MenuController.Instance != null) MenuController.Instance.RefreshBackButton();
        }

        protected static void CloseMenu()
        {
            if (MenuController.Instance != null) MenuController.Instance.CloseMenu();
        }

        /// <summary>
        /// Пора перестроить данные: раз в <paramref name="interval"/> с. Перестраивать — только
        /// при изменении: пересоздание кнопок сбивает наведение луча в VR.
        /// </summary>
        protected bool RefreshDue(float interval = 0.5f)
        {
            _screenRefreshTimer += Time.unscaledDeltaTime;
            if (_screenRefreshTimer < interval) return false;
            _screenRefreshTimer = 0f;
            return true;
        }

        /// <summary>
        /// Раскладка содержимого экрана по правилам каркаса: столбец во всю ширину, растёт вниз.
        /// Задаётся в <c>Screen_Base.prefab</c>; здесь — страховка для экрана, собранного иначе.
        /// </summary>
        public static void ApplyContentLayout(GameObject screen)
        {
            var rt = (RectTransform)screen.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            var layout = screen.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = screen.AddComponent<VerticalLayoutGroup>();
            layout.spacing = MenuTheme.Instance.Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
    }
}
