using VrBattlegrounds;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим 1: возрождение при физическом возврате на точку спавна.
    /// Раунд не имеет ограничения по жизням.
    ///
    /// Команды, участвующие в режиме, задаются через поле <c>teams</c> в Inspector.
    /// </summary>
    public class RespawnMode : GameMode
    {
        public override void OnRoundEnd()
        {
            // TODO: логика завершения раунда
        }

        public override bool CanRespawn() => true;

        public override TeamData CheckWinCondition()
        {
            // TODO: условие победы (по времени или по счёту)
            return null;
        }
    }
}