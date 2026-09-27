using VrBattlegrounds.Core;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// «Разминка» — переходная стадия между матчами: арсенал открыт, оружие стреляет,
    /// бесконечный карман, урона по игрокам нет, смерти нет.
    ///
    /// <para>
    /// <b>Где идёт.</b> С разминки стартует любая карта — и лобби, и боевая
    /// (<c>GameplayManager.OnStartServer</c>, режим — первая разминка из
    /// <c>MapData.supportedModes</c>). У лобби других режимов нет; на боевой карте
    /// админ жмёт «Начать матч», и режим меняется на месте, без перезагрузки сцены.
    /// Матч на карте кончился — снова разминка, затем следующая карта серии.
    /// Раньше это был <c>LobbyMode</c>, заданный полем «режим сцены» только лобби.
    /// </para>
    ///
    /// <para>
    /// <b>Команды.</b> Разминка не сбрасывает ни команды, ни общий счёт серии, ни статистику:
    /// игрок с командой матча (CT/T) её сохраняет, игрок без команды получает единственную
    /// команду режима «Разминка» (<see cref="TeamAssignmentKind.KeepOrDefault"/> в данных).
    /// Команда одна, поэтому планшет не предлагает её выбор — только скин.
    /// </para>
    ///
    /// <para>
    /// <b>Правила.</b> Сам режим только объявляет их. Исполняют их стена и
    /// <c>GameplayManager</c>; то, что специфично для разминки, — компоненты на префабе
    /// режима, как у Elimination: <see cref="WarmupMagazineSupply"/> (бесконечный карман)
    /// и <c>LooseItemSweeper</c> с возвратом оружия на стену.
    /// </para>
    /// </summary>
    public class WarmupMode : GameMode
    {
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

        protected override bool CanStartGameplay() => true;

        protected override void StartGameplay()
        {
            GameLog.Match.Info("[WarmupMode] Разминка: арсенал открыт, урона по игрокам нет.");
        }

        public override void StopGameplay()
        {
            GameLog.Match.Info("[WarmupMode] Разминка остановлена.");
        }
    }
}
