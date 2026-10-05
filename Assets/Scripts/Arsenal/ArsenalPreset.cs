using System.Collections.Generic;
using System;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Упорядоченный ассортимент карты; каталог оружия остаётся общим.</summary>
    [CreateAssetMenu(fileName = "ArsenalPreset", menuName = "VR Battlegrounds/Arsenal/Preset")]
    public sealed class ArsenalPreset : ScriptableObject
    {
        [SerializeField] private string _presetId;
        [SerializeField] private ArsenalPresentationStyle _presentationStyle;
        [Serializable]
        public struct Entry
        {
            public WeaponInfo Weapon;
            public ArsenalPresentationZone Zone;
        }
        [SerializeField] private List<Entry> _entries = new List<Entry>();
        public string PresetId => _presetId;
        public ArsenalPresentationStyle PresentationStyle => _presentationStyle;
        public IReadOnlyList<Entry> Entries => _entries;
    }
    public enum ArsenalPresentationZone { Pegboard, Shelf }
}
