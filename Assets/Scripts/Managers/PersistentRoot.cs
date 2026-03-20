using UnityEngine;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Вешается на корневой объект (например "--- MANAGERS ---"),
    /// чтобы вся иерархия менеджеров переживала смену сцен цельным блоком.
    /// Сохраняет порядок инспектора и предотвращает конфликты DontDestroyOnLoad у вложенных объектов.
    /// </summary>
    public class PersistentRoot : MonoBehaviour
    {
        private void Awake()
        {
            // Unity требует, чтобы DontDestroyOnLoad применялся только к корневым объектам
            transform.SetParent(null);
            
            DontDestroyOnLoad(gameObject);
        }
    }
}
