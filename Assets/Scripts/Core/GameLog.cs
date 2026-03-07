using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Статический хелпер для логирования с проверкой уровня через GameSettings.
    ///
    /// Использование:
    ///   GameLog.Info(GameSettings.Instance.LogLevelNetwork,   "[MapManager] Загрузка карты...");
    ///   GameLog.Warning(GameSettings.Instance.LogLevelPlayer, "[PlayerController] Предупреждение");
    ///   GameLog.Error(                                        "[GameNetworkManager] Критическая ошибка");
    ///
    /// Ошибки (Error) логируются всегда — независимо от уровня.
    /// </summary>
    public static class GameLog
    {
        /// <summary>
        /// Пишет Info-лог, если уровень категории >= LogLevel.Info.
        /// </summary>
        public static void Info(LogLevel categoryLevel, string message, Object context = null)
        {
            if (categoryLevel < LogLevel.Info)
                return;

            if (context != null)
                Debug.Log(message, context);
            else
                Debug.Log(message);
        }

        /// <summary>
        /// Пишет Verbose-лог, если уровень категории >= LogLevel.Verbose.
        /// </summary>
        public static void Verbose(LogLevel categoryLevel, string message, Object context = null)
        {
            if (categoryLevel < LogLevel.Verbose)
                return;

            if (context != null)
                Debug.Log(message, context);
            else
                Debug.Log(message);
        }

        /// <summary>
        /// Пишет Warning-лог, если уровень категории >= LogLevel.Warnings.
        /// </summary>
        public static void Warning(LogLevel categoryLevel, string message, Object context = null)
        {
            if (categoryLevel < LogLevel.Warnings)
                return;

            if (context != null)
                Debug.LogWarning(message, context);
            else
                Debug.LogWarning(message);
        }

        /// <summary>
        /// Пишет Error-лог всегда — независимо от уровня категории.
        /// </summary>
        public static void Error(string message, Object context = null)
        {
            if (context != null)
                Debug.LogError(message, context);
            else
                Debug.LogError(message);
        }
    }
}
