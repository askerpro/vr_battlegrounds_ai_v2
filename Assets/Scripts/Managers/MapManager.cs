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
    [DefaultExecutionOrder(ManagerOrder.MapManager)]
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
                GameLog.Network.Warning("[MapManager] LoadMap вызван не на сервере — игнорируем.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                GameLog.Network.Warning("[MapManager] LoadMap: пустое имя сцены — игнорируем.");
                return;
            }

            if (IsLoading)
            {
                GameLog.Network.Warning($"[MapManager] LoadMap: уже идёт загрузка, запрос на '{sceneName}' игнорируется.");
                return;
            }

            _loadCoroutine = StartCoroutine(DeferredLoadMap(sceneName));
        }

        /// <summary>
        /// Ждёт, пока Mirror закончит начатые <c>AddPlayer</c>, и только потом зовёт
        /// <c>ServerChangeScene</c>: иначе сервер получит дублирующий <c>AddPlayer</c>
        /// уже после смены сцены.
        ///
        /// <para>
        /// Здесь было ожидание первого <c>PlayerConnected</c> с таймаутом в пять секунд —
        /// то есть «подождём, вдруг кто-нибудь подключится». Таймаут стоял ради
        /// Server-only режима без host-клиента: там ждать было некого, и загрузка
        /// каждый раз стоила пять секунд и предупреждения в логе. Теперь вместо
        /// времени проверяется само условие — <see cref="ConnectionsSettled" />.
        /// </para>
        /// </summary>
        private IEnumerator DeferredLoadMap(string sceneName)
        {
            IsLoading = true;
            _pendingScene = sceneName;
            MapLoadStarted?.Invoke(sceneName);

            if (!ConnectionsSettled())
            {
                GameLog.Network.Verbose(
                    $"[MapManager] Загрузка карты '{sceneName}': ждём, пока Mirror закончит AddPlayer " +
                    $"({DescribeUnsettled()})...");

                while (!ConnectionsSettled())
                    yield return null;
            }

            // Ещё кадр: Mirror должен завершить внутреннюю обработку AddPlayer
            // (Ready, SpawnObjects) до того, как мы сменим сцену.
            yield return null;

            GameLog.Network.Info($"[MapManager] ServerChangeScene: {sceneName}");
            CurrentMap = sceneName;
            NetworkManager.singleton.ServerChangeScene(sceneName);

            IsLoading = false;
            _loadCoroutine = null;
            _pendingScene = null;
            MapLoadCompleted?.Invoke(sceneName);
        }

        /// <summary>
        /// Условие готовности к смене сцены: ни одно соединение не находится
        /// в середине <c>AddPlayer</c>.
        ///
        /// <para>
        /// «В середине» — это <c>isReady</c> без <c>identity</c>. Клиент сообщил, что
        /// догрузил текущую сцену, но сессию сервер ему ещё не создал: игра спавнит её
        /// не автоматически, а в ответ на свой <c>GamePlayerConnectMessage</c>
        /// (<c>autoCreatePlayer = false</c>). Сменить сцену в этом промежутке и значит
        /// получить второй <c>AddPlayer</c> в новой сцене.
        /// </para>
        ///
        /// <para>
        /// Соединение, которое ещё не <c>isReady</c>, ждать не нужно и вредно: оно
        /// не начинало <c>AddPlayer</c>, а после <c>ServerChangeScene</c> пройдёт весь
        /// путь заново — ровно как клиент, подключившийся кадром позже. Ждать его
        /// значило бы зависнуть навсегда на клиенте, который до сессии не доходит
        /// (например, роль наблюдателя: <c>SpectatorConnectMessage</c> сессию не создаёт).
        /// </para>
        ///
        /// <para>
        /// Ни одного соединения — условие выполнено сразу. Это и есть тот случай,
        /// ради которого стоял таймаут.
        /// </para>
        /// </summary>
        private static bool ConnectionsSettled()
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null) continue;

                if (conn.isReady && conn.identity == null)
                    return false;
            }

            return true;
        }

        /// <summary>Кого именно ждём — для лога, чтобы зависание было видно по имени.</summary>
        private static string DescribeUnsettled()
        {
            int total = NetworkServer.connections.Count;
            int pending = 0;

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn != null && conn.isReady && conn.identity == null)
                    pending++;
            }

            return $"соединений {total}, из них без сессии {pending}";
        }
    }
}
