using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Локальная подсветка своей стены. Сверяет владельца с локальной сессией, включая поздний spawn,
    /// переподключение и смену сторон. Меняет только выделенные индикаторы, не оружие и не правила хвата.
    /// Материал индикаторов назначается префабом; источников света компонент не создаёт.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalOwnerHighlight : MonoBehaviour
    {
        [SerializeField] private Renderer[] _indicators = new Renderer[0];
        private ArsenalWallController _wall;
        private bool _presentationVisible = true;

        public void Configure(Renderer[] indicators)
        {
            SetVisible(false);
            _indicators = indicators ?? new Renderer[0];
            Refresh();
        }

        /// <summary>Общий визуальный gate: скрытый корпус не показывает даже owner-индикаторы.</summary>
        public void SetPresentationVisible(bool visible)
        {
            _presentationVisible = visible;
            Refresh();
        }

        private void Awake() => _wall = GetComponent<ArsenalWallController>();
        private void OnEnable() => Refresh();
        private void Update() => Refresh();
        private void OnDisable() => SetVisible(false);

        private void Refresh()
        {
            if (_wall == null) _wall = GetComponent<ArsenalWallController>();
            PlayerSession local = PlayerSession.LocalSession;
            bool visible = _presentationVisible && isActiveAndEnabled && _wall != null && local != null && local.netId != 0 &&
                           _wall.OwnerSessionNetId == local.netId;
            SetVisible(visible);
        }

        private void SetVisible(bool visible)
        {
            if (_indicators == null) return;
            foreach (Renderer indicator in _indicators)
                if (indicator != null && indicator.enabled != visible) indicator.enabled = visible;
        }
    }
}
