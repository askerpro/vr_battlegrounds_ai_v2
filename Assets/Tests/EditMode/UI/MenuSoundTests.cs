using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Клик кнопки меню планшета звучит (решение пользователя): нажатие лазером в VR иначе подтверждено только цветом.
    /// Звук задаёт тема (<see cref="MenuTheme.ClickSound"/>), играет <see cref="KitButton"/> — поэтому кнопок в обход
    /// набора в меню быть не должно.
    /// </summary>
    public class MenuSoundTests
    {
        [Test]
        public void В_теме_есть_звук_клика()
        {
            var theme = AssetDatabase.LoadAssetAtPath<MenuTheme>("Assets/Resources/MenuTheme.asset");
            Assert.IsNotNull(theme, "Нет Resources/MenuTheme.asset.");
            Assert.IsNotNull(theme.ClickSound, "MenuTheme.ClickSound пуст — клики меню молчат.");
            Assert.Greater(theme.ClickVolume, 0f, "Громкость клика 0.");
        }

        [Test]
        public void Нажатие_кнопки_играет_клик()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            var played = new List<AudioClip>();
            System.Action<KitButton, AudioClip> handler = (b, clip) => played.Add(clip);
            MenuClickSound.Played += handler;
            try
            {
                KitButton button = MenuKit.Button(canvas.transform, "Тест", null);
                var click = new PointerEventData(eventSystem.GetComponent<EventSystem>()) { button = PointerEventData.InputButton.Left };
                button.OnPointerClick(click);

                Assert.AreEqual(1, played.Count, "Нажатие кнопки меню не сыграло клик.");
                Assert.AreSame(MenuTheme.Instance.ClickSound, played[0], "Сыгран не клип темы.");
                Assert.IsNotNull(canvas.GetComponent<AudioSource>(), "Клик не на канвасе планшета — звук не от планшета.");

                button.interactable = false;
                button.OnPointerClick(click);
                Assert.AreEqual(1, played.Count, "Недоступная кнопка щёлкает.");
            }
            finally
            {
                MenuClickSound.Played -= handler;
                Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(eventSystem);
            }
        }

        /// <summary>Кнопка меню не в обход набора: только <see cref="KitButton"/> звучит по теме.</summary>
        [Test]
        public void Кнопки_меню_только_из_набора()
        {
            var failures = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/UI/Menu" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (Button button in root.GetComponentsInChildren<Button>(true))
                    if (!(button is KitButton)) failures.Add($"{path} :: {button.name}: Button не из набора — клик без звука");
                foreach (Toggle toggle in root.GetComponentsInChildren<Toggle>(true))
                    failures.Add($"{path} :: {toggle.name}: Toggle не из набора — клик без звука");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
