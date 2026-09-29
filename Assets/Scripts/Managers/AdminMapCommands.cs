using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>Кнопки админа на карте.</summary>
    public enum MapCommand
    {
        /// <summary>«Начать матч»: разминка → режим матча на месте.</summary>
        GoLive = 0,
        /// <summary>«Пауза»: раунд прерывается без победителя, карта — в разминку.</summary>
        Pause = 1,
        /// <summary>«Продолжить»: матч с прерванного раунда.</summary>
        Resume = 2,
        /// <summary>«Стоп»: конец всей серии, возврат в лобби.</summary>
        Stop = 3,
        /// <summary>
        /// «Следующая карта»: серия переходит к следующей карте, после последней — в лобби.
        /// Сама серия после конца карты дальше не идёт: карта стоит в разминке до этой кнопки.
        /// </summary>
        NextMap = 4
    }

    /// <summary>
    /// Кнопки админа на карте: когда какая имеет смысл и что делает на сервере.
    ///
    /// <para>
    /// Правило видимости — чистая функция (<see cref="IsAvailable"/>): её зовёт экран
    /// <c>MenuMatchManager</c> на любой машине по реплицированному состоянию, а сервер —
    /// ещё раз перед исполнением: команда с клиента, которую экран не показал бы, не
    /// исполняется. Право админа — <see cref="SessionPermissions.IsAdmin"/>.
    /// </para>
    /// </summary>
    public static class AdminMapCommands
    {
        /// <summary>
        /// Имеет ли кнопка смысл сейчас: «Начать матч» — на карте с режимом матча, когда матча
        /// нет; «Пауза» — во время матча, если режим её умеет; «Продолжить» — на паузе;
        /// «Стоп» — пока идёт серия; «Следующая карта» — в разминке идущей серии (во время матча
        /// и на паузе переход оборвал бы игру на карте).
        /// </summary>
        public static bool IsAvailable(MapCommand command, MapState state, bool modeSupportsPause,
                                       bool mapHasMatchModes, bool seriesRunning)
        {
            switch (command)
            {
                case MapCommand.GoLive: return state == MapState.Warmup && mapHasMatchModes;
                case MapCommand.Pause: return state == MapState.Live && modeSupportsPause;
                case MapCommand.Resume: return state == MapState.Paused;
                case MapCommand.Stop: return seriesRunning;
                case MapCommand.NextMap: return state == MapState.Warmup && seriesRunning;
                default: return false;
            }
        }

        /// <summary>То же правило по живым объектам этой машины.</summary>
        public static bool IsAvailable(MapCommand command)
        {
            MapReferee manager = MapReferee.Instance;
            Series series = Series.Instance;

            if (manager == null) return command == MapCommand.Stop && series != null && series.IsRunning;

            bool supportsPause = manager.ActiveGameMode != null && manager.ActiveGameMode.SupportsPause;
            bool hasMatch = manager.CurrentMap == null ||
                            Maps.MapModeRules.ResolveMatchMode(manager.CurrentMap, null, null) != null;

            return IsAvailable(command, manager.CurrentState, supportsPause, hasMatch, series != null && series.IsRunning);
        }

        /// <summary>
        /// «Начать» в меню выбора сессии: серия из очереди карт. Зовёт
        /// <c>PlayerSession.CmdAdminStartSeries</c>. Карты, несовместимые с режимом или
        /// отсутствующие в реестре, отбрасываются.
        /// </summary>
        public static bool ServerStartSeries(PlayerSession admin, string modeId, string[] maps)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[AdminMapCommands] Старт серии отклонён: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            SessionManager session = SessionManager.Instance;
            if (session == null || maps == null || maps.Length == 0) return false;

            GameModes.GameModeData mode = session.FindModeData(modeId);
            var valid = new System.Collections.Generic.List<string>();
            foreach (string scene in maps)
            {
                Maps.MapData map = session.FindMap(scene);
                if (map != null && mode != null && mode != session.ModeRegistry?.Warmup &&
                    Maps.MapModeRules.IsCompatible(map, mode) && !valid.Contains(scene))
                    valid.Add(scene);
            }

            if (valid.Count == 0)
            {
                GameLog.Match.Warning($"[AdminMapCommands] Старт серии отклонён: нет совместимых карт для '{modeId}'.");
                return false;
            }

            session.SetSeries(modeId, valid);
            session.StartSession();
            return true;
        }

        /// <summary>Исполнение на сервере. Зовёт <c>PlayerSession.CmdAdminMapCommand</c>.</summary>
        /// <returns>true — команда исполнена.</returns>
        public static bool ServerExecute(PlayerSession admin, MapCommand command)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[AdminMapCommands] {command} отклонена: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            if (!IsAvailable(command))
            {
                GameLog.Match.Warning($"[AdminMapCommands] {command} сейчас не имеет смысла — отклонена.");
                return false;
            }

            GameLog.Match.Info($"[AdminMapCommands] {admin.PlayerName}: {command}.");

            MapReferee manager = MapReferee.Instance;
            switch (command)
            {
                case MapCommand.GoLive: return manager != null && manager.GoLive();
                case MapCommand.Pause: return manager != null && manager.Pause();
                case MapCommand.Resume: return manager != null && manager.Resume();
                case MapCommand.Stop:
                    Series.Instance.ServerEnd();
                    return true;
                case MapCommand.NextMap:
                    return Series.Instance.ServerAdvance() != null;
                default: return false;
            }
        }
    }
}
