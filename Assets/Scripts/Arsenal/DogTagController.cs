using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Панель жетона на стене арсенала.
    ///
    /// <para>
    /// Жетон — <b>жест, которым игрок объявляет готовность к раунду</b>, и больше ничего.
    /// Стену он не закрывает: она общая, и закрытие по первому жетону оставляло остальных
    /// без снаряжения (RDY-01, задача T-29). Захват уходит в
    /// <see cref="OnTagGrabbed"/>, дальше — <c>ArsenalWallController.HandleTagGrabbed</c>
    /// → <c>PlayerSession.CmdSetReady(true)</c>.
    /// </para>
    ///
    /// <para>
    /// Взять жетон можно один раз за фазу закупки: <see cref="_hasBeenGrabbed"/>
    /// сбрасывает только <see cref="ResetTag"/>, а его зовут при открытии стены.
    /// Это оставляет открытым RDY-03 — отменивший готовность не может объявить её
    /// заново жетоном; разбор в <c>Docs/audit/network-audit-2026-08.md</c>.
    /// </para>
    /// </summary>
    public class DogTagController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────
        [Header("Dog Tag")]
        [SerializeField] private UxrGrabbableObjectAnchor _tagAnchor;
        [SerializeField] private UxrGrabbableObject _tagObject;

        [Header("Visual")]
        [SerializeField] private Light _readyLight;
        [SerializeField] private Color _readyColor = Color.cyan;
        [SerializeField] private Color _grabColor  = Color.yellow;

        // ── Events ─────────────────────────────────────────────
        /// <summary>
        /// Игрок снял жетон со стойки. Подписан <c>ArsenalWallController</c> — он
        /// пересылает жест на сессию игрока, где тот превращается в готовность.
        /// Игрок может быть <c>null</c>: захват без определённого владельца.
        /// </summary>
        public System.Action<PlayerController> OnTagGrabbed;

        // ── Properties ─────────────────────────────────────────
        public bool IsTagOnRack => _tagAnchor != null && _tagAnchor.CurrentPlacedObject != null;

        // ── State ──────────────────────────────────────────────
        private bool _hasBeenGrabbed;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            if (_tagAnchor == null)
                _tagAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>();
            if (_tagObject == null)
                _tagObject = GetComponentInChildren<UxrGrabbableObject>();
        }

        private void OnEnable()
        {
            if (_tagAnchor != null)
            {
                _tagAnchor.Removed += OnTagRemoved;
            }
        }

        private void OnDisable()
        {
            if (_tagAnchor != null)
            {
                _tagAnchor.Removed -= OnTagRemoved;
            }
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Resets the dog tag for a new prep phase.
        /// </summary>
        public void ResetTag()
        {
            _hasBeenGrabbed = false;
            SetLight(_readyColor, true);

            if (_tagObject != null)
                _tagObject.enabled = true;

            GameLog.Arsenal.Info("[Arsenal] Dog tag reset — ready for new prep phase.");
        }

        /// <summary>
        /// Disables the dog tag interaction (e.g. during round active phase).
        /// </summary>
        public void Disable()
        {
            if (_tagObject != null)
                _tagObject.enabled = false;

            SetLight(_readyColor, false);
        }

        // ── Private ────────────────────────────────────────────

        private void OnTagRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (_hasBeenGrabbed) return;

            _hasBeenGrabbed = true;
            SetLight(_grabColor, true);

            GameLog.Arsenal.Info("[Arsenal] Жетон взят — игрок объявляет готовность к раунду.");
            
            PlayerController player = null;
            if (e.Grabber != null && e.Grabber.Avatar != null)
            {
                player = e.Grabber.Avatar.GetComponentInParent<PlayerController>();
            }
            
            OnTagGrabbed?.Invoke(player);
        }

        private void SetLight(Color color, bool on)
        {
            if (_readyLight != null)
            {
                _readyLight.color   = color;
                _readyLight.enabled = on;
            }
        }
    }
}