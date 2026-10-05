using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Снимок одной классифицированной попытки до подготовки; не live копия боезапаса.</summary>
    public readonly struct WeaponTriggerAttemptContext
    {
        public UxrFirearmWeapon Firearm { get; }
        public int TriggerIndex { get; }
        public UxrGrabber MainGrabber { get; }
        public UxrHandSide Side { get; }
        public uint PressSequence { get; }
        public uint Revision { get; }
        public UxrGrabbableObject Magazine { get; }
        public UxrGrabbableObjectAnchor Anchor { get; }
        public int MagazineRounds { get; }
        public bool ChamberRound { get; }
        public WeaponChamberPolicy Policy { get; }
        public UxrFirearmTriggerDecision Decision { get; }

        public WeaponTriggerAttemptContext(UxrFirearmWeapon firearm, UxrFirearmLocalTriggerAttempt attempt)
        {
            Firearm = firearm; TriggerIndex = attempt.TriggerIndex; MainGrabber = attempt.MainGrabber;
            Side = attempt.Side; PressSequence = attempt.PressSequence; Revision = attempt.Revision;
            Magazine = attempt.Magazine; Anchor = attempt.Anchor; MagazineRounds = attempt.MagazineRounds;
            ChamberRound = attempt.ChamberRound; Policy = (WeaponChamberPolicy)attempt.PolicyId; Decision = attempt.Decision;
        }
    }
}
