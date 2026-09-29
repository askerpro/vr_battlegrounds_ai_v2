using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>Кому видна вкладка планшета.</summary>
    public enum MenuVisibility
    {
        Everyone = 0,
        AdminOnly = 1,
        DebugOnly = 2,
    }

    /// <summary>Вкладка навигационной панели: раздел верхнего уровня (корень своего стека).</summary>
    [Serializable]
    public struct MenuTab
    {
        public MenuScreenType Screen;
        public string Label;
        public MenuVisibility Visibility;

        public MenuTab(MenuScreenType screen, string label, MenuVisibility visibility = MenuVisibility.Everyone)
        {
            Screen = screen;
            Label = label;
            Visibility = visibility;
        }

        /// <summary>Видна ли вкладка. Права это не проверяет — их проверяет сервер; здесь — не показывать лишнего.</summary>
        public static bool IsVisible(MenuVisibility visibility, bool isAdmin, bool debugEnabled)
        {
            switch (visibility)
            {
                case MenuVisibility.AdminOnly: return isAdmin;
                case MenuVisibility.DebugOnly: return debugEnabled;
                default: return true;
            }
        }
    }

    /// <summary>
    /// Список разделов колонки (чистая логика, <c>MenuTabsTests</c>): игровые разделы объявляет
    /// активный режим (<c>GameModeData.menuTabs</c>; пусто — <see cref="DefaultModeTabs"/>), системные
    /// (<see cref="SystemTabs"/>) добавляются всегда — режим не может убрать калибровку или отладку.
    /// Разделы, экрана которых нет на планшете, отбрасываются.
    /// </summary>
    public static class MenuTabs
    {
        /// <summary>Сколько разделов помещается в колонку.</summary>
        public const int Max = 6;

        /// <summary>Игровые разделы, если режим своих не объявил (и вне режима: офлайн, до старта).</summary>
        public static List<MenuTab> DefaultModeTabs() => new List<MenuTab>
        {
            new MenuTab(MenuScreenType.Main, "Обзор"),
            new MenuTab(MenuScreenType.TeamSelection, "Команда"),
            new MenuTab(MenuScreenType.Statistics, "Статистика"),
        };

        /// <summary>Системные разделы — есть при любом режиме.</summary>
        public static List<MenuTab> SystemTabs() => new List<MenuTab>
        {
            new MenuTab(MenuScreenType.PhysicalSpaceSync, "Калибровка"),
            new MenuTab(MenuScreenType.MatchManager, "Админ", MenuVisibility.AdminOnly),
            new MenuTab(MenuScreenType.Debug, "Отладка", MenuVisibility.DebugOnly),
        };

        /// <summary>
        /// Разделы колонки: игровые режима (или по умолчанию), затем системные; без повторов экрана
        /// (системный экран в списке режима — на месте режима) и без экранов, которых нет на планшете.
        /// </summary>
        public static List<MenuTab> Resolve(IList<MenuTab> modeTabs, Func<MenuScreenType, bool> screenExists = null)
        {
            var result = new List<MenuTab>();
            IList<MenuTab> game = modeTabs != null && modeTabs.Count > 0 ? modeTabs : DefaultModeTabs();

            foreach (MenuTab tab in game) Add(result, tab, screenExists);
            foreach (MenuTab tab in SystemTabs()) Add(result, tab, screenExists);
            return result;
        }

        private static void Add(List<MenuTab> list, MenuTab tab, Func<MenuScreenType, bool> screenExists)
        {
            if (tab.Screen == MenuScreenType.None) return;
            if (screenExists != null && !screenExists(tab.Screen)) return;
            if (list.Exists(t => t.Screen == tab.Screen)) return;
            if (string.IsNullOrEmpty(tab.Label)) tab.Label = tab.Screen.ToString();
            list.Add(tab);
        }
    }

    /// <summary>
    /// Стек навигации планшета (чистая логика, <c>MenuNavigationTests</c>). Вкладка — корень стека,
    /// <see cref="Push"/> — вложенный экран, <see cref="Back"/> — на шаг назад. На корне «назад»
    /// означает «закрыть меню»: левый слот панели показывает «Закрыть».
    /// </summary>
    public class MenuNavigation
    {
        /// <summary>
        /// Глубина, после которой пишется предупреждение: меню можно проваливать вглубь (мастер
        /// «Далее → Далее»), но больше 6 уровней — повод упростить. Переход не запрещается.
        /// </summary>
        public const int MaxDepth = 6;

        private readonly List<MenuScreenType> _stack = new List<MenuScreenType>();

        public MenuScreenType Current => _stack.Count > 0 ? _stack[_stack.Count - 1] : MenuScreenType.None;
        public MenuScreenType Root => _stack.Count > 0 ? _stack[0] : MenuScreenType.None;
        public bool IsAtRoot => _stack.Count <= 1;
        public int Depth => _stack.Count;

        /// <summary>Открыть раздел: стек сбрасывается до вкладки.</summary>
        public void OpenTab(MenuScreenType tab)
        {
            _stack.Clear();
            if (tab != MenuScreenType.None) _stack.Add(tab);
        }

        /// <summary>Вложенный экран. Повторный переход на текущий экран стек не растит.</summary>
        public void Push(MenuScreenType screen)
        {
            if (screen == MenuScreenType.None || screen == Current) return;

            // Экран уже в стеке (например, «Отладка» → «Матч» → «Отладка»): вернуться к нему, а не зациклить стек.
            int existing = _stack.IndexOf(screen);
            if (existing >= 0)
            {
                _stack.RemoveRange(existing + 1, _stack.Count - existing - 1);
                return;
            }

            if (_stack.Count >= MaxDepth)
                VrBattlegrounds.Core.GameLog.UI.Warning($"[MenuNavigation] Глубже {MaxDepth} уровней: {string.Join(" → ", _stack)} → {screen}.");

            _stack.Add(screen);
        }

        /// <summary>На шаг назад. <c>false</c> — стек на корне: вызывающий закрывает меню.</summary>
        public bool Back()
        {
            if (IsAtRoot) return false;
            _stack.RemoveAt(_stack.Count - 1);
            return true;
        }

        public void Clear() => _stack.Clear();
    }

    /// <summary>
    /// Скролл стиком (чистая логика, <c>StickScrollTests</c>): отклонение стика → смещение
    /// <c>ScrollRect.verticalNormalizedPosition</c>. Скорость задана в пикселях канваса в секунду,
    /// поэтому длинный и короткий список листаются одинаково быстро.
    /// </summary>
    public static class StickScrollMath
    {
        public const float DeadZone = 0.25f;

        /// <summary>Два стика в один сигнал: берётся отклонённый сильнее (оба в разные стороны не складываются в ноль).</summary>
        public static float Combine(float left, float right) => Mathf.Abs(left) >= Mathf.Abs(right) ? left : right;

        /// <summary>
        /// Новая нормализованная позиция (1 — верх, 0 — низ). Стик вверх (y &gt; 0) листает вверх.
        /// Содержимое помещается — позиция не меняется.
        /// </summary>
        public static float Step(float normalizedPosition, float stickY, float deltaTime,
                                 float contentHeight, float viewportHeight, float pixelsPerSecond)
        {
            float scrollable = contentHeight - viewportHeight;
            if (scrollable <= 0.5f || Mathf.Abs(stickY) < DeadZone || deltaTime <= 0f) return normalizedPosition;

            // Плавный вход за мёртвой зоной: на краю зоны — 0, при полном отклонении — полная скорость.
            float input = Mathf.Sign(stickY) * (Mathf.Abs(stickY) - DeadZone) / (1f - DeadZone);
            float delta = input * pixelsPerSecond * deltaTime / scrollable;
            return Mathf.Clamp01(normalizedPosition + delta);
        }
    }

}
