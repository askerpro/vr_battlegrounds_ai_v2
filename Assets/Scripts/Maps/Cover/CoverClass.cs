namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Класс укрытия для пули (LD-27, <c>Docs/level-design-principles.md</c> §7). Без разметки всё —
    /// <see cref="Hard"/>: неразмеченная стена пулю останавливает, случайного прострела не бывает.
    /// </summary>
    public enum CoverClass
    {
        /// <summary>Толстое: бетон, металл, земля. Не пробивается никогда.</summary>
        Hard,

        /// <summary>Тонкое: дерево, гипс, жесть. Пробивается с потерей урона (<c>WallPenetration</c>).</summary>
        Soft,

        /// <summary>Только закрывает обзор: листва, сетка. Пулю не останавливает и урон не снижает.</summary>
        Visual
    }
}
