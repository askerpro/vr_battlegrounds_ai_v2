using Mirror;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Права сессии на сервере. Раньше проверка жила в правилах смены команды режима
    /// (<c>TeamChangeRules.IsAdmin</c>), хотя право админа — свойство сессии, а не режима:
    /// им пользуются и команды, и управление матчем.
    /// </summary>
    public static class SessionPermissions
    {
        /// <summary>
        /// Вправе ли сессия действовать как админ. Админ — хост (его соединение локальное,
        /// см. «Роли пользователей» в gameplay.md) или сессия с флагом <c>IsAdmin</c>.
        /// Серверная проверка: <c>connectionToClient</c> есть только на сервере.
        /// </summary>
        public static bool IsAdmin(PlayerSession session)
        {
            if (session == null) return false;
            if (session.IsAdmin) return true;
            return session.connectionToClient is LocalConnectionToClient;
        }
    }
}
