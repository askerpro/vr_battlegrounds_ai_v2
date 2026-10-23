using System.Collections.Generic;
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

        /// <summary>Гаснут ствол и выданный магазин: <see cref="ArsenalMagazineOffer.Assign"/> кладёт его ребёнком якоря.</summary>
        protected override IEnumerable<GameObject> DimmedParts()
        {
            foreach (GameObject part in base.DimmedParts()) yield return part;
            if (_magAnchor == null) yield break;
            foreach (Transform child in _magAnchor.transform) yield return child.gameObject;
        }
    }
}
