using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    public enum ArsenalCompositionMode { Authored, Generated }

    /// <summary>Пассивная единственная station key и ссылки shell. Map lifetime и admission принадлежат bootstrap.</summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalStationCompositionBinding : MonoBehaviour
    {
        [SerializeField] private string _stationKey;
        [SerializeField] private ArsenalCompositionMode _mode = ArsenalCompositionMode.Authored;
        [SerializeField] private ArsenalWallController _controller;
        [SerializeField] private ArsenalStationPresetBinding _authoredBinding;
        [SerializeField] private ArsenalStationAnchor _stationAnchor;
        [SerializeField] private ArsenalEquipmentPoses _equipmentPoses;
        [SerializeField] private Mirror.NetworkIdentity _stationIdentity;
        public string StationKey => _stationKey;
        public ArsenalCompositionMode Mode => _mode;
        public ArsenalWallController Controller => _controller;
        public ArsenalStationPresetBinding AuthoredBinding => _authoredBinding;
        public ArsenalStationAnchor StationAnchor => _stationAnchor;
        public ArsenalEquipmentPoses EquipmentPoses => _equipmentPoses;
        public Mirror.NetworkIdentity StationIdentity => _stationIdentity;
    }
}
