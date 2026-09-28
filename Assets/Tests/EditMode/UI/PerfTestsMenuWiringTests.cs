using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Проводка экрана «Перф-тесты» в планшете: экран есть и настроен, вход — только с экрана
    /// «Отладка» и только в режиме отладки (<see cref="DebugOnlyElements"/>), «Назад» ведёт
    /// обратно в «Отладку».
    /// </summary>
    public class PerfTestsMenuWiringTests
    {
        private const string Tablet = "Assets/Prefabs/UI/Menu/VR/PlayerAdmin/Lobby/PlayerAdminLobbyTabletMenu.prefab";

        private static GameObject LoadTablet()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Tablet);
            Assert.IsNotNull(go, $"Нет {Tablet}.");
            return go;
        }

        private static Object Field(Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;

        private static MenuPerfTests PerfScreen(GameObject tablet)
        {
            MenuPerfTests perf = tablet.GetComponentsInChildren<MenuPerfTests>(true).FirstOrDefault();
            Assert.IsNotNull(perf, "На планшете нет экрана «Перф-тесты» (MenuPerfTests).");
            return perf;
        }

        [Test]
        public void На_планшете_экран_Перф_тесты_настроен()
        {
            MenuPerfTests perf = PerfScreen(LoadTablet());
            Assert.AreEqual(MenuScreenType.PerfTests, perf.ScreenType);
            Assert.IsNotNull(Field(perf, "_rowsContainer"), "Не назначен контейнер строк.");
            Assert.IsNotNull(Field(perf, "_buttonPrefab"), "Не назначен префаб кнопки.");
            Assert.IsFalse(perf.gameObject.activeSelf, "Экран «Перф-тесты» не должен быть открыт по умолчанию.");
        }

        [Test]
        public void Вход_только_с_экрана_Отладка_и_только_в_режиме_отладки()
        {
            GameObject tablet = LoadTablet();
            MenuDebug debug = tablet.GetComponentsInChildren<MenuDebug>(true).FirstOrDefault();
            Assert.IsNotNull(debug, "Контроль: на планшете есть экран «Отладка».");

            SwitchMenuButton[] entries = tablet.GetComponentsInChildren<SwitchMenuButton>(true)
                .Where(b => b.TargetScreen == MenuScreenType.PerfTests).ToArray();
            Assert.IsNotEmpty(entries, "Нет кнопки перехода к «Перф-тестам».");

            var debugOnly = new HashSet<Object>();
            foreach (DebugOnlyElements elements in tablet.GetComponentsInChildren<DebugOnlyElements>(true))
            {
                var list = new SerializedObject(elements).FindProperty("_elements");
                for (int i = 0; i < list.arraySize; i++) debugOnly.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            foreach (SwitchMenuButton entry in entries)
            {
                Assert.IsTrue(entry.transform.IsChildOf(debug.transform), $"Вход в «Перф-тесты» '{entry.name}' не на экране «Отладка».");
                Assert.IsTrue(debugOnly.Contains(entry.gameObject), $"Вход в «Перф-тесты» '{entry.name}' виден без режима отладки.");
            }
        }

        [Test]
        public void Назад_ведёт_в_Отладку()
        {
            MenuPerfTests perf = PerfScreen(LoadTablet());
            SwitchMenuButton[] back = perf.GetComponentsInChildren<SwitchMenuButton>(true);
            Assert.IsNotEmpty(back, "На экране «Перф-тесты» нет кнопки «Назад».");
            Assert.IsTrue(back.All(b => b.TargetScreen == MenuScreenType.Debug), "«Назад» с «Перф-тестов» ведёт не в «Отладку».");
        }
    }
}
