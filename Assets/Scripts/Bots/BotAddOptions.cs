using VrBattlegrounds.DevTools.E2E.Input;

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

        /// <summary>
        /// Источник ввода устройства (контракт tasks/bots-input/contracts/bot-input-design.md): бот управляется кадром
        /// шлема и контроллеров, как обычный игрок, — тело получает только <see cref="BotInputKit"/>, директор
        /// решений не принимает. null — прежний комплект player-flow (Blaze). Один источник на бота на все его тела.
        /// </summary>
        public IAvatarInputSource InputSource { get; set; }

        /// <summary>
        /// Прежнее тело player-flow (BotAvatarDriver, BotHands, BotRuntimeController) вместо ввода устройства с мозгом Blaze.
        /// Только для замеров стенда прежнего пути до его удаления (bot-input итерация 2); по умолчанию — ввод.
        /// </summary>
        public bool LegacyBody { get; set; }
    }
}
