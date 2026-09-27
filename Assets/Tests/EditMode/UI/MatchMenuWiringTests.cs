using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Проводка меню серии в префабах: очередь карт (номер на плитке, «Начать», «Очистить»),
    /// экран админа «Матч» с четырьмя кнопками, экран «Статистика» у всех, кнопки перехода
    /// на главном экране планшета; «Матч» — только админу.
    /// </summary>
    public class MatchMenuWiringTests
    {
        private const string Tablet = "Assets/Prefabs/UI/Menu/VR/PlayerAdmin/Lobby/PlayerAdminLobbyTabletMenu.prefab";
        private const string Screens = "Assets/Prefabs/UI/Menu/VR/MenuScreens/";

        private static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, $"Нет {path}.");
            return go;
        }

        private static Object Field(Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;

        [Test]
        public void Плитка_карты_показывает_номер_в_очереди()
        {
            Transform number = Load(Screens + "MapEntry.prefab").GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "QueueNumber");
            Assert.IsNotNull(number, "На плитке карты нет QueueNumber.");
            Assert.IsNotNull(number.GetComponent<TMPro.TMP_Text>(), "QueueNumber — не текст.");
        }

        [Test]
        public void Выбор_сессии_с_кнопками_Начать_и_Очистить()
        {
            var setup = Load(Screens + "Screen_SessionSetup.prefab").GetComponent<MenuSessionSetup>();
            Assert.IsNotNull(Field(setup, "_startButton"), "Нет кнопки «Начать» — серию не запустить.");
            Assert.IsNotNull(Field(setup, "_clearButton"), "Нет кнопки «Очистить».");
        }

        [Test]
        public void На_планшете_экраны_Матч_и_Статистика_и_кнопки_к_ним()
        {
            GameObject tablet = Load(Tablet);
            MenuScreen[] screens = tablet.GetComponentsInChildren<MenuScreen>(true);

            var match = screens.OfType<MenuMatchManager>().FirstOrDefault();
            Assert.IsNotNull(match, "На планшете нет экрана «Матч».");
            Assert.AreEqual(MenuScreenType.MatchManager, match.ScreenType);
            foreach (string f in new[] { "_startMatchButton", "_pauseButton", "_resumeButton", "_stopButton" })
                Assert.IsNotNull(Field(match, f), $"У экрана «Матч» не назначено {f}.");

            var stats = screens.OfType<MenuStatistics>().FirstOrDefault();
            Assert.IsNotNull(stats, "На планшете нет экрана «Статистика».");
            Assert.AreEqual(MenuScreenType.Statistics, stats.ScreenType);
            Assert.IsNotNull(Field(stats, "_table"));

            SwitchMenuButton[] switches = tablet.GetComponentsInChildren<SwitchMenuButton>(true);
            Assert.IsTrue(switches.Any(b => b.TargetScreen == MenuScreenType.Statistics), "Нет кнопки перехода к «Статистике».");
            SwitchMenuButton toMatch = switches.FirstOrDefault(b => b.TargetScreen == MenuScreenType.MatchManager);
            Assert.IsNotNull(toMatch, "Нет кнопки перехода к экрану «Матч».");

            var adminOnly = tablet.GetComponentsInChildren<AdminOnlyElements>(true).FirstOrDefault();
            Assert.IsNotNull(adminOnly, "Кнопка «Матч» видна всем: нет AdminOnlyElements.");
            var so = new SerializedObject(adminOnly).FindProperty("_elements");
            bool listed = false;
            for (int i = 0; i < so.arraySize; i++) listed |= so.GetArrayElementAtIndex(i).objectReferenceValue == toMatch.gameObject;
            Assert.IsTrue(listed, "Кнопка «Матч» не в списке только-для-админа.");
        }

        /// <summary>
        /// Выбор серии («Играть») — только админу: сервер и так отклонит не-админа, но кнопку
        /// ему не показываем. Проверяются все входы в экран выбора сессии на планшете и экран
        /// по умолчанию — чтобы новый вход не появился в обход.
        /// </summary>
        [Test]
        public void Вход_в_выбор_серии_только_у_админа()
        {
            GameObject tablet = Load(Tablet);

            var adminOnly = new System.Collections.Generic.HashSet<Object>();
            foreach (AdminOnlyElements elements in tablet.GetComponentsInChildren<AdminOnlyElements>(true))
            {
                var list = new SerializedObject(elements).FindProperty("_elements");
                for (int i = 0; i < list.arraySize; i++) adminOnly.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            SwitchMenuButton[] entries = tablet.GetComponentsInChildren<SwitchMenuButton>(true)
                .Where(b => b.TargetScreen == MenuScreenType.SessionSetup).ToArray();
            Assert.IsNotEmpty(entries, "Контроль: на планшете есть вход в выбор серии.");

            foreach (SwitchMenuButton entry in entries)
                Assert.IsTrue(adminOnly.Contains(entry.gameObject),
                    $"Вход в выбор серии '{entry.name}' виден не-админу: нет его в AdminOnlyElements.");

            Assert.AreNotEqual(MenuScreenType.SessionSetup, tablet.GetComponentInChildren<MenuView>(true).DefaultScreen,
                "Планшет открывается сразу на выборе серии — в обход кнопки.");
        }
    }
}
