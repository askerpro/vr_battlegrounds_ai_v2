using System.Collections.Generic;
using System.Linq;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Узкий доступ логики матча к списку игроков.
    ///
    /// Зачем интерфейс. Машина состояний раунда — обычный C#-класс, но вопрос
    /// «все ли готовы» она задавала синглтону <see cref="PlayersManager"/>, которого
    /// в EditMode-тесте нет. Из-за этого фазы раунда нельзя было прогнать юнит-тестом,
    /// и ошибки MATCH-01/MATCH-02 годами жили незамеченными.
    ///
    /// В игре реализация одна — <see cref="PlayersManagerRoster"/>. В тестах подставляется
    /// заглушка, и живые аватары становятся не нужны.
    /// </summary>
    public interface IPlayerRoster
    {
        /// <summary>Все подключённые сессии команды — и живые, и мёртвые.</summary>
        IEnumerable<PlayerSession> GetPlayers(TeamData team);

        /// <summary>Живые сессии команды: аватар заспавнен и не мёртв.</summary>
        IEnumerable<PlayerSession> GetAlivePlayers(TeamData team);
    }

    /// <summary>
    /// Боевая реализация <see cref="IPlayerRoster"/> поверх <see cref="PlayersManager"/>.
    ///
    /// Отсутствие менеджера — не ошибка, а нормальное состояние: сцена, открытая без сети,
    /// и любой тест логики матча живут без него. Поэтому вместо падения — пустой список.
    /// </summary>
    public sealed class PlayersManagerRoster : IPlayerRoster
    {
        public IEnumerable<PlayerSession> GetPlayers(TeamData team)
        {
            PlayersManager manager = PlayersManager.Instance;
            if (manager == null) return Enumerable.Empty<PlayerSession>();

            return manager.GetPlayers(team);
        }

        public IEnumerable<PlayerSession> GetAlivePlayers(TeamData team)
        {
            PlayersManager manager = PlayersManager.Instance;
            if (manager == null) return Enumerable.Empty<PlayerSession>();

            return manager.GetAlivePlayers(team);
        }
    }
}
