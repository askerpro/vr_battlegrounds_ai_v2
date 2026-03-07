using System;
using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Единственная ответственность: управление сетевыми подключениями.
    /// Логика команд и старт матча — в подписчиках событий (GameplayManager, DebugOrchestrator).
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        /// <summary>Игрок подключился и спавнился на сервере.</summary>
        public static event Action<PlayerController> PlayerConnected;

        /// <summary>Игрок отключился от сервера.</summary>
        public static event Action<PlayerController> PlayerDisconnected;

        /// <summary>Сервер завершил загрузку сцены. Параметр — имя загруженной сцены.</summary>
        public static event Action<string> ServerSceneChanged;

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            ServerSceneChanged?.Invoke(sceneName);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);

            PlayerController player = conn.identity.GetComponent<PlayerController>();
            if (player == null)
                return;

            PlayerConnected?.Invoke(player);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn.identity != null)
            {
                PlayerController player = conn.identity.GetComponent<PlayerController>();
                if (player != null)
                {
                    PlayerDisconnected?.Invoke(player);
                }
            }

            base.OnServerDisconnect(conn);
        }
    }
}
