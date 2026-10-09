using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Коробка приёма якоря оружия в координатах слота: центр, размер, поворот.</summary>
    [Serializable]
    public struct ArsenalPlaceZone
    {
        public Vector3 Center;
        public Vector3 Size;
        public Vector3 EulerAngles;
        public Quaternion Rotation => Quaternion.Euler(EulerAngles);
        /// <summary>Коробка задана; нулевой размер — приём сферой якоря как есть.</summary>
        public bool IsSet => Size.x > 0f && Size.y > 0f && Size.z > 0f;
    }

    /// <summary>
    ///     Раскладка слота одного вида (панель или полка): как в слоте лежит содержимое — оружие, магазин, карточка,
    ///     опоры — и где слот принимает возвращаемое оружие. Позы — в координатах слота, применяются как есть.
    ///     <para>
    ///     Префаб слота ссылается на раскладку по умолчанию (<see cref="ArsenalSlotController.DefaultLayout" />), оружие
    ///     может принести свою (<see cref="WeaponInfo.TryGetSlotLayout" />). Одну раскладку могут разделять несколько
    ///     стволов: правка ассета меняет их все. Настраивается на стенде раскладки.
    ///     </para>
    /// </summary>
    [CreateAssetMenu(fileName = "SlotLayout", menuName = "VR Battlegrounds/Arsenal/Slot Layout")]
    public sealed class ArsenalSlotLayout : ScriptableObject
    {
        [Tooltip("Вид слота, к которому подходит раскладка.")]
        [SerializeField] private ArsenalPresentationZone _slotKind;
        [SerializeField] private ArsenalPresentationPose _itemTarget;
        [SerializeField] private ArsenalPresentationPose _magazineTarget;
        [SerializeField] private ArsenalPresentationPose _cardTarget;
        [SerializeField] private Vector2 _cardSize = new Vector2(.15f, .16f);
        [SerializeField] private float _cardFontSize = .16f;
        [SerializeField] private List<ArsenalSupportPose> _supports = new List<ArsenalSupportPose>();
        [Tooltip("Где слот принимает возвращаемое оружие: точка приёма оружия должна попасть в коробку.")]
        [SerializeField] private ArsenalPlaceZone _placeZone;

        public ArsenalPresentationZone SlotKind => _slotKind;
        public ArsenalPresentationPose ItemTarget => _itemTarget;
        public ArsenalPresentationPose MagazineTarget => _magazineTarget;
        public ArsenalPresentationPose CardTarget => _cardTarget;
        public Vector2 CardSize => _cardSize;
        public float CardFontSize => _cardFontSize;
        public IReadOnlyList<ArsenalSupportPose> Supports => _supports;
        public ArsenalPlaceZone PlaceZone => _placeZone;
    }
}
