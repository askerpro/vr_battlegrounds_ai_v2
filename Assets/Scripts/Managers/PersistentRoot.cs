using UnityEngine;

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
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.PersistentRoot)]
    public class PersistentRoot : MonoBehaviour
    {
        private void Awake()
        {
            // Unity требует, чтобы DontDestroyOnLoad применялся только к корневым объектам
            transform.SetParent(null);

            DontDestroyOnLoad(gameObject);

            ManagerBootstrap.Declare();
        }

        /// <summary>
        /// Первый <c>Start</c> в сцене гарантированно позже последнего <c>Awake</c> —
        /// на этом и построена проверка состава: раньше проверять было бы нечего.
        /// </summary>
        private void Start()
        {
            ManagerBootstrap.Verify();
        }
    }
}
