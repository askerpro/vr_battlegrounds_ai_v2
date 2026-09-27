using System;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Выбор точки захвата, когда предмет уже держит другая рука.
    ///
    /// <para>
    /// UltimateXR (<c>UxrGrabManager.GetClosestGrabbableObject</c>) сравнивает точки одного
    /// предмета только по расстоянию, а захват занятой точки трактует как передачу из руки
    /// в руку. У компактного оружия основная рукоять ближе к второй ладони, чем дополнительная
    /// точка, и вместо хвата двумя руками оружие перескакивает в другую руку.
    /// </para>
    ///
    /// <para>
    /// Правило: точка, которую держит другая рука, уступает, если свободная точка того же
    /// предмета достижима. Нет достижимых свободных — передача из руки в руку работает как раньше.
    /// </para>
    /// </summary>
    public static class TwoHandGrabPolicy
    {
        /// <summary>
        /// Разрешает ли политика <paramref name="grabber"/> брать <paramref name="grabPoint"/>.
        /// Подключается через <c>UxrGrabber.CanGrabDelegate</c>.
        /// </summary>
        public static bool IsGrabAllowed(UxrGrabber grabber, UxrGrabbableObject grabbable, int grabPoint)
        {
            if (grabbable == null || !grabbable.AllowMultiGrab || grabbable.GrabPointCount < 2)
            {
                return true;
            }

            UxrGrabManager manager = UxrGrabManager.Instance;

            if (manager == null || !manager.IsBeingGrabbed(grabbable))
            {
                return true;
            }

            // Рекурсия ограничена: CanBeGrabbedByGrabber снова зовёт делегат, но только для
            // свободной точки, а для неё политика отвечает сразу, не спрашивая достижимость.
            return !ShouldYieldToFreePoint(grabPoint, grabbable.GrabPointCount,
                                           p => manager.GetGrabbingHand(grabbable, p, out UxrGrabber holder) && holder != grabber,
                                           p => grabbable.CanBeGrabbedByGrabber(grabber, p));
        }

        /// <summary>
        /// Должна ли <paramref name="grabPoint"/> уступить свободной точке того же предмета.
        /// </summary>
        public static bool ShouldYieldToFreePoint(int grabPoint, int grabPointCount,
                                                  Func<int, bool> isHeldByOtherHand,
                                                  Func<int, bool> isFreePointReachable)
        {
            if (!isHeldByOtherHand(grabPoint))
            {
                return false;
            }

            for (int point = 0; point < grabPointCount; ++point)
            {
                if (point != grabPoint && !isHeldByOtherHand(point) && isFreePointReachable(point))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
