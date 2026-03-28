using System;
using System.Collections;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Управляет загрузкой карт (сцен) через Mirror.
    /// Инкапсулирует особенности Mirror: ServerChangeScene нельзя вызывать
    /// синхронно из OnServerSceneChanged — нужна отложенная загрузка.
    ///
    /// Singleton: живёт на том же GameObject, что и NetworkManager (DontDestroyOnLoad).
    /// </summary>
    public class MapManager : MonoBehaviour
    {
        public static MapManager Instance { get; private set; }

        /// <summary>Вызывается перед началом загрузки карты. Параметр — имя сцены.</summary>
        public static event Action<string> MapLoadStarted;

        /// <summary>Вызывается после завершения загрузки карты. Параметр — имя сцены.</summary>
        public static event Action<string> MapLoadCompleted;

        /// <summary>Идёт ли сейчас загрузка карты.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>Имя текущей загруженной карты (null если карта не загружена).</summary>
        public string CurrentMap { get; private set; }

        private Coroutine _loadCoroutine;
        private string _pendingScene;
        private bool _waitingForPlayer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            PlayersManager.OnSessionConnected -= OnPlayerConnectedForLoad;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Загружает карту через Mirror ServerChangeScene.
        /// Безопасно вызывать из любого callback'а Mirror — загрузка откладывается
        /// на конец кадра, чтобы Mirror завершил все внутренние операции
        /// (Ready, AddPlayer, SpawnObjects).
        ///
        /// Только сервер.
        /// </summary>
        /// <param name="sceneName">Имя сцены карты (например "TestMap1").</param>
        public void LoadMap(string sceneName)
        {
            if (!NetworkServer.active)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, "[MapManager] LoadMap вызван не на сервере — игнорируем.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, "[MapManager] LoadMap: пустое имя сцены — игнорируем.");
                return;
            }

            if (IsLoading)
            {
                GameLog.Warning(GameSettings.Instance.LogLevelNetwork, $"[MapManager] LoadMap: уже идёт загрузка, запрос на '{sceneName}' игнорируется.");
                return;
            }

            _loadCoroutine = StartCoroutine(DeferredLoadMap(sceneName));
        }

        /// <summary>
        /// Ждёт события PlayerConnected (игрок заспавнился на сервере),
        /// только потом вызывает ServerChangeScene.
        /// Это гарантирует что сервер уже обработал AddPlayer для текущей сцены
        /// и не получит дублирующий AddPlayer после смены сцены.
        /// </summary>
        private IEnumerator DeferredLoadMap(string sceneName)
        {
            IsLoading = true;
            MapLoadStarted?.Invoke(sceneName);

            // Если игроки уже есть — грузим сразу (повторная смена карты).
            // Если нет — подписываемся на PlayerConnected и ждём первого спавна.
            GameNetworkManager nm = NetworkManager.singleton as GameNetworkManager;
            bool playerAlreadySpawned = PlayersManager.Instance != null && PlayersManager.Instance.Sessions.Count > 0;

            if (!playerAlreadySpawned)
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelNetwork,
                    $"[MapManager] Загрузка карты '{sceneName}': ждём события PlayerConnected...");

                _waitingForPlayer = true;
                PlayersManager.OnSessionConnected += OnPlayerConnectedForLoad;

                // Таймаут на случай Server-only режима без Host-клиента
                const float timeout = 5f;
                float elapsed = 0f;
                while (_waitingForPlayer && elapsed < timeout)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                PlayersManager.OnSessionConnected -= OnPlayerConnectedForLoad;
                _waitingForPlayer = false;

                if (elapsed >= timeout)
                {
                    GameLog.Warning(GameSettings.Instance.LogLevelNetwork,
                        $"[MapManager] PlayerConnected не пришёл за {timeout}s — продолжаем загрузку.");
                }
                else
                {
                    GameLog.Verbose(GameSettings.Instance.LogLevelNetwork,
                        "[MapManager] PlayerConnected получен, ждём конца кадра...");

                    // Ждём ещё один кадр: Mirror должен завершить внутреннюю обработку
                    // AddPlayer (Ready, SpawnObjects) до того, как мы сменим сцену.
                    yield return null;
                }
            }
            else
            {
                GameLog.Verbose(GameSettings.Instance.LogLevelNetwork,
                    $"[MapManager] Игроки уже есть, загружаем карту '{sceneName}' сразу.");
            }

            GameLog.Info(GameSettings.Instance.LogLevelNetwork, $"[MapManager] ServerChangeScene: {sceneName}");
            CurrentMap = sceneName;
            NetworkManager.singleton.ServerChangeScene(sceneName);

            IsLoading = false;
            _loadCoroutine = null;
            _pendingScene = null;
            MapLoadCompleted?.Invoke(sceneName);
        }

        /// <summary>
        /// Обработчик PlayerConnected — сигнализирует корутине что игрок заспавнился.
        /// </summary>
        private void OnPlayerConnectedForLoad(PlayerSession session)
        {
            _waitingForPlayer = false;
        }
    }
}
