using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>Клиент → сервер: «Матч с ботами» (кнопка на «Обзоре» планшета).</summary>
    public struct BotMatchRequestMessage : NetworkMessage
    {
    }

    /// <summary>
    /// Сетевой вход «Матча с ботами» (T-48): запрос с планшета и его серверная обработка. Регистрирует
    /// <c>GameNetworkManager</c> в <c>OnStartServer</c> — Mirror чистит обработчики при каждой остановке сети.
    /// Решает сервер (<see cref="BotMatchStarter.ServerRequest"/>); ответа нет — ответ игроку это начавшийся матч,
    /// отказ виден в логе сервера (кнопку планшет и так показывает только тому, кому можно).
    /// </summary>
    public static class BotMatchNetwork
    {
        public static void RegisterServerHandlers()
        {
            NetworkServer.ReplaceHandler<BotMatchRequestMessage>(HandleRequest);
        }

        // ── Клиент ──────────────────────────────────────────────────────────

        /// <summary>Отправить запрос серверу. false — нет подключения.</summary>
        public static bool Request()
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
            {
                GameLog.Match.Warning("[BotMatch] Запрос не отправлен: нет подключения к серверу.");
                return false;
            }

            NetworkClient.Send(new BotMatchRequestMessage());
            GameLog.Match.Info("[BotMatch] Запрос серверу: матч с ботами.");
            return true;
        }

        /// <summary>
        /// Можно ли этой машине предложить кнопку: админка показана (<paramref name="adminUi"/>) или своя сессия —
        /// единственный человек среди сессий, которые видит клиент (боты узнаются по реплицированному токену).
        /// </summary>
        public static bool LocalCanRequest(bool adminUi)
        {
            PlayerSession local = PlayerSession.LocalSession;
            if (local == null) return false;

            int others = BotMatchStarter.CountHumans(
                Object.FindObjectsByType<PlayerSession>(FindObjectsSortMode.None), local);
            return BotMatchPlan.CanRequest(adminUi, local.Role == GameRole.Player, others);
        }

        // ── Сервер ──────────────────────────────────────────────────────────

        private static void HandleRequest(NetworkConnectionToClient conn, BotMatchRequestMessage msg)
        {
            PlayerSession session = conn != null && conn.identity != null ? conn.identity.GetComponent<PlayerSession>() : null;
            if (session == null) return;

            BotMatchStarter.ServerRequest(session, out string result);
            GameLog.Match.Info($"[BotMatch] {session.PlayerName}: матч с ботами — {result}.");
        }
    }
}
