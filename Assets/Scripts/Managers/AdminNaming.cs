using System.Collections.Generic;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>
    /// Админ задаёт названия команд и ники игроков (с планшета, экран «Игроки и команды»).
    /// Сервер. Право — <see cref="SessionPermissions.IsAdmin"/>, ввод чистит <see cref="TeamNames.Sanitize"/>.
    ///
    /// <para>
    /// <b>Ник запоминается по устройству</b> (<c>DeviceToken</c>) на время жизни сервера: игрок,
    /// переподключившийся без снимка сессии, получает свой ник обратно, а не случайный <c>Player_1234</c>.
    /// </para>
    /// </summary>
    public static class AdminNaming
    {
        private static readonly Dictionary<string, string> Nicknames = new Dictionary<string, string>();

        /// <summary>Ник, который админ дал этому устройству, или null.</summary>
        public static string NicknameFor(string deviceToken) =>
            !string.IsNullOrEmpty(deviceToken) && Nicknames.TryGetValue(deviceToken, out string name) ? name : null;

        /// <summary>Забыть все ники (тесты, остановка сервера).</summary>
        public static void ClearNicknames() => Nicknames.Clear();

        /// <returns>true — название задано (пустое — сброшено к имени ассета).</returns>
        public static bool ServerRenameTeam(PlayerSession admin, int teamIndex, string raw)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[AdminNaming] Переименование команды отклонено: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            TeamData team = TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamIndex) : null;
            if (team == null || TeamNameService.Instance == null)
            {
                GameLog.Match.Warning($"[AdminNaming] Переименование отклонено: команды {teamIndex} или сервиса названий нет.");
                return false;
            }

            string name = TeamNames.Sanitize(raw);
            TeamNameService.Instance.ServerSetName(teamIndex, name);
            GameLog.Match.Info($"[AdminNaming] {admin.PlayerName}: команда {teamIndex} ('{team.displayName}') → '{team.Name}'.");
            return true;
        }

        /// <returns>true — ник задан.</returns>
        public static bool ServerRenamePlayer(PlayerSession admin, PlayerSession target, string raw)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[AdminNaming] Смена ника отклонена: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            string name = TeamNames.Sanitize(raw);
            if (target == null || string.IsNullOrEmpty(name))
            {
                GameLog.Match.Warning("[AdminNaming] Смена ника отклонена: нет игрока или пустой ник.");
                return false;
            }

            string old = target.PlayerName;
            target.PlayerName = name;
            if (target.ActiveAvatar != null) target.ActiveAvatar.AvatarPlayerName = name;
            if (!string.IsNullOrEmpty(target.DeviceToken)) Nicknames[target.DeviceToken] = name;

            GameLog.Match.Info($"[AdminNaming] {admin.PlayerName}: ник '{old}' → '{name}'.");
            return true;
        }
    }
}
