using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>Клиент → сервер: добавить одного бота (<c>add</c>) или убрать всех.</summary>
    public struct BotRequestMessage : NetworkMessage
    {
        public bool add;
    }

    /// <summary>
    /// Сетевой вход ботов: запрос с клиента (планшет, экран «Отладка»; меню редактора-клиента)
    /// и его серверная обработка. Регистрирует <c>GameNetworkManager</c> в <c>OnStartServer</c> —
    /// Mirror чистит обработчики при каждой остановке сети. Ответ — общий
    /// <see cref="DebugReplyMessage"/> режима отладки: строка статуса и табличка на шлеме.
    ///
    /// <para>
    /// <b>Решает сервер.</b> Ботов добавляет только админ (<see cref="SessionPermissions.IsAdmin"/>) и только
    /// на сервере, разрешающем отладку (<see cref="DebugAdminPolicy.ServerAllows"/>: Development или
    /// <c>-vrb-debug-admin</c>) — на прод-сервере без флага ботов не будет.
    /// </para>
    /// </summary>
    public static class BotNetwork
    {
        public static void RegisterServerHandlers()
        {
            NetworkServer.ReplaceHandler<BotRequestMessage>(HandleRequest);
        }

        // ── Клиент ──────────────────────────────────────────────────────────

        public static bool Request(bool add, out string reason)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
            {
                reason = "нет подключения к серверу";
                return false;
            }

            NetworkClient.Send(new BotRequestMessage { add = add });
            GameLog.Debug.Info($"[Bots] Запрос серверу: {(add ? "добавить бота" : "убрать всех ботов")}.");
            reason = null;
            return true;
        }

        /// <summary>
        /// Сколько ботов видит клиент: по <c>DeviceToken</c> сессий, он реплицируется.
        /// Список ботов директора есть только у сервера.
        /// </summary>
        public static int CountVisibleBots()
        {
            int count = 0;
            foreach (PlayerSession session in UnityEngine.Object.FindObjectsByType<PlayerSession>(UnityEngine.FindObjectsSortMode.None))
            {
                if (session != null && BotDirector.IsBotToken(session.DeviceToken)) count++;
            }
            return count;
        }

        // ── Сервер ──────────────────────────────────────────────────────────

        private static void HandleRequest(NetworkConnectionToClient conn, BotRequestMessage msg)
        {
            PlayerSession session = conn != null && conn.identity != null ? conn.identity.GetComponent<PlayerSession>() : null;
            if (session == null) return;

            if (!DebugAdminPolicy.ServerAllows(UnityEngine.Debug.isDebugBuild, System.Environment.GetCommandLineArgs()))
            {
                Reply(conn, false, $"Боты: сервер не разрешает отладку (не Development, без {DebugAdminPolicy.CommandLineFlag}).");
                return;
            }

            if (!SessionPermissions.IsAdmin(session))
            {
                GameLog.Debug.Warning($"[Bots] {session.PlayerName}: запрос ботов без прав админа отклонён.");
                Reply(conn, false, "Боты: нет прав админа.");
                return;
            }

            if (!msg.add)
            {
                int removed = BotDirector.Instance != null ? BotDirector.Instance.Bots.Count : 0;
                BotDirector.Instance?.RemoveAll();
                Reply(conn, true, $"Боты: убрано {removed}.");
                return;
            }

            BotDirector director = BotDirector.EnsureInstance();
            PlayerSession bot = director != null ? director.AddBot() : null;

            if (bot == null)
            {
                Reply(conn, false, "Боты: не удалось создать — см. лог сервера.");
                return;
            }

            GameLog.Debug.Info($"[Bots] {session.PlayerName} добавил {bot.PlayerName}.");
            Reply(conn, true, $"Боты: добавлен {bot.PlayerName} (всего {director.Bots.Count}).");
        }

        private static void Reply(NetworkConnectionToClient conn, bool ok, string text) =>
            conn.Send(new DebugReplyMessage { ok = ok, text = text });
    }
}
