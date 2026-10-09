using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Пассивный ключ станции и ссылки её оболочки. Слоты собирает <see cref="ArsenalStationComposer" /> в ряды корпуса
    ///     (<see cref="ArsenalSlotRow" />); жизнь станции в запуске карты и допуск принадлежат MapBootstrap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalStationCompositionBinding : MonoBehaviour
    {
        [SerializeField] private string _stationKey;
        [SerializeField] private ArsenalWallController _controller;
        [SerializeField] private ArsenalStationAnchor _stationAnchor;
        [SerializeField] private ArsenalEquipmentPoses _equipmentPoses;
        [SerializeField] private Mirror.NetworkIdentity _stationIdentity;
        [Tooltip("Набор оформления станции; пусто — без набора (корпус не выбирается).")]
        [SerializeField] private string _visualSetId;
        [Tooltip("Явно выбранный корпус из набора; пусто — универсальный, иначе без корпуса.")]
        [SerializeField] private string _decorationId;
        /// <summary>Заказ оформления, настроенный человеком для этой станции; генератор применяет его как есть.</summary>
        public ArsenalVisualRequest VisualRequest => new ArsenalVisualRequest(_visualSetId, _decorationId);
        public string StationKey => _stationKey;
        public ArsenalWallController Controller => _controller;
        public ArsenalStationAnchor StationAnchor => _stationAnchor;
        public ArsenalEquipmentPoses EquipmentPoses => _equipmentPoses;
        public Mirror.NetworkIdentity StationIdentity => _stationIdentity;
    }
}
