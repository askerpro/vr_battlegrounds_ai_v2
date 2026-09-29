using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Совместимость режимов с картой — чистые правила без сети и синглтонов
    /// (проверяет <c>MapModeRulesTests</c>). Список совместимых режимов — у карты
    /// (<see cref="MapData.supportedModes"/>); здесь только выбор из него.
    ///
    /// <list type="bullet">
    /// <item>Любая карта стартует в разминке — её включает <c>MapReferee</c> сам
    ///       (<see cref="GameModeRegistry.Warmup"/>); в списки карт она не входит.</item>
    /// <item>«Начать матч» берёт выбор администратора, если он совместим с картой, иначе
    ///       первый совместимый режим матча, а если такого нет (лобби — пустой список) —
    ///       ничего (<see cref="ResolveMatchMode"/>).</item>
    /// </list>
    ///
    /// Сцена не из реестра (<c>map == null</c>, тестовая сцена) — совместим любой режим
    /// матча. У карты реестра пустой список значит «режимов матча нет».
    /// </summary>
    public static class MapModeRules
    {
        /// <summary>Совместим ли режим с картой.</summary>
        public static bool IsCompatible(MapData map, GameModeData mode)
        {
            if (mode == null) return false;
            if (map == null) return true;
            if (map.supportedModes == null) return false;

            foreach (GameModeData supported in map.supportedModes)
                if (supported != null && supported.modeId == mode.modeId) return true;

            return false;
        }

        /// <summary>
        /// Какой режим матча запустить на карте: <paramref name="selected"/>, если это режим
        /// матча и он совместим с картой; иначе первый совместимый режим матча; иначе null.
        /// </summary>
        public static GameModeData ResolveMatchMode(MapData map, GameModeData selected, GameModeRegistry registry)
        {
            GameModeData warmup = registry != null ? registry.Warmup : null;

            if (selected != null && selected != warmup && IsCompatible(map, selected))
                return selected;

            if (map != null)
            {
                if (map.supportedModes == null) return null;
                foreach (GameModeData mode in map.supportedModes)
                    if (mode != null && mode != warmup) return mode;
                return null;
            }

            if (registry != null)
            {
                foreach (GameModeData mode in registry.MatchModes) return mode;
            }

            return null;
        }
    }
}
