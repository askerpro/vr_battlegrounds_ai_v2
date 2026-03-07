using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Оркестратор быстрой инициализации для отладки.
    /// Подписывается на события GameNetworkManager и выполняет заскриптованный
    /// сценарий из DebugBootstrapConfig: назначает команду, загружает карту, запускает матч.
    ///
    /// Не меняет продакшн-код — использует те же публичные API, что и обычная игра.
    /// Загрузку карт делегирует MapManager — не знает о нюансах Mirror.
    ///
    /// Как использовать:
    ///   1. Добавить этот компонент на любой GameObject в сцене (например "DebugOrchestrator").
    ///   2. Назначить DebugBootstrapConfig в поле Config.
    ///   3. Чтобы отключить — деактивировать GameObject.
    /// </summary>
    public class DebugOrchestrator : MonoBehaviour
    {
        [SerializeField] private DebugBootstrapConfig _config;

        private void Awake()
        {
            if (_config == null || !_config.enabled)
            {
                gameObject.SetActive(false);
                return;
            }
        }

        private void OnEnable()
        {
            GameNetworkManager.PlayerConnected    += OnPlayerConnected;
            GameNetworkManager.PlayerDisconnected += OnPlayerDisconnected;
            GameNetworkManager.ServerSceneChanged += OnServerSceneChanged;
            UxrAvatar.LocalAvatarChanged          += OnLocalAvatarChanged;
        }

        private void OnDisable()
        {
            GameNetworkManager.PlayerConnected    -= OnPlayerConnected;
            GameNetworkManager.PlayerDisconnected -= OnPlayerDisconnected;
            GameNetworkManager.ServerSceneChanged -= OnServerSceneChanged;
            UxrAvatar.LocalAvatarChanged          -= OnLocalAvatarChanged;
        }

        private void Start()
        {
            if (!_config.enabled)
                return;
        }

        /// <summary>
        /// Вызывается когда локальный аватар готов.
        /// Назначает команду локальному игроку.
        /// </summary>
        private void OnLocalAvatarChanged(object sender, UxrAvatarEventArgs e)
        {
            if (e.Avatar == null)
                return;

            PlayerController localPlayer = e.Avatar.GetComponent<PlayerController>();
            if (localPlayer == null)
                return;

            // Команду назначает только сервер
            if (!NetworkServer.active)
                return;

            localPlayer.Team = _config.autoTeam;
            Debug.Log($"[DebugOrchestrator] Команда назначена: {_config.autoTeam}");
        }

        /// <summary>
        /// Вызывается при каждом подключении игрока.
        /// Проверяет условие автостарта матча.
        /// </summary>
        private void OnPlayerConnected(PlayerController player)
        {
            if (!_config.autoStartMatch)
                return;

            if (!NetworkServer.active)
                return;

            GameNetworkManager networkManager = NetworkManager.singleton as GameNetworkManager;
            if (networkManager == null)
                return;

            int totalPlayers = networkManager.Players.Count;
            if (totalPlayers < _config.minPlayersToAutoStart)
            {
                Debug.Log($"[DebugOrchestrator] Игроков: {totalPlayers}/{_config.minPlayersToAutoStart} — ожидаем ещё.");
                return;
            }

            if (MatchManager.Instance == null)
            {
                Debug.LogWarning("[DebugOrchestrator] MatchManager не найден — матч не запущен.");
                return;
            }

            Debug.Log("[DebugOrchestrator] Автостарт матча.");
            MatchManager.Instance.StartMatch();
        }

        /// <summary>
        /// Вызывается при отключении игрока. Зарезервировано для будущей логики.
        /// </summary>
        private void OnPlayerDisconnected(PlayerController player) { }

        /// <summary>
        /// Срабатывает при каждой смене сцены на сервере.
        /// Автозагрузку карты делает только если загружена onlineScene (сцена после подъёма сервера).
        /// </summary>
        private void OnServerSceneChanged(string sceneName)
        {
            GameNetworkManager nm = NetworkManager.singleton as GameNetworkManager;
            if (nm == null)
                return;

            // Автозагрузку делаем только если загрузилась именно onlineScene
            if (sceneName != nm.onlineScene)
                return;

            TryAutoLoadMap();
        }

        /// <summary>
        /// Загружает карту из конфига через MapManager.
        /// Все нюансы Mirror (отложенная загрузка) инкапсулированы в MapManager.
        /// </summary>
        private void TryAutoLoadMap()
        {
            if (string.IsNullOrEmpty(_config.autoLoadMapScene))
                return;

            if (MapManager.Instance == null)
            {
                Debug.LogWarning("[DebugOrchestrator] MapManager не найден — автозагрузка карты невозможна.");
                return;
            }

            Debug.Log($"[DebugOrchestrator] Автозагрузка карты: {_config.autoLoadMapScene}");
            MapManager.Instance.LoadMap(_config.autoLoadMapScene);
        }
    }
}
