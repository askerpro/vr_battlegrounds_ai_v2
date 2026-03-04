namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Режим 2: раунд заканчивается при полном уничтожении одной команды.
    /// После гибели игрок не возрождается до следующего раунда.
    /// </summary>
    public class EliminationMode : GameMode
    {
        public override void OnRoundEnd()
        {
            // TODO: логика завершения раунда
        }

        public override bool CanRespawn() => false;

        public override Team CheckWinCondition()
        {
            // TODO: условие победы (ликвидация всей команды)
            return Team.None;
        }
    }
}