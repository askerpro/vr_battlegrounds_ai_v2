using Mirror;
using Mirror;
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
    /// сценарий из DebugBootstrapConfig: равномерно распределяет игроков по командам,
    /// загружает карту, запускает матч.
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
        }

        private void OnDisable()
        {
            GameNetworkManager.PlayerConnected    -= OnPlayerConnected;
            GameNetworkManager.PlayerDisconnected -= OnPlayerDisconnected;
            GameNetworkManager.ServerSceneChanged -= OnServerSceneChanged;
        }

        private void Start()
        {
            if (!_config.enabled)
                return;
        }

        /// <summary>
        /// Вызывается при каждом подключении игрока.
        /// Назначает команду (равномерное распределение) и при необходимости стартует матч.
        /// </summary>
        private void OnPlayerConnected(PlayerController player)
        {
            if (!NetworkServer.active)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] OnPlayerConnected: сервер не активен, пропуск.");
                return;
            }

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] OnPlayerConnected: игрок={player.name}");

            TryAssignTeam(player);
            TryTeleportToSpawnZone(player);
            TryStartMatch();
        }

        private void TryTeleportToSpawnZone(PlayerController player)
        {
            if (player.Team == null) return;

            // Ищем спавн зону для назначенной команды
            TeamSpawnZone targetZone = null;
            TeamSpawnZone[] zones = FindObjectsOfType<TeamSpawnZone>();
            foreach (var zone in zones)
            {
                if (zone.Team == player.Team)
                {
                    targetZone = zone;
                    break;
                }
            }

            if (targetZone != null)
            {
                GameLog.Info(GameSettings.Instance.LogLevelDebug, $"[DebugOrchestrator] {player.name} начинает в зоне спавна команды {player.Team.displayName}");
                player.Respawn(targetZone.transform);
            }
        }

        private void OnPlayerDisconnected(PlayerController player)
        {
            GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] OnPlayerDisconnected: игрок={player.name}");
        }

        /// <summary>
        /// Проверяет условия автостарта и запускает матч если они выполнены.
        /// Вызывается как при подключении игроков, так и после загрузки сцены карты.
        /// </summary>
        private void TryStartMatch()
        {
            if (!_config.autoStartMatch)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryStartMatch: autoStartMatch выключен.");
                return;
            }

            PlayersManager playersManager = PlayersManager.Instance;
            int playerCount = playersManager != null ? playersManager.Players.Count : 0;

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] TryStartMatch: игроков={playerCount}, минимум={_config.minPlayersToAutoStart}");

            if (playersManager == null)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryStartMatch: PlayersManager.Instance == null. Повторная попытка через 1 кадр.");
                StartCoroutine(RetryStartMatchCoroutine());
                return;
            }

            if (playerCount < _config.minPlayersToAutoStart)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] TryStartMatch: недостаточно игроков ({playerCount}/{_config.minPlayersToAutoStart}).");
                return;
            }

            MatchManager matchManager = MatchManager.Instance;
            if (matchManager == null)
            {
                // MatchManager живёт только в сцене карты — при первом подключении в Offline сцене это нормально.
                GameLog.Warning(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryStartMatch: MatchManager.Instance == null — " +
                    "возможно карта ещё не загружена. Матч запустится после загрузки карты.");
                return;
            }

            if (matchManager.IsMatchActive)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryStartMatch: матч уже активен.");
                return;
            }

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] TryStartMatch: достаточно игроков ({playerCount}) и карта загружена — запускаем матч.");
            matchManager.StartMatch();

        }

        private System.Collections.IEnumerator RetryStartMatchCoroutine()
        {
            yield return null; // 1 кадр
            GameLog.Verbose(GameSettings.Instance.LogLevelDebug, "[DebugOrchestrator] RetryStartMatchCoroutine: повторный вызов TryStartMatch.");
            TryStartMatch();
        }

        /// <summary>
        /// Назначает игроку команду с наименьшим числом участников (round-robin по балансу).
        /// Если teamsForAutoAssign пуст — команда не назначается.
        /// </summary>
        private void TryAssignTeam(PlayerController player)
        {
            if (_config.teamsForAutoAssign == null || _config.teamsForAutoAssign.Count == 0)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] TryAssignTeam: teamsForAutoAssign пуст — команда не назначается.");
                return;
            }

            PlayersManager pm = PlayersManager.Instance;

            TeamData bestTeam = null;
            int bestCount = int.MaxValue;

            foreach (TeamData team in _config.teamsForAutoAssign)
            {
                if (team == null) continue;

                // Считаем сколько игроков уже в этой команде (не считая только что подключившегося)
                int count = 0;
                if (pm != null)
                {
                    foreach (PlayerController p in pm.Players)
                    {
                        if (p != player && p.Team == team)
                            count++;
                    }
                }

                if (count < bestCount)
                {
                    bestCount = count;
                    bestTeam  = team;
                }
            }

            if (bestTeam == null) return;

            player.Team = bestTeam;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Команда назначена игроку {player.name}: {bestTeam.displayName} (в команде: {bestCount + 1})");
        }

        /// <summary>
        /// Вызывается когда сервер завершил загрузку сцены.
        /// Если задан autoLoadMapScene — загружает карту.
        /// После загрузки сцены карты пытается запустить матч (игроки могли подключиться раньше).
        /// </summary>
        private void OnServerSceneChanged(string sceneName)
        {
            if (!NetworkServer.active)
                return;

            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] OnServerSceneChanged: сцена='{sceneName}'");

            TryAutoLoadMap();

            // Игроки подключились ДО загрузки карты (в Offline-сцене) — MatchManager тогда не существовал.
            // Теперь карта загружена — пробуем запустить матч.
            TryStartMatch();
        }

        private void TryAutoLoadMap()
        {
            if (string.IsNullOrEmpty(_config.autoLoadMapScene))
                return;

            // Загружаем карту только один раз за сессию.
            if (_mapLoadRequested)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelDebug,
                    "[DebugOrchestrator] Карта уже была запрошена, повторный вызов игнорируется.");
                return;
            }

            // Устанавливаем режим через GameManager до загрузки карты — MatchManager прочитает его при старте.
            if (!string.IsNullOrEmpty(_config.autoGameModeId) && GameManager.Instance != null)
            {
                GameManager.Instance.SetSession(_config.autoLoadMapScene, _config.autoGameModeId);
                GameLog.Info(GameSettings.Instance.LogLevelDebug,
                    $"[DebugOrchestrator] Сессия установлена: карта={_config.autoLoadMapScene}, режим={_config.autoGameModeId}");
            }

            _mapLoadRequested = true;
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[DebugOrchestrator] Автозагрузка карты: {_config.autoLoadMapScene}");
            MapManager.Instance?.LoadMap(_config.autoLoadMapScene);
        }
    }
}
