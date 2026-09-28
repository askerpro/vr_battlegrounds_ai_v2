using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Маленькое табло на часах аватара: кольцо ХП по периметру (зелёный → красный)
    /// и остаток времени раунда в центре. Кладётся руками в иерархию аватара;
    /// игрока находит сам — <see cref="PlayerController"/> в родителях.
    ///
    /// <para>
    /// Видит табло только хозяин аватара: у чужих копий содержимое выключено,
    /// чтобы не тратить на них TMP-перестройки.
    /// </para>
    /// </summary>
    public class WristDisplay : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Всё видимое содержимое. Выключается на чужих аватарах.")]
        [SerializeField] private GameObject _content;
        [Tooltip("Кольцо ХП: Image Filled / Radial 360.")]
        [SerializeField] private Image _healthRing;
        [Tooltip("Остаток времени, мм:сс.")]
        [SerializeField] private TMP_Text _timeText;
        [Tooltip("Число ХП под таймером. Необязательно.")]
        [SerializeField] private TMP_Text _healthText;

        [Header("Настройки")]
        [SerializeField] private float _maxHealth = 100f;
        [Tooltip("Ниже этой доли ХП кольцо пульсирует.")]
        [SerializeField, Range(0f, 1f)] private float _lowHealthThreshold = 0.25f;
        [SerializeField] private float _pulseSpeed = 6f;
        [SerializeField] private Color _combatTimeColor = Color.white;
        [SerializeField] private Color _prepareTimeColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color _pausedTimeColor = new Color(0.6f, 0.6f, 0.6f);

        private PlayerController _player;
        private int _shownSeconds = int.MinValue;
        private int _shownHealth = int.MinValue;
        private RoundState? _shownState;
        private bool _shownNoTimer;

        private void Awake()
        {
            _player = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            // Без игрока (табло лежит в сцене само по себе) показываем как есть — удобно для
            // проверки вёрстки. На чужом аватаре — прячем.
            bool visible = _player == null || _player.isOwned;
            if (_content != null && _content.activeSelf != visible) _content.SetActive(visible);
            if (!visible) return;

            UpdateHealth();
            UpdateTime();
        }

        private void UpdateHealth()
        {
            float health = _player != null && _player.IsAlive ? _player.Health : (_player != null ? 0f : _maxHealth);
            float fraction = WristDisplayFace.HealthFraction(health, _maxHealth);

            if (_healthRing != null)
            {
                Color color = WristDisplayFace.HealthColor(fraction);
                if (fraction > 0f && fraction <= _lowHealthThreshold)
                    color.a = Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * _pulseSpeed));

                _healthRing.fillAmount = fraction;
                _healthRing.color = color;
            }

            int rounded = Mathf.CeilToInt(health);
            if (_healthText != null && rounded != _shownHealth)
            {
                _shownHealth = rounded;
                _healthText.text = rounded.ToString();
            }
        }

        private void UpdateTime()
        {
            if (_timeText == null) return;

            GameMode mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode : null;
            if (!RoundClock.TryGetTimeRemaining(mode, out float seconds, out RoundState? state))
            {
                if (_shownNoTimer) return;
                _shownNoTimer = true;
                _shownSeconds = int.MinValue;
                _timeText.text = WristDisplayFace.FormatTime(float.NaN);
                _timeText.color = _pausedTimeColor;
                return;
            }

            // TMP перестраивает меш на каждое присвоение — трогаем текст раз в секунду.
            int whole = Mathf.CeilToInt(seconds);
            if (!_shownNoTimer && whole == _shownSeconds && state == _shownState) return;

            _shownNoTimer = false;
            _shownSeconds = whole;
            _shownState = state;
            _timeText.text = WristDisplayFace.FormatTime(seconds);
            _timeText.color = TimeColor(state);
        }

        private Color TimeColor(RoundState? state)
        {
            switch (state)
            {
                case null:
                case RoundState.Combat:
                    return _combatTimeColor;
                case RoundState.Equipment:
                case RoundState.Countdown:
                    return _prepareTimeColor;
                default:
                    return _pausedTimeColor;
            }
        }
    }
}
