using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Хранит выбор текущей игровой сессии: карту и режим.
    /// Переживает смену сцен (DontDestroyOnLoad вместе с NetworkManager).
    /// Синхронизирует выбор на всех клиентах через SyncVar.
    ///
    /// Singleton: живёт на том же GameObject, что и GameNetworkManager и MapManager.
    ///
    /// Использование:
    ///   — Администратор вызывает SetSession(mapScene, modeId) через AdminMenuController
    ///   — MatchManager читает SelectedGameModeData и SelectedMap при StartMatch()
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.SessionManager)]
    public class SessionManager : NetworkBehaviour
    {
        public static SessionManager Instance { get; private set; }

        [Header("Реестры")]
        [Tooltip("Реестр всех карт. Назначить MapRegistry asset.")]
        [SerializeField] private MapRegistry _mapRegistry;

        [Tooltip("Реестр всех игровых режимов. Назначить GameModeRegistry asset.")]
        [SerializeField] private GameModeRegistry _gameModeRegistry;

        // Идентификаторы хранятся как строки — безопасно через смены сцен
        [SyncVar] private string _selectedMapScene = "";
        [SyncVar] private string _selectedModeId = "";

        /// <summary>Данные выбранной карты. Null если карта не выбрана.</summary>
        public MapData SelectedMap
            => _mapRegistry != null ? _mapRegistry.GetBySceneName(_selectedMapScene) : null;

        /// <summary>Данные выбранного игрового режима. Null если режим не выбран.</summary>
        public GameModeData SelectedGameModeData
        {
            get
            {
                if (_gameModeRegistry == null)
                {
                    GameLog.Error("[SessionManager] SelectedGameModeData: _gameModeRegistry не назначен в Inspector!");
                    return null;
                }
                if (string.IsNullOrEmpty(_selectedModeId))
                {
                    GameLog.Match.Warning(
                        "[SessionManager] SelectedGameModeData: режим не выбран (_selectedModeId пуст).");
                    return null;
                }
                GameModeData result = _gameModeRegistry.GetById(_selectedModeId);
                if (result == null)
                    GameLog.Error(
                        $"[SessionManager] SelectedGameModeData: режим '{_selectedModeId}' не найден в реестре. " +
                        $"Доступные режимы: {string.Join(", ", System.Array.ConvertAll(_gameModeRegistry.modes, m => m != null ? m.modeId : "null"))}");
                return result;
            }
        }

        /// <summary>Имя сцены выбранной карты.</summary>
        public string SelectedMapScene => _selectedMapScene;

        /// <summary>Идентификатор выбранного режима.</summary>
        public string SelectedModeId => _selectedModeId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            transform.SetParent(null); // Ensure it's a root object
            DontDestroyOnLoad(gameObject); // Survive scene transitions
        }

        /// <summary>
        /// Устанавливает карту и режим для следующей сессии.
        /// Реплицируется на все клиенты через SyncVar.
        /// Только сервер.
        /// </summary>
        [Server]
        public void SetSession(string mapScene, string modeId)
        {
            if (string.IsNullOrEmpty(mapScene))
            {
                GameLog.Match.Warning(
                    "[SessionManager] SetSession: пустое имя карты — игнорируем.");
                return;
            }

            if (string.IsNullOrEmpty(modeId))
            {
                GameLog.Match.Warning(
                    "[SessionManager] SetSession: пустой modeId — игнорируем.");
                return;
            }

            _selectedMapScene = mapScene;
            _selectedModeId = modeId;

            GameLog.Match.Info(
                $"[SessionManager] Сессия настроена: карта={mapScene}, режим={modeId}");
        }

        /// <summary>
        /// Загружает выбранную карту через MapManager.
        /// Вызывать после SetSession().
        /// Только сервер.
        /// </summary>
        [Server]
        public void StartSession()
        {
            if (string.IsNullOrEmpty(_selectedMapScene))
            {
                GameLog.Error("[SessionManager] StartSession: карта не выбрана.");
                return;
            }

            if (string.IsNullOrEmpty(_selectedModeId))
            {
                GameLog.Error("[SessionManager] StartSession: режим не выбран.");
                return;
            }

            GameLog.Match.Info(
                $"[SessionManager] Запуск сессии: карта={_selectedMapScene}, режим={_selectedModeId}");

            MapManager.Instance?.LoadMap(_selectedMapScene);
        }
    }
}
