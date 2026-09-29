using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Ставит игроку команду и скин. Сервер. Единственный исполнитель смены команды —
    /// для выбора игроком, выдачи админом и политики режима; кто вправе менять, решают
    /// вызывающие (<see cref="TeamChangeRules"/>), здесь только «как».
    ///
    /// <para>
    /// Путей два, и выбирает их наличие аватара, а не режим:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Аватара нет</b> — игрок только подключается (<c>PlayersManager</c> зовёт
    ///       подписчиков до <c>SpawnAvatar</c>) или сцена только что сменилась (аватар
    ///       пересоздаст <c>GameNetworkManager.OnServerReady</c>). Достаточно записать
    ///       команду в сессию: спавн сам возьмёт её зону и скин.</item>
    /// <item><b>Аватар жив</b> — <c>AvatarManager.ChangeAvatar</c>: пересоздание аватара
    ///       под скин команды. Откалиброванного игрока спавн не двигает.</item>
    /// </list>
    ///
    /// <para>
    /// Перед сменой поднимается <see cref="MatchTeams.TeamChangeRequested"/> — хуки
    /// режима (сброс статистики и т.п.).
    /// </para>
    /// </summary>
    public static class SessionTeamAssigner
    {
        /// <summary>Команда со скином по выбору: игрок выбрал его сам в планшете.</summary>
        public static void Apply(PlayerSession session, TeamData team, int avatarIndex, string reason)
        {
            if (session == null || team == null) return;

            GameLog.Match.Info(
                $"[{reason}] {session.PlayerName}: команда {session.TeamIndex} → {team.Name}, скин {avatarIndex}.");

            MatchTeams.NotifyTeamChangeRequested(session, team.teamIndex, avatarIndex);

            if (session.ActiveAvatar != null && AvatarManager.Instance != null)
            {
                AvatarManager.Instance.ChangeAvatar(session.connectionToClient, session, team.teamIndex, avatarIndex);
                return;
            }

            session.TeamIndex = team.teamIndex;
            session.AvatarIndex = avatarIndex;
        }

        /// <summary>
        /// Команда с сохранением скина: если текущий скин есть в новой команде
        /// (<see cref="TeamData.IndexOfAvatar"/>) — он, иначе первый. Политика режима и админ.
        /// </summary>
        public static void Apply(PlayerSession session, TeamData team, string reason)
        {
            if (session == null || team == null) return;

            TeamData oldTeam = session.TeamIndex != 0 && TeamRegistry.Instance != null
                ? TeamRegistry.Instance.GetByIndex(session.TeamIndex)
                : null;
            AvatarData currentSkin = oldTeam != null ? oldTeam.GetAvatar(session.AvatarIndex) : null;

            Apply(session, team, team.IndexOfAvatar(currentSkin), reason);
        }

        /// <summary>
        /// Снимает команду <b>без пересоздания аватара</b>: сцена вот-вот сменится (конец серии),
        /// и аватар всё равно будет создан заново — уже без команды, киборгом.
        /// </summary>
        public static void ClearBeforeSceneChange(PlayerSession session, string reason)
        {
            if (session == null || session.TeamIndex == 0) return;

            GameLog.Match.Info($"[{reason}] {session.PlayerName}: команда {session.TeamIndex} снята (до смены сцены).");

            MatchTeams.NotifyTeamChangeRequested(session, 0, 0);

            session.TeamIndex = 0;
            session.AvatarIndex = 0;
        }
    }
}
