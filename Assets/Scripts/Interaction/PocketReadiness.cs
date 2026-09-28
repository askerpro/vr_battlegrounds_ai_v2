using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Готов ли карман своего аватара к действию руки прямо сейчас: принять то, что в руке
    /// (отпусти — и предмет встанет), или отдать содержимое (нажми grip — и достанешь).
    /// Ответ берётся из решений самого UltimateXR, а не из своей копии геометрии, чтобы сигнал
    /// совпадал с тем, что игра реально сделает.
    ///
    /// <para>
    /// <b>Принять</b> — события <see cref="UxrGrabManager.AnchorRangeEntered" /> /
    /// <see cref="UxrGrabManager.AnchorRangeLeft" /> (тот же флаг, что включает
    /// <c>Activate On Compatible Near</c> якоря): SDK выбирает ближайший совместимый якорь так
    /// же, как при отпускании. Не проверяет SDK одно — что предмет держит одна рука; при хвате
    /// двумя отпускание ничего не кладёт, поэтому это условие добавлено здесь.
    /// </para>
    ///
    /// <para>
    /// <b>Отдать</b> — <see cref="UxrGrabManager.GetClosestGrabbableObject(UxrGrabber, out UxrGrabbableObject, out int, IEnumerable{UxrGrabbableObject})" />,
    /// тот же вызов, которым SDK выбирает цель при нажатии grip. Карман готов, если цель — его
    /// прокси (прокси хватаемый, только когда в кармане что-то есть) или лежащий в нём предмет.
    /// <c>PlacedObjectRangeEntered</c> / <c>Activate On Hand Near And Grabbable</c> не годятся:
    /// меряют до точек хвата самого предмета, не видят прокси и спрятанные магазины, и
    /// <c>GrabberNear</c> в SDK выставляется в <c>null</c> (known-issues).
    /// </para>
    /// </summary>
    public sealed class PocketReadiness : IDisposable
    {
        private readonly UxrAvatar _avatar;

        // Карман → рука, чей предмет в него встанет. Ведётся событиями SDK.
        private readonly Dictionary<UxrGrabbableObjectAnchor, UxrGrabber> _accepting = new Dictionary<UxrGrabbableObjectAnchor, UxrGrabber>();

        private List<UxrGrabbableObjectAnchor> _pockets;

        public PocketReadiness(UxrAvatar avatar)
        {
            _avatar = avatar;
            UxrGrabManager.Instance.AnchorRangeEntered += GrabManager_AnchorRangeEntered;
            UxrGrabManager.Instance.AnchorRangeLeft    += GrabManager_AnchorRangeLeft;
        }

        public void Dispose()
        {
            if (UxrGrabManager.HasInstance)
            {
                UxrGrabManager.Instance.AnchorRangeEntered -= GrabManager_AnchorRangeEntered;
                UxrGrabManager.Instance.AnchorRangeLeft    -= GrabManager_AnchorRangeLeft;
            }

            _accepting.Clear();
        }

        /// <summary>Карман, готовый к действию руки <paramref name="side" />, или null.</summary>
        public UxrGrabbableObjectAnchor GetReadyPocket(UxrHandSide side)
        {
            UxrGrabber grabber = _avatar.GetGrabber(side);
            if (grabber == null) return null;

            return grabber.GrabbedObject != null ? GetPocketReadyToAccept(grabber) : GetPocketReadyToGive(grabber);
        }

        private UxrGrabbableObjectAnchor GetPocketReadyToAccept(UxrGrabber grabber)
        {
            // SDK кладёт предмет, только если отпускает последняя держащая рука.
            if (UxrGrabManager.Instance.GetHandsGrabbingCount(grabber.GrabbedObject, false) != 1) return null;

            foreach (KeyValuePair<UxrGrabbableObjectAnchor, UxrGrabber> pair in _accepting)
            {
                // Занятый карман SDK из списка не убирает: событие «ушёл» для него не шлётся.
                if (pair.Value == grabber && pair.Key != null && pair.Key.CurrentPlacedObject == null)
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private UxrGrabbableObjectAnchor GetPocketReadyToGive(UxrGrabber grabber)
        {
            // Полный поиск SDK перебирает все хватаемые предметы сцены (с прокси карманов чужих
            // аватаров), а рука почти всегда далеко от своих карманов. Цель поиска может быть
            // «карманной», только если хоть один прокси или предмет своего кармана вообще берётся
            // этой рукой, — нет таких, и результат заранее null. Есть — полный поиск, как раньше:
            // что-то постороннее может оказаться ближе, и тогда grip возьмёт его, а не карман.
            if (!AnyOwnPocketItemGrabbable(grabber))
            {
                return null;
            }

            if (!UxrGrabManager.Instance.GetClosestGrabbableObject(grabber, out UxrGrabbableObject target, out int grabPoint) ||
                UxrGrabManager.Instance.IsBeingGrabbed(target, grabPoint))
            {
                return null;
            }

            if (target.CurrentAnchor != null && IsOwnPocket(target.CurrentAnchor))
            {
                return target.CurrentAnchor;
            }

            foreach (UxrGrabbableObjectAnchor anchor in GetPockets())
            {
                if (anchor != null && anchor.GrabProxy == target)
                {
                    return anchor;
                }
            }

            return null;
        }

        /// <summary>
        /// Берётся ли рукой хоть одна точка прокси или содержимого своих карманов. Та же проверка
        /// (<see cref="UxrGrabbableObject.CanBeGrabbedByGrabber" />), которой SDK отбирает кандидатов
        /// в <c>GetClosestGrabbableObject</c>, поэтому «нет» здесь точно значит «цель поиска не карманная».
        /// </summary>
        private bool AnyOwnPocketItemGrabbable(UxrGrabber grabber)
        {
            foreach (UxrGrabbableObjectAnchor anchor in GetPockets())
            {
                if (anchor == null) continue;

                if (IsGrabbableByGrabber(anchor.GrabProxy, grabber) || IsGrabbableByGrabber(anchor.CurrentPlacedObject, grabber))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGrabbableByGrabber(UxrGrabbableObject grabbable, UxrGrabber grabber)
        {
            if (grabbable == null) return false;

            for (int point = 0; point < grabbable.GrabPointCount; ++point)
            {
                if (grabbable.CanBeGrabbedByGrabber(grabber, point))
                {
                    return true;
                }
            }

            return false;
        }

        private List<UxrGrabbableObjectAnchor> GetPockets()
        {
            // Карманы аватара за игру не меняются — собираются один раз, а не каждый кадр.
            if (_pockets == null)
            {
                _pockets = new List<UxrGrabbableObjectAnchor>(_avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true));
                _pockets.RemoveAll(anchor => !IsOwnPocket(anchor));
            }

            return _pockets;
        }

        private bool IsOwnPocket(UxrGrabbableObjectAnchor anchor)
        {
            return AnchorRole.IsAvatarPocket(anchor) && anchor.GetComponentInParent<UxrAvatar>(true) == _avatar;
        }

        private void GrabManager_AnchorRangeEntered(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableAnchor != null && e.Grabber != null && e.Grabber.Avatar == _avatar && IsOwnPocket(e.GrabbableAnchor))
            {
                _accepting[e.GrabbableAnchor] = e.Grabber;
            }
        }

        private void GrabManager_AnchorRangeLeft(object sender, UxrManipulationEventArgs e)
        {
            if (e.GrabbableAnchor != null)
            {
                _accepting.Remove(e.GrabbableAnchor);
            }
        }
    }
}
