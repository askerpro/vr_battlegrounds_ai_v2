using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Дополнительные точки предмета — только для второй руки: берутся, когда основную точку
    /// (индекс 0) уже держит другая рука того же игрока.
    ///
    /// <para>
    /// У пистолета дополнительная точка (поддержка) в паре сантиметров от рукояти, и UltimateXR
    /// выбирает её наравне с основной: подносишь руку к лежащему пистолету — он иногда
    /// берётся «поддерживающим» хватом. Правило читает <c>GrabRules</c> через
    /// <c>UxrGrabber.CanGrabDelegate</c>, поэтому недоступная точка и не подсвечивается.
    /// Уже начатый захват не прерывается: делегат спрашивается только при поиске нового.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class SupportGripRequiresMain : MonoBehaviour
    {
        public const int MainPoint = 0;

        [Tooltip("Дополнительные точки берутся, только когда основную держит другая рука того же игрока. Снять — берутся всегда.")]
        [SerializeField] private bool _requireMainHeld = true;

        public bool RequireMainHeld => _requireMainHeld;

        public bool AllowsGrab(UxrGrabber grabber, UxrGrabbableObject grabbable, int grabPoint)
        {
            if (!_requireMainHeld || grabPoint == MainPoint || grabbable == null)
            {
                return true;
            }

            return grabber != null && grabber.Avatar != null &&
                   UxrGrabManager.Instance.GetGrabbingHand(grabbable, MainPoint, out UxrGrabber holder) &&
                   holder != grabber && holder.Avatar == grabber.Avatar;
        }
    }
}
