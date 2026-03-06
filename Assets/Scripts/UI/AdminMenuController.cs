using Mirror;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.UI
{
    /// <summary>
    /// Меню администратора арены (только для Host/Server).
    /// Администратор — первый подключившийся игрок (Host).
    /// Предоставляет управление матчем: выбор карты, режима, старт/стоп/пауза.
    ///
    /// Активируется только если текущий клиент является сервером.
    /// Если Host одновременно игрок — ему доступны и AdminMenu, и PlayerMenu.
    /// </summary>
    public class AdminMenuController : MonoBehaviour
    {
        [Header("Зависимости")]
        [SerializeField] private MatchManager _matchManager;

        [Header("Настройки карт")]
        [Tooltip("Реестр всех доступных карт. Назначить MapRegistry asset.")]
        [SerializeField] private MapRegistry _mapRegistry;

        private void Start()
        {
            // Меню администратора видно только серверу / хосту
            bool isAdmin = NetworkServer.active;
            gameObject.SetActive(isAdmin);
        }

        /// <summary>
        /// Запускает матч с выбранной картой и режимом.
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnStartMatchPressed()
        {
            if (!NetworkServer.active)
                return;

            _matchManager.StartMatch();
        }

        /// <summary>
        /// Останавливает текущий матч.
        /// Вызывается кнопкой UI.
        /// </summary>
        public void OnStopMatchPressed()
        {
            if (!NetworkServer.active)
                return;

            // TODO: вызвать MatchManager.StopMatch() когда метод будет реализован
        }

        /// <summary>
        /// Загружает карту по индексу из MapRegistry.
        /// Вызывается элементом выбора карты в UI.
        /// </summary>
        /// <param name="mapIndex">Индекс карты в MapRegistry.maps</param>
        public void OnMapSelected(int mapIndex)
        {
            if (!NetworkServer.active)
                return;

            if (_mapRegistry == null || mapIndex < 0 || mapIndex >= _mapRegistry.maps.Length)
            {
                Debug.LogWarning($"[AdminMenuController] Некорректный индекс карты или не назначен MapRegistry: {mapIndex}");
                return;
            }

            string sceneName = _mapRegistry.maps[mapIndex].sceneName;
            NetworkManager.singleton.ServerChangeScene(sceneName);
        }

        /// <summary>
        /// Возвращает список карт для построения UI меню.
        /// </summary>
        public MapData[] GetMaps() => _mapRegistry != null ? _mapRegistry.maps : new MapData[0];
    }
}
