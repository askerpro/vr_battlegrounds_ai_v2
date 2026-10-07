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
    [DefaultExecutionOrder(ManagerOrder.MapLoader)]
    public class MapLoader : MonoBehaviour
    {
        public static MapLoader Instance { get; private set; }

        /// <summary>Вызывается перед началом загрузки карты. Параметр — имя сцены.</summary>
        public static event Action<string> MapLoadStarted;

        /// <summary>
        /// Сервер догрузил сцену карты (Mirror завершил смену сцены и заспавнил её объекты).
        /// Не готовность gameplay: её публикует <c>MapBootstrap</c> через <c>MapRunAuthority</c>.
        /// </summary>
        public static event Action<string> MapLoadCompleted;

        /// <summary>
        /// Принятая загрузка отменена до завершения смены сцены: сервер остановлен, загрузчик уничтожен
        /// или Mirror не начал смену. Параметр — имя сцены. Закрытый запуск прежней карты остаётся закрытым.
        /// </summary>
        public static event Action<string> MapLoadCancelled;

        /// <summary>Идёт ли сейчас загрузка карты — от принятого запроса до конца смены сцены Mirror или отмены.</summary>
        public bool IsLoading => _activeGeneration != 0;

        /// <summary>
        /// Номер принятой загрузки: растёт на каждый принятый запрос, отклонённый его не меняет. Корутина загрузки
        /// завершает и отменяет только свой номер — запоздавший шаг старой загрузки новую не трогает.
        /// </summary>
        public ulong LoadGeneration { get; private set; }

        /// <summary>Имя текущей загруженной карты (null если карта не загружена).</summary>
        public string CurrentMap { get; private set; }

        private Coroutine _loadCoroutine;
        private ulong _activeGeneration;
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

        private void OnDisable()
        {
            // Корутина остановлена вместе с компонентом: загрузка не завершится, держать IsLoading нельзя.
            CancelLoad(_activeGeneration, "загрузчик выключен");
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
        /// <param name="onAccepted">
        /// Разрушительное действие вызывающего, которое допустимо только под принятую загрузку (серия изымает
        /// снаряжение). Выполняется синхронно после принятия и до <see cref="MapLoadStarted"/>; отклонённый
        /// запрос его не вызывает — живая карта не теряет снаряжение из-за игнорируемой команды.
        /// </param>
        /// <returns>true — запрос принят и загрузка начата.</returns>
        public bool LoadMap(string sceneName, Action onAccepted = null)
        {
            if (!CanAcceptLoad(sceneName, out string reason))
            {
                GameLog.Network.Warning($"[MapLoader] LoadMap('{sceneName}') игнорируется: {reason}.");
                return false;
            }

            ulong generation = ++LoadGeneration;
            _activeGeneration = generation;
            _pendingScene = sceneName;

            try { onAccepted?.Invoke(); }
            catch (Exception error) { GameLog.Error($"[MapLoader] Действие перед загрузкой '{sceneName}' отказало: {error}"); }

            _loadCoroutine = StartCoroutine(DeferredLoadMap(sceneName, generation));
            return true;
        }

        /// <summary>
        /// Будет ли принят запрос загрузки. Проверка до разрушительных действий вызывающего
        /// (изъятие снаряжения серии): отклонённый запрос не трогает живую карту.
        /// </summary>
        public bool CanAcceptLoad(string sceneName, out string reason)
        {
            reason = !NetworkServer.active ? "вызов не на сервере"
                : string.IsNullOrEmpty(sceneName) ? "пустое имя сцены"
                : IsLoading ? "уже идёт загрузка"
                : null;
            return reason == null;
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
        private IEnumerator DeferredLoadMap(string sceneName, ulong generation)
        {
            MapLoadStarted?.Invoke(sceneName);

            if (!ConnectionsSettled())
            {
                GameLog.Network.Verbose(
                    $"[MapLoader] Загрузка карты '{sceneName}': ждём, пока Mirror закончит AddPlayer " +
                    $"({DescribeUnsettled()})...");

                while (IsCurrent(generation) && NetworkServer.active && !ConnectionsSettled())
                    yield return null;
            }

            // Ещё кадр: Mirror должен завершить внутреннюю обработку AddPlayer
            // (Ready, SpawnObjects) до того, как мы сменим сцену.
            yield return null;

            if (!IsCurrent(generation)) yield break;
            if (!NetworkServer.active || NetworkManager.singleton == null)
            {
                CancelLoad(generation, "сервер остановлен до смены сцены");
                yield break;
            }

            GameLog.Network.Info($"[MapLoader] ServerChangeScene: {sceneName}");
            NetworkManager.singleton.ServerChangeScene(sceneName);
            if (!NetworkServer.isLoadingScene)
            {
                CancelLoad(generation, "Mirror не начал смену сцены");
                yield break;
            }
            CurrentMap = sceneName;

            // Загрузка держится до конца асинхронной смены сцены: повторный запрос в этом окне
            // начал бы вторую смену поверх первой.
            while (IsCurrent(generation) && NetworkServer.active && NetworkServer.isLoadingScene)
                yield return null;

            if (!IsCurrent(generation)) yield break;
            if (!NetworkServer.active)
            {
                CancelLoad(generation, "сервер остановлен во время смены сцены");
                yield break;
            }

            _activeGeneration = 0;
            _loadCoroutine = null;
            _pendingScene = null;
            MapLoadCompleted?.Invoke(sceneName);
        }

        private bool IsCurrent(ulong generation) => generation != 0 && generation == _activeGeneration;

        /// <summary>Отменить загрузку <paramref name="generation"/>, если она ещё текущая. Повтор безопасен.</summary>
        private void CancelLoad(ulong generation, string reason)
        {
            if (!IsCurrent(generation)) return;
            string scene = _pendingScene;
            _activeGeneration = 0;
            _pendingScene = null;
            if (_loadCoroutine != null && isActiveAndEnabled) StopCoroutine(_loadCoroutine);
            _loadCoroutine = null;
            GameLog.Network.Warning($"[MapLoader] Загрузка '{scene}' (№{generation}) отменена: {reason}.");
            MapLoadCancelled?.Invoke(scene);
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
