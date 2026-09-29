using System;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Чистая логика дизайн-системы меню (T-32): контраст цветов темы, стек навигации, видимость
    /// разделов, скролл стиком.
    /// </summary>
    public class MenuKitLogicTests
    {
        /// <summary>Порог для текста на кнопках и в строках: в VR строже веба (SDF и линзы съедают контраст).</summary>
        private const float TextContrast = 7f;

        /// <summary>Недоступное должно читаться, но отличаться от доступного.</summary>
        private const float DisabledContrast = 4.5f;

        private static MenuTheme Theme
        {
            get
            {
                MenuTheme theme = Resources.Load<MenuTheme>(nameof(MenuTheme));
                Assert.IsNotNull(theme, "Нет Resources/MenuTheme.asset — меню красится темой по умолчанию из кода.");
                return theme;
            }
        }

        // ── Контраст ─────────────────────────────────────────────────────

        [Test]
        public void Контраст_считается_по_WCAG()
        {
            Assert.AreEqual(21f, ColorContrast.Ratio(Color.white, Color.black), 0.01f);
            Assert.AreEqual(1f, ColorContrast.Ratio(Color.gray, Color.gray), 0.001f);
            // Контроль: серый текст старых кнопок на фоне экрана — ниже порога.
            Assert.Less(ColorContrast.Ratio(MenuTheme.Hex("7A7A7A"), MenuTheme.Hex("1D1D24")), TextContrast);
        }

        [Test]
        public void Каждая_кнопка_в_каждом_состоянии_контрастна()
        {
            MenuTheme theme = Theme;
            foreach (MenuButtonRole role in Enum.GetValues(typeof(MenuButtonRole)))
            foreach (MenuButtonState state in Enum.GetValues(typeof(MenuButtonState)))
            {
                MenuButtonColors c = theme.Button(role, state);
                float ratio = ColorContrast.Ratio(c.Text, c.Fill);
                float min = state == MenuButtonState.Disabled ? DisabledContrast : TextContrast;
                Assert.GreaterOrEqual(ratio, min, $"Кнопка {role}/{state}: текст к заливке {ratio:F1}:1 < {min}:1.");
            }
        }

        [Test]
        public void Текст_контрастен_к_фону_экрана_и_строки()
        {
            MenuTheme theme = Theme;
            foreach (Color surface in new[] { theme.Surface, theme.SurfaceRaised })
            {
                Assert.GreaterOrEqual(ColorContrast.Ratio(theme.TextPrimary, surface), TextContrast, "TextPrimary");
                Assert.GreaterOrEqual(ColorContrast.Ratio(theme.TextSecondary, surface), TextContrast, "TextSecondary");
            }
            Assert.GreaterOrEqual(ColorContrast.Ratio(theme.TextOnAccent, theme.Accent), TextContrast, "TextOnAccent");
        }

        [Test]
        public void Доступная_кнопка_не_выглядит_недоступной_а_выбранная_отличается()
        {
            MenuTheme theme = Theme;
            MenuButtonColors normal = theme.Button(MenuButtonRole.Secondary, MenuButtonState.Normal);
            MenuButtonColors disabled = theme.Button(MenuButtonRole.Secondary, MenuButtonState.Disabled);
            MenuButtonColors selected = theme.Button(MenuButtonRole.Tab, MenuButtonState.Selected);

            Assert.AreNotEqual(normal.Text, disabled.Text, "Текст доступной и недоступной кнопки одинаков.");
            Assert.AreNotEqual(normal.Fill, selected.Fill, "Выбранная вкладка не отличается от обычной.");
            Assert.AreNotEqual(disabled.Fill, selected.Fill, "Выбранная вкладка выглядит как недоступная (старый приём interactable=false).");
        }

        [Test]
        public void Текст_не_мельче_минимума()
        {
            MenuTheme theme = Theme;
            foreach (MenuTextRole role in Enum.GetValues(typeof(MenuTextRole)))
                Assert.GreaterOrEqual(theme.FontSize(role), 36f, $"Роль {role}: мельче 36 px (≈ 12 dmm на 1 м).");
            Assert.GreaterOrEqual(theme.ButtonMinSize, 32f);
            Assert.GreaterOrEqual(theme.RowHeight, 80f, "Цель луча ниже 80 px (≈ 27 dmm).");
        }

        // ── Навигация ────────────────────────────────────────────────────

        [Test]
        public void На_корне_раздела_назад_означает_закрыть()
        {
            var nav = new MenuNavigation();
            nav.OpenTab(MenuScreenType.Main);
            Assert.IsTrue(nav.IsAtRoot);
            Assert.IsFalse(nav.Back(), "На корне Back() обязан вернуть false — контроллер закрывает меню.");
            Assert.AreEqual(MenuScreenType.Main, nav.Current);
        }

        [Test]
        public void Вложенный_экран_и_назад()
        {
            var nav = new MenuNavigation();
            nav.OpenTab(MenuScreenType.MatchManager);
            nav.Push(MenuScreenType.PlayersTeams);

            Assert.AreEqual(MenuScreenType.PlayersTeams, nav.Current);
            Assert.AreEqual(MenuScreenType.MatchManager, nav.Root, "Раздел в колонке остаётся выделенным во вложенном экране.");
            Assert.IsFalse(nav.IsAtRoot);

            Assert.IsTrue(nav.Back());
            Assert.AreEqual(MenuScreenType.MatchManager, nav.Current);
        }

        /// <summary>Мастер «Далее → Далее»: «Назад» возвращает ровно на один шаг, до корня раздела.</summary>
        [Test]
        public void Многоуровневое_меню_назад_на_один_уровень()
        {
            var nav = new MenuNavigation();
            nav.OpenTab(MenuScreenType.MatchManager);
            nav.Push(MenuScreenType.SessionSetup);
            nav.Push(MenuScreenType.PlayersTeams);
            nav.Push(MenuScreenType.Statistics);
            Assert.AreEqual(4, nav.Depth);

            Assert.IsTrue(nav.Back());
            Assert.AreEqual(MenuScreenType.PlayersTeams, nav.Current);
            Assert.IsTrue(nav.Back());
            Assert.AreEqual(MenuScreenType.SessionSetup, nav.Current);
            Assert.IsTrue(nav.Back());
            Assert.AreEqual(MenuScreenType.MatchManager, nav.Current);
            Assert.IsTrue(nav.IsAtRoot);
            Assert.IsFalse(nav.Back(), "На корне — закрыть меню.");
        }

        [Test]
        public void Раздел_сбрасывает_стек()
        {
            var nav = new MenuNavigation();
            nav.OpenTab(MenuScreenType.Debug);
            nav.Push(MenuScreenType.PerfTests);
            nav.OpenTab(MenuScreenType.Statistics);

            Assert.AreEqual(1, nav.Depth);
            Assert.AreEqual(MenuScreenType.Statistics, nav.Current);
        }

        [Test]
        public void Переход_на_экран_из_стека_не_зацикливает_стек()
        {
            var nav = new MenuNavigation();
            nav.OpenTab(MenuScreenType.Debug);
            nav.Push(MenuScreenType.MatchManager);
            nav.Push(MenuScreenType.Debug);

            Assert.AreEqual(1, nav.Depth, "Возврат на экран из стека должен срезать стек, а не расти.");
            nav.Push(MenuScreenType.PerfTests);
            nav.Push(MenuScreenType.PerfTests);
            Assert.AreEqual(2, nav.Depth, "Повторный переход на текущий экран стек не растит.");
        }

        [Test]
        public void Видимость_разделов_по_правам()
        {
            Assert.IsTrue(MenuTab.IsVisible(MenuVisibility.Everyone, false, false));
            Assert.IsFalse(MenuTab.IsVisible(MenuVisibility.AdminOnly, false, true));
            Assert.IsTrue(MenuTab.IsVisible(MenuVisibility.AdminOnly, true, false));
            Assert.IsFalse(MenuTab.IsVisible(MenuVisibility.DebugOnly, true, false));
            Assert.IsTrue(MenuTab.IsVisible(MenuVisibility.DebugOnly, false, true));
        }

        [Test]
        public void Разделы_по_умолчанию_не_больше_шести_и_админские_закрыты()
        {
            var tabs = MenuView.DefaultTabs();
            Assert.LessOrEqual(tabs.Count, 6, "Колонка рассчитана не больше чем на 6 разделов.");
            Assert.AreEqual(MenuScreenType.Main, tabs[0].Screen, "Первый раздел — «Обзор».");

            Assert.AreEqual(MenuVisibility.AdminOnly, tabs.Find(t => t.Screen == MenuScreenType.MatchManager).Visibility);
            Assert.AreEqual(MenuVisibility.DebugOnly, tabs.Find(t => t.Screen == MenuScreenType.Debug).Visibility);
            Assert.AreEqual(MenuVisibility.Everyone, tabs.Find(t => t.Screen == MenuScreenType.Statistics).Visibility);
            Assert.IsFalse(tabs.Exists(t => t.Screen == MenuScreenType.SessionSetup),
                "Выбор серии — не раздел для всех: вход в него только из «Админ».");
        }

        // ── Скролл стиком ────────────────────────────────────────────────

        [Test]
        public void Стик_в_мёртвой_зоне_не_листает()
        {
            Assert.AreEqual(0.5f, StickScrollMath.Step(0.5f, 0.2f, 0.1f, 3000f, 900f, 1400f));
        }

        [Test]
        public void Стик_вверх_листает_вверх_и_упирается_в_край()
        {
            float up = StickScrollMath.Step(0.5f, 1f, 0.1f, 3000f, 900f, 1400f);
            Assert.Greater(up, 0.5f);
            Assert.AreEqual(140f / 2100f, up - 0.5f, 1e-4f, "Полное отклонение — ровно pixelsPerSecond × dt.");

            float down = StickScrollMath.Step(0.5f, -1f, 0.1f, 3000f, 900f, 1400f);
            Assert.Less(down, 0.5f);

            Assert.AreEqual(1f, StickScrollMath.Step(0.99f, 1f, 1f, 3000f, 900f, 1400f), "Позиция зажата в [0, 1].");
        }

        [Test]
        public void Содержимое_помещается_стик_не_двигает()
        {
            Assert.AreEqual(1f, StickScrollMath.Step(1f, -1f, 0.1f, 800f, 900f, 1400f));
        }

        [Test]
        public void Два_стика_берётся_отклонённый_сильнее()
        {
            Assert.AreEqual(-0.9f, StickScrollMath.Combine(-0.9f, 0.3f));
            Assert.AreEqual(0.8f, StickScrollMath.Combine(0.1f, 0.8f));
            Assert.AreEqual(0.5f, StickScrollMath.Combine(0.5f, -0.5f), "Встречные отклонения не гасят друг друга в ноль.");
        }
    }
}
