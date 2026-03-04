namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим 1: возрождение при физическом возврате на точку спавна.
    /// Раунд не имеет ограничения по жизням.
    /// </summary>
    public class RespawnMode : GameMode
    {
        public override void OnRoundEnd()
        {
            // TODO: логика завершения раунда
        }

        public override bool CanRespawn() => true;

        public override Team CheckWinCondition()
        {
            // TODO: условие победы (по времени или по счёту)
            return Team.None;
        }
    }
}