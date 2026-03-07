using Mirror;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.UI
{
    /// <summary>
    /// Меню администратора арены (только для Host/Server).
    /// Администратор — первый подключившийся игрок (Host).
    /// Предоставляет управление сессией: выбор карты, режима, старт/стоп матча.
    ///
    /// Активируется только если текущий клиент является сервером.
    /// Если Host одновременно игрок — ему доступны и AdminMenu, и PlayerMenu.
    /// </summary>
    public class AdminMenuController : MonoBehaviour
    {
        [Header("Реестры")]
        [Tooltip("Реестр всех доступных карт. Назначить MapRegistry asset.")]
        [SerializeField] private MapRegistry _mapRegistry;

        [Tooltip("Реестр всех доступных режимов. Назначить GameModeRegistry asset.")]
        [SerializeField] private GameModeRegistry _gameModeRegistry;

        private void Start()
        {
            // Меню администратора видно только серверу / хосту
            bool isAdmin = NetworkServer.active;
            gameObject.SetActive(isAdmin);
        }

        /// <summary>
        /// Устанавливает выбранную карту в SessionManager.
        /// Вызывается элементом выбора карты в UI.
        /// </summary>
        /// <param name="mapIndex">Индекс карты в MapRegistry.maps</param>
        public void OnMapSelected(int mapIndex)
        {
            if (!NetworkServer.active || _mapRegistry == null)
                return;

            if (mapIndex < 0 || mapIndex >= _mapRegistry.maps.Length)
                return;

            string currentModeId = SessionManager.Instance != null ? SessionManager.Instance.SelectedModeId : "";
            SessionManager.Instance?.SetSession(_mapRegistry.maps[mapIndex].sceneName, currentModeId);
        }

        /// <summary>
        /// Устанавливает выбранный игровой режим в SessionManager.
        /// Вызывается элементом выбора режима в UI.
        /// </summary>
        /// <param name="modeIndex">Индекс режима в GameModeRegistry.modes</param>
        public void OnModeSelected(int modeIndex)
        {
            if (!NetworkServer.active || _gameModeRegistry == null)
                return;

            if (modeIndex < 0 || modeIndex >= _gameModeRegistry.modes.Length)
                return;

            string currentMap = SessionManager.Instance != null ? SessionManager.Instance.SelectedMapScene : "";
            SessionManager.Instance?.SetSession(currentMap, _gameModeRegistry.modes[modeIndex].modeId);
        }

        /// <summary>
        /// Загружает выбранную карту и запускает сессию.
        /// Вызывается кнопкой "Начать игру" в UI.
        /// </summary>
        public void OnStartSessionPressed()
        {
            if (!NetworkServer.active)
                return;

            SessionManager.Instance?.StartSession();
        }

        /// <summary>
        /// Запускает матч на текущей карте.
        /// Вызывается кнопкой "Старт матча" в UI.
        /// </summary>
        public void OnStartMatchPressed()
        {
            if (!NetworkServer.active)
                return;

            GameplayManager.Instance?.StartGameplay();
        }

        /// <summary>
        /// Останавливает текущий матч и возвращает всех в Lobby.
        /// Вызывается кнопкой "Стоп / Выйти в лобби" в UI.
        /// </summary>
        public void OnStopMatchPressed()
        {
            if (!NetworkServer.active)
                return;

            GameplayManager.Instance?.StopGameplay();
            MapManager.Instance?.LoadMap("Lobby");
        }

        /// <summary>Возвращает список карт для построения UI меню.</summary>
        public MapData[] GetMaps() => _mapRegistry != null ? _mapRegistry.maps : new MapData[0];

        /// <summary>Возвращает список режимов для построения UI меню.</summary>
        public GameModeData[] GetModes() => _gameModeRegistry != null ? _gameModeRegistry.modes : new GameModeData[0];
    }
}
