namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Корень оружия, который сам играет отдачу через <see cref="HapticService" /> (контракт <c>haptics-api</c>, ревизия 2).
    /// Если <see cref="OwnsRecoilHaptics" /> = true, <see cref="SdkHapticRouter" /> отбрасывает вибрацию выстрела UltimateXR
    /// (типы Shot/ShotBig/ShotBigger) на руке, которая держит это оружие, — иначе отдача прозвучала бы дважды.
    /// Значение постоянно для экземпляра (из данных ствола) и во время игры не переключается. Реализует weapon-system.
    /// </summary>
    public interface IWeaponRecoilHapticsOwner
    {
        bool OwnsRecoilHaptics { get; }
    }
}
