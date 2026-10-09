using UltimateXR.Manipulation;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Ограничения манипуляции предметами: что игроку запрещено делать руками и для каких предметов.
    /// Здесь только решение «можно ли»; механику (какая рука держит какую точку) проверяют правила хвата
    /// (<see cref="TwoHandGrabPolicy" />), подключённые через <see cref="GrabRules" />.
    /// </summary>
    public static class ManipulationConstraints
    {
        /// <summary>
        /// Может ли вторая рука игрока забрать предмет у первой (перехват из руки в руку) для предметов,
        /// на которые распространяется ограничение (<see cref="AllowsHandTransfer" />). Выключено решением
        /// пользователя 2026-10-09: рука, тянувшаяся к магазину в кармане, перехватывала оружие. Включить —
        /// если игрокам не хватит быстрой перекладки из руки в руку.
        /// </summary>
        public static bool AllowHandTransfer = false;

        /// <summary>
        /// Можно ли перехватить <paramref name="grabbable" /> своей второй рукой. Ограничение сейчас
        /// распространяется на огнестрел; остальные предметы перехватываются как раньше.
        /// </summary>
        public static bool AllowsHandTransfer(UxrGrabbableObject grabbable)
        {
            return AllowHandTransfer || !GrabbableHierarchyCache.IsFirearm(grabbable);
        }
    }
}
