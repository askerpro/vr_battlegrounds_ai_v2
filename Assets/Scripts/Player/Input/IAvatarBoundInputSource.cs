// Источник ввода, привязанный к телу (контракт tasks/bots-input/contracts/bot-input-design.md).
using UltimateXR.Avatar;
using VrBattlegrounds.DevTools.E2E.Input;

namespace VrBattlegrounds.Player.Input
{
    /// <summary>
    /// Источник ввода, которому нужно знать тело (пилот): владелец устройства привязывает его к аватару до
    /// <see cref="AvatarInputDevice.Connect"/> — первый кадр устройство применяет сразу при подключении.
    /// </summary>
    public interface IAvatarBoundInputSource : IAvatarInputSource
    {
        /// <summary>
        /// Привязать к телу; <paramref name="keepHead"/> — продолжить с прежней мировой позы головы (новое тело на той же
        /// карте), иначе голова над корнем нового тела.
        /// </summary>
        void Bind(UxrAvatar avatar, bool keepHead);
    }
}
