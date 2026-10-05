using TMPro;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Закреплённая карточка оружия: физическое основание, цена и данные из WeaponInfo.
    /// Темп — применяемый SDK предел, а не номинальная частота до округления.
    /// В разминке характеристики остаются, цена заменяется отметкой «БЕСПЛАТНО».
    /// </summary>
    public sealed class ArsenalPriceTag : MonoBehaviour
    {
        private static readonly Color AffordableColor = new Color(0.10f, 0.12f, 0.08f);
        private static readonly Color ExpensiveColor = new Color(0.42f, 0.12f, 0.08f);
        [SerializeField] private TextMeshPro _text;
        private Material _backingMaterial;
        private string _shown;
        private Color _shownColor;

        public static ArsenalPriceTag Create(ArsenalSlotController slot) => Create(slot, ArsenalPresentationApplicator.Resolve(slot));

        public static ArsenalPriceTag Create(ArsenalSlotController slot, ArsenalPresentationSnapshot presentation)
        {
            var tag = slot.GetComponentInChildren<ArsenalPriceTag>(true);
            bool created = tag == null;
            var go = created ? new GameObject("WeaponCard") : tag.gameObject;
            if (created) go.transform.SetParent(slot.transform, false);
            ArsenalPresentationApplicator.ApplyCard(slot, go.transform, presentation);

            if (created) tag = go.AddComponent<ArsenalPriceTag>();
            Vector2 size = presentation.CardSize;
            if (created) tag.CreateBacking(size);
            var backing = go.transform.Find("CardBacking");
            if (backing != null) backing.localScale = new Vector3(size.x, size.y, 0.001f);
            if (tag._text == null) tag._text = go.GetComponentInChildren<TextMeshPro>(true);
            var textObject = tag._text != null ? tag._text.gameObject : new GameObject("CardText");
            if (tag._text == null) textObject.transform.SetParent(go.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.0007f);
            if (tag._text == null) tag._text = textObject.AddComponent<TextMeshPro>();
            tag.ConfigureText(size, presentation.CardFontSize);
            return tag;
        }

        private void ConfigureText(Vector2 size, float baseFontSize)
        {
            _text.font = TMP_Settings.defaultFontAsset;
            _text.alignment = TextAlignmentOptions.Center;
            _text.rectTransform.sizeDelta = size - new Vector2(0.014f, 0.014f);
            // Длинное имя переносится и уменьшает всю карточку; цена и характеристики не обрезаются многоточием.
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = baseFontSize * 0.6f;
            _text.fontSizeMax = baseFontSize;
            _text.fontSize = baseFontSize;
        }

        public void Show(WeaponInfo info, bool affordable, bool free = false)
        {
            if (info == null) { Hide(); return; }
            string price = free ? "БЕСПЛАТНО" : $"${info.Price}";
            string heading = $"<size=110%><b>{Escape(info.DisplayName)}</b></size>\n{price}";
            string details;
            if (info.HasBalance)
            {
                string damage = info.Pellets > 1 ? $"{info.Pellets} × {info.Damage:0.#}" : $"{info.Damage:0.#}";
                int rpm = WeaponInfo.ShotFrequency(info.FireRate) * 60;
                string mode = info.FullAuto ? "АВТО" : "ПОЛУАВТО";
                details = $"\nУрон {damage}\nМагазин {info.MagazineSize}\nДо {rpm}/мин\n{mode}";
            }
            else
            {
                details = $"\n{CategoryLabel(info.Category)}";
            }
            Present(heading + details, free || affordable ? AffordableColor : ExpensiveColor);
        }

        /// <summary>Совместимость со старым вызывающим кодом до миграции слотов.</summary>
        public void Show(string weaponName, int price, bool affordable) =>
            Present($"<b>{Escape(weaponName)}</b>  ${price}", affordable ? AffordableColor : ExpensiveColor);

        public void Hide() => gameObject.SetActive(false);

        private void Present(string text, Color color)
        {
            gameObject.SetActive(true);
            if (_text == null) _text = GetComponentInChildren<TextMeshPro>(true);
            if (_text == null) return;
            if (_shown != text) { _shown = text; _text.text = text; }
            if (_shownColor != color) { _shownColor = color; _text.color = color; }
            if (!Application.isPlaying) _text.ForceMeshUpdate(true, true);
        }

        private void CreateBacking(Vector2 size)
        {
            var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "CardBacking";
            backing.transform.SetParent(transform, false);
            backing.transform.localScale = new Vector3(size.x, size.y, 0.001f);
            Collider collider = backing.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            var renderer = backing.GetComponent<Renderer>();
            _backingMaterial = new Material(renderer.sharedMaterial) { name = "ArsenalCardBacking_Runtime" };
            _backingMaterial.color = new Color(0.76f, 0.72f, 0.56f);
            renderer.sharedMaterial = _backingMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static string Escape(string value) => (value ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        private static string CategoryLabel(WeaponCategory category)
        {
            switch (category)
            {
                case WeaponCategory.Rifle: return "ОГНЕСТРЕЛЬНОЕ ОРУЖИЕ";
                case WeaponCategory.Pistol: return "КОРОТКОСТВОЛЬНОЕ ОРУЖИЕ";
                case WeaponCategory.Equipment: return "СНАРЯЖЕНИЕ";
                case WeaponCategory.Melee: return "ХОЛОДНОЕ ОРУЖИЕ";
                default: return "СНАРЯЖЕНИЕ";
            }
        }

        private void OnDestroy()
        {
            if (_backingMaterial == null) return;
            if (Application.isPlaying) Destroy(_backingMaterial);
            else DestroyImmediate(_backingMaterial);
        }
    }
}
