using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Деталь предмета (затвор, цевьё помпы, чека гранаты) берётся, только когда сам предмет
    /// уже в руке у того же игрока.
    ///
    /// <para>
    /// Деталь — отдельный <see cref="UxrGrabbableObject" /> внутри предмета, и UltimateXR выбирает
    /// её наравне с рукоятью: к лежащему пистолету подносишь руку — активны и рукоять, и затвор.
    /// Правило читает <c>GrabRules</c> через <c>UxrGrabber.CanGrabDelegate</c>, поэтому недоступная
    /// деталь и не подсвечивается. Уже начатый захват не прерывается: делегат спрашивается только
    /// при поиске нового.
    /// </para>
    ///
    /// <para>
    /// Родитель — ближайший <see cref="UxrGrabbableObject" /> выше по иерархии. Ссылка
    /// <c>UxrGrabbableObject.GrabbableParent</c> не годится: SDK заполняет её только у деталей,
    /// ограниченных родителем, а у затвора <c>Gun_real</c> она пустая.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class GrabOnlyWhenParentHeld : MonoBehaviour
    {
        [Tooltip("Деталь берётся, только когда предмет-родитель в руке у того же игрока. Снять — деталь берётся всегда.")]
        [SerializeField] private bool _requireParentHeld = true;

        public bool RequireParentHeld => _requireParentHeld;

        /// <summary>Ближайший предмет-родитель; null — зависеть не от чего, правило не действует.</summary>
        public UxrGrabbableObject Parent => transform.parent != null ? transform.parent.GetComponentInParent<UxrGrabbableObject>(true) : null;

        public bool AllowsGrab(UxrGrabber grabber)
        {
            return !_requireParentHeld || AllowsGrab(grabber, Parent);
        }

        /// <summary>
        /// То же с уже найденным <paramref name="parent" /> (<see cref="Parent" />) — без подъёма по
        /// иерархии. <c>GrabRules</c> берёт родителя из <see cref="GrabbableHierarchyCache" />.
        /// </summary>
        public bool AllowsGrab(UxrGrabber grabber, UxrGrabbableObject parent)
        {
            if (!_requireParentHeld)
            {
                return true;
            }

            if (parent == null)
            {
                return true;
            }

            return grabber != null && grabber.Avatar != null && UxrGrabManager.Instance.IsBeingGrabbedBy(parent, grabber.Avatar);
        }
    }
}
