using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Разделы колонки планшета объявляет режим (<c>GameModeData.menuTabs</c>), системные добавляются
    /// всегда (<see cref="MenuTabs"/>). Проверяется правило и каждый режим реестра (и разминка):
    /// не больше 6 разделов, все экраны есть на планшете, системные на месте.
    /// </summary>
    public class MenuTabsTests
    {
        private const string RegistryPath = "Assets/Data/GameModes/GameModeRegistry.asset";
        private const string Tablet = "Assets/Prefabs/UI/Menu/Tablet/Tablet.prefab";

        private static readonly MenuScreenType[] System =
            { MenuScreenType.PhysicalSpaceSync, MenuScreenType.MatchManager, MenuScreenType.Debug };

        [Test]
        public void Режим_без_списка_получает_разделы_по_умолчанию_и_системные()
        {
            List<MenuTab> tabs = MenuTabs.Resolve(null);
            CollectionAssert.AreEqual(
                new[] { MenuScreenType.Main, MenuScreenType.TeamSelection, MenuScreenType.Statistics,
                        MenuScreenType.PhysicalSpaceSync, MenuScreenType.MatchManager, MenuScreenType.Debug },
                tabs.Select(t => t.Screen).ToArray());
            CollectionAssert.AreEqual(tabs.Select(t => t.Screen), MenuTabs.Resolve(new List<MenuTab>()).Select(t => t.Screen));
        }

        [Test]
        public void Режим_объявляет_свои_игровые_разделы_системные_остаются()
        {
            var mode = new List<MenuTab>
            {
                new MenuTab(MenuScreenType.Statistics, "Счёт"),
                new MenuTab(MenuScreenType.Main, "Обзор"),
            };
            List<MenuTab> tabs = MenuTabs.Resolve(mode);

            Assert.AreEqual(MenuScreenType.Statistics, tabs[0].Screen, "Порядок режима сохраняется.");
            Assert.AreEqual("Счёт", tabs[0].Label, "Подпись режима сохраняется.");
            Assert.IsFalse(tabs.Exists(t => t.Screen == MenuScreenType.TeamSelection), "Раздел, которого режим не объявил, не добавляется.");
            foreach (MenuScreenType s in System)
                Assert.IsTrue(tabs.Exists(t => t.Screen == s), $"Системный раздел {s} пропал — режим не может его убрать.");
        }

        [Test]
        public void Без_повторов_и_без_экранов_которых_нет_на_планшете()
        {
            var mode = new List<MenuTab>
            {
                new MenuTab(MenuScreenType.Main, "Обзор"),
                new MenuTab(MenuScreenType.Main, "Обзор ещё раз"),
                new MenuTab(MenuScreenType.Debug, "Отладка режима", MenuVisibility.DebugOnly),
                new MenuTab(MenuScreenType.Settings, "Настройки"),
            };
            List<MenuTab> tabs = MenuTabs.Resolve(mode, s => s != MenuScreenType.Settings);

            Assert.AreEqual(1, tabs.Count(t => t.Screen == MenuScreenType.Main));
            Assert.AreEqual(1, tabs.Count(t => t.Screen == MenuScreenType.Debug), "Системный экран в списке режима — один раз.");
            Assert.IsFalse(tabs.Exists(t => t.Screen == MenuScreenType.Settings), "Экрана нет на планшете — раздела нет.");
        }

        [Test]
        public void Каждый_режим_реестра_помещается_в_колонку_и_ведёт_на_экраны_планшета()
        {
            var registry = AssetDatabase.LoadAssetAtPath<GameModeRegistry>(RegistryPath);
            Assert.IsNotNull(registry, $"Нет {RegistryPath}.");
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>(Tablet);
            Assert.IsNotNull(tablet);
            var screens = new HashSet<MenuScreenType>(tablet.GetComponentsInChildren<MenuScreen>(true).Select(s => s.ScreenType));

            var modes = new List<GameModeData>(registry.modes.Where(m => m != null));
            if (registry.warmup != null) modes.Add(registry.warmup);
            Assert.IsNotEmpty(modes, "Контроль: в реестре есть режимы.");

            foreach (GameModeData mode in modes)
            {
                foreach (MenuTab tab in mode.menuTabs)
                    Assert.IsTrue(screens.Contains(tab.Screen), $"Режим '{mode.modeId}': раздел «{tab.Label}» ведёт на {tab.Screen}, которого нет на планшете.");

                List<MenuTab> tabs = MenuTabs.Resolve(mode.menuTabs, screens.Contains);
                Assert.LessOrEqual(tabs.Count, MenuTabs.Max, $"Режим '{mode.modeId}': {tabs.Count} разделов — колонка вмещает {MenuTabs.Max}.");
                foreach (MenuScreenType s in System)
                    Assert.IsTrue(tabs.Exists(t => t.Screen == s), $"Режим '{mode.modeId}': нет системного раздела {s}.");
            }
        }
    }
}
