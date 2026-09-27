using TMPro;
using UnityEngine;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Табло обратного отсчёта закупки на панели жетона.
    ///
    /// <para>
    /// Показывается только при старте раунда по таймеру (<see cref="RoundStartRule.Timer" />)
    /// и только в фазе <c>Equipment</c> — там, где при старте по готовности висит жетон.
    /// Жест с жетоном в этом режиме ничего не значит, а «сколько осталось» игроку нужно
    /// именно у стены, где он выбирает оружие.
    /// </para>
    ///
    /// <para>
    /// Только читает: правило и остаток берёт у <see cref="EliminationMode" />
    /// (<see cref="EliminationMode.EquipmentTimeRemaining" /> считается локально от
    /// реплицированного момента старта фазы), поэтому одинаково работает на сервере,
    /// хосте и клиенте и ничего не шлёт по сети. Жетон прячет стена, а не табло:
    /// каждый отвечает за своё.
    /// </para>
    ///
    /// <para>
    /// Текст, если его не назначили в инспекторе, создаётся в <c>Awake</c>: 3D TextMeshPro
    /// со шрифтом проекта по умолчанию (Roboto Condensed, кириллица есть —
    /// <c>Docs/ui-fonts.md</c>). Лицевая сторона — к игроку, в −Z панели.
    /// </para>
    /// </summary>
    public sealed class ArsenalPurchaseTimerDisplay : MonoBehaviour
    {
        [Tooltip("Текст табло. Пусто — создаётся автоматически дочерним объектом.")]
        [SerializeField] private TMP_Text _text;

        [Header("Автосоздание текста")]
        [Tooltip("Смещение текста от панели. −Z — к игроку.")]
        [SerializeField] private Vector3 _localOffset = new Vector3(0f, 0f, -0.01f);

        [Tooltip("Размер шрифта 3D TextMeshPro. 0.8 — цифры высотой около 8 см.")]
        [SerializeField] private float _fontSize = 0.8f;

        [SerializeField] private Color _color = new Color(1f, 0.8f, 0.2f);

        /// <summary>Как часто искать режим, пока его нет, секунды. Поиск по сцене недешёвый.</summary>
        private const float ModeLookupInterval = 1f;

        private EliminationMode _mode;
        private float _nextModeLookup;
        private int _shownSeconds = -1;

        private void Awake()
        {
            if (_text == null) _text = CreateText();
            _text.enabled = false;
        }

        private void Update()
        {
            if (_mode == null && Time.unscaledTime >= _nextModeLookup)
            {
                _mode = FindFirstObjectByType<EliminationMode>();
                _nextModeLookup = Time.unscaledTime + ModeLookupInterval;
            }

            bool show = _mode != null
                        && _mode.RoundStartRule == RoundStartRule.Timer
                        && _mode.CurrentRoundState == RoundState.Equipment;

            if (_text.enabled != show)
            {
                _text.enabled = show;
                _shownSeconds = -1;
            }

            if (!show) return;

            // Округление вверх: «0:01» висит до самого конца, «0:00» не показывается.
            int seconds = Mathf.CeilToInt(_mode.EquipmentTimeRemaining);
            if (seconds == _shownSeconds) return;

            _shownSeconds = seconds;
            _text.text = $"<size=45%>ЗАКУПКА</size>\n{seconds / 60}:{seconds % 60:00}";
        }

        private TMP_Text CreateText()
        {
            var go = new GameObject("PurchaseTimerText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _localOffset;
            go.transform.localRotation = Quaternion.identity;

            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = _fontSize;
            text.color = _color;
            text.rectTransform.sizeDelta = new Vector2(0.4f, 0.2f);
            return text;
        }
    }
}
