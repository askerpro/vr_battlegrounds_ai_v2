using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Готов ли якорь к действию руки своего аватара прямо сейчас: принять то, что в руке (отпусти — и предмет встанет),
    /// или отдать содержимое (нажми grip — и достанешь). Работает для любого <see cref="UxrGrabbableObjectAnchor" /> —
    /// карманы своего аватара и якоря вне аватаров (гнёзда магазинов, слоты арсенала); какие якоря отслеживать, решает
    /// фильтр владельца. Ответ берётся из решений самого UltimateXR, а не из своей копии геометрии, чтобы сигнал совпадал с
    /// тем, что игра реально сделает.
    ///
    /// <para>
    /// <b>Принять</b> — выбранная SDK пара якорь/рука через общий <see cref="AnchorPlacementReadiness" /> (повторная
    /// проверка локальной руки, совместимости, свободного гнезда и единственной удерживающей руки при чтении).
    /// </para>
    ///
    /// <para>
    /// <b>Отдать</b> — <see cref="UxrGrabManager.GetClosestGrabbableObject(UxrGrabber, out UxrGrabbableObject, out int, IEnumerable{UxrGrabbableObject})" />,
    /// тот же вызов, которым SDK выбирает цель при нажатии grip. Якорь готов, если цель — его прокси (прокси хватаемый,
    /// только когда в якоре что-то есть) или лежащий в нём предмет. <c>PlacedObjectRangeEntered</c> не годится: меряет до
    /// точек хвата самого предмета, не видит прокси и спрятанные магазины, а <c>GrabberNear</c> в SDK — <c>null</c>
    /// (known-issues). Полный поиск SDK дорогой, поэтому он идёт, только если хоть один отслеживаемый якорь вообще отдаёт
    /// что-то этой руке.
    /// </para>
    /// </summary>
    public sealed class AnchorReadiness : IDisposable
    {
        private readonly UxrAvatar _avatar;
        private readonly Func<UxrGrabbableObjectAnchor, bool> _tracked;
        private readonly Func<bool> _includeWorld;

        private UxrGrabManager _manager;
        private AnchorPlacementReadiness _placement;
        private List<UxrGrabbableObjectAnchor> _ownPockets;
        private readonly List<UxrGrabbableObjectAnchor> _candidates = new List<UxrGrabbableObjectAnchor>();
        private bool _disposed;

        /// <param name="avatar">Свой аватар: его карманы — кандидаты.</param>
        /// <param name="tracked">Отслеживать ли якорь (например, у его роли есть клип вибрации).</param>
        /// <param name="includeWorld">Искать ли кандидатов среди якорей вне аватаров — дороже, только если нужно.</param>
        public AnchorReadiness(UxrAvatar avatar, Func<UxrGrabbableObjectAnchor, bool> tracked, Func<bool> includeWorld)
        {
            _avatar = avatar;
            _tracked = tracked ?? throw new ArgumentNullException(nameof(tracked));
            _includeWorld = includeWorld ?? (() => false);
            EnsureManager();
        }

        public void Dispose()
        {
            _disposed = true;
            _placement?.Dispose();
            _placement = null;
            _manager = null;
        }

        /// <summary>Якорь, готовый к действию руки <paramref name="side" />, или null.</summary>
        public UxrGrabbableObjectAnchor GetReadyAnchor(UxrHandSide side)
        {
            if (_disposed) return null;
            EnsureManager();
            if (_manager == null || _avatar == null) return null;
            UxrGrabber grabber = _avatar.GetGrabber(side);
            if (grabber == null) return null;

            CollectCandidates();
            if (_candidates.Count == 0) return null;
            return grabber.GrabbedObject != null ? GetReadyToAccept(grabber) : GetReadyToGive(grabber);
        }

        private UxrGrabbableObjectAnchor GetReadyToAccept(UxrGrabber grabber)
        {
            // SDK кладёт предмет, только если отпускает последняя держащая рука.
            foreach (UxrGrabbableObjectAnchor anchor in _candidates)
                if (_placement.TryGetReadyGrabber(anchor, out UxrGrabber ready) && ready == grabber) return anchor;
            return null;
        }

        private UxrGrabbableObjectAnchor GetReadyToGive(UxrGrabber grabber)
        {
            if (!AnyCandidateGives(grabber)) return null;

            if (!_manager.GetClosestGrabbableObject(grabber, out UxrGrabbableObject target, out int grabPoint) ||
                _manager.IsBeingGrabbed(target, grabPoint))
                return null;

            foreach (UxrGrabbableObjectAnchor anchor in _candidates)
                if (anchor != null && (target.CurrentAnchor == anchor || anchor.GrabProxy == target)) return anchor;
            return null;
        }

        /// <summary>
        /// Берётся ли рукой хоть одна точка прокси или содержимого отслеживаемых якорей — той же проверкой
        /// (<see cref="UxrGrabbableObject.CanBeGrabbedByGrabber" />), которой SDK отбирает кандидатов поиска. «Нет» здесь
        /// точно значит, что цель поиска не из этих якорей.
        /// </summary>
        private bool AnyCandidateGives(UxrGrabber grabber)
        {
            foreach (UxrGrabbableObjectAnchor anchor in _candidates)
            {
                if (anchor == null) continue;
                if (IsGrabbableByGrabber(anchor.GrabProxy, grabber) || IsGrabbableByGrabber(anchor.CurrentPlacedObject, grabber))
                    return true;
            }
            return false;
        }

        private static bool IsGrabbableByGrabber(UxrGrabbableObject grabbable, UxrGrabber grabber)
        {
            if (grabbable == null) return false;
            for (int point = 0; point < grabbable.GrabPointCount; ++point)
                if (grabbable.CanBeGrabbedByGrabber(grabber, point)) return true;
            return false;
        }

        private void CollectCandidates()
        {
            _candidates.Clear();

            // Карманы аватара за игру не меняются — собираются один раз.
            if (_ownPockets == null)
            {
                _ownPockets = new List<UxrGrabbableObjectAnchor>(_avatar.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true));
                _ownPockets.RemoveAll(anchor => !IsOwnPocket(anchor));
            }
            foreach (UxrGrabbableObjectAnchor anchor in _ownPockets)
                if (anchor != null && anchor.isActiveAndEnabled && _tracked(anchor)) _candidates.Add(anchor);

            if (!_includeWorld()) return;
            foreach (UxrGrabbableObjectAnchor anchor in UxrGrabbableObjectAnchor.EnabledComponents)
                if (!AnchorRole.IsAvatarPocket(anchor) && _tracked(anchor)) _candidates.Add(anchor);
        }

        private bool IsCandidate(UxrGrabbableObjectAnchor anchor) => _candidates.Contains(anchor);

        private bool IsOwnPocket(UxrGrabbableObjectAnchor anchor) =>
            AnchorRole.IsAvatarPocket(anchor) && anchor.GetComponentInParent<UxrAvatar>(true) == _avatar;

        private void EnsureManager()
        {
            UxrGrabManager current = UxrGrabManager.HasInstance ? UxrGrabManager.Instance : null;
            if (_manager == current) return;
            _placement?.Dispose();
            _manager = current;
            _placement = current == null ? null : new AnchorPlacementReadiness(current, IsCandidate);
        }
    }
}
