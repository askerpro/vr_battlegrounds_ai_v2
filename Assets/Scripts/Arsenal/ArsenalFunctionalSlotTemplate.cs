using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Source identity роли; runtime UID из map lifetime здесь не вычисляется.</summary>
    [Serializable]
    public struct ArsenalTemplateRole
    {
        public string RoleKey, SourceUniqueId, SourceComponentId, ComponentType;
        public ArsenalTemplateRole(string key, string uid, string id, string type)
        { RoleKey = key; SourceUniqueId = uid; SourceComponentId = id; ComponentType = type; }
    }

    /// <summary>Исходная поверхность и явная generated-проекция; whole root не масштабируется.</summary>
    [Serializable]
    public struct ArsenalSurfaceProjection
    {
        public string RoleKey, SourceComponentId, SourcePath;
        public string ProjectionProfileGuid, ProjectionProfileFingerprint, ProjectionSurfaceComponentId;
        public ArsenalPresentationPose SourcePose, ProjectedPose;
        public Vector3 SourceScale, ProjectedScale;
        public Bounds SourceMeshBounds, SourceColliderBounds;
        public Bounds ProjectedMeshBounds, ProjectedColliderBounds;
    }

    [Serializable]
    public struct ArsenalStructuralGeometry
    {
        public string SourceComponentId, ComponentType;
        public Bounds SlotLocalBounds;
    }

    /// <summary>Замороженное описание functional resource. Геометрия карточки/SDK frames проектируется отдельно.</summary>
    [Serializable]
    public sealed class ArsenalFunctionalSlotTemplate
    {
        [SerializeField] private string _templateId, _sourceGuid, _sourceFingerprint;
        [SerializeField] private ArsenalPresentationZone _zone;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private float _panelWidth, _gap;
        [SerializeField] private ArsenalPresentationPose _openRow, _closedRow;
        [SerializeField] private ArsenalSurfaceProjection _surface;
        [SerializeField] private ArsenalTemplateRole[] _roles = Array.Empty<ArsenalTemplateRole>();
        [SerializeField] private ArsenalStructuralGeometry[] _structure = Array.Empty<ArsenalStructuralGeometry>();
        public string TemplateId => _templateId;
        public string SourceGuid => _sourceGuid;
        public string SourceFingerprint => _sourceFingerprint;
        public ArsenalPresentationZone Zone => _zone;
        public GameObject Prefab => _prefab;
        public float PanelWidth => _panelWidth;
        public float Gap => _gap;
        public ArsenalPresentationPose OpenRow => _openRow;
        public ArsenalPresentationPose ClosedRow => _closedRow;
        public ArsenalSurfaceProjection Surface => _surface;
        public IReadOnlyList<ArsenalTemplateRole> Roles => Array.AsReadOnly(_roles);
        public IReadOnlyList<ArsenalStructuralGeometry> Structure => Array.AsReadOnly(_structure);
        public ArsenalFunctionalSlotTemplate(string id, string guid, string fingerprint, ArsenalPresentationZone zone,
            GameObject prefab, float width, float gap, ArsenalPresentationPose open, ArsenalPresentationPose closed,
            ArsenalSurfaceProjection surface, IEnumerable<ArsenalTemplateRole> roles, IEnumerable<ArsenalStructuralGeometry> structure)
        {
            _templateId = id; _sourceGuid = guid; _sourceFingerprint = fingerprint; _zone = zone;
            _prefab = prefab; _panelWidth = width; _gap = gap; _openRow = open; _closedRow = closed; _surface = surface;
            _roles = new List<ArsenalTemplateRole>(roles).ToArray(); _structure = new List<ArsenalStructuralGeometry>(structure).ToArray();
        }
        public ArsenalFunctionalSlotTemplate Freeze() => new ArsenalFunctionalSlotTemplate(_templateId, _sourceGuid,
            _sourceFingerprint, _zone, _prefab, _panelWidth, _gap, _openRow, _closedRow, _surface, _roles, _structure);
    }
}

