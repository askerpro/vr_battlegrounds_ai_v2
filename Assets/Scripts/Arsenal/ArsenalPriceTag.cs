using TMPro;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Ценник слота стены арсенала (T-45): «TR15 · $2900» под точкой, где висит ствол. Зелёный —
    /// владельцу стены по карману, красный — не хватает денег. Виден только при экономике матча:
    /// в разминке всё бесплатно, и ценник прячется.
    ///
    /// <para>
    /// Создаётся слотом при первой нужде, а не лежит в префабе: префаб стены — общий ассет карт,
    /// и 3D-текст на каждом слоте там не нужен никому, кроме экономики. Шрифт — TMP по умолчанию
    /// (Roboto Condensed, кириллица есть, <c>Docs/ui-fonts.md</c>), лицом к игроку — в −Z стены, как
    /// табло закупки на панели жетона.
    /// </para>
    /// </summary>
    public sealed class ArsenalPriceTag : MonoBehaviour
    {
        /// <summary>Сдвиг ценника от точки подвеса ствола: вниз и к игроку, в осях стены.</summary>
        private static readonly Vector3 OffsetInWall = new Vector3(0f, -0.09f, -0.06f);

        private const float FontSize = 0.32f;

        private static readonly Color AffordableColor = new Color(0.45f, 1f, 0.45f);
        private static readonly Color ExpensiveColor = new Color(1f, 0.35f, 0.3f);

        private TextMeshPro _text;

        public static ArsenalPriceTag Create(ArsenalSlotController slot)
        {
            var go = new GameObject("PriceTag");
            go.transform.SetParent(slot.transform, false);

            Transform wall = slot.Wall != null ? slot.Wall.transform : slot.transform;
            Vector3 anchor = slot.ItemAnchor != null ? slot.ItemAnchor.transform.position : slot.transform.position;
            go.transform.SetPositionAndRotation(anchor + wall.rotation * OffsetInWall, wall.rotation);

            var tag = go.AddComponent<ArsenalPriceTag>();
            tag._text = go.AddComponent<TextMeshPro>();
            tag._text.alignment = TextAlignmentOptions.Center;
            tag._text.fontSize = FontSize;
            tag._text.rectTransform.sizeDelta = new Vector2(0.3f, 0.06f);
            tag._text.textWrappingMode = TextWrappingModes.NoWrap;
            return tag;
        }

        public void Show(string weaponName, int price, bool affordable)
        {
            gameObject.SetActive(true);
            _text.color = affordable ? AffordableColor : ExpensiveColor;
            _text.text = $"{weaponName}  ${price}";
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
