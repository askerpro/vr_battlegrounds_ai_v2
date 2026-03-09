namespace UltimateXR.Mechanics.Weapons
{
    public abstract partial class UxrWeapon
    {
        /// <summary>
        ///     Gets whether the weapon can be used. By default it is true if the owner is not dead and the global check
        ///     in <see cref="UxrWeaponManager" /> allows it.
        /// </summary>
        public virtual bool CanUse
        {
            get
            {
                if (Owner != null && Owner.IsDead)
                {
                    return false;
                }

                if (UxrWeaponManager.HasInstance)
                {
                    if (!UxrWeaponManager.Instance.WeaponSystemEnabled)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
