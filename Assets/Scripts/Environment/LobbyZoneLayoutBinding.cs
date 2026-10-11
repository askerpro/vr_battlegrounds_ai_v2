using System;
using UnityEngine;
using VrBattlegrounds.LevelDesign;

namespace VrBattlegrounds.LobbyZones
{
    /// <summary>Авторские данные общей зоны. В игре используется сохранённое размещение без слежения.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyZoneLayoutBinding : MonoBehaviour
    {
        [Serializable]
        public struct GroupPlacement
        {
            public Transform group;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [SerializeField] private Transform _sourceZone = null;
        [SerializeField] private Transform _environmentRoot = null;
        [SerializeField] private Transform _gameplayRoot = null;
        [SerializeField] private GroupPlacement[] _groups = Array.Empty<GroupPlacement>();
        [SerializeField] private Vector3 _lastSourcePosition = Vector3.zero;
        [SerializeField] private Quaternion _lastSourceRotation = Quaternion.identity;
        [SerializeField, HideInInspector] private bool _constrainedToPhysicalArena = false;
        [SerializeField, HideInInspector] private PhysicalArenaDefinition _physicalArena = null;
        public Transform SourceZone => _sourceZone;
        public Transform EnvironmentRoot => _environmentRoot;
        public Transform GameplayRoot => _gameplayRoot;
        public GroupPlacement[] Groups => _groups == null ? Array.Empty<GroupPlacement>() : (GroupPlacement[])_groups.Clone();
        public Vector3 LastSourcePosition => _lastSourcePosition;
        public Quaternion LastSourceRotation => _lastSourceRotation;
        public bool ConstrainedToPhysicalArena => _constrainedToPhysicalArena;
        public PhysicalArenaDefinition PhysicalArena => _physicalArena;

#if UNITY_EDITOR
        /// <summary>Данные записывает только редакторский владелец компоновки.</summary>
        public void Configure(Transform source, Transform environment, Transform gameplay, GroupPlacement[] groups)
        {
            _sourceZone = source;
            _environmentRoot = environment;
            _gameplayRoot = gameplay;
            _groups = (GroupPlacement[])groups.Clone();
            RecordSourceFrame();
        }

        public void RecordSourceFrame()
        {
            _lastSourcePosition = _sourceZone.position;
            _lastSourceRotation = _sourceZone.rotation;
        }

        /// <summary>Потеря ссылки не снимает явно назначенное ограничение физической арены.</summary>
        public void SetPhysicalArenaConstraint(PhysicalArenaDefinition definition)
        {
            _constrainedToPhysicalArena = true;
            _physicalArena = definition;
        }
#endif
    }
}
