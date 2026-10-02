using TMPro;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Наручные часы игрока (T-46) — весь его HUD. Постоянно показывают статус: кольцо ХП по периметру,
    /// остаток фазы в центре, счёт «свои : чужие», деньги экономики и ХП числом. Статус прерывается только
    /// нотификациями (<see cref="WatchNotifications"/>): на их срок вместо статуса — текст, со звуком и
    /// вибрацией контроллера той руки, на которой часы.
    ///
    /// <para>
    /// Кладётся руками в иерархию аватара (префаб <c>WristWatch_HUD</c>); игрока находит сам —
    /// <see cref="PlayerController"/> в родителях. Видит и слышит часы только хозяин аватара: у чужих
    /// копий содержимое выключено, чтобы не тратить на них TMP-перестройки.
    /// </para>
    /// </summary>
    public class WristDisplay : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Всё видимое содержимое. Выключается на чужих аватарах.")]
        [SerializeField] private GameObject _content;
        [Tooltip("Кольцо ХП: Image Filled / Radial 360. Видно и под нотификацией.")]
        [SerializeField] private Image _healthRing;
        [Tooltip("Статус: время, счёт, деньги, ХП. Прячется на время нотификации.")]
        [SerializeField] private GameObject _statusGroup;
        [Tooltip("Остаток времени, мм:сс.")]
        [SerializeField] private TMP_Text _timeText;
        [Tooltip("Число ХП. Необязательно.")]
        [SerializeField] private TMP_Text _healthText;
        [Tooltip("Деньги экономики матча, «$800». Пусто, когда экономики нет.")]
        [SerializeField] private TMP_Text _moneyText;
        [Tooltip("Счёт «свои : чужие».")]
        [SerializeField] private TMP_Text _scoreText;
        [Tooltip("Нотификация: показывается вместо статуса.")]
        [SerializeField] private GameObject _notificationGroup;
        [SerializeField] private TMP_Text _notificationText;

        [Header("Звук")]
        [Tooltip("Источник звука часов. Нет — создаётся в Awake.")]
        [SerializeField] private AudioSource _audio;
        [Tooltip("Короткий «пик»: большинство нотификаций.")]
        [SerializeField] private AudioClip _beepClip;
        [Tooltip("Тревога: своя смерть.")]
        [SerializeField] private AudioClip _alertClip;
        [Tooltip("Касса: деньги пришли или ушли.")]
        [SerializeField] private AudioClip _moneyClip;

        [Header("Вибрация")]
        [Tooltip("Сила вибрации по приоритету: Low, Normal, High, Critical.")]
        [SerializeField] private float[] _hapticAmplitude = { 0.25f, 0.45f, 0.7f, 1f };
        [SerializeField] private float _hapticSeconds = 0.15f;
        [SerializeField] private float _criticalHapticSeconds = 0.4f;

        [Header("Настройки")]
        [SerializeField] private float _maxHealth = 100f;
        [Tooltip("Ниже этой доли ХП кольцо пульсирует.")]
        [SerializeField, Range(0f, 1f)] private float _lowHealthThreshold = 0.25f;
        [SerializeField] private float _pulseSpeed = 6f;
        [SerializeField] private Color _combatTimeColor = Color.white;
        [SerializeField] private Color _prepareTimeColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color _pausedTimeColor = new Color(0.6f, 0.6f, 0.6f);

        [Header("Цвета нотификаций (как MenuTheme)")]
        [SerializeField] private Color _notificationColor = Color.white;
        [Tooltip("High — MenuTheme.Accent.")]
        [SerializeField] private Color _importantColor = new Color(1f, 0.686f, 0f);
        [SerializeField] private Color _criticalColor = new Color(1f, 0.35f, 0.31f);
        [Tooltip("Деньги — MenuTheme.Success.")]
        [SerializeField] private Color _moneyColor = new Color(0.31f, 0.82f, 0.478f);

        private PlayerController _player;
        private UxrAvatar _avatar;
        private int _shownSeconds = int.MinValue;
        private int _shownHealth = int.MinValue;
        private int? _shownMoney = int.MinValue;
        private int _shownOwn = int.MinValue, _shownEnemy = int.MinValue;
        private RoundPhase? _shownState;
        private bool _shownNoTimer;
        private bool? _shownNotifying;
        private string _shownNotification;

        private void Awake()
        {
            _player = GetComponentInParent<PlayerController>();
            _avatar = GetComponentInParent<UxrAvatar>();

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                // Звук идёт от запястья, но в пределах метра — без затухания: рука всегда ближе.
                _audio.spatialBlend = 1f;
                _audio.minDistance = 1f;
            }
        }

        private void OnEnable() => WatchNotifications.Started += HandleNotificationStarted;

        private void OnDisable() => WatchNotifications.Started -= HandleNotificationStarted;

        /// <summary>Часы своего аватара (или часы без игрока — проверка вёрстки в сцене).</summary>
        private bool IsMine => _player == null || _player.isOwned;

        private void Update()
        {
            bool visible = IsMine;
            if (_content != null && _content.activeSelf != visible) _content.SetActive(visible);
            if (!visible) return;

            UpdateHealthRing();

            bool notifying = WatchNotifications.IsShowing;
            ApplyMode(notifying);
            if (notifying)
            {
                UpdateNotification(WatchNotifications.Current);
                return;
            }

            UpdateHealthText();
            UpdateTime();
            UpdateScoreAndMoney();
        }

        // ── Нотификация ───────────────────────────────────────

        private void ApplyMode(bool notifying)
        {
            if (_shownNotifying == notifying) return;
            _shownNotifying = notifying;
            _shownNotification = null;

            if (_statusGroup != null) _statusGroup.SetActive(!notifying);
            if (_notificationGroup != null) _notificationGroup.SetActive(notifying);

            if (!notifying)
            {
                // Статус был спрятан — перерисовать всё заново.
                _shownSeconds = _shownHealth = _shownOwn = _shownEnemy = int.MinValue;
                _shownMoney = int.MinValue;
                _shownNoTimer = false;
            }
        }

        private void UpdateNotification(WatchNotification notification)
        {
            if (_notificationText == null || _shownNotification == notification.Text) return;
            _shownNotification = notification.Text;
            _notificationText.text = notification.Text;
            _notificationText.color = NotificationColor(notification);
        }

        private Color NotificationColor(WatchNotification notification)
        {
            if (notification.Sound == WatchSound.Money) return _moneyColor;
            switch (notification.Priority)
            {
                case WatchPriority.Critical: return _criticalColor;
                case WatchPriority.High: return _importantColor;
                default: return _notificationColor;
            }
        }

        /// <summary>Старт нотификации: звук с запястья и вибрация руки с часами.</summary>
        private void HandleNotificationStarted(WatchNotification notification)
        {
            if (!IsMine || !isActiveAndEnabled) return;

            AudioClip clip = notification.Sound == WatchSound.Alert ? _alertClip
                           : notification.Sound == WatchSound.Money ? _moneyClip
                           : _beepClip;
            if (clip == null) clip = _beepClip;
            if (clip != null && _audio != null) _audio.PlayOneShot(clip);

            Vibrate(notification.Priority);
        }

        private void Vibrate(WatchPriority priority)
        {
            if (_avatar == null || _avatar.AvatarMode != UxrAvatarMode.Local || _avatar.ControllerInput == null) return;

            int index = Mathf.Clamp((int)priority, 0, _hapticAmplitude.Length - 1);
            float amplitude = _hapticAmplitude.Length > 0 ? _hapticAmplitude[index] : 0.5f;
            float seconds = priority == WatchPriority.Critical ? _criticalHapticSeconds : _hapticSeconds;

            _avatar.ControllerInput.SendHapticFeedback(WatchHand(), UxrHapticClipType.RumbleFreqNormal, amplitude, seconds);
        }

        /// <summary>
        /// Рука с часами — ближайшая к часам кисть аватара. Часы могут висеть на предплечье, а не на
        /// кисти, поэтому по расстоянию, а не по родителю.
        /// </summary>
        private UxrHandSide WatchHand()
        {
            Transform left = _avatar.GetHandBone(UxrHandSide.Left);
            Transform right = _avatar.GetHandBone(UxrHandSide.Right);
            if (left == null) return UxrHandSide.Right;
            if (right == null) return UxrHandSide.Left;

            Vector3 p = transform.position;
            return (left.position - p).sqrMagnitude <= (right.position - p).sqrMagnitude ? UxrHandSide.Left : UxrHandSide.Right;
        }

        // ── Статус ────────────────────────────────────────────

        private void UpdateHealthRing()
        {
            if (_healthRing == null) return;

            float fraction = WristDisplayFace.HealthFraction(CurrentHealth(), _maxHealth);
            Color color = WristDisplayFace.HealthColor(fraction);
            if (fraction > 0f && fraction <= _lowHealthThreshold)
                color.a = Mathf.Lerp(0.35f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * _pulseSpeed));

            _healthRing.fillAmount = fraction;
            _healthRing.color = color;
        }

        private float CurrentHealth() =>
            _player != null ? (_player.IsAlive ? _player.Health : 0f) : _maxHealth;

        private void UpdateHealthText()
        {
            int rounded = Mathf.CeilToInt(CurrentHealth());
            if (_healthText == null || rounded == _shownHealth) return;
            _shownHealth = rounded;
            _healthText.text = rounded.ToString();
        }

        private void UpdateScoreAndMoney()
        {
            MapReferee referee = MapReferee.Instance;
            GameMode mode = referee != null ? referee.ActiveGameMode : null;
            PlayerSession local = PlayerSession.LocalSession;
            int localTeam = local != null ? local.TeamIndex : -1;

            if (_scoreText != null)
            {
                bool hasScore = WatchScore.TryGet(mode, localTeam, out int own, out int enemy);
                if (!hasScore) own = enemy = -1;
                if (own != _shownOwn || enemy != _shownEnemy)
                {
                    _shownOwn = own;
                    _shownEnemy = enemy;
                    _scoreText.text = hasScore ? WristDisplayFace.FormatScore(own, enemy) : string.Empty;
                }
            }

            if (_moneyText != null)
            {
                MatchEconomy economy = MatchEconomy.Current;
                int? money = economy != null && local != null && economy.TryGetMoney(local, out int m) ? m : (int?)null;
                if (money != _shownMoney)
                {
                    _shownMoney = money;
                    _moneyText.text = WristDisplayFace.FormatMoney(money);
                }
            }
        }

        private void UpdateTime()
        {
            if (_timeText == null) return;

            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (!RoundClock.TryGetTimeRemaining(mode, out float seconds, out RoundPhase? state))
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

        private Color TimeColor(RoundPhase? state)
        {
            switch (state)
            {
                case null:
                case RoundPhase.Combat:
                    return _combatTimeColor;
                case RoundPhase.Equipment:
                case RoundPhase.Countdown:
                    return _prepareTimeColor;
                default:
                    return _pausedTimeColor;
            }
        }
    }
}
