using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player
{
    public class SpectatorController : NetworkBehaviour
    {
        [Header("References")]
        // Real avatar object
        public GameObject Geo = null;

        // Ghost avatar object
        public GameObject Ghost = null;

        [SyncVar(hook = nameof(OnSpectatingChanged))]
        private bool _isSpectating = false;

        public void ToggleSpectator()
        {
            if (!isServer) return;
            SetSpectating(!_isSpectating);
        }

        public bool IsSpectating()
        {
            return _isSpectating;
        }

        [Server]
        public void SetSpectating(bool value)
        {
            if (_isSpectating == value) return;
            
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[SpectatorController] {name}: режим наблюдения {(value ? "включен" : "выключен")} (сервер)", this);
            _isSpectating = value;
        }

        public void StartSpectating()
        {
            if (!isServer) return;
            SetSpectating(true);
        }

        public void EndSpectating()
        {
            if (!isServer) return;
            SetSpectating(false);
        }

        private void OnSpectatingChanged(bool oldValue, bool newValue)
        {
            GameLog.Verbose(GameSettings.Instance.LogLevelPlayer, $"[SpectatorController] {name}: SyncVar наблюдателя изменено {oldValue} -> {newValue}", this);
            UpdateVisuals(newValue);
        }

        private void UpdateVisuals(bool spectating)
        {
            GameLog.Verbose(GameSettings.Instance.LogLevelPlayer, $"[SpectatorController] {name}: обновление визуализации (spectating={spectating})", this);
            if (Geo != null) Geo.SetActive(!spectating);
            if (Ghost != null) Ghost.SetActive(spectating);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            UpdateVisuals(_isSpectating);
        }
    }
}
