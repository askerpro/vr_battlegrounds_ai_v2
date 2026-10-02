using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>Нематериальная сетка заменяет видимые лазерные стены, сохраняя правила логической зоны.</summary>
    public sealed class SpawnZoneBoundaryVisual : MonoBehaviour
    {
        [SerializeField] private TeamSpawnZone _zone;
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private bool _frontFaceOnly;
        public bool FrontFaceOnly => _frontFaceOnly;

        public void Configure(TeamSpawnZone zone, Renderer[] renderers, bool frontFaceOnly = false)
        { _zone = zone; _renderers = renderers; _frontFaceOnly = frontFaceOnly; }

        private void OnEnable()
        {
            if(_zone==null)return;
            _zone.BorderVisibilityChanged+=ApplyVisibility;
            ApplyVisibility(_zone.BorderVisible);
        }

        private void OnDisable()
        {
            if(_zone==null)return;
            _zone.BorderVisibilityChanged-=ApplyVisibility;
        }

        private void ApplyVisibility(bool visible)
        {
            if(_renderers==null)return;
            foreach(var renderer in _renderers)if(renderer!=null)renderer.enabled=visible;
        }
    }
}
