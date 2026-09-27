using Mirror;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Кто и когда вправе сменить команду. Чистые правила без побочных эффектов —
    /// исполняет смену <see cref="SessionTeamAssigner"/>, входы — у <c>GameplayManager</c>.
    ///
    /// <para>
    /// <b>Игрок</b> выбирает сам только команду активного режима и только пока выбор
    /// открыт (<see cref="GameMode.TeamChoiceLocked"/> — до старта матча). Почему до старта:
    /// после него сменить сторону значит выйти из раунда посреди боя, составы команд уже
    /// разыграны (сеты, смена сторон), а перебежчик ломает баланс. Скин внутри своей
    /// команды игрок меняет всегда. <b>Админ</b> выдаёт любую команду из
    /// <c>TeamRegistry</c> в любой момент — он и разбирается с опоздавшими.
    /// </para>
    /// </summary>
    public static class TeamChangeRules
    {
        public static bool CanPlayerChoose(GameMode mode, int currentTeamIndex, int newTeamIndex, out string reason)
        {
            reason = null;

            // Режима нет (карта до старта, сцена без режима) — правил нет, как раньше.
            if (mode == null) return true;

            if (mode.Teams.Length > 0 &&
                System.Array.FindIndex(mode.Teams, t => t != null && t.teamIndex == newTeamIndex) < 0)
            {
                reason = $"команды {newTeamIndex} нет в режиме {mode.GetType().Name}";
                return false;
            }

            if (mode.TeamChoiceLocked && newTeamIndex != currentTeamIndex)
            {
                reason = "матч уже начался — сменить команду может только админ";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Вправе ли сессия действовать как админ. Админ — хост (его соединение локальное,
        /// см. «Роли пользователей» в gameplay.md) или сессия с флагом <c>IsAdmin</c>.
        /// </summary>
        public static bool IsAdmin(PlayerSession session)
        {
            if (session == null) return false;
            if (session.IsAdmin) return true;
            return session.connectionToClient is LocalConnectionToClient;
        }
    }
}
