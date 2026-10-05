using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Пассивный сериализуемый carrier editor-стенда; поведение находится в Editor. Сцена исключена из build.</summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalLayoutAuthoringStand : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/Debug/ArsenalLayoutAuthoring.unity";
        public const string OwnerId = "arsenal-layout-authoring-v1";
        [Serializable] public sealed class SupportHandle
        {
            public string Role;
            public ArsenalSupportAnchorKind AnchorKind;
            public Transform Handle;
        }
        [Serializable] public sealed class SlotHandles
        {
            public WeaponInfo Weapon;
            public ArsenalPresentationZone Zone;
            public Transform Frame;
            public Transform Item;
            public Transform Magazine;
            public Transform Card;
            public string CommittedBaseline;
            public List<SupportHandle> Supports = new List<SupportHandle>();
        }
        [SerializeField, HideInInspector] private string _owner = OwnerId;
        [SerializeField, HideInInspector] private ArsenalPreset _preset;
        [SerializeField, HideInInspector] private ArsenalPresentationStyle _style;
        [SerializeField, HideInInspector] private List<SlotHandles> _slots = new List<SlotHandles>();
        [SerializeField, HideInInspector] private string _stagedBaseline;
        [SerializeField, HideInInspector] private string _sourceBaseline;
        public string Owner => _owner;
        public string SourceBaseline => _sourceBaseline;
        public string StagedBaseline => _stagedBaseline;
        public ArsenalPreset Preset => _preset;
        public ArsenalPresentationStyle Style => _style;
        public IReadOnlyList<SlotHandles> Slots => _slots;
    }
}
