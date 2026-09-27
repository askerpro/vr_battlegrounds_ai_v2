using UltimateXR.Manipulation;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Все правила «можно ли этой руке взять эту точку» в одном месте. Подключаются через
    /// <c>UxrGrabber.CanGrabDelegate</c> (<see cref="PlayerGrabManager" />), поэтому UltimateXR
    /// по ним же решает, что подсвечивать при приближении руки.
    /// </summary>
    public static class GrabRules
    {
        public static bool IsGrabAllowed(UxrGrabber grabber, UxrGrabbableObject grabbable, int grabPoint)
        {
            if (grabbable != null && grabbable.TryGetComponent(out GrabOnlyWhenParentHeld parentRule) && !parentRule.AllowsGrab(grabber))
            {
                return false;
            }

            if (!AnchoredItemGrabRule.AllowsGrab(grabber, grabbable))
            {
                return false;
            }

            return TwoHandGrabPolicy.IsGrabAllowed(grabber, grabbable, grabPoint);
        }
    }
}
