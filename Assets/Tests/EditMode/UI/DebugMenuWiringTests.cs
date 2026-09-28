using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Проводка экрана «Отладка» в планшете: экран есть и настроен, кнопка перехода к нему —
    /// на главном экране и видна только в режиме отладки (<see cref="DebugOnlyElements"/>).
    /// </summary>
    public class DebugMenuWiringTests
    {
        private const string Tablet = "Assets/Prefabs/UI/Menu/VR/PlayerAdmin/Lobby/PlayerAdminLobbyTabletMenu.prefab";

        private static GameObject LoadTablet()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Tablet);
            Assert.IsNotNull(go, $"Нет {Tablet}.");
            return go;
        }

        private static Object Field(Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;

        [Test]
        public void На_планшете_экран_Отладка_с_контейнером_и_кнопкой()
        {
            MenuDebug debug = LoadTablet().GetComponentsInChildren<MenuDebug>(true).FirstOrDefault();
            Assert.IsNotNull(debug, "На планшете нет экрана «Отладка» (MenuDebug).");
            Assert.AreEqual(MenuScreenType.Debug, debug.ScreenType);
            Assert.IsNotNull(Field(debug, "_rowsContainer"), "У экрана «Отладка» не назначен контейнер строк.");
            Assert.IsNotNull(Field(debug, "_buttonPrefab"), "У экрана «Отладка» не назначен префаб кнопки.");
            Assert.IsFalse(debug.gameObject.activeSelf, "Экран «Отладка» не должен быть открыт по умолчанию.");
        }

        [Test]
        public void Кнопка_Отладка_видна_только_в_режиме_отладки()
        {
            GameObject tablet = LoadTablet();

            // «Назад» с «Перф-тестов» тоже ведёт в «Отладку», но сам экран достижим только из неё.
            SwitchMenuButton[] entries = tablet.GetComponentsInChildren<SwitchMenuButton>(true)
                .Where(b => b.TargetScreen == MenuScreenType.Debug && b.GetComponentInParent<MenuPerfTests>(true) == null).ToArray();
            Assert.IsNotEmpty(entries, "Нет кнопки перехода к экрану «Отладка».");

            var debugOnly = new System.Collections.Generic.HashSet<Object>();
            foreach (DebugOnlyElements elements in tablet.GetComponentsInChildren<DebugOnlyElements>(true))
            {
                var list = new SerializedObject(elements).FindProperty("_elements");
                for (int i = 0; i < list.arraySize; i++) debugOnly.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            }

            foreach (SwitchMenuButton entry in entries)
            {
                Assert.IsTrue(debugOnly.Contains(entry.gameObject), $"Вход в «Отладку» '{entry.name}' виден без режима отладки.");
                Assert.IsFalse(entry.gameObject.activeSelf, $"Вход в «Отладку» '{entry.name}' включён в префабе.");
            }
        }
    }
}

