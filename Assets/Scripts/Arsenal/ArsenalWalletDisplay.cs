using TMPro;
using UnityEngine;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Цифровой экран баланса: владелец, деньги и доход за прошлый раунд из существующей экономики.
    /// Configure связывает экран префаба; без него создаётся небольшой корпус с экранной поверхностью.
    /// Компонент только читает сетевые данные. Compose сохраняет прежний контракт содержимого.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalWalletDisplay : MonoBehaviour
    {
        private static readonly Vector3 OffsetFromTagPanel = new Vector3(0f, 0.24f, -0.02f);
        private static readonly Vector3 DefaultLocalPosition = new Vector3(1.147f, 1.66f, -0.16f);
        private static readonly Color MoneyColor = new Color(0.45f, 1f, 0.45f);

        [SerializeField] private TextMeshPro _display;
        private ArsenalWallController _wall;
        private GameObject _fallback;
        private Material _housingMaterial;
        private Material _screenMaterial;
        private string _shown;
        private bool _composed;
        private (bool, bool, PlayerSession, int, int, string) _signature;

        public string ShownText => _shown;

        /// <summary>Назначает экран префаба. Корпус остаётся активным даже без экономики; текст гаснет.</summary>
        public void Configure(TextMeshPro display)
        {
            if (_display != null) _display.enabled = false;
            if (_fallback != null)
            {
                _fallback.SetActive(false);
                if (Application.isPlaying) Destroy(_fallback);
                else DestroyImmediate(_fallback);
                _fallback = null;
            }
            _display = display;
            _composed = false;
            Refresh();
        }

        private void Awake() => _wall = GetComponent<ArsenalWallController>();
        private void Update() => Refresh();

        public void Refresh()
        {
            if (_wall == null) _wall = GetComponent<ArsenalWallController>();
            MatchEconomy economy = MatchEconomy.Current;
            PlayerSession owner = _wall != null ? _wall.OwnerSession : null;
            bool hasOwner = _wall != null && _wall.OwnerSessionNetId != 0;
            var signature = (economy != null, hasOwner, owner,
                             owner != null && economy != null ? economy.GetMoney(owner) : 0,
                             owner != null && economy != null ? economy.GetRoundIncome(owner) : 0,
                             owner != null ? owner.PlayerName : null);
            if (_composed && signature.Equals(_signature)) return;
            _composed = true;
            _signature = signature;
            _shown = Compose(economy, owner, hasOwner);
            if (_shown == null)
            {
                if (_display != null) _display.enabled = false;
                return;
            }
            if (_display == null) _display = CreateDisplay();
            _display.enabled = true;
            _display.text = _shown;
        }

        public static string Compose(MatchEconomy economy, PlayerSession owner, bool hasOwner)
        {
            if (economy == null) return null;
            if (!hasOwner) return "<size=60%>СВОБОДНАЯ СТЕНА</size>";
            if (owner == null) return "<size=60%>…</size>";

            string name = string.IsNullOrEmpty(owner.PlayerName) ? "Игрок" : owner.PlayerName;
            int money = economy.GetMoney(owner);
            int income = economy.GetRoundIncome(owner);
            string incomeLine = income > 0 ? $"  <size=60%><color=#B8FFB8>+{income}</color></size>" : "";
            return $"<size=55%>{name}</size>\n${money}{incomeLine}";
        }

        private TextMeshPro CreateDisplay()
        {
            _fallback = new GameObject("WalletDisplay");
            _fallback.transform.SetParent(transform, false);
            DogTagController tagPanel = GetComponentInChildren<DogTagController>(true);
            _fallback.transform.localPosition = tagPanel != null
                ? transform.InverseTransformPoint(tagPanel.transform.position) + OffsetFromTagPanel
                : DefaultLocalPosition;

            CreatePlate("Housing", Vector3.zero, new Vector3(0.55f, 0.24f, 0.035f),
                        new Color(0.095f, 0.11f, 0.095f), ref _housingMaterial);
            CreatePlate("ScreenSurface", new Vector3(0f, 0f, -0.02f), new Vector3(0.51f, 0.20f, 0.005f),
                        new Color(0.005f, 0.015f, 0.01f), ref _screenMaterial);
            var textObject = new GameObject("WalletText");
            textObject.transform.SetParent(_fallback.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.026f);
            var text = textObject.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 0.55f;
            text.color = MoneyColor;
            text.rectTransform.sizeDelta = new Vector2(0.49f, 0.18f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private void CreatePlate(string name, Vector3 position, Vector3 size, Color color, ref Material material)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = name;
            plate.transform.SetParent(_fallback.transform, false);
            plate.transform.localPosition = position;
            plate.transform.localScale = size;
            Collider collider = plate.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
            var renderer = plate.GetComponent<Renderer>();
            ReleaseMaterial(material);
            material = new Material(renderer.sharedMaterial) { name = "ArsenalWallet" + name + "_Runtime" };
            material.color = color;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void ReleaseMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        private void OnDestroy()
        {
            ReleaseMaterial(_housingMaterial);
            ReleaseMaterial(_screenMaterial);
            if (_fallback != null)
            {
                if (Application.isPlaying) Destroy(_fallback);
                else DestroyImmediate(_fallback);
            }
        }
    }
}
