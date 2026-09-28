using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Какое время показывать игроку для активного режима. Общий источник для
    /// <see cref="HUDWidget_RoundTimer"/> и <see cref="WristDisplay"/>, чтобы табло не расходились.
    /// </summary>
    public static class RoundClock
    {
        /// <summary>
        /// Остаток текущей фазы в секундах. <c>false</c> — у режима нет таймера (или режима нет).
        /// Elimination: в обратном отсчёте и паузах — их остаток, в закупке с пределом — остаток
        /// закупки, иначе — время раунда. Respawn — остаток матча.
        /// </summary>
        public static bool TryGetTimeRemaining(GameMode mode, out float seconds, out RoundState? state)
        {
            seconds = 0f;
            state = null;

            switch (mode)
            {
                case EliminationMode elimination:
                    state = elimination.CurrentRoundState;
                    seconds = SelectEliminationTime(elimination.CurrentRoundState,
                        elimination.CountdownTimeRemaining,
                        elimination.EquipmentTimeRemaining,
                        elimination.RoundTimeRemaining);
                    return true;

                case RespawnMode respawn:
                    seconds = respawn.TimeRemaining;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// В закупке показываем, сколько её осталось; без предела ожидания остатка нет —
        /// тогда полное время раунда.
        /// </summary>
        public static float SelectEliminationTime(RoundState state, float countdown, float equipment, float round)
        {
            if (state == RoundState.Countdown) return countdown;
            if (state == RoundState.Equipment && equipment > 0f) return equipment;
            return round;
        }
    }
}
