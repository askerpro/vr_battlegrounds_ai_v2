namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Теги главной категории объекта. Тег отвечает на вопрос «что это в первую очередь»,
    /// поэтому категории взаимоисключающие: граната — <see cref="Weapon" />, а то, что она
    /// ещё метательная и берётся в руку, видно по её компонентам.
    ///
    /// <para>
    /// Кто какой тег получает, решает <see cref="GameTagRules" />; расставляет —
    /// <c>Tools/VR Battlegrounds/Gameplay/Apply Game Tags</c>. Руками теги не ставятся:
    /// <c>GameTagsTests</c> сверяет их с компонентами и падает на расхождении.
    /// </para>
    ///
    /// <para>
    /// Тег висит на корне сущности, а коллайдер обычно на дочернем объекте. Поэтому при
    /// попадании нужен <c>hit.rigidbody.CompareTag</c> или подъём к корню, а не
    /// <c>hit.collider.CompareTag</c>. Исключение — <see cref="Environment" />: он стоит
    /// прямо на коллайдерах геометрии.
    /// </para>
    /// </summary>
    public static class GameTags
    {
        /// <summary>Встроенный тег Unity. Корень аватара — и игрока, и аватара меню.</summary>
        public const string Player = "Player";

        /// <summary>Оружие, включая гранату.</summary>
        public const string Weapon = "Weapon";

        /// <summary>Магазин, в том числе встроенный в префаб оружия.</summary>
        public const string Magazine = "Magazine";

        /// <summary>Зона спавна команды.</summary>
        public const string SpawnZone = "SpawnZone";

        /// <summary>Стена арсенала и её слоты.</summary>
        public const string Arsenal = "Arsenal";

        /// <summary>Неподвижная геометрия с коллайдером: пол, стены, препятствия.</summary>
        public const string Environment = "Environment";

        /// <summary>Все теги игры. Только их инструмент разметки имеет право ставить и снимать.</summary>
        public static readonly string[] All = { Player, Weapon, Magazine, SpawnZone, Arsenal, Environment };

        /// <summary>Теги, которые нужно завести в TagManager (<see cref="Player" /> встроенный).</summary>
        public static readonly string[] Custom = { Weapon, Magazine, SpawnZone, Arsenal, Environment };

        public static bool IsGameTag(string tag) => System.Array.IndexOf(All, tag) >= 0;
    }
}
