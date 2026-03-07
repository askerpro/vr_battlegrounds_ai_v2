using System.Linq;
using VrBattlegrounds;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим "Ликвидация": раунд заканчивается при полном уничтожении одной команды.
    /// После гибели игрок не возрождается до следующего раунда.
    ///
    /// Команды, участвующие в режиме, задаются через поле <c>teams</c> в Inspector.
    /// </summary>
    public class EliminationMode : GameMode
    {
        public override void OnRoundEnd() { }

        public override bool CanRespawn() => false;

        /// <summary>
        /// Победа достаётся команде, у которой ещё остались живые игроки.
        /// Null — раунд продолжается (живы все команды) или ничья (все мертвы).
        /// </summary>
        public override TeamData CheckWinCondition()
        {
            PlayersManager playersManager = PlayersManager.Instance;
            if (playersManager == null)
                return null;

            TeamData lastAlive = null;
            int aliveTeamsCount = 0;

            foreach (TeamData team in teams)
            {
                if (team == null)
                    continue;

                if (playersManager.GetAlivePlayers(team).Any())
                {
                    lastAlive = team;
                    aliveTeamsCount++;
                }
            }

            // Раунд продолжается — живы несколько команд
            if (aliveTeamsCount > 1)
                return null;

            // Ровно одна команда жива — она победила; ноль команд — null (ничья)
            return aliveTeamsCount == 1 ? lastAlive : null;
        }
    }
}
