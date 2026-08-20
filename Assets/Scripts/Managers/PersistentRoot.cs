using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Вешается на корневой объект (например "--- MANAGERS ---"),
    /// чтобы вся иерархия менеджеров переживала смену сцен цельным блоком.
    /// Сохраняет порядок инспектора и предотвращает конфликты DontDestroyOnLoad у вложенных объектов.
    ///
    /// <para>
    /// Заодно — точка входа инициализации: <see cref="ManagerBootstrap" /> объявляет
    /// состав менеджеров в <c>Awake</c> и проверяет его в <c>Start</c>. Сама логика
    /// состава живёт там, а не здесь: этот класс отвечает только за корень иерархии.
    /// </para>
    ///
    /// <para>
    /// <c>[DefaultExecutionOrder]</c> обязателен и стоит первым числом из
    /// <see cref="ManagerOrder" />: <c>DontDestroyOnLoad</c> должен отработать до
    /// <c>Awake</c> любого менеджера ниже по иерархии.
    /// </para>
    ///
    /// <para>
    /// <b>Отсев дубликата — тоже его работа.</b> Префаб <c>--- MANAGERS ---</c> лежит
    /// в сцене <c>Offline</c>, а эта сцена загружается не один раз за процесс: в неё
    /// возвращаются из матча и в неё же Mirror уводит клиента при разрыве связи.
    /// Каждая такая загрузка поднимает вторую копию всей ветки. Раньше отсева здесь
    /// не было, и ветку разбирал по частям первый попавшийся менеджер — см. NET-20
    /// и комментарий к <see cref="Start" />.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.PersistentRoot)]
    public class PersistentRoot : MonoBehaviour
    {
        /// <summary>
        /// Живой корень ветки постоянных менеджеров. Всё, что поднялось вторым, —
        /// дубликат из перезагруженной сцены <c>Offline</c> и подлежит удалению.
        /// </summary>
        public static PersistentRoot Instance { get; private set; }

        private bool _isDuplicate;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Дубликат. Ветку не трогаем до Start — почему именно так, см. Start.
                // Ни DontDestroyOnLoad, ни объявления состава: и то и другое сделал
                // живой корень при старте процесса.
                _isDuplicate = true;
                return;
            }

            Instance = this;

            // Unity требует, чтобы DontDestroyOnLoad применялся только к корневым объектам
            transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            ManagerBootstrap.Declare();
        }

        /// <summary>
        /// Первый <c>Start</c> в сцене гарантированно позже последнего <c>Awake</c> —
        /// на этом построены обе здешние операции.
        ///
        /// <para>
        /// <b>Проверка состава</b> (живой корень): раньше проверять было бы нечего,
        /// менеджеры ещё не проснулись.
        /// </para>
        ///
        /// <para>
        /// <b>Уничтожение дубликата</b> — и именно здесь, а не в <c>Awake</c>. Компонент
        /// в ветке имеет право сам распорядиться своим временем жизни и уйти из-под
        /// корня: <c>Mirror.NetworkManager.InitializeSingleton</c> в своём <c>Awake</c>
        /// делает <c>SetParent(null)</c> и <c>DontDestroyOnLoad</c>. Если корень к этому
        /// моменту уже помечен на уничтожение, Unity гасит всю ветку сразу, и «спасшийся»
        /// объект остаётся выключенным навсегда: <c>Awake</c> ему выполнили, а
        /// <c>Start</c>, <c>Update</c> и <c>LateUpdate</c> — уже нет.
        /// </para>
        ///
        /// <para>
        /// Ровно так ломалось переподключение (<b>NET-20</b>): после разрыва Mirror
        /// уничтожает свой прежний <c>NetworkManager</c> загрузкой сцены <c>Offline</c>
        /// и рассчитывает, что менеджер из этой сцены его заменит. Замена поднималась
        /// выключенной, её <c>LateUpdate</c> не вызывался, <c>UpdateScene()</c> не
        /// закрывал загрузку — и <c>NetworkClient.isLoadingScene</c> оставался
        /// взведённым навсегда. При взведённом флаге клиент не разбирает входящие
        /// сообщения вовсе: соединение устанавливается, а ни сцены, ни сессии клиент
        /// уже не получает.
        /// </para>
        ///
        /// <para>
        /// Отсрочка до <c>Start</c> не продлевает жизнь дубликату: <c>Start</c> этого
        /// класса — самый ранний в сцене (порядок <see cref="ManagerOrder.PersistentRoot" />),
        /// поэтому до <c>Start</c> остальных компонентов ветки дело не доходит.
        /// </para>
        /// </summary>
        private void Start()
        {
            if (_isDuplicate)
            {
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[PersistentRoot] Сцена подняла вторую копию ветки менеджеров ('{gameObject.name}') — " +
                    "уничтожаю её целиком. Живой корень поднялся при старте процесса и пережил смену сцены.");

                Destroy(gameObject);
                return;
            }

            ManagerBootstrap.Verify();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
