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
            if (grabbable == null)
            {
                return true;
            }

            // Карта на этой машине ещё не играбельна: удалённый клиент не применил свежий начальный снимок
            // своего запуска, сервер не открыл server Ready или уже закрыл карту (Closing). Захват сейчас
            // описал бы состояние, которого у сервера нет. Удерживаемое этим не отпускается.
            if (!Maps.Runtime.MapRunAdmission.IsLocalPlayable)
            {
                return false;
            }

            // Делегат SDK спрашивает каждую точку каждого предмета каждый кадр, до проверки
            // расстояния, — производные от иерархии данные берутся из покадрового кэша.
            GrabOnlyWhenParentHeld parentRule = GrabbableHierarchyCache.GetPartRule(grabbable, out UxrGrabbableObject partParent);

            if (parentRule != null && !parentRule.AllowsGrab(grabber, partParent))
            {
                return false;
            }

            SupportGripRequiresMain supportRule = GrabbableHierarchyCache.GetSupportRule(grabbable);

            if (supportRule != null && !supportRule.AllowsGrab(grabber, grabbable, grabPoint))
            {
                return false;
            }

            if (!AnchoredItemGrabRule.AllowsGrab(grabber, grabbable))
            {
                return false;
            }

            // Стена арсенала при экономике матча: только своя и по карману (T-45).
            if (!Arsenal.ArsenalGrabRule.AllowsGrab(grabber, grabbable))
            {
                return false;
            }

            return TwoHandGrabPolicy.IsGrabAllowed(grabber, grabbable, grabPoint, ManipulationConstraints.AllowsHandTransfer(grabbable));
        }
    }
}
