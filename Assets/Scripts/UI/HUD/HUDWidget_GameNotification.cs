using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.UI;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Информационный HUD: системные сообщения со звуком. Живёт только на HUD своего игрока
    /// (<see cref="PlayerHUDManager"/> спавнит HUD владельцу), поэтому звук играет ровно один раз
    /// на каждой машине с игроком — хосте и клиентах, — и никогда на выделенном сервере.
    ///
    /// <para>
    /// Что говорить и каким звуком — <see cref="HudNotificationTexts"/>. Сообщения идут очередью:
    /// последний выстрел раунда даёт «вы погибли» и «раунд выиграла команда …» в одном кадре,
    /// и второе раньше затирало первое.
    /// </para>
    ///
    /// <para>
    /// Первая фаза, которую виджет узнаёт после появления, — это не смена, а текущее состояние
    /// (поздний клиент, смена режима): она показывается без звука.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class HUDWidget_GameNotification : MonoBehaviour
    {
        private const int MaxQueue = 4;

        [SerializeField] private Text _notificationText;
        [SerializeField] private float _fadeDuration = 0.5f;

        [Header("Звук")]
        [Tooltip("2D-источник звука HUD. Нет — создаётся в Awake.")]
        [SerializeField] private AudioSource _audio;
        [Tooltip("Короткий «пик»: смена фазы, итог раунда, чужое убийство.")]
        [SerializeField] private AudioClip _beep;
        [Tooltip("Тревога: погиб сам игрок.")]
        [SerializeField] private AudioClip _alert;

        private CanvasGroup _canvasGroup;
        private PlayerHUDManager _hudManager;
        private Coroutine _showCoroutine;
        private readonly Queue<HudMessage> _queue = new Queue<HudMessage>();
        private bool _phaseSeen;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f;
            }
        }

        private void Start()
        {
            // Подписываемся на целевые уведомления (если сервер шлёт конкретному игроку)
            _hudManager = GetComponentInParent<PlayerHUDManager>();
            if (_hudManager != null)
                _hudManager.OnNotificationReceived += ShowNotification;

            // Глобальные семантические события для всех режимов
            GameMode.OnMatchStartedLocal += HandleMatchStarted;
            GameMode.OnMatchEndedLocal += HandleMatchEnded;
            GameplayManager.PlayerKilledLocal += HandlePlayerKilled;

            // Семантические события специфичные для EliminationMode
            EliminationMode.OnSetStartedLocal += HandleSetStarted;
            EliminationMode.OnSetEndedLocal += HandleSetEnded;
            EliminationMode.OnRoundStartedLocal += HandleRoundStarted;
            EliminationMode.OnRoundEndedLocal += HandleRoundEnded;
            EliminationMode.OnRoundStateChangedLocal += HandleRoundStateChanged;
            EliminationMode.OnSidesSwappedLocal += HandleSidesSwapped;
        }

        private void OnDestroy()
        {
            if (_hudManager != null)
                _hudManager.OnNotificationReceived -= ShowNotification;

            GameMode.OnMatchStartedLocal -= HandleMatchStarted;
            GameMode.OnMatchEndedLocal -= HandleMatchEnded;
            GameplayManager.PlayerKilledLocal -= HandlePlayerKilled;

            EliminationMode.OnSetStartedLocal -= HandleSetStarted;
            EliminationMode.OnSetEndedLocal -= HandleSetEnded;
            EliminationMode.OnRoundStartedLocal -= HandleRoundStarted;
            EliminationMode.OnRoundEndedLocal -= HandleRoundEnded;
            EliminationMode.OnRoundStateChangedLocal -= HandleRoundStateChanged;
            EliminationMode.OnSidesSwappedLocal -= HandleSidesSwapped;
        }

        private void HandleSidesSwapped() => Enqueue(HudNotificationTexts.SidesSwapped());

        private const float ReturnToBaseInterval = 5f;
        private float _nextReturnToBaseCheck;

        /// <summary>
        /// Напоминание «вернитесь в свою зону» (<see cref="HudNotificationTexts.AskReturnToBase"/>):
        /// раз в <see cref="ReturnToBaseInterval"/> с, пока игрок не на базе и в очереди нет
        /// другого сообщения. Состояние — реплицированное (фаза, здоровье, зона сессии).
        /// </summary>
        private void Update()
        {
            if (Time.time < _nextReturnToBaseCheck) return;
            _nextReturnToBaseCheck = Time.time + 1f;

            if (_showCoroutine != null) return;

            PlayerSession local = PlayerSession.LocalSession;
            PlayerController avatar = local != null ? local.ActiveAvatar : null;
            var mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode as EliminationMode : null;
            if (avatar == null || mode == null) return;

            // Вышел из зоны на отсчёте — отсчёт стоит, напоминание чаще.
            if (HudNotificationTexts.AskReturnForCountdown(avatar.IsAlive, local.IsInSpawnZone, mode.CurrentRoundState))
            {
                Enqueue(HudNotificationTexts.ReturnForCountdown());
                _nextReturnToBaseCheck = Time.time + 2f;
                return;
            }

            if (!HudNotificationTexts.AskReturnToBase(avatar.IsAlive, local.IsInSpawnZone)) return;

            Enqueue(HudNotificationTexts.ReturnToBase());
            _nextReturnToBaseCheck = Time.time + ReturnToBaseInterval;
        }

        // ── Обработчики семантических событий ────────────────────────────────

        private void HandleMatchStarted()
            => Enqueue(new HudMessage("Матч начался! В бой!", 4f, HudSound.Beep));

        private void HandleMatchEnded(TeamData winner)
        {
            Enqueue(new HudMessage(winner != null ? $"Матч завершен!\nПобедили {winner.Name}!" : "Матч завершился вничью!",
                                   5f, HudSound.Beep));
        }

        private void HandleSetStarted(int setNum)
            => Enqueue(new HudMessage($"Сет {setNum} начинается", 3f, HudSound.None));

        private void HandleSetEnded(TeamData winner)
        {
            Enqueue(new HudMessage(winner != null ? $"Сет за командой {winner.Name}!" : "Сет завершился вничью!",
                                   4f, HudSound.Beep));
        }

        private void HandleRoundStarted(int roundNum)
            => Enqueue(new HudMessage($"Раунд {roundNum}", 2f, HudSound.None));

        private void HandleRoundEnded(TeamData winner)
        {
            Enqueue(new HudMessage(winner != null ? $"Раунд выиграла команда {winner.Name}!" : "Раунд завершился вничью!",
                                   3f, HudSound.Beep));
        }

        private void HandleRoundStateChanged(RoundState state)
        {
            HudMessage message = HudNotificationTexts.Phase(state, RoundScore());

            // Первая узнанная фаза — текущее состояние, а не переход: без звука.
            if (!_phaseSeen)
            {
                _phaseSeen = true;
                message = new HudMessage(message.Text, message.Duration, HudSound.None);
            }

            Enqueue(message);
        }

        private void HandlePlayerKilled(KillNotice kill)
        {
            PlayerSession local = PlayerSession.LocalSession;
            Enqueue(HudNotificationTexts.Kill(kill, local != null ? local.netId : 0u, local != null ? local.TeamIndex : 0));
        }

        /// <summary>Счёт раундов «CT 1 — 0 T» из реплицированного состояния режима.</summary>
        private static string RoundScore()
        {
            var mode = GameplayManager.Instance != null ? GameplayManager.Instance.ActiveGameMode as EliminationMode : null;
            if (mode == null || mode.Teams == null || mode.Teams.Length < 2 || mode.Teams[0] == null || mode.Teams[1] == null) return null;

            TeamData a = mode.Teams[0], b = mode.Teams[1];
            return $"{a.Name} {mode.GetRoundScore(a)} — {mode.GetRoundScore(b)} {b.Name}";
        }

        // ── Очередь и показ ────────────────────────────────────────────────

        /// <summary>Целевое уведомление сервера (<see cref="PlayerHUDManager.SendToPlayer"/>): с «пиком».</summary>
        private void ShowNotification(string message, float duration)
            => Enqueue(new HudMessage(message, duration, HudSound.Beep));

        private void Enqueue(HudMessage message)
        {
            if (message.IsEmpty) return;

            // Только звук (итог боя) — сразу, текст в очереди не занимает.
            if (string.IsNullOrEmpty(message.Text))
            {
                Play(message.Sound);
                return;
            }

            if (_queue.Count >= MaxQueue) _queue.Dequeue();
            _queue.Enqueue(message);

            if (_showCoroutine == null && isActiveAndEnabled)
                _showCoroutine = StartCoroutine(ShowQueue());
        }

        private IEnumerator ShowQueue()
        {
            while (_queue.Count > 0)
            {
                HudMessage message = _queue.Dequeue();

                if (_notificationText != null) _notificationText.text = message.Text;
                _canvasGroup.alpha = 1f;
                Play(message.Sound);

                // Пока ждёт следующее — держим короче, чтобы очередь не отставала от игры.
                float hold = _queue.Count > 0 ? Mathf.Min(message.Duration, 1.5f) : message.Duration;
                float until = Time.time + hold;
                while (Time.time < until)
                {
                    if (_queue.Count > 0 && Time.time > until - hold + 1.5f) break;
                    yield return null;
                }
            }

            float timer = 0f;
            while (timer < _fadeDuration)
            {
                timer += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / _fadeDuration);

                // Пришло новое во время затухания — показываем без паузы.
                if (_queue.Count > 0)
                {
                    _showCoroutine = StartCoroutine(ShowQueue());
                    yield break;
                }

                yield return null;
            }

            _canvasGroup.alpha = 0f;
            _showCoroutine = null;
        }

        private void Play(HudSound sound)
        {
            AudioClip clip = sound == HudSound.Alert ? _alert : sound == HudSound.Beep ? _beep : null;
            if (clip != null && _audio != null) _audio.PlayOneShot(clip);
        }
    }
}
