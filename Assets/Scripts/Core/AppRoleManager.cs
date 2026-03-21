using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Глобальное хранилище текущей роли устройства (VR-игрок или планшет-админ).
    /// Эта роль используется при подключении к серверу для инстанцирования правильного префаба.
    /// </summary>
    public static class AppRoleManager
    {
        private static ClientDeviceRole? _localRoleOverride = null;

        public static ClientDeviceRole LocalRole
        {
            get
            {
                if (_localRoleOverride.HasValue)
                {
                    return _localRoleOverride.Value;
                }

#if BUILD_ROLE_ADMIN
                return ClientDeviceRole.TabletAdmin;
#else
                return ClientDeviceRole.VRPlayer;
#endif
            }
        }

        /// <summary>
        /// Позволяет переопределить роль во время отладки (например, из DebugOrchestrator).
        /// </summary>
        public static void SetDebugOverride(ClientDeviceRole role)
        {
            if (!Application.isEditor)
            {
                Debug.LogWarning("[AppRoleManager] Попытка переопределить роль вне редактора!");
                return;
            }
            _localRoleOverride = role;
        }
    }
}
