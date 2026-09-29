using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Managers
{
    /// <summary>Кнопки админа на карте.</summary>
    public enum MatchCommand
    {
        /// <summary>«Начать матч»: разминка → режим матча на месте.</summary>
        StartMatch = 0,
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
    public static class AdminMatchCommands
    {
        /// <summary>
        /// Имеет ли кнопка смысл сейчас: «Начать матч» — на карте с режимом матча, когда матча
        /// нет; «Пауза» — во время матча, если режим её умеет; «Продолжить» — на паузе;
        /// «Стоп» — пока идёт серия; «Следующая карта» — в разминке идущей серии (во время матча
        /// и на паузе переход оборвал бы игру на карте).
        /// </summary>
        public static bool IsAvailable(MatchCommand command, MapState state, bool modeSupportsPause,
                                       bool mapHasMatchModes, bool seriesRunning)
        {
            switch (command)
            {
                case MatchCommand.StartMatch: return state == MapState.Warmup && mapHasMatchModes;
                case MatchCommand.Pause: return state == MapState.Live && modeSupportsPause;
                case MatchCommand.Resume: return state == MapState.Paused;
                case MatchCommand.Stop: return seriesRunning;
                case MatchCommand.NextMap: return state == MapState.Warmup && seriesRunning;
                default: return false;
            }
        }

        /// <summary>То же правило по живым объектам этой машины.</summary>
        public static bool IsAvailable(MatchCommand command)
        {
            MapReferee manager = MapReferee.Instance;
            Series series = Series.Instance;

            if (manager == null) return command == MatchCommand.Stop && series != null && series.IsRunning;

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
                GameLog.Match.Warning($"[AdminMatchCommands] Старт серии отклонён: {(admin != null ? admin.PlayerName : "null")} не админ.");
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
                GameLog.Match.Warning($"[AdminMatchCommands] Старт серии отклонён: нет совместимых карт для '{modeId}'.");
                return false;
            }

            session.SetSeries(modeId, valid);
            session.StartSession();
            return true;
        }

        /// <summary>Исполнение на сервере. Зовёт <c>PlayerSession.CmdAdminMatchCommand</c>.</summary>
        /// <returns>true — команда исполнена.</returns>
        public static bool ServerExecute(PlayerSession admin, MatchCommand command)
        {
            if (!SessionPermissions.IsAdmin(admin))
            {
                GameLog.Match.Warning($"[AdminMatchCommands] {command} отклонена: {(admin != null ? admin.PlayerName : "null")} не админ.");
                return false;
            }

            if (!IsAvailable(command))
            {
                GameLog.Match.Warning($"[AdminMatchCommands] {command} сейчас не имеет смысла — отклонена.");
                return false;
            }

            GameLog.Match.Info($"[AdminMatchCommands] {admin.PlayerName}: {command}.");

            MapReferee manager = MapReferee.Instance;
            switch (command)
            {
                case MatchCommand.StartMatch: return manager != null && manager.StartMatch();
                case MatchCommand.Pause: return manager != null && manager.PauseMatch();
                case MatchCommand.Resume: return manager != null && manager.ResumeMatch();
                case MatchCommand.Stop:
                    Series.Instance.ServerEnd();
                    return true;
                case MatchCommand.NextMap:
                    return Series.Instance.ServerAdvance() != null;
                default: return false;
            }
        }
    }
}
