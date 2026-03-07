using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Оркестратор быстрой инициализации для отладки.
    /// Подписывается на события GameNetworkManager и выполняет заскриптованный
    /// сценарий из DebugBootstrapConfig: назначает команду, загружает карту, запускает матч.
    ///
    /// Не меняет продакшн-код — использует те же публичные API, что и обычная игра.
    ///
    /// Как использовать:
    ///   1. Добавить этот компонент на любой GameObject в сцене (например "DebugOrchestrator").
    ///   2. Назначить DebugBootstrapConfig в поле Config.
    ///   3. Чтобы отключить — деактивировать GameObject.
    /// </summary>
    public class DebugOrchestrator : MonoBehaviour
    {
        [SerializeField] private DebugBootstrapConfig _config;

        // Флаг: карта уже была запрошена в этой сессии — не грузить повторно.
        private bool _mapLoadRequested;

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

            if (!NetworkServer.active)
                return;

            localPlayer.Team = _config.autoTeam;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Команда назначена: {_config.autoTeam}");
        }

        /// <summary>
        /// Вызывается при каждом подключении игрока.
        /// Если игроков достаточно и autoStartMatch включён — стартует матч.
        /// </summary>
        private void OnPlayerConnected(PlayerController player)
        {
            if (!NetworkServer.active || !_config.autoStartMatch)
                return;

            PlayersManager playersManager = PlayersManager.Instance;
            if (playersManager == null || playersManager.Players.Count < _config.minPlayersToAutoStart)
                return;

            MatchManager matchManager = MatchManager.Instance;
            if (matchManager == null)
                return;

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Достаточно игроков ({playersManager.Players.Count}) — запускаем матч");
            matchManager.StartMatch();
        }

        private void OnPlayerDisconnected(PlayerController player) { }

        /// <summary>
        /// Вызывается когда сервер завершил загрузку сцены.
        /// Если задан autoLoadMapScene — загружает карту.
        /// </summary>
        private void OnServerSceneChanged(string sceneName)
        {
            if (!NetworkServer.active)
                return;

            TryAutoLoadMap();
        }

        private void TryAutoLoadMap()
        {
            if (string.IsNullOrEmpty(_config.autoLoadMapScene))
                return;

            // Загружаем карту только один раз за сессию.
            if (_mapLoadRequested)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] Карта уже была запрошена, повторный вызов игнорируется.");
                return;
            }

            _mapLoadRequested = true;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Автозагрузка карты: {_config.autoLoadMapScene}");
            MapManager.Instance?.LoadMap(_config.autoLoadMapScene);
        }
    }
}
