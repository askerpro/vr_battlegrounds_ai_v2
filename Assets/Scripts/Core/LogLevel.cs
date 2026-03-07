namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Уровни логирования для системы логов игры.
    /// Уровни иерархические: каждый следующий включает предыдущий.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>Логи отключены.</summary>
        None = 0,

        /// <summary>Только ошибки.</summary>
        Errors = 1,

        /// <summary>Ошибки и предупреждения.</summary>
        Warnings = 2,

        /// <summary>Ошибки, предупреждения и важные события (старт матча, подключение игрока и т.д.).</summary>
        Info = 3,

        /// <summary>Всё вышеперечисленное плюс детальный поток событий.</summary>
        Verbose = 4,
    }
}
