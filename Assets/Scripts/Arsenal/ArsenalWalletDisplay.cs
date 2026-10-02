using TMPro;
using UnityEngine;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Табло стены арсенала (T-45): чья стена и сколько у владельца денег, плюс доход за прошлый
    /// раунд («+3250»). Видно только при экономике матча; в разминке всё бесплатно — табло пустое.
    ///
    /// <para>
    /// Только читает: владельца — у стены (<see cref="ArsenalWallController.OwnerSession"/>, SyncVar),
    /// деньги — у <see cref="MatchEconomy.Current"/> (SyncDictionary). Поэтому одинаково работает
    /// на сервере, хосте и клиенте и ничего не шлёт по сети.
    /// </para>
    ///
    /// <para>
    /// Ставит его сама стена в <c>Awake</c>, текст создаётся при первой нужде — над панелью жетона
    /// (там же табло обратного отсчёта закупки), лицом к игроку в −Z стены. Шрифт — TMP по умолчанию
    /// (Roboto Condensed, <c>Docs/ui-fonts.md</c>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalWalletDisplay : MonoBehaviour
    {
        /// <summary>Над центром панели жетона, чуть к игроку.</summary>
        private static readonly Vector3 OffsetFromTagPanel = new Vector3(0f, 0.24f, -0.02f);

        /// <summary>Где панель жетона на стандартной стене — если панели нет.</summary>
        private static readonly Vector3 DefaultLocalPosition = new Vector3(1.147f, 1.66f, -0.16f);

        private const float FontSize = 0.55f;

        private static readonly Color MoneyColor = new Color(0.45f, 1f, 0.45f);

        private ArsenalWallController _wall;
        private TextMeshPro _text;
        private string _shown;
        private bool _composed;
        private (bool, bool, PlayerSession, int, int, string) _signature;

        private void Awake()
        {
            _wall = GetComponent<ArsenalWallController>();
        }

        private void Update()
        {
            Refresh();
        }

        /// <summary>Текст табло сейчас; null — табло скрыто. Для тестов.</summary>
        public string ShownText => _shown;

        /// <summary>Сверяет табло с владельцем и деньгами. Пустой, если ничего не изменилось.</summary>
        public void Refresh()
        {
            if (_wall == null) _wall = GetComponent<ArsenalWallController>();

            // Строка собирается только при изменении: Update зовётся каждый кадр на каждой стене.
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

            string text = Compose(economy, owner, hasOwner);
            if (text == _shown) return;
            _shown = text;

            if (text == null)
            {
                if (_text != null) _text.gameObject.SetActive(false);
                return;
            }

            if (_text == null) _text = CreateText();
            _text.gameObject.SetActive(true);
            _text.text = text;
        }

        /// <summary>Что написать на табло; null — экономики нет, табло скрыто.</summary>
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

        private TextMeshPro CreateText()
        {
            var go = new GameObject("WalletText");
            go.transform.SetParent(transform, false);

            DogTagController tagPanel = GetComponentInChildren<DogTagController>(true);
            go.transform.localPosition = tagPanel != null
                ? transform.InverseTransformPoint(tagPanel.transform.position) + OffsetFromTagPanel
                : DefaultLocalPosition;
            go.transform.localRotation = Quaternion.identity;

            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = FontSize;
            text.color = MoneyColor;
            text.rectTransform.sizeDelta = new Vector2(0.5f, 0.2f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}
