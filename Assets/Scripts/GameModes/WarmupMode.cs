using VrBattlegrounds.Core;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// «Разминка» — не режим матча, а состояние карты «режим матча не запущен или на паузе»:
    /// арсенал открыт, оружие стреляет, бесконечный карман, урона по игрокам нет, смерти нет.
    ///
    /// <para>
    /// <b>Почему всё же режим.</b> Правила разминки — ответы на те же вопросы, что задают
    /// любому режиму (стрелять ли, открыт ли арсенал, рисовать ли зоны, можно ли сменить
    /// команду). Стена, оружие, HUD и планшет спрашивают <c>MapReferee.ActiveGameMode</c> и
    /// не знают, какой он. Отличается разминка жизненным циклом, и его ведёт
    /// <c>MapReferee</c>: её не выбирают (в каталоге режимов и в списках карт её нет —
    /// <see cref="GameModeRegistry.warmup"/>), у неё нет команд и победителя, она
    /// включается сама — на старте карты, на паузе и после конца матча.
    /// </para>
    ///
    /// <para>
    /// <b>Команды.</b> Своих команд у разминки нет, и она никого не переназначает: игрок
    /// с командой (Военные/Повстанцы) её сохраняет, игрок без команды остаётся без неё
    /// (аватар — киборг) и выбирает команду в планшете. Общий счёт серии и статистику
    /// разминка тоже не трогает.
    /// </para>
    ///
    /// <para>
    /// <b>Правила.</b> Сам режим только объявляет их. Исполняют их стена и
    /// <c>MapReferee</c>; то, что специфично для разминки, — компоненты на префабе
    /// режима, как у Elimination: <see cref="WarmupMagazineSupply"/> (бесконечный карман)
    /// и <c>LooseItemSweeper</c> с возвратом оружия на стену.
    /// </para>
    /// </summary>
    public class WarmupMode : GameMode
    {
        public override bool IsWarmup => true;

        /// <summary>Смерти в разминке нет — возрождать некого.</summary>
        public override bool CanRespawn() => false;

        public override bool WeaponsEnabled => true;

        /// <summary>
        /// В разминке урон по игрокам не проходит: смерть невозможна. Стрельба по
        /// мишеням и предметам работает как обычно — отменяется только урон по актору игрока.
        /// </summary>
        public override bool PlayersTakeDamage => false;

        public override ArsenalRules ArsenalRules =>
            new ArsenalRules(isOpen: true, usesReadinessTag: false, replacesLostWeapons: true);

        protected override bool CanBegin() => true;

        protected override void Begin()
        {
            GameLog.Match.Info("[WarmupMode] Разминка: арсенал открыт, урона по игрокам нет.");
        }

        public override void ForceStop()
        {
            GameLog.Match.Info("[WarmupMode] Разминка остановлена.");
        }
    }
}
