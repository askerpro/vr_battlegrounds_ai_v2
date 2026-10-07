using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик окна приёма патрона (<see cref="CartridgeIntake"/>): «патрон предложен» — в окне лежит доступный
    /// патрон нужного типа, последним его положил держатель оружия; барьер приёма SDK поднят/снят.
    /// Сеть и проверку на сервере ведёт <see cref="CartridgeIntake"/>; датчик ничего не отправляет.
    /// </summary>
    internal sealed class WeaponIntakeSensor
    {
        private readonly UxrFirearmWeapon _weapon;
        private readonly CartridgeIntake _intake;
        private readonly int _trigger;
        private ulong _sequence;
        private int _offeredUnit;

        public WeaponIntakeSensor(UxrFirearmWeapon weapon, int trigger)
        {
            _weapon = weapon; _trigger = trigger;
            _intake = weapon.GetComponent<CartridgeIntake>();
            BarrierUp = weapon.IsAmmoAdmissionPending(trigger);
        }

        public bool HasIntake => _intake != null && _intake.Intake != null;

        /// <summary>Барьер приёма по последнему замеру.</summary>
        public bool BarrierUp { get; private set; }

        /// <summary>Жетон нового предложения (у каждого патрона — свой).</summary>
        public ulong NextToken() => ++_sequence;

        /// <summary>Замер: смена барьера и новый предложенный патрон (один раз на патрон, пока он в окне).</summary>
        public void Poll(UxrAvatar mainAvatar, out bool barrierRaised, out bool barrierLowered, out bool offered)
        {
            bool up = _weapon.IsAmmoAdmissionPending(_trigger);
            barrierRaised = up && !BarrierUp;
            barrierLowered = !up && BarrierUp;
            BarrierUp = up;
            offered = false;
            Cartridge cartridge = OfferedCartridge(mainAvatar);
            int unit = cartridge != null ? cartridge.GetInstanceID() : 0;
            if (unit == 0) { _offeredUnit = 0; return; }
            if (unit == _offeredUnit) return;
            _offeredUnit = unit;
            offered = true;
        }

        /// <summary>Барьер поднят старым кодом — предложение текущего патрона считается сделанным.</summary>
        public void MarkOffered(UxrAvatar mainAvatar)
        {
            Cartridge cartridge = OfferedCartridge(mainAvatar);
            if (cartridge != null) _offeredUnit = cartridge.GetInstanceID();
        }

        private Cartridge OfferedCartridge(UxrAvatar mainAvatar)
        {
            if (!HasIntake || mainAvatar == null || _intake.Intake.CurrentPlacedObject == null) return null;
            Cartridge cartridge = _intake.Intake.CurrentPlacedObject.GetComponent<Cartridge>();
            return cartridge != null && cartridge.IsAvailable && cartridge.AmmoType == _intake.AmmoType &&
                   cartridge.LastHandlingAvatar == mainAvatar && cartridge.LastPlacedAnchor == _intake.Intake ? cartridge : null;
        }
    }
}
