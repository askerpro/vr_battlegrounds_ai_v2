using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Сборка строк экрана кодом: горизонтальная строка, подпись, кнопка из префаба
    /// (<c>SlimButton_IconText</c>). Общая для экранов, которые строят содержимое по данным —
    /// «Отладка» (<see cref="MenuDebug"/>) и «Перф-тесты» (<see cref="MenuPerfTests"/>).
    /// </summary>
    public static class MenuRowBuilder
    {
        public const float RowHeight = 80f;

        public static Transform Row(Transform parent)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = RowHeight;
            return row.transform;
        }

        public static TMP_Text Label(Transform parent, string text, float width, float height = 70f)
        {
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(parent, false);

            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 32f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            var element = label.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            return tmp;
        }

        public static Button Button(GameObject prefab, Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            if (prefab == null) return null;

            GameObject go = Object.Instantiate(prefab, parent);
            go.name = "Btn_" + text;

            TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;

            Button button = go.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(onClick);
            return button;
        }
    }
}
