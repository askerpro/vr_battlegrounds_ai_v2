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
            // Запрос — структура, а не пара лямбд: делегат зовётся каждый кадр, замыкания давали мусор.
            return !ShouldYieldToFreePoint(grabPoint, grabbable.GrabPointCount, new LivePointQuery(manager, grabber, grabbable));
        }

        /// <summary>
        /// Должна ли <paramref name="grabPoint"/> уступить свободной точке того же предмета.
        /// </summary>
        public static bool ShouldYieldToFreePoint(int grabPoint, int grabPointCount,
                                                  Func<int, bool> isHeldByOtherHand,
                                                  Func<int, bool> isFreePointReachable)
        {
            return ShouldYieldToFreePoint(grabPoint, grabPointCount, new DelegatePointQuery(isHeldByOtherHand, isFreePointReachable));
        }

        private static bool ShouldYieldToFreePoint<TQuery>(int grabPoint, int grabPointCount, TQuery query) where TQuery : struct, IPointQuery
        {
            if (!query.IsHeldByOtherHand(grabPoint))
            {
                return false;
            }

            for (int point = 0; point < grabPointCount; ++point)
            {
                if (point != grabPoint && !query.IsHeldByOtherHand(point) && query.IsFreePointReachable(point))
                {
                    return true;
                }
            }

            return false;
        }

        private interface IPointQuery
        {
            bool IsHeldByOtherHand(int point);
            bool IsFreePointReachable(int point);
        }

        /// <summary>Живой запрос к UltimateXR — без аллокаций (generic по структуре, без боксинга).</summary>
        private readonly struct LivePointQuery : IPointQuery
        {
            private readonly UxrGrabManager     _manager;
            private readonly UxrGrabber         _grabber;
            private readonly UxrGrabbableObject _grabbable;

            public LivePointQuery(UxrGrabManager manager, UxrGrabber grabber, UxrGrabbableObject grabbable)
            {
                _manager   = manager;
                _grabber   = grabber;
                _grabbable = grabbable;
            }

            public bool IsHeldByOtherHand(int point) => _manager.GetGrabbingHand(_grabbable, point, out UxrGrabber holder) && holder != _grabber;

            public bool IsFreePointReachable(int point) => _grabbable.CanBeGrabbedByGrabber(_grabber, point);
        }

        /// <summary>Запрос через делегаты — для юнит-тестов политики без сцены.</summary>
        private readonly struct DelegatePointQuery : IPointQuery
        {
            private readonly Func<int, bool> _isHeldByOtherHand;
            private readonly Func<int, bool> _isFreePointReachable;

            public DelegatePointQuery(Func<int, bool> isHeldByOtherHand, Func<int, bool> isFreePointReachable)
            {
                _isHeldByOtherHand    = isHeldByOtherHand;
                _isFreePointReachable = isFreePointReachable;
            }

            public bool IsHeldByOtherHand(int point) => _isHeldByOtherHand(point);

            public bool IsFreePointReachable(int point) => _isFreePointReachable(point);
        }
    }
}
