namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     VR Battlegrounds patch 72: read-only источник владельца equipment claim.
    ///     true означает активный override, даже если owner=null; false сохраняет legacy SDK owner.
    /// </summary>
    public interface IUxrWeaponOwnerProvider
    {
        bool TryGetOwner(out UxrActor owner);
    }
}
