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
    ///
    /// <para>
    /// <b>Сам жетон в панели не лежит.</b> Панель держит крючок (<see cref="_tagAnchor"/>) и префаб
    /// жетона (<see cref="_tagPrefab"/>), а экземпляр выдаёт сервер — стена арсенала, тем же путём,
    /// что оружие слотов (<c>ArsenalWallController.ServerEnsureDogTag</c>): создание через
    /// <c>MapRunAdmission</c>, <c>NetworkServer.Spawn</c>, выравнивание <c>UniqueId</c> по <c>netId</c>.
    /// Каждая машина сама вешает приехавший экземпляр на крючок (<see cref="BindTag"/>). Пока
    /// экземпляра нет (стена ещё не выдала, клиент ещё не получил), все методы панели безопасны и
    /// управляют только крючком.
    /// </para>
    /// </summary>
    public class DogTagController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────
        [Header("Dog Tag")]
        [SerializeField] private UxrGrabbableObjectAnchor _tagAnchor;

        [Tooltip("Префаб жетона (DogTagItem + UxrGrabbableObject + NetworkIdentity). Экземпляр выдаёт " +
                 "сервер стеной арсенала и вешает на крючок _tagAnchor; в сцене жетона нет.")]
        [SerializeField] private GameObject _tagPrefab;

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

        /// <summary>Префаб жетона, который выдаёт стена. null — панель без жетона.</summary>
        public GameObject TagPrefab => _tagPrefab;

        /// <summary>Крючок жетона.</summary>
        public UxrGrabbableObjectAnchor TagAnchor => _tagAnchor;

        /// <summary>Выданный жетон на этой машине или null: ещё не выдан, не приехал или уничтожен.</summary>
        public UxrGrabbableObject TagObject => _tagObject;

        /// <summary>Жетон на этой машине есть (уничтоженный объект Unity сравнивается с null как null).</summary>
        public bool HasTag => _tagObject != null;

        // ── State ──────────────────────────────────────────────
        private bool _hasBeenGrabbed;

        /// <summary>
        /// Выданный экземпляр жетона. Не сериализуется: жетон живёт только в рантайме, ставит его
        /// <see cref="BindTag"/>.
        /// </summary>
        [System.NonSerialized] private UxrGrabbableObject _tagObject;

        // ── Unity ──────────────────────────────────────────────

        private void Awake()
        {
            if (_tagAnchor == null)
                _tagAnchor = GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
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
        /// Вешает выданный экземпляр жетона на крючок этой машины. Зовёт стена: сервер — сразу после
        /// создания, до <c>NetworkServer.Spawn</c> (спавн-сообщение несёт уже положение на крючке),
        /// клиент — когда экземпляр приехал и проснулся, если сервер объявил, что жетон на крючке
        /// (иначе — <see cref="AdoptTag"/>).
        ///
        /// <para>
        /// Учёт «жетон на крючке» заводится тихо (<c>SetNetworkAnchor</c>), как у оружия слотов:
        /// выдача не действие игрока, и каждая машина приходит к одному учёту сама. Без него крючок
        /// не поднял бы <c>Removed</c> при захвате, и жест готовности не дошёл бы до стены.
        /// Синхронизируемых свойств метод не пишет — до спавна их не адресовать.
        /// </para>
        /// </summary>
        /// <returns>false — это не жетон или у панели нет крючка; экземпляр не тронут.</returns>
        public bool BindTag(GameObject instance)
        {
            if (!AdoptTag(instance)) return false;

            // Поза на крючке — та же, к которой приводит PlaceObject при возврате (выравнивание по самому якорю).
            Transform tag = instance.transform;
            tag.SetParent(_tagAnchor.transform, false);
            tag.localPosition = Vector3.zero;
            tag.localRotation = Quaternion.identity;

            Rigidbody body = _tagObject.RigidBodySource;
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            _tagObject.SetNetworkAnchor(_tagAnchor);

            GameLog.Arsenal.Info($"[Arsenal] Жетон повешен на крючок панели '{name}'.", this);
            return true;
        }

        /// <summary>
        /// Запоминает выданный экземпляр как жетон этой панели, не трогая его положения. Клиент так
        /// принимает жетон, который на сервере сейчас не на крючке (в чьей-то руке, брошен на пол):
        /// где он, скажет начальный снимок UltimateXR, а перевешивать его на крючок значило бы
        /// разойтись с сервером.
        /// </summary>
        /// <returns>false — это не жетон или у панели нет крючка.</returns>
        public bool AdoptTag(GameObject instance)
        {
            if (instance == null || _tagAnchor == null) return false;

            DogTagItem item = instance.GetComponent<DogTagItem>();
            if (item == null || item.Grabbable == null)
            {
                GameLog.Arsenal.Error($"[Arsenal] Панели '{name}' выдали '{instance.name}' без DogTagItem — это не жетон.", this);
                return false;
            }

            _tagObject = item.Grabbable;
            instance.name = "DogTag";
            return true;
        }

        /// <summary>
        /// Разрешает брать жетон, не начиная новой закупки: отметка «уже брали» остаётся. Так стена
        /// приводит к правилам жетон, появившийся посреди фазы (выдан впервые, приехал к клиенту,
        /// заменил пропавший). <see cref="ResetTag"/> здесь не годится: замена выброшенного за мир
        /// жетона давала бы объявить готовность второй раз за ту же закупку.
        /// </summary>
        public void EnableGrab()
        {
            SetLight(_hasBeenGrabbed ? _grabColor : _readyColor, true);

            if (_tagObject != null && StateEventAuthority.IsWorldAuthority)
                _tagObject.IsGrabbable = true;
        }

        /// <summary>
        /// Автор мира: пока жетон не нужен, брошенный жетон возвращается на спрятанный крючок, как только
        /// его отпустили. Иначе жетон, который держали в момент <see cref="SetInUse"/>(false), остался бы
        /// лежать видимым до следующей закупки. Зовёт стена каждый кадр; на крючке или в руке — пусто.
        /// </summary>
        public void ReturnLooseTag()
        {
            if (!StateEventAuthority.IsWorldAuthority) return;
            ReturnToRack(allowHiddenAnchor: true);
        }

        /// <summary>
        /// Готовит жетон к новой фазе закупки: снова можно взять со стойки.
        /// </summary>
        public void ResetTag()
        {
            _hasBeenGrabbed = false;
            SetLight(_readyColor, true);

            // Синхронизируемое свойство пишет только сервер — см. ArsenalSlotController.SetItemGrabbable.
            if (_tagObject != null && StateEventAuthority.IsWorldAuthority)
            {
                _tagObject.IsGrabbable = true;
                ReturnToRack();
            }

            GameLog.Arsenal.Info("[Arsenal] Dog tag reset — ready for new prep phase.");
        }

        /// <summary>
        /// Показывает или прячет жетон с якорем. Жетон нужен только при старте раунда
        /// по готовности (<c>RoundStartRule.Readiness</c>); при старте по таймеру объявлять
        /// им нечего, и на его месте показывается табло <see cref="ArsenalPurchaseTimerDisplay" />.
        /// Сама панель остаётся активной — на ней живёт табло.
        ///
        /// Прячется жетон в режиме таймера, а там он с самого начала не хватаем
        /// (<see cref="Disable" /> на закрытой стене), и в разминке. Жетон, который держат в момент
        /// смены режима, остаётся в руке; после отпускания его возвращает <see cref="ReturnLooseTag"/>.
        ///
        /// <para>
        /// Прячется крючок, а с ним и висящий на нём жетон; сам жетон не выключается. Он сетевой
        /// объект, а Mirror спавнит подключившемуся клиенту только объекты с включённым
        /// <c>activeSelf</c> (<c>NetworkServer.SpawnObserversForConnection</c>): выключенный жетон
        /// не доехал бы до позднего клиента вовсе. Поэтому брошенный жетон автор мира сначала
        /// возвращает на крючок.
        /// </para>
        /// </summary>
        public void SetInUse(bool inUse)
        {
            bool changed = false;

            if (!inUse && StateEventAuthority.IsWorldAuthority)
                ReturnToRack();

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

        /// <summary>
        /// Возвращает жетон на крючок. Раньше новая закупка только снова разрешала его брать:
        /// сорванный и брошенный жетон оставался лежать, где бросили, и в следующих раундах
        /// объявить готовность было нечем. Ставит автор мира — сервер: размещение синхронизируемое
        /// (<c>PlaceObject</c>, канал состояния UltimateXR), клиенты получают его событием.
        /// Жетон в чьей-то руке не отнимается — его отпустят, и он вернётся к следующей закупке.
        /// </summary>
        /// <param name="allowHiddenAnchor">
        /// Вешать и на выключенный крючок (жетон не нужен): <c>PlaceObject</c> проверяет только
        /// <c>enabled</c> якоря, а жетон уйдёт в скрытую иерархию вместе с крючком.
        /// </param>
        private void ReturnToRack(bool allowHiddenAnchor = false)
        {
            if (_tagAnchor == null || _tagObject == null || IsTagOnRack || UxrGrabManager.Instance == null) return;
            if (UxrGrabManager.Instance.IsBeingGrabbed(_tagObject)) return;
            if (!_tagObject.gameObject.activeInHierarchy) return;
            if (!allowHiddenAnchor && !_tagAnchor.gameObject.activeInHierarchy) return;

            // ReturnLooseTag зовёт это каждый кадр: лог — только об удавшемся возврате.
            if (UxrGrabManager.Instance.PlaceObject(_tagObject, _tagAnchor, UxrPlacementOptions.None, true))
                GameLog.Arsenal.Info($"[Arsenal] Жетон возвращён на крючок (на крючке: {IsTagOnRack}).");
        }

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