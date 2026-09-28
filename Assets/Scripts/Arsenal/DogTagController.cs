using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
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
        /// Готовит жетон к новой фазе закупки: снова можно взять со стойки.
        /// </summary>
        public void ResetTag()
        {
            _hasBeenGrabbed = false;
            SetLight(_readyColor, true);

            // Синхронизируемое свойство пишет только сервер — см. ArsenalSlotController.SetItemGrabbable.
            if (_tagObject != null && StateEventAuthority.IsWorldAuthority)
                _tagObject.IsGrabbable = true;

            GameLog.Arsenal.Info("[Arsenal] Dog tag reset — ready for new prep phase.");
        }

        /// <summary>
        /// Показывает или прячет жетон с якорем. Жетон нужен только при старте раунда
        /// по готовности (<c>RoundStartRule.Readiness</c>); при старте по таймеру объявлять
        /// им нечего, и на его месте показывается табло <see cref="ArsenalPurchaseTimerDisplay" />.
        /// Сама панель остаётся активной — на ней живёт табло.
        ///
        /// Прячется жетон только в режиме таймера, а там он с самого начала не хватаем
        /// (<see cref="Disable" /> на закрытой стене), поэтому в руке его в этот момент не бывает.
        /// </summary>
        public void SetInUse(bool inUse)
        {
            bool changed = false;

            if (_tagObject != null && _tagObject.gameObject.activeSelf != inUse)
            {
                _tagObject.gameObject.SetActive(inUse);
                changed = true;
            }

            if (_tagAnchor != null && _tagAnchor.gameObject.activeSelf != inUse)
            {
                _tagAnchor.gameObject.SetActive(inUse);
                changed = true;
            }

            if (!changed) return;

            GameLog.Arsenal.Info(inUse
                ? "[Arsenal] Жетон показан: раунд стартует по готовности."
                : "[Arsenal] Жетон скрыт: раунд стартует по таймеру, на панели — табло.");
        }

        /// <summary>
        /// Запрещает брать жетон (стена закрывается).
        ///
        /// <para>
        /// Через <c>IsGrabbable</c>, а не выключением компонента. UltimateXR читает
        /// <c>IsGrabbable</c> только при поиске нового захвата, а выключенный компонент молча
        /// стирает из <c>UxrGrabManager</c> запись о текущем захвате. Так и было: время закупки
        /// истекало, пока игрок держал жетон, стена закрывалась, и отпускание на клиенте падало
        /// с «RuntimeManipulationInfo not found for object DogTag». Теперь жетон в руке остаётся
        /// в руке и отпускается штатно, а со стойки закрытой стены его не взять.
        /// </para>
        /// </summary>
        public void Disable()
        {
            if (_tagObject != null && StateEventAuthority.IsWorldAuthority)
                _tagObject.IsGrabbable = false;

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