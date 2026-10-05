using System;
using System.Collections.ObjectModel;
using System.Linq;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Числовое направление авторской позиции; не выводится из формы её окружения.</summary>
    public sealed class MapGrowthPositionAnchor
    {
        public string PositionId { get; }
        public float ThreatYaw { get; }
        public MapGrowthPositionAnchor(string positionId, float threatYaw)
        {
            if (string.IsNullOrWhiteSpace(positionId) || !PositionImpactValidation.Finite(threatYaw)) throw new ArgumentException("Нужен ID позиции и конечный угол угрозы.");
            PositionId = positionId; ThreatYaw = threatYaw;
        }
    }
    /// <summary>Возможности палитры сняты главным потоком; workers не читают definition или prefab.</summary>
    public sealed class MapGrowthShapeCapabilities
    {
        public string ShapeId { get; }
        public bool Openings { get; }
        public bool Detail { get; }
        private readonly CoverClass[] materials;
        public ReadOnlyCollection<CoverClass> Materials => Array.AsReadOnly(materials);
        public MapGrowthShapeCapabilities(string shapeId, bool openings, bool detail, CoverClass[] materials)
        {
            if (string.IsNullOrWhiteSpace(shapeId) || materials == null || materials.Length == 0 || materials.Distinct().Count() != materials.Length)
                throw new ArgumentException("Нужны ID формы и уникальные разрешённые материалы.");
            ShapeId = shapeId; Openings = openings; Detail = detail; this.materials = (CoverClass[])materials.Clone();
        }
    }
    internal sealed class MapGrowthRandom
    {
        private uint state;
        public MapGrowthRandom(int seed, int attempt) => state = Mix(unchecked((uint)seed) ^ Mix(unchecked((uint)attempt) + 1));
        internal static uint Mix(uint value)
        {
            unchecked { value += 0x9e3779b9u; value = (value ^ (value >> 16)) * 0x85ebca6bu; value = (value ^ (value >> 13)) * 0xc2b2ae35u; return (value ^ (value >> 16)) | 1u; }
        }
        public int Next(int maximum)
        {
            if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
            state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (int)(state % (uint)maximum);
        }
    }
}
