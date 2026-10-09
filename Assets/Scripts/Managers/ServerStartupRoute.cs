using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Managers
{
    /// <summary>Жизнь запроса маршрута старта (<see cref="ServerStartupRoute"/>).</summary>
    public enum StartupRouteState : byte
    {
        /// <summary>Запроса нет: сервер стартует штатно, в <c>onlineScene</c>.</summary>
        None,
        /// <summary>Запрос принят, сеть ещё не выбрала первую сцену; владелец может снять его.</summary>
        Requested,
        /// <summary>Первая сцена сервера — цель запроса; ждём захвата режима серией.</summary>
        SceneConsumed
    }

    /// <summary>
    /// Владение запросом маршрута старта. <see cref="Dispose"/> снимает свой запрос, пока сеть его не использовала
    /// (<see cref="StartupRouteState.Requested"/>); после выбора сцены запрос доводится до захвата режима.
    /// </summary>
    public sealed class StartupRouteHandle : IDisposable
    {
        public string RequestId { get; }
        public string Owner { get; }

        internal StartupRouteHandle(string requestId, string owner)
        {
            RequestId = requestId;
            Owner = owner;
        }

        public void Dispose() => ServerStartupRoute.Cancel(RequestId, Owner);
    }

    /// <summary>
    /// Маршрут старта сервера: сервер или хост стартует сразу в целевую сцену (карта или отладочный стенд из
    /// <see cref="MapRuntimeCatalog"/>) вместо <c>onlineScene</c> (Lobby), при необходимости — серией из одной карты
    /// с режимом. Контракт <c>map-startup-route</c> ревизии 2.
    ///
    /// <para>
    /// Порядок Mirror разный: <c>StartServer</c> → <c>OnStartServer</c> → <c>ServerChangeScene(onlineScene)</c>;
    /// <c>StartHost</c> → <c>ServerChangeScene(onlineScene)</c> → (сцена загружена) → <c>OnStartServer</c>. Общая точка —
    /// переопределённый <c>GameNetworkManager.ServerChangeScene</c>: при запросе первая смена сцены сервера грузит цель.
    /// Режим захватывается, когда есть и выбранная сцена, и <see cref="Series"/>: у сервера — сразу при выборе сцены,
    /// у хоста — в <c>OnStartServer</c>. Поле <c>onlineScene</c> не меняется (NET-21); без запроса путь Mirror прежний.
    /// </para>
    /// </summary>
    public static class ServerStartupRoute
    {
        public const string Busy = "StartupRoute.Busy";
        public const string SceneNotLoadable = "StartupRoute.SceneNotLoadable";
        public const string ModeIncompatible = "StartupRoute.ModeIncompatible";
        public const string InvalidRequest = "StartupRoute.InvalidRequest";
        public const string NetworkActive = "StartupRoute.NetworkActive";

        private sealed class Request
        {
            public string Id;
            public string Owner;
            public string Scene;
            public string ModeId;
            public StartupRouteState State;
        }

        private static Request _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProcessState() => _current = null;

        public static StartupRouteState State => _current?.State ?? StartupRouteState.None;
        public static string RequestId => _current?.Id;
        public static string Owner => _current?.Owner;
        public static string TargetScene => _current?.Scene;
        public static string ModeId => _current?.ModeId;

        /// <summary>
        /// Запросить старт сервера сразу в <paramref name="scene"/>. Вызывать до <c>StartServer</c>/<c>StartHost</c>.
        /// Пустой <paramref name="modeId"/> — без серии, режим выберет запуск карты как обычно.
        /// </summary>
        /// <returns>false — отказ с именованным кодом в <paramref name="error"/>; на Lobby не откатывается.</returns>
        public static bool TryRequest(string scene, string modeId, string owner,
                                      out StartupRouteHandle handle, out string error)
        {
            // Каталог живёт на GameNetworkManager из сцены Offline: до её загрузки (BeforeSceneLoad) запрашивать рано.
            // Окно запроса — после Awake менеджеров Offline и до Start, который поднимает сеть (например, sceneLoaded).
            if (NetworkManager.singleton == null)
            {
                handle = null;
                error = SceneNotLoadable + ": сетевой менеджер ещё не создан — звать после загрузки Offline, до старта сети";
                return false;
            }
            return TryRequestCore(scene, modeId, owner, Network.GameNetworkManager.MapCatalog,
                                  Application.CanStreamedLevelBeLoaded, NetworkServer.active, out handle, out error);
        }

        internal static bool TryRequestCore(string scene, string modeId, string owner, MapRuntimeCatalog catalog,
                                            Func<string, bool> canLoad, bool serverActive,
                                            out StartupRouteHandle handle, out string error)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(owner))
            {
                error = InvalidRequest + ": нужны сцена и владелец";
                return false;
            }
            if (serverActive)
            {
                error = NetworkActive + ": сервер уже запущен, первая сцена выбрана";
                return false;
            }
            if (_current != null)
            {
                error = $"{Busy}: запрос {_current.Id} владельца '{_current.Owner}' ({_current.State})";
                return false;
            }
            if (!Validate(catalog, canLoad, scene, modeId, out string canonicalScene, out error))
                return false;

            _current = new Request
            {
                Id = Guid.NewGuid().ToString("N"),
                Owner = owner,
                Scene = canonicalScene,
                ModeId = string.IsNullOrWhiteSpace(modeId) ? string.Empty : modeId,
                State = StartupRouteState.Requested
            };
            handle = new StartupRouteHandle(_current.Id, owner);
            GameLog.Network.Info($"[ServerStartupRoute] Запрос {_current.Id} ({owner}): старт в '{_current.Scene}'" +
                                 (_current.ModeId.Length > 0 ? $", серия '{_current.ModeId}'." : "."));
            return true;
        }

        /// <summary>
        /// Снять свой запрос. Снимается только запрос с этим <paramref name="requestId"/> и владельцем и только в
        /// <see cref="StartupRouteState.Requested"/>: после выбора сцены запрос уже исполняется.
        /// </summary>
        /// <returns>true — запрос снят.</returns>
        public static bool Cancel(string requestId, string owner)
        {
            if (_current == null || _current.Id != requestId || _current.Owner != owner) return false;
            if (_current.State != StartupRouteState.Requested) return false;
            GameLog.Network.Info($"[ServerStartupRoute] Запрос {requestId} ({owner}) снят до старта сети.");
            _current = null;
            return true;
        }

        /// <summary>
        /// Проверка цели: сцена из каталога (реестр или отладочные стенды), загружаемая (в списке сборки), режим —
        /// матча и совместим с картой. Без побочных эффектов.
        /// </summary>
        internal static bool Validate(MapRuntimeCatalog catalog, Func<string, bool> canLoad, string scene, string modeId,
                                      out string canonicalScene, out string error)
        {
            canonicalScene = null;
            if (catalog == null)
            {
                error = SceneNotLoadable + ": каталог карт не назначен (GameNetworkManager.MapCatalog)";
                return false;
            }
            MapData map = catalog.FindMap(scene);
            if (map == null)
            {
                error = $"{SceneNotLoadable}: '{scene}' нет в MapRuntimeCatalog (ни в реестре, ни в отладочных стендах)";
                return false;
            }
            if (canLoad != null && !canLoad(map.sceneName))
            {
                error = $"{SceneNotLoadable}: '{map.sceneName}' нет в списке сборки";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(modeId))
            {
                GameModeRegistry modes = catalog.Modes;
                GameModeData mode = modes != null ? modes.GetById(modeId) : null;
                if (mode == null || mode == modes.Warmup)
                {
                    error = $"{ModeIncompatible}: '{modeId}' — не режим матча каталога";
                    return false;
                }
                if (!MapModeRules.IsCompatible(map, mode))
                {
                    error = $"{ModeIncompatible}: '{modeId}' не поддерживается картой '{map.sceneName}'";
                    return false;
                }
            }
            canonicalScene = map.sceneName;
            error = null;
            return true;
        }

        /// <summary>
        /// <c>GameNetworkManager.ServerChangeScene</c>: первая смена сцены сервера при запросе грузит цель вместо
        /// <c>onlineScene</c>. Любая другая смена — без изменений.
        /// </summary>
        internal static string ResolveServerScene(string requested, string onlineScene)
        {
            if (_current == null || _current.State != StartupRouteState.Requested || requested != onlineScene)
                return requested;

            _current.State = StartupRouteState.SceneConsumed;
            GameLog.Network.Info($"[ServerStartupRoute] Запрос {_current.Id}: первая сцена сервера '{_current.Scene}' " +
                                 $"вместо '{onlineScene}'.");
            string target = _current.Scene;
            TryComplete();
            return target;
        }

        /// <summary><c>GameNetworkManager.OnStartServer</c> после спавна SessionContext: у хоста — захват режима.</summary>
        internal static void OnServerStarted() => TryComplete();

        /// <summary><c>GameNetworkManager.OnStopServer</c>: сервер остановлен до захвата режима.</summary>
        internal static void OnServerStopped()
        {
            if (_current == null || _current.State != StartupRouteState.SceneConsumed) return;
            GameLog.Network.Warning($"[ServerStartupRoute] Запрос {_current.Id}: сервер остановлен до захвата режима " +
                                    $"'{_current.ModeId}' — запрос снят.");
            _current = null;
        }

        /// <summary>Сцена выбрана и (если нужен режим) серия есть — захватить режим и завершить запрос.</summary>
        private static void TryComplete()
        {
            if (_current == null || _current.State != StartupRouteState.SceneConsumed) return;
            if (_current.ModeId.Length > 0)
            {
                Series series = Series.Instance;
                if (series == null) return; // хост: Series появится в OnStartServer после загрузки сцены
                series.ServerBeginStartup(_current.Scene, _current.ModeId);
            }
            GameLog.Network.Info($"[ServerStartupRoute] Запрос {_current.Id} исполнен.");
            _current = null;
        }
    }
}
