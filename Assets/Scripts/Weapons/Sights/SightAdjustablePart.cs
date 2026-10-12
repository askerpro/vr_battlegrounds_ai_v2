using UnityEngine;

namespace VrBattlegrounds.Weapons.Sights
{
    public enum SightPartRole { Rear, Front }

    /// <summary>
    /// Половина механического прицела: деталь целика (Rear) или мушки (Front). Pivot объекта стоит ровно на точке
    /// прицеливания: Rear — центр отверстия/середина прорези, Front — вершина столбика. Только данные: в runtime
    /// компонент ничего не делает и выстрел не меняет.
    ///
    /// <para>
    /// Подвижная деталь (ход больше нуля) двигается только по своей локальной оси Y в пределах
    /// [−<see cref="TravelDown"/>, +<see cref="TravelUp"/>] от <see cref="NeutralLocalPosition"/>; горизонталь не
    /// регулируется. Неподвижная половина помечается тем же компонентом с нулевым ходом: сведению нужна её точка.
    /// У каждого прицела ровно два компонента (Rear и Front), подвижный из них — не больше одного.
    /// Значения хода — в локальных единицах родителя детали (метры при масштабе комплекта 1).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VR Battlegrounds/Weapons/Sight Adjustable Part")]
    public sealed class SightAdjustablePart : MonoBehaviour
    {
        [Tooltip("Rear — деталь целика (pivot в центре отверстия/прорези); Front — деталь мушки (pivot на вершине столбика).")]
        [SerializeField] private SightPartRole _role;

        [Tooltip("Собранная позиция детали до поправки сведения (localPosition при нулевом сдвиге).")]
        [SerializeField] private Vector3 _neutralLocalPosition;

        [Tooltip("Ход вниз от собранной позиции по локальной Y, м (≥ 0). Ноль у неподвижной половины.")]
        [SerializeField, Min(0f)] private float _travelDown;

        [Tooltip("Ход вверх от собранной позиции по локальной Y, м (≥ 0). Ноль у неподвижной половины.")]
        [SerializeField, Min(0f)] private float _travelUp;

        public SightPartRole Role => _role;
        public Vector3 NeutralLocalPosition => _neutralLocalPosition;
        public float TravelDown => _travelDown;
        public float TravelUp => _travelUp;

        /// <summary>Подвижна ли деталь (есть ход хотя бы в одну сторону).</summary>
        public bool IsAdjustable => _travelDown > 0f || _travelUp > 0f;

        /// <summary>Текущий сдвиг сведения по локальной Y относительно собранной позиции.</summary>
        public float Offset => transform.localPosition.y - _neutralLocalPosition.y;

        /// <summary>Попадает ли сдвиг в физический ход детали (с допуском float-округления).</summary>
        public bool IsWithinTravel(float offset) => offset >= -_travelDown - 1e-7f && offset <= _travelUp + 1e-7f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _role == SightPartRole.Rear ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, .0008f);
        }
    }
}
