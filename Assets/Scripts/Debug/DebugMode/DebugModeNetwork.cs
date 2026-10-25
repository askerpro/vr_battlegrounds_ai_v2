using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Сетевая часть режима отладки: регистрация сообщений, серверная обработка (права админа,
    /// телепорт) и запросы с клиента. Регистрирует <c>GameNetworkManager</c> в
    /// <c>OnStartServer</c> / <c>OnStartClient</c> — Mirror чистит обработчики при каждой
    /// остановке сети. Сообщения, а не <c>[Command]</c> на <see cref="PlayerSession"/>: сетевой
    /// каркас сессии ради отладки не меняется (как и у стресс-теста).
    ///
    /// <para>
    /// <b>Власть — у сервера.</b> Права админа выдаёт он по <see cref="DebugAdminPolicy"/>,
    /// телепорт выполняет только для админа (<see cref="SessionPermissions.IsAdmin"/>: хост или
    /// права, выданные режимом) <b>с включённым режимом отладки на его устройстве</b> (последний
    /// <see cref="DebugAdminRequestMessage"/>), только собственного аватара отправителя, точку ищет сам.
    /// </para>
    /// </summary>
    public static class DebugModeNetwork
    {
        /// <summary>Сервер: netId сессий, которым права админа выдал режим отладки.</summary>
        private static readonly HashSet<uint> Granted = new HashSet<uint>();

        /// <summary>Сервер: netId сессий, чьё устройство сообщило о включённом режиме отладки.</summary>
        private static readonly HashSet<uint> DebugEnabled = new HashSet<uint>();

        /// <summary>Клиент: последний ответ сервера — строка статуса на планшете.</summary>
        public static string LastReply { get; private set; } = string.Empty;

        /// <summary>Клиент: пришёл ответ сервера.</summary>
        public static event Action<DebugReplyMessage> ReplyReceived;

        public static void RegisterServerHandlers()
        {
            Granted.Clear();
            DebugEnabled.Clear();
            NetworkServer.ReplaceHandler<DebugAdminRequestMessage>(HandleAdminRequest);
            NetworkServer.ReplaceHandler<DebugTeleportRequestMessage>(HandleTeleportRequest);
        }

        public static void RegisterClientHandlers()
        {
            LastReply = string.Empty;
            NetworkClient.ReplaceHandler<DebugReplyMessage>(HandleReply);
        }

        // ── Клиент ──────────────────────────────────────────────────────────

        public static bool RequestAdmin(bool enable)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready) return false;
            NetworkClient.Send(new DebugAdminRequestMessage { enable = enable });
            GameLog.Debug.Info($"[DebugMode] Запрос серверу: {(enable ? "выдать" : "снять")} права режима отладки.");
            return true;
        }

        public static bool RequestTeleport(string targetId, out string reason)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
            {
                reason = "нет подключения к серверу";
                return false;
            }

            NetworkClient.Send(new DebugTeleportRequestMessage { targetId = targetId });
            GameLog.Debug.Info($"[DebugMode] Запрос телепорта к '{targetId}'.");
            reason = null;
            return true;
        }

        private static void HandleReply(DebugReplyMessage msg)
        {
            LastReply = msg.text ?? string.Empty;
            if (!msg.ok) GameLog.Debug.Warning($"[DebugMode] Сервер отказал: {msg.text}.");
            PerfOverlay.Show(msg.text, msg.ok ? 3f : 6f);
            ReplyReceived?.Invoke(msg);
        }

        // ── Сервер ──────────────────────────────────────────────────────────

        private static void HandleAdminRequest(NetworkConnectionToClient conn, DebugAdminRequestMessage msg)
        {
            PlayerSession session = SessionOf(conn);
            if (session == null) return;

            if (msg.enable) DebugEnabled.Add(session.netId);
            else DebugEnabled.Remove(session.netId);

            bool allows = DebugAdminPolicy.ServerAllows(Debug.isDebugBuild, Environment.GetCommandLineArgs());
            DebugAdminDecision decision = DebugAdminPolicy.Decide(msg.enable, SessionPermissions.IsAdmin(session), Granted.Contains(session.netId), allows);

            switch (decision)
            {
                case DebugAdminDecision.Grant:
                    session.IsAdmin = true;
                    Granted.Add(session.netId);
                    GameLog.Debug.Warning($"[DebugMode] {session.PlayerName} (conn {conn.connectionId}): права админа выданы режимом отладки.");
                    Reply(conn, true, "Режим отладки: права админа выданы.");
                    break;

                case DebugAdminDecision.Revoke:
                    session.IsAdmin = false;
                    Granted.Remove(session.netId);
                    GameLog.Debug.Info($"[DebugMode] {session.PlayerName}: права режима отладки сняты.");
                    Reply(conn, true, "Режим отладки: права админа сняты.");
                    break;

                case DebugAdminDecision.AlreadyAdmin:
                    Reply(conn, true, "Режим отладки: это устройство уже админ.");
                    break;

                case DebugAdminDecision.Rejected:
                    GameLog.Debug.Warning($"[DebugMode] {session.PlayerName} (conn {conn.connectionId}): отказ в правах — сервер не Development и без {DebugAdminPolicy.CommandLineFlag}.");
                    Reply(conn, false, $"Сервер не разрешает отладку (не Development, без {DebugAdminPolicy.CommandLineFlag}): админ-пункты недоступны.");
                    break;
            }
        }

        private static void HandleTeleportRequest(NetworkConnectionToClient conn, DebugTeleportRequestMessage msg)
        {
            PlayerSession session = SessionOf(conn);
            if (session == null)
            {
                GameLog.Debug.Warning($"[DebugMode] conn={(conn != null ? conn.connectionId.ToString() : "нет")}: " +
                                      $"телепорт к '{msg.targetId}' отклонён — у соединения нет сессии.");
                return;
            }

            if (!SessionPermissions.IsAdmin(session))
            {
                GameLog.Debug.Warning($"[DebugMode] {session.PlayerName}: телепорт без прав админа отклонён.");
                Reply(conn, false, "Телепорт: нет прав админа.");
                return;
            }

            // Права админа телепорт не открывают: нужен ещё режим отладки на устройстве.
            if (!DebugEnabled.Contains(session.netId))
            {
                GameLog.Debug.Warning($"[DebugMode] {session.PlayerName}: телепорт без режима отладки на устройстве отклонён.");
                Reply(conn, false, "Телепорт: режим отладки на устройстве выключен.");
                return;
            }

            PlayerController avatar = session.ActiveAvatar;
            if (avatar == null)
            {
                GameLog.Debug.Warning($"[DebugMode] {PlayerLogName.Of(session)}: телепорт к '{msg.targetId}' отклонён — у игрока нет аватара.");
                Reply(conn, false, "Телепорт: у игрока нет аватара.");
                return;
            }

            if (!DebugTeleportTargets.TryResolve(msg.targetId, out DebugTeleportTarget target))
            {
                GameLog.Debug.Warning($"[DebugMode] {PlayerLogName.Of(session)}: телепорт отклонён — точки '{msg.targetId}' " +
                                      $"на карте сервера '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' нет.");
                Reply(conn, false, $"Телепорт: точки '{msg.targetId}' на карте сервера нет.");
                return;
            }

            // Корень «после» — цель: переносит владелец аватара (RPC → MoveAvatarTo), сервер видит
            // итог позже через NetworkTransform. Голова «до» — чтобы сверить её с корнем после переноса.
            string headBefore = PlayerBodyCenter.TryGetHead(avatar, out Vector3 head) ? head.ToString("F2") : "нет";
            Vector3 rootBefore = avatar.transform.position;
            GameLog.Debug.Info($"[DebugMode] {PlayerLogName.Of(session)}: телепорт к «{target.Label}» — " +
                               $"корень до={rootBefore:F2} → после={target.Position:F2} " +
                               $"(Δy={(target.Position.y - rootBefore.y).ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture)}), " +
                               $"голова до={headBefore}.");
            avatar.ServerDevTeleport(target.Position, target.Rotation);
            Reply(conn, true, "Телепорт: " + target.Label + ".");
        }

        private static PlayerSession SessionOf(NetworkConnectionToClient conn) =>
            conn != null && conn.identity != null ? conn.identity.GetComponent<PlayerSession>() : null;

        private static void Reply(NetworkConnectionToClient conn, bool ok, string text) =>
            conn.Send(new DebugReplyMessage { ok = ok, text = text });
    }
}
