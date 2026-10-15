namespace VrBattlegrounds.Bots
{
    /// <summary>Настройки бота при добавлении в <see cref="BotDirector"/>.</summary>
    public sealed class BotAddOptions
    {
        /// <summary>
        /// Директор сам выбирает, оплачивает и предлагает оружие. По умолчанию включено для матча;
        /// стенд отключает, когда управляет снаряжением сам. Значение копируется при добавлении бота.
        /// </summary>
        public bool AutoEquip { get; set; } = true;
    }
}
