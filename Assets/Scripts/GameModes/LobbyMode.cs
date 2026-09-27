using VrBattlegrounds.Core;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Лобби как игровой режим: одна команда «Лобби», одна зона спавна, матча нет.
    ///
    /// <para>
    /// <b>Зачем режим, а не правило сцены.</b> Раньше лобби описывал компонент сцены
    /// <c>LobbyFreePlay</c>: в лобби не было ни <c>GameplayManager</c>, ни режима, и всё,
    /// что на картах решает режим (стена арсенала, оружие, команды, спавн), в лобби
    /// приходилось дублировать особым случаем. Теперь лобби — частный случай режима:
    /// системы спрашивают активный режим одинаково на любой сцене, и любая будущая
    /// правка режимов достаётся лобби сама.
    /// </para>
    ///
    /// <para>
    /// <b>Откуда берётся.</b> Не из выбора администратора: <c>SessionManager.SelectedGameModeData</c> —
    /// это режим <i>следующего матча</i>. Лобби-режим задан самой сценой — полем
    /// «режим сцены» у <c>GameplayManager</c> в <c>Lobby.unity</c>, — и стартует без
    /// администратора. В реестре режимов матча (<c>GameModeRegistry</c>) его нет.
    /// </para>
    ///
    /// <para>
    /// <b>Правила.</b> Сам режим только объявляет их (оружие стреляет, арсенал открыт,
    /// жетона нет, пропавшее оружие заменяется). Исполняют их стена и
    /// <c>GameplayManager</c>; то, что специфично для лобби и не касается других систем, —
    /// компоненты на префабе режима, как у Elimination: <see cref="LobbyMagazineSupply"/>
    /// (бесконечный карман) и <c>LooseItemSweeper</c> с возвратом оружия на стену.
    /// </para>
    ///
    /// <para>
    /// Команда одна, поэтому планшет не предлагает её выбор — только скин
    /// (в команде «Лобби» все аватары). Раздаёт команду базовый режим той же политикой,
    /// что и на картах: из одной команды автобаланс выбирает её же.
    /// </para>
    /// </summary>
    public class LobbyMode : GameMode
    {
        /// <summary>Смерти в лобби нет — возрождать некого.</summary>
        public override bool CanRespawn() => false;

        public override bool WeaponsEnabled => true;

        /// <summary>
        /// В лобби урон по игрокам не проходит: смерть в лобби невозможна. Стрельба по
        /// мишеням и предметам работает как обычно — отменяется только урон по актору игрока.
        /// </summary>
        public override bool PlayersTakeDamage => false;

        public override ArsenalRules ArsenalRules =>
            new ArsenalRules(isOpen: true, usesReadinessTag: false, replacesLostWeapons: true);

        protected override bool CanStartGameplay() => true;

        protected override void StartGameplay()
        {
            GameLog.Match.Info("[LobbyMode] Лобби: свободная игра, арсенал открыт, команда одна.");
        }

        public override void StopGameplay()
        {
            GameLog.Match.Info("[LobbyMode] Лобби-режим остановлен.");
        }
    }
}
