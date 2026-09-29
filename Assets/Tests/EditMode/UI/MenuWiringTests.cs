using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.UI.Menu;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Проводка планшета (T-32): все экраны на месте и правильного класса, разделы колонки
    /// ведут на существующие экраны, экраны админа и «Перф-тесты» достижимы только из своих
    /// разделов (админ / отладка). Заменяет прежние Match/Debug/PerfTests-WiringTests, которые
    /// проверяли кнопки-переходы с зашитой целью.
    /// </summary>
    public class MenuWiringTests
    {
        private const string Tablet = "Assets/Prefabs/UI/Menu/Tablet/Tablet.prefab";

        private static GameObject LoadTablet()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Tablet);
            Assert.IsNotNull(go, $"Нет {Tablet}.");
            return go;
        }

        private static MenuScreen Screen(GameObject tablet, MenuScreenType type)
        {
            MenuScreen screen = tablet.GetComponentsInChildren<MenuScreen>(true).FirstOrDefault(s => s.ScreenType == type);
            Assert.IsNotNull(screen, $"На планшете нет экрана {type}.");
            return screen;
        }

        [TestCase(MenuScreenType.TeamSelection, typeof(MenuTeamSelection))]
        [TestCase(MenuScreenType.Statistics, typeof(MenuStatistics))]
        [TestCase(MenuScreenType.PhysicalSpaceSync, typeof(MenuPhysicalSpaceSync))]
        [TestCase(MenuScreenType.MatchManager, typeof(MenuMatchManager))]
        [TestCase(MenuScreenType.PlayersTeams, typeof(MenuPlayersTeams))]
        [TestCase(MenuScreenType.SessionSetup, typeof(MenuSessionSetup))]
        [TestCase(MenuScreenType.Debug, typeof(MenuDebug))]
        [TestCase(MenuScreenType.PerfTests, typeof(MenuPerfTests))]
        public void Экран_на_планшете_и_закрыт_по_умолчанию(MenuScreenType type, Type expected)
        {
            MenuScreen screen = Screen(LoadTablet(), type);
            Assert.IsInstanceOf(expected, screen);
            Assert.IsFalse(screen.gameObject.activeSelf, $"Экран {type} открыт в префабе — при старте экраны налезут друг на друга.");
        }

        [Test]
        public void Разделы_колонки_ведут_на_экраны_планшета()
        {
            GameObject tablet = LoadTablet();
            MenuView view = tablet.GetComponent<MenuView>();
            var tabs = MenuView.DefaultTabs();

            foreach (MenuTab tab in tabs) Screen(tablet, tab.Screen);
            Assert.IsTrue(tabs.Exists(t => t.Screen == view.DefaultScreen), "Планшет открывается не на разделе колонки.");
            Assert.AreNotEqual(MenuScreenType.SessionSetup, view.DefaultScreen, "Планшет открывается сразу на выборе серии — в обход админа.");
        }

        [Test]
        public void Экраны_админа_только_из_раздела_Админ()
        {
            var tabs = MenuView.DefaultTabs();
            Assert.AreEqual(MenuVisibility.AdminOnly, tabs.Find(t => t.Screen == MenuScreenType.MatchManager).Visibility);

            foreach (MenuScreenType admin in new[] { MenuScreenType.PlayersTeams, MenuScreenType.SessionSetup })
            {
                Assert.IsFalse(tabs.Exists(t => t.Screen == admin), $"{admin} — раздел, видимый всем.");
                Assert.IsTrue(MenuMatchManager.Links.Any(l => l.screen == admin), $"Из раздела «Админ» нет входа в {admin}.");
                Assert.IsTrue(MenuDebug.Links.Where(l => l.screen == admin).All(l => l.adminOnly), $"Из «Отладки» вход в {admin} виден не-админу.");
            }
        }

        [Test]
        public void Перф_тесты_только_из_Отладки()
        {
            var tabs = MenuView.DefaultTabs();
            Assert.IsFalse(tabs.Exists(t => t.Screen == MenuScreenType.PerfTests), "«Перф-тесты» — раздел колонки, а должны быть внутри «Отладки».");
            Assert.AreEqual(MenuVisibility.DebugOnly, tabs.Find(t => t.Screen == MenuScreenType.Debug).Visibility);
            Assert.IsTrue(MenuDebug.Links.Any(l => l.screen == MenuScreenType.PerfTests), "Из «Отладки» нет входа в «Перф-тесты».");
        }

        [Test]
        public void Выбор_серии_с_реестрами()
        {
            var setup = (MenuSessionSetup)Screen(LoadTablet(), MenuScreenType.SessionSetup);
            var so = new SerializedObject(setup);
            Assert.IsNotNull(so.FindProperty("_gameModeRegistry").objectReferenceValue, "Не назначен реестр режимов.");
            Assert.IsNotNull(so.FindProperty("_mapRegistry").objectReferenceValue, "Не назначен реестр карт.");
        }

        [Test]
        public void Номер_в_очереди_на_плитке_карты()
        {
            Assert.AreEqual("TestMap", MenuSessionSetup.TileLabel("TestMap", 0));
            Assert.AreEqual("2 · TestMap", MenuSessionSetup.TileLabel("TestMap", 2));
        }

        [Test]
        public void Главное_действие_админа_по_приоритету()
        {
            Assert.AreEqual(MapCommand.GoLive, MenuMatchManager.PrimaryCommand(new[] { MapCommand.Stop, MapCommand.GoLive }));
            Assert.AreEqual(MapCommand.Resume, MenuMatchManager.PrimaryCommand(new[] { MapCommand.Resume, MapCommand.Stop }));
            Assert.IsNull(MenuMatchManager.PrimaryCommand(new[] { MapCommand.Pause, MapCommand.Stop }),
                "«Пауза» и «Стоп» — не главное действие: пауза случайным нажатием и стоп серии опасны.");
            Assert.AreEqual("В лобби", MenuMatchManager.Label(MapCommand.NextMap, lastMap: true));
        }
    }
}
