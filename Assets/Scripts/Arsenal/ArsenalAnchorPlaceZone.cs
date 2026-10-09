using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Коробка приёма якоря вместо сферы: предмет можно положить, только когда его точка приёма
    ///     (<see cref="UxrGrabbableObject.DropProximityTransform" />) внутри коробки. Работает через штатный валидатор
    ///     якоря, без правки SDK: сфера якоря (<see cref="UxrGrabbableObjectAnchor.MaxPlaceDistance" />) расширяется до
    ///     дальнего угла коробки, а валидатор отсекает всё вне её — приём, подсветка и выбор якоря идут по коробке.
    ///     <para>
    ///     Коробка задана в координатах слота (<see cref="Frame" />) и двигается вместе с ним. Проверка — чистая геометрия,
    ///     одинаковая на всех машинах. Ставит и настраивает сборщик слота по раскладке (<see cref="ArsenalSlotLayout" />).
    ///     </para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public sealed class ArsenalAnchorPlaceZone : MonoBehaviour
    {
        [SerializeField] private Transform _frame;
        [SerializeField] private ArsenalPlaceZone _zone;
        private UxrGrabbableObjectAnchor _anchor;
        private bool _subscribed;

        public Transform Frame => _frame;
        public ArsenalPlaceZone Zone => _zone;

        public void Configure(Transform frame, ArsenalPlaceZone zone)
        {
            Unsubscribe();
            _frame = frame;
            _zone = zone;
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || _frame == null || !_zone.IsSet) return;
            if (_anchor == null) _anchor = GetComponent<UxrGrabbableObjectAnchor>();
            _anchor.MaxPlaceDistance = CoveringRadius();
            _anchor.AddPlacingValidator(Contains);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_anchor != null) _anchor.RemovePlacingValidator(Contains);
            _subscribed = false;
        }

        /// <summary>Точка приёма предмета внутри коробки.</summary>
        public bool Contains(UxrGrabbableObject item)
        {
            if (item == null || _frame == null) return false;
            Vector3 local = Quaternion.Inverse(_zone.Rotation) * (_frame.InverseTransformPoint(item.DropProximityTransform.position) - _zone.Center);
            Vector3 half = _zone.Size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Радиус сферы якоря, покрывающей коробку: расстояние до её дальнего угла.</summary>
        private float CoveringRadius()
        {
            Vector3 origin = _anchor.DropProximityTransform.position;
            float radius = 0f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = _zone.Center + _zone.Rotation * Vector3.Scale(_zone.Size * .5f,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                radius = Mathf.Max(radius, Vector3.Distance(origin, _frame.TransformPoint(corner)));
            }
            return radius;
        }

        private void OnDrawGizmosSelected()
        {
            if (_frame == null || !_zone.IsSet) return;
            Gizmos.color = new Color(.2f, .9f, .4f, .8f);
            Gizmos.matrix = _frame.localToWorldMatrix * Matrix4x4.TRS(_zone.Center, _zone.Rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, _zone.Size);
        }
    }
}
