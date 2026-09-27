using VrBattlegrounds.GameModes;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Совместимость режимов с картой — чистые правила без сети и синглтонов
    /// (проверяет <c>MapModeRulesTests</c>). Список совместимых режимов — у карты
    /// (<see cref="MapData.supportedModes"/>); здесь только выбор из него.
    ///
    /// <list type="bullet">
    /// <item>Любая карта стартует в разминке (<see cref="ResolveWarmup"/>).</item>
    /// <item>«Начать матч» берёт выбор администратора, если он совместим с картой, иначе
    ///       первый совместимый режим матча, а если такого нет (лобби) — ничего
    ///       (<see cref="ResolveMatchMode"/>).</item>
    /// </list>
    ///
    /// Карта не из реестра (тестовая сцена) или карта с пустым списком — совместим любой
    /// режим реестра: так вёл себя выбор карт в меню и до этого.
    /// </summary>
    public static class MapModeRules
    {
        /// <summary>Совместим ли режим с картой.</summary>
        public static bool IsCompatible(MapData map, GameModeData mode)
        {
            if (mode == null) return false;
            if (map == null || map.supportedModes == null || map.supportedModes.Length == 0) return true;

            foreach (GameModeData supported in map.supportedModes)
                if (supported != null && supported.modeId == mode.modeId) return true;

            return false;
        }

        /// <summary>Разминка карты: первая разминка из её списка, иначе разминка реестра.</summary>
        public static GameModeData ResolveWarmup(MapData map, GameModeRegistry registry)
        {
            if (map != null && map.supportedModes != null)
            {
                foreach (GameModeData mode in map.supportedModes)
                    if (mode != null && mode.isWarmup) return mode;
            }

            return registry != null ? registry.Warmup : null;
        }

        /// <summary>
        /// Какой режим матча запустить на карте: <paramref name="selected"/>, если это режим
        /// матча и он совместим с картой; иначе первый совместимый режим матча; иначе null.
        /// </summary>
        public static GameModeData ResolveMatchMode(MapData map, GameModeData selected, GameModeRegistry registry)
        {
            if (selected != null && !selected.isWarmup && IsCompatible(map, selected))
                return selected;

            if (map != null && map.supportedModes != null && map.supportedModes.Length > 0)
            {
                foreach (GameModeData mode in map.supportedModes)
                    if (mode != null && !mode.isWarmup) return mode;
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
