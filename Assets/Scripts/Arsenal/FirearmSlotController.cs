using UnityEngine;
using UltimateXR.Manipulation;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Слот огнестрельного оружия с отдельным якорем запасного магазина.
    /// Якорь отдельного интерактивного магазина; выдачу ведёт ArsenalMagazineSupply.
    /// </summary>
    public class FirearmSlotController : ArsenalSlotController
    {
        [Header("Отдельный магазин")]
        [Tooltip("Якорь настоящего сетевого магазина (автопоиск по имени MagAnchor)")]
        [SerializeField] private UxrGrabbableObjectAnchor _magAnchor;


        protected override void Awake()
        {
            base.Awake();

            if (_magAnchor == null)
            {
                var anchors = GetComponentsInChildren<UxrGrabbableObjectAnchor>();
                foreach (var a in anchors)
                {
                    if (a.gameObject.name.Contains("Mag"))
                    {
                        _magAnchor = a;
                        break;
                    }
                }
            }
        }


        /// <summary>Якорь серверного предложения и размещения в редакторе.</summary>
        public UxrGrabbableObjectAnchor MagAnchor => _magAnchor;
    }
}
