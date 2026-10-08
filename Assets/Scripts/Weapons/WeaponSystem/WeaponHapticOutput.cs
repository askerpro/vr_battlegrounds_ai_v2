using UltimateXR.Avatar;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственная точка, где оружие трогает мотор вибрации (контракт haptics-api, ревизия 2). Клип берётся из данных
    /// ствола (<see cref="WeaponHapticSet"/>), рука — от исполнителя.
    ///
    /// <para>
    /// <b>Временно</b>, пока сервис <c>VrBattlegrounds.Haptics.HapticService</c> не влит в dev: клип уходит напрямую в
    /// <c>UxrControllerInput.SendHapticFeedback(side, clip)</c> и только локальной руке. Когда сервис вольют, тело
    /// <see cref="Play"/> заменяется одной строкой <c>HapticService.Play(clip, hand, HapticHandRole.Primary, 1f)</c>;
    /// вызывающий код не меняется. Приоритет, пауза и кулдаун клипа появятся с SDK-патчем 66 и сервисом.
    /// </para>
    /// </summary>
    internal static class WeaponHapticOutput
    {
        public static bool Play(UxrHapticClip clip, UxrGrabber hand)
        {
            if (!WeaponHapticSet.Has(clip) || hand == null || hand.Avatar == null ||
                hand.Avatar.AvatarMode != UxrAvatarMode.Local || hand.Avatar.ControllerInput == null) return false;
            hand.Avatar.ControllerInput.SendHapticFeedback(hand.Side, clip);
            return true;
        }
    }
}
