using System;
using System.Collections.Generic;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Серверные входы смены команды: выбор игроком, выдача админом, разовый автобаланс.
    /// Правила «кто и когда» — <see cref="TeamChangeRules"/> и
    /// <see cref="SessionPermissions"/>, исполнение — <see cref="SessionTeamAssigner"/>.
    ///
    /// <para>
    /// Раньше всё это жило в <c>MapReferee</c>, который и так отвечает за жизнь
    /// режима на карте. Команды к жизни режима отношения не имеют — им нужен только
    /// активный режим, и он приходит параметром. Поэтому сервис статический, без
    /// объекта в сцене: входы из <c>PlayerSession</c> передают
    /// <c>MapReferee.Instance.ActiveGameMode</c>, тесты — свой режим.
    /// </para>
    /// </summary>
    public static class MatchTeams
    {
        /// <summary>Серверное событие: игроку меняют команду/скин (хуки режима — сброс статистики и т.п.).</summary>
        public static event Action<PlayerSession, int, int> TeamChangeRequested;

        /// <summary>Поднимает <see cref="TeamChangeRequested"/>. Зовёт исполнитель смены.</summary>
        internal static void NotifyTeamChangeRequested(PlayerSession session, int teamId, int avatarId)
        {
            TeamChangeRequested?.Invoke(session, teamId, avatarId);
        }

        /// <summary>
        /// Игрок сам выбрал команду и скин в планшете (<c>PlayerSession.CmdRequestTeamChange</c>).
        /// Разрешено только в команду активного режима и только пока выбор открыт
        /// (<see cref="GameMode.TeamChoiceLocked"/>); скин в своей команде — всегда.
        /// </summary>
        /// <returns>true — смена применена.</returns>
        public static bool ServerPlayerRequest(GameMode mode, PlayerSession session, int newTeamId, int newAvatarId)
        {
            if (session == null) return false;

            GameLog.Match.Info(
                $"[MatchTeams] Игрок {session.PlayerName} запросил смену: Команда {newTeamId}, Скин {newAvatarId}");

            if (!TeamChangeRules.CanPlayerChoose(mode, session.TeamIndex, newTeamId, out string reason))
            {
                GameLog.Match.Warning($"[MatchTeams] Смена отклонена ({session.PlayerName}): {reason}.");
                return false;
            }

            TeamData team = FindTeam(mode, newTeamId);
            if (team == null)
            {
                GameLog.Match.Warning($"[MatchTeams] Смена отклонена ({session.PlayerName}): команды {newTeamId} нет в реестре.");
                return false;
            }

            SessionTeamAssigner.Apply(session, team, newAvatarId, "MatchTeams/выбор игрока");
            return true;
        }

        /// <summary>
        /// Админ выдаёт игроку команду: всегда и в любую команду из <c>TeamRegistry</c>
        /// (например, опоздавшего — в команду матча). Скин сохраняется, если он есть в новой команде.
        /// </summary>
        /// <returns>true — команда выдана.</returns>
        public static bool ServerAdminAssign(GameMode mode, PlayerSession admin, PlayerSession target, int teamId)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[MatchTeams] Выдача команды отклонена: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            TeamData team = FindTeam(mode, teamId);
            if (target == null || team == null)
            {
                GameLog.Match.Warning($"[MatchTeams] Выдача команды отклонена: нет игрока или команды {teamId}.");
                return false;
            }

            SessionTeamAssigner.Apply(target, team, $"MatchTeams/админ {admin.PlayerName}");
            return true;
        }

        /// <summary>
        /// Админ разово раскладывает игроков без команды режима автобалансом
        /// (<see cref="TeamAutoBalance.Plan{TPlayer}"/>). Политику режима не меняет.
        /// </summary>
        /// <returns>Сколько игроков получили команду.</returns>
        public static int ServerAdminAutoBalance(GameMode mode, PlayerSession admin)
        {
            if (!SessionPermissions.IsAdmin(admin) || mode == null) return 0;

            var players = new List<PlayerSession>();
            foreach (PlayerSession session in mode.PlayerRoster.GetAllPlayers())
            {
                if (session != null && session.Role == GameRole.Player) players.Add(session);
            }

            var plan = TeamAutoBalance.Plan(mode.Teams, players, s => s.TeamIndex);
            foreach (var pair in plan)
                SessionTeamAssigner.Apply(pair.Key, pair.Value, $"MatchTeams/автобаланс админа {admin.PlayerName}");

            return plan.Count;
        }

        /// <summary>Команда по индексу: сначала из активного режима, затем из реестра.</summary>
        private static TeamData FindTeam(GameMode mode, int teamId)
        {
            if (mode != null)
            {
                foreach (TeamData t in mode.Teams)
                    if (t != null && t.teamIndex == teamId) return t;
            }

            return teamId != 0 && TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamId) : null;
        }
    }
}
