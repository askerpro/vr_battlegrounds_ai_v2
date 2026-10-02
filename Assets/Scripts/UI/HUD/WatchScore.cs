using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Счёт активного режима глазами игрока: «свои : чужие» (<see cref="GameMode.GetScore"/> — раунды
    /// в Elimination, фраги в Respawn). Общий для статуса часов и нотификации итогов раунда.
    /// </summary>
    public static class WatchScore
    {
        /// <summary>
        /// <c>false</c> — режима или двух команд нет. Игрок без команды режима видит счёт в порядке
        /// команд режима.
        /// </summary>
        public static bool TryGet(GameMode mode, int localTeam, out int own, out int enemy)
        {
            own = 0;
            enemy = 0;
            if (mode == null || mode.Teams == null || mode.Teams.Length < 2) return false;

            TeamData mine = null;
            foreach (TeamData team in mode.Teams)
                if (team != null && team.teamIndex == localTeam) mine = team;
            if (mine == null) mine = mode.Teams[0];
            if (mine == null) return false;

            own = mode.GetScore(mine);
            foreach (TeamData team in mode.Teams)
                if (team != null && team != mine && mode.GetScore(team) > enemy) enemy = mode.GetScore(team);
            return true;
        }
    }
}
