using UltimateXR.Manipulation;
using VrBattlegrounds.Economy;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Можно ли этой руке снять ствол со стены арсенала (T-45): при экономике матча — только
    /// владельцу стены и только если хватает денег (<see cref="ArsenalPurchaseRules.MayTake"/>).
    /// Предмет не на стене и режим без экономики — правило не мешает.
    ///
    /// <para>
    /// Работает на машине того, кто тянется (<c>GrabRules</c> через <c>UxrGrabber.CanGrabDelegate</c>),
    /// по реплицированным владельцу и деньгам — поэтому чужая стена не подсвечивается под рукой и не
    /// берётся. Это удобство, а не защита: сервер проверяет то же самое сам (<c>ArsenalCheckout</c>).
    /// Общий запрет на дорогой ствол — <c>IsGrabbable</c>, его пишет сервер (<see cref="ArsenalSlotController.ApplyOffer"/>).
    /// </para>
    /// </summary>
    public static class ArsenalGrabRule
    {
        public static bool AllowsGrab(UxrGrabber grabber, UxrGrabbableObject grabbable)
        {
            UxrGrabbableObjectAnchor directAnchor = grabbable != null ? grabbable.CurrentAnchor : null;
            ArsenalMagazineOffer magazineOffer = directAnchor != null ? directAnchor.GetComponentInParent<ArsenalMagazineOffer>() : null;
            if (magazineOffer != null && magazineOffer.Anchor == directAnchor)
                return magazineOffer.AllowsGrab(grabber);

            ArsenalSlotController slot = SlotHolding(grabbable);
            if (slot == null) return true;

            MatchEconomy economy = MatchEconomy.Current;
            if (economy == null) return true;

            ArsenalWallController wall = slot.Wall;
            PlayerSession owner = wall != null ? wall.OwnerSession : null;
            int money = owner != null ? economy.GetMoney(owner) : 0;

            return ArsenalPurchaseRules.MayTake(true, wall != null ? wall.OwnerSessionNetId : 0u,
                                                SessionNetIdOf(grabber), money, slot.Price);
        }

        /// <summary>Слот стены, в котором висит оружие этого предмета (или его детали), либо null.</summary>
        public static ArsenalSlotController SlotHolding(UxrGrabbableObject grabbable)
        {
            if (grabbable == null) return null;

            // Дешёвый отсев: у предмета не на стене нет ни якоря, ни якоря у корня-оружия.
            UxrGrabbableObjectAnchor anchor = grabbable.CurrentAnchor;
            if (anchor == null)
            {
                WeaponComponent weapon = grabbable.GetComponentInParent<WeaponComponent>();
                UxrGrabbableObject root = weapon != null ? weapon.GetComponent<UxrGrabbableObject>() : null;
                anchor = root != null ? root.CurrentAnchor : null;
            }

            return anchor != null ? anchor.GetComponentInParent<ArsenalSlotController>() : null;
        }

        private static uint SessionNetIdOf(UxrGrabber grabber)
        {
            if (grabber == null || grabber.Avatar == null) return 0u;
            PlayerController player = grabber.Avatar.GetComponentInParent<PlayerController>();
            return player != null && player.Session != null ? player.Session.netId : 0u;
        }
    }
}
