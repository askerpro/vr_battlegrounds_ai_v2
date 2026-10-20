using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Пассивный сериализуемый carrier editor-стенда раскладки одного ствола; поведение находится в Editor. Сцена
    /// исключена из build и в Git хранит только carrier: слот выбранного оружия — временная проекция, которую стенд
    /// строит при открытии из окна «Настройка оружия» и не сохраняет в сцену.
    /// </summary>
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
            /// <summary>Коробка приёма: поза ручки — центр и поворот, размер — BoxCollider ручки.</summary>
            public Transform PlaceZone;
            public List<SupportHandle> Supports = new List<SupportHandle>();
        }
        [SerializeField, HideInInspector] private string _owner = OwnerId;
        [SerializeField, HideInInspector] private ArsenalPresentationStyle _style;
        [SerializeField, HideInInspector] private SlotHandles _slot = new SlotHandles();
        [SerializeField, HideInInspector] private string _stagedBaseline;
        [SerializeField, HideInInspector] private string _sourceBaseline;
        public string Owner => _owner;
        public string SourceBaseline => _sourceBaseline;
        public string StagedBaseline => _stagedBaseline;
        public ArsenalPresentationStyle Style => _style;
        /// <summary>Слот выбранного оружия; пуст (<see cref="HasSlot"/> = false), пока стенд не открыт на стволе.</summary>
        public SlotHandles Slot => _slot;
        public bool HasSlot => _slot != null && _slot.Weapon != null && _slot.Frame != null;
    }
}
