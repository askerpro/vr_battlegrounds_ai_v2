using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Глобальное хранилище данных о типе локального устройства, роли и его привилегиях.
    /// Используется при подключении к серверу для отправки правильного сетевого сообщения.
    /// </summary>
    public static class LocalClientProfile
    {
        private static ClientDeviceType? _localDeviceOverride = null;
        private static bool? _localAdminOverride = null;
        private static GameRole? _localRoleOverride = null;

        public static ClientDeviceType LocalDeviceType
        {
            get
            {
                if (_localDeviceOverride.HasValue)
                    return _localDeviceOverride.Value;

#if BUILD_ROLE_ADMIN
                return ClientDeviceType.Tablet;
#else
                return ClientDeviceType.VR;
#endif
            }
        }

        public static bool IsLocalAdmin
        {
            get
            {
                if (_localAdminOverride.HasValue)
                    return _localAdminOverride.Value;

#if BUILD_ROLE_ADMIN
                return true;
#else
                return false;
#endif
            }
        }

        public static GameRole LocalRole
        {
            get
            {
                if (_localRoleOverride.HasValue)
                    return _localRoleOverride.Value;

#if BUILD_ROLE_ADMIN
                return GameRole.Spectator;
#else
                return GameRole.Player;
#endif
            }
        }

        /// <summary>
        /// Позволяет переопределить настройки во время отладки (например, из DebugOrchestrator).
        /// </summary>
        public static void SetDebugOverride(ClientDeviceType deviceType, bool isAdmin, GameRole role)
        {
            if (!Application.isEditor)
            {
                GameLog.Debug.Warning("[LocalClientProfile] Попытка переопределить настройки вне редактора!");
                return;
            }
            _localDeviceOverride = deviceType;
            _localAdminOverride = isAdmin;
            _localRoleOverride = role;
        }
    }
}
