namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Что идёт в навигационную сетку ботов (<see cref="BotNavMesh"/>) — чистое правило, тесты <c>BotRouteTests</c>.
    /// Только неподвижная геометрия карты: твёрдый включённый коллайдер на слое окружения, без <c>Rigidbody</c>
    /// (подвижное), не на аватаре и не на хватаемом предмете (оружие, магазины, планшет). Триггеры — зоны спавна,
    /// карманы, лазерная сетка — не препятствия.
    /// </summary>
    public static class BotNavMeshSources
    {
        public static bool Include(bool enabled, bool isTrigger, bool onEnvironmentLayer, bool hasRigidbody, bool onAvatarOrItem) =>
            enabled && !isTrigger && onEnvironmentLayer && !hasRigidbody && !onAvatarOrItem;
    }
}
