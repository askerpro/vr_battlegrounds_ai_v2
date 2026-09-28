using System;
using System.Collections.Generic;

namespace VrBattlegrounds.DevTools
{
    /// <summary>Что сервер делает с запросом прав админа по режиму отладки.</summary>
    public enum DebugAdminDecision
    {
        /// <summary>Выдать права (<c>PlayerSession.IsAdmin = true</c>) и запомнить, что выдал режим.</summary>
        Grant,
        /// <summary>Снять права, выданные режимом отладки.</summary>
        Revoke,
        /// <summary>Сессия и так админ (хост): ничего не менять.</summary>
        AlreadyAdmin,
        /// <summary>Сервер не разрешает режим отладки: права не выдаются.</summary>
        Rejected,
        /// <summary>Выключение у сессии, которой режим прав не выдавал: не трогать.</summary>
        Nothing,
    }

    /// <summary>
    /// Серверное правило режима отладки: когда устройство с включённым режимом получает права
    /// админа (кнопки «Матч», «Игроки и команды», «Играть», телепорт).
    ///
    /// <para>
    /// Права выдаёт <b>только сервер</b>, и только если он сам разрешает отладку: сборка
    /// Development (в том числе редактор — <c>Debug.isDebugBuild</c>) или флаг командной строки
    /// <see cref="CommandLineFlag"/>. Прод-сервер без флага отказывает, и жест режима отладки
    /// на любом шлеме админом не делает. Снимается только то, что выдал режим: админ по другой
    /// причине (хост) выключением режима прав не теряет.
    /// </para>
    /// </summary>
    public static class DebugAdminPolicy
    {
        /// <summary>Флаг запуска прод-сервера, разрешающий режим отладки: <c>VrBattlegroundsServer.exe -vrb-debug-admin</c>.</summary>
        public const string CommandLineFlag = "-vrb-debug-admin";

        /// <summary>Разрешает ли этот сервер режим отладки.</summary>
        public static bool ServerAllows(bool isDebugBuild, IReadOnlyList<string> commandLineArgs)
        {
            if (isDebugBuild) return true;
            if (commandLineArgs == null) return false;

            foreach (string arg in commandLineArgs)
            {
                if (string.Equals(arg, CommandLineFlag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <param name="enable">Режим на устройстве включён.</param>
        /// <param name="isAdmin">Сессия сейчас админ (<c>SessionPermissions.IsAdmin</c>).</param>
        /// <param name="grantedByDebug">Права этой сессии выдал режим отладки.</param>
        /// <param name="serverAllows"><see cref="ServerAllows"/>.</param>
        public static DebugAdminDecision Decide(bool enable, bool isAdmin, bool grantedByDebug, bool serverAllows)
        {
            if (!enable) return grantedByDebug ? DebugAdminDecision.Revoke : DebugAdminDecision.Nothing;
            if (isAdmin) return DebugAdminDecision.AlreadyAdmin;
            return serverAllows ? DebugAdminDecision.Grant : DebugAdminDecision.Rejected;
        }
    }
}
