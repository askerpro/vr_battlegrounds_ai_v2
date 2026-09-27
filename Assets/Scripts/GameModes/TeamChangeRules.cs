namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Кто и когда вправе сменить команду. Чистые правила без побочных эффектов —
    /// исполняет смену <see cref="SessionTeamAssigner"/>, входы — у <see cref="MatchTeams"/>.
    ///
    /// <para>
    /// <b>Игрок</b> выбирает сам только команду активного режима и только пока выбор
    /// открыт (<see cref="GameMode.TeamChoiceLocked"/> — до старта матча). Почему до старта:
    /// после него сменить сторону значит выйти из раунда посреди боя, составы команд уже
    /// разыграны (сеты, смена сторон), а перебежчик ломает баланс. Скин внутри своей
    /// команды игрок меняет всегда — и в разминке тоже, где его команда матча (CT/T)
    /// не входит в команды режима. <b>Админ</b> выдаёт любую команду из
    /// <c>TeamRegistry</c> в любой момент — он и разбирается с опоздавшими
    /// (право — <c>Player.SessionPermissions.IsAdmin</c>).
    /// </para>
    /// </summary>
    public static class TeamChangeRules
    {
        public static bool CanPlayerChoose(GameMode mode, int currentTeamIndex, int newTeamIndex, out string reason)
        {
            reason = null;

            // Режима нет (смена сцены, сцена без режима) — правил нет.
            if (mode == null) return true;

            // Своя команда — это смена скина: разрешена всегда. Раньше проверка «команда
            // из режима» шла первой, и в разминке игрок с командой матча не мог сменить скин.
            if (newTeamIndex == currentTeamIndex && currentTeamIndex != 0) return true;

            if (mode.Teams.Length > 0 &&
                System.Array.FindIndex(mode.Teams, t => t != null && t.teamIndex == newTeamIndex) < 0)
            {
                reason = $"команды {newTeamIndex} нет в режиме {mode.GetType().Name}";
                return false;
            }

            if (mode.TeamChoiceLocked)
            {
                reason = "матч уже начался — сменить команду может только админ";
                return false;
            }

            return true;
        }
    }
}
