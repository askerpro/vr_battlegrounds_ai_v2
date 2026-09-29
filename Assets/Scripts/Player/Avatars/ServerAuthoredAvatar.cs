using Mirror;
using UnityEngine;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Аватар без владельца — тело, которое двигает сам сервер: бот, кукла стресс-теста.
    ///
    /// <para>
    /// <b>Зачем переключать NetworkTransform.</b> У аватара игрока все
    /// <c>NetworkTransform</c> (корень, камера, кисти) работают в <c>ClientToServer</c>:
    /// позу присылает шлем владельца. У объекта без владельца Mirror в этом режиме позу
    /// не рассылает вовсе (<c>NetworkTransformUnreliable.UpdateServerBroadcast</c>), и
    /// у клиентов тело стоит замороженным. <c>ServerToClient</c> — поза уходит от сервера.
    /// </para>
    ///
    /// <para>
    /// Звать до <c>NetworkServer.Spawn</c>: направление синхронизации читается при спавне.
    /// </para>
    /// </summary>
    public static class ServerAuthoredAvatar
    {
        /// <summary>Переводит все <c>NetworkTransform</c> тела на рассылку с сервера.</summary>
        public static void Prepare(GameObject avatar)
        {
            if (avatar == null) return;

            foreach (NetworkTransformBase nt in avatar.GetComponentsInChildren<NetworkTransformBase>(true))
            {
                nt.syncDirection = SyncDirection.ServerToClient;
            }
        }

        /// <summary>Метка владельца для имени объекта в иерархии.</summary>
        public static string OwnerLabel(NetworkConnectionToClient conn)
        {
            return conn != null ? $"connId={conn.connectionId}" : "без владельца";
        }
    }
}
