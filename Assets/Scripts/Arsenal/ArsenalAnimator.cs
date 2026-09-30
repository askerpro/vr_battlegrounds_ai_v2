using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Drives Arsenal Wall open/close animations via Unity Animator.
    ///
    /// Animation Clips handle all movement (shelf slide, shutter roll).
    /// Opening и Closing — один клип <c>Arsenal_Open</c>, Closing играет его со скоростью −1.
    ///
    /// Animator Controller states:
    ///   Idle_Open ──► Closing ──[exit]──► Idle_Closed
    ///   Idle_Closed ──► Opening ──[exit]──► Idle_Open
    ///
    /// <para>
    /// <b>Анимация играет, только если поза меняется.</b> Раньше команда подавалась триггером
    /// <c>Open</c>/<c>Close</c>. Триггер, поданный в состояние без перехода по нему (<c>Close</c>
    /// при закрытой стене), не гаснет, а ждёт: следующее открытие доигрывало до <c>Idle_Open</c>
    /// и тут же закрывало стену залежавшимся триггером. Теперь состояния запускаются
    /// напрямую через <c>Animator.Play</c>: уже открытая стена на повторное «открыть»
    /// не шевелится, а смена направления посреди анимации разворачивает её с текущей
    /// позы, а не с начала клипа.
    /// </para>
    /// <para>
    /// Позу покоя держат сами <c>Idle_*</c>: оба играют тот же клип, <c>Idle_Closed</c>
    /// на кадре 0 (скорость 0), <c>Idle_Open</c> — на последнем (Motion Time = параметр
    /// <c>OpenedPoseTime</c>, по умолчанию 1). Пустые <c>Idle_*</c> с Write Defaults
    /// показывали позу префаба, то есть закрытую, и открытая стена прыгала в закрытую.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class ArsenalAnimator : MonoBehaviour
    {
        // ── Animator Hashes ────────────────────────────────────
        private static readonly int TriggerClose = Animator.StringToHash("Close");
        private static readonly int TriggerOpen  = Animator.StringToHash("Open");
        private static readonly int StateClosing = Animator.StringToHash("Closing");
        private static readonly int StateOpening = Animator.StringToHash("Opening");
        private static readonly int StateIdleClosed = Animator.StringToHash("Idle_Closed");
        private static readonly int StateIdleOpen   = Animator.StringToHash("Idle_Open");

        // ── References ─────────────────────────────────────────
        private Animator _animator;

        // ── Callbacks ──────────────────────────────────────────
        private System.Action _onCloseComplete;
        private System.Action _onOpenComplete;

        // ── State ──────────────────────────────────────────────
        private bool _isAnimating;
        /// <summary>True while an animation sequence is playing.</summary>
        public bool IsAnimating => _isAnimating;

        /// <summary>
        /// Анимация стены действительно поехала (true — открытие). Не поднимается, если поза уже на месте или стена уже
        /// едет туда же, — звук (<see cref="ArsenalWallSounds"/>) не дублируется. Разворот посреди анимации — поднимается.
        /// </summary>
        public event System.Action<bool> SequenceStarted;

        /// <summary>Анимация доехала до позы покоя (true — открыта).</summary>
        public event System.Action<bool> SequenceCompleted;

        /// <summary>Куда едет текущая анимация; смысл имеет только при <see cref="_isAnimating"/>.</summary>
        private bool _animatingToOpen;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Plays the closing animation (shelf retract → shutter down).
        /// Уже закрытая стена не анимируется — колбэк зовётся сразу.
        /// </summary>
        /// <param name="onComplete">Callback when the Closing clip finishes.</param>
        public void PlayCloseSequence(System.Action onComplete = null)
        {
            PlaySequence(open: false, onComplete);
        }

        /// <summary>
        /// Plays the opening animation (shutter up → shelf slide out).
        /// Уже открытая стена не анимируется — колбэк зовётся сразу.
        /// </summary>
        /// <param name="onComplete">Callback when the Opening clip finishes.</param>
        public void PlayOpenSequence(System.Action onComplete = null)
        {
            PlaySequence(open: true, onComplete);
        }

        /// <summary>
        /// Immediately snaps to open state (no animation).
        /// </summary>
        public void SetOpenImmediate()
        {
            SnapTo(StateIdleOpen);
        }

        /// <summary>
        /// Immediately snaps to closed state (no animation).
        /// </summary>
        public void SetClosedImmediate()
        {
            SnapTo(StateIdleClosed);
        }

        // ── Private ────────────────────────────────────────────

        private bool EnsureAnimator()
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            return _animator != null && _animator.runtimeAnimatorController != null && _animator.isActiveAndEnabled;
        }

        private void PlaySequence(bool open, System.Action onComplete)
        {
            string label = open ? "Open" : "Close";

            // Команда нового направления отменяет ожидание противоположного.
            _onOpenComplete = null;
            _onCloseComplete = null;

            if (!EnsureAnimator())
            {
                _isAnimating = false;
                onComplete?.Invoke();
                return;
            }

            ResetTriggers();

            int idleTarget = open ? StateIdleOpen : StateIdleClosed;
            int moveTarget = open ? StateOpening : StateClosing;
            int moveAway   = open ? StateClosing : StateOpening;
            int idleAway   = open ? StateIdleClosed : StateIdleOpen;

            AnimatorStateInfo state = GetEffectiveState();

            if (state.shortNameHash == idleTarget)
            {
                // Поза уже та, что нужна: проигрывать нечего. Play всё равно нужен:
                // если в этом же кадре уже запущено обратное направление, аниматор его
                // ещё не применил и показывает старое состояние — команду надо отменить.
                _isAnimating = false;
                _animator.Play(idleTarget, 0, 0f);
                GameLog.Arsenal.Verbose($"[Arsenal Anim] {label}: поза уже на месте, анимация пропущена.", this);
                onComplete?.Invoke();
                return;
            }

            if (open) _onOpenComplete = onComplete;
            else      _onCloseComplete = onComplete;

            bool wasMovingSameWay = _isAnimating && _animatingToOpen == open;
            _isAnimating = true;
            _animatingToOpen = open;

            if (state.shortNameHash == moveTarget || wasMovingSameWay)
            {
                // Уже едем куда надо — ждём конца, не перезапуская клип.
                GameLog.Arsenal.Verbose($"[Arsenal Anim] {label}: анимация уже идёт.", this);
                return;
            }

            float startTime = 0f;

            if (state.shortNameHash == moveAway)
            {
                // Разворот посреди анимации. Closing — тот же клип со скоростью −1, поэтому
                // пройденная доля одного направления — это оставшаяся доля другого.
                startTime = 1f - Mathf.Clamp01(state.normalizedTime);
            }
            else if (state.shortNameHash != idleAway)
            {
                GameLog.Arsenal.Warning($"[Arsenal Anim] {label}: неизвестное состояние аниматора, клип с начала.");
            }

            _animator.Play(moveTarget, 0, startTime);
            SequenceStarted?.Invoke(open);
            GameLog.Arsenal.Info($"[Arsenal Anim] {label} sequence started (t={startTime:F2}).");
        }

        /// <summary>
        /// Состояние, в котором аниматор окажется: во время перехода — целевое.
        /// Иначе стена в переходе Opening → Idle_Open сочла бы себя «ещё открывающейся».
        /// </summary>
        private AnimatorStateInfo GetEffectiveState()
        {
            return _animator.IsInTransition(0)
                ? _animator.GetNextAnimatorStateInfo(0)
                : _animator.GetCurrentAnimatorStateInfo(0);
        }

        private void SnapTo(int idleState)
        {
            if (!EnsureAnimator()) return;

            _isAnimating = false;
            _onOpenComplete = null;
            _onCloseComplete = null;

            ResetTriggers();
            _animator.Play(idleState, 0, 0f);

            // Note: Animator.Update can throw if not fully initialized or if no valid states exist
            if (_animator.gameObject.activeInHierarchy)
                _animator.Update(0f);
        }

        /// <summary>
        /// Триггеры контроллера код больше не подаёт, но гасит их на случай, если их
        /// взвёл кто-то ещё (инспектор, старая сцена): залежавшийся триггер — ровно
        /// тот дефект, от которого класс ушёл.
        /// </summary>
        private void ResetTriggers()
        {
            _animator.ResetTrigger(TriggerOpen);
            _animator.ResetTrigger(TriggerClose);
        }

        // ── Animation Events (called from clips) ──────────────
        // Событий на клипе сейчас нет — завершение ловит Update. Методы оставлены
        // публичными на случай, если события вернут.

        /// <summary>
        /// Called by Animation Event on the last frame of Arsenal_Close.
        /// </summary>
        public void OnCloseAnimationComplete()
        {
            _isAnimating = false;
            GameLog.Arsenal.Info("[Arsenal Anim] Close sequence complete.");
            SequenceCompleted?.Invoke(false);
            System.Action callback = _onCloseComplete;
            _onCloseComplete = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Called by Animation Event on the last frame of Arsenal_Open.
        /// </summary>
        public void OnOpenAnimationComplete()
        {
            _isAnimating = false;
            GameLog.Arsenal.Info("[Arsenal Anim] Open sequence complete.");
            SequenceCompleted?.Invoke(true);
            System.Action callback = _onOpenComplete;
            _onOpenComplete = null;
            callback?.Invoke();
        }

        // ── Completion: Check state via Update ────────────────

        private void Update()
        {
            if (!_isAnimating || _animator == null) return;

            var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

            // Close finished — transitioned to Idle_Closed
            if (!_animatingToOpen && stateInfo.shortNameHash == StateIdleClosed)
            {
                OnCloseAnimationComplete();
            }
            // Open finished — transitioned to Idle_Open
            else if (_animatingToOpen && stateInfo.shortNameHash == StateIdleOpen)
            {
                OnOpenAnimationComplete();
            }
        }
    }
}
