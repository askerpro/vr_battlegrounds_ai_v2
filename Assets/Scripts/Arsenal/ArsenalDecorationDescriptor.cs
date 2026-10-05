using System;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Fixed artwork metadata: его frame не меняет layout profile и scale предметов.</summary>
    [Serializable]
    public sealed class ArsenalDecorationDescriptor
    {
        [SerializeField] private string _decorationId, _visualSetId, _sourceFingerprint;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private int _pegCapacity, _shelfCapacity;
        [SerializeField] private bool _defaultUniversal;
        [SerializeField] private ArsenalPresentationPose _slotFrame;
        [SerializeField] private Vector3 _frameScale = Vector3.one;
        [SerializeField] private Bounds _usableBounds, _artworkBounds;
        public string DecorationId => _decorationId;
        public string VisualSetId => _visualSetId;
        public string SourceFingerprint => _sourceFingerprint;
        public GameObject Prefab => _prefab;
        public int PegCapacity => _pegCapacity;
        public int ShelfCapacity => _shelfCapacity;
        public bool DefaultUniversal => _defaultUniversal;
        public ArsenalPresentationPose SlotFrame => _slotFrame;
        public Vector3 FrameScale => _frameScale;
        public Bounds UsableBounds => _usableBounds;
        public Bounds ArtworkBounds => _artworkBounds;
        public ArsenalDecorationDescriptor(string id, string set, GameObject prefab, string sourceFingerprint,
            int peg, int shelf, bool universal, ArsenalPresentationPose frame, Vector3 scale, Bounds usable, Bounds artwork)
        {
            _decorationId=id; _visualSetId=set; _prefab=prefab; _sourceFingerprint=sourceFingerprint;
            _pegCapacity=peg; _shelfCapacity=shelf; _defaultUniversal=universal; _slotFrame=frame;
            _frameScale=scale; _usableBounds=usable; _artworkBounds=artwork;
        }
        public ArsenalDecorationDescriptor Freeze() => new ArsenalDecorationDescriptor(_decorationId,_visualSetId,_prefab,
            _sourceFingerprint,_pegCapacity,_shelfCapacity,_defaultUniversal,_slotFrame,_frameScale,_usableBounds,_artworkBounds);
    }
}

