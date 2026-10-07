using System;
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
        private static TemporaryScope _temporaryScope;

        public static bool HasTemporaryOverride => _temporaryScope != null;

        /// <summary>Диагностический ключ сохраняет различие между override и его отсутствием.</summary>
        public static string OverrideStateKey => (_localDeviceOverride?.ToString() ?? "~") + "/" +
            (_localAdminOverride?.ToString() ?? "~") + "/" + (_localRoleOverride?.ToString() ?? "~");

        /// <summary>Единственный временный владелец профиля; Dispose восстанавливает исходные nullable-поля.</summary>
        public static IDisposable BeginTemporaryOverride(ClientDeviceType deviceType, bool isAdmin, GameRole role, string restoreStateKey = null)
        {
            if (!Application.isEditor) throw new InvalidOperationException("Временный профиль стенда доступен только в Editor.");
            if (_temporaryScope != null) throw new InvalidOperationException("У профиля уже есть временный владелец.");
            var scope = new TemporaryScope(restoreStateKey);
            _temporaryScope = scope;
            _localDeviceOverride = deviceType;
            _localAdminOverride = isAdmin;
            _localRoleOverride = role;
            return scope;
        }

        private sealed class TemporaryScope : IDisposable
        {
            private readonly ClientDeviceType? _device = _localDeviceOverride;
            private readonly bool? _admin = _localAdminOverride;
            private readonly GameRole? _role = _localRoleOverride;

            public TemporaryScope(string restoreStateKey)
            {
                if (restoreStateKey == null) return;
                string[] parts = restoreStateKey.Split('/');
                if (parts.Length != 3) throw new ArgumentException("Некорректный снимок профиля.");
                _device = parts[0] == "~" ? (ClientDeviceType?)null : (ClientDeviceType)Enum.Parse(typeof(ClientDeviceType), parts[0]);
                _admin = parts[1] == "~" ? (bool?)null : bool.Parse(parts[1]);
                _role = parts[2] == "~" ? (GameRole?)null : (GameRole)Enum.Parse(typeof(GameRole), parts[2]);
            }

            public void Dispose()
            {
                if (!ReferenceEquals(_temporaryScope, this)) return;
                _localDeviceOverride = _device;
                _localAdminOverride = _admin;
                _localRoleOverride = _role;
                _temporaryScope = null;
            }
        }

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
        /// Позволяет переопределить настройки во время отладки (например, из DebugOrchestrator — отладочного сценария редактора).
        /// </summary>
        public static void SetDebugOverride(ClientDeviceType deviceType, bool isAdmin, GameRole role)
        {
            if (HasTemporaryOverride)
            {
                GameLog.Debug.Verbose("[LocalClientProfile] Отладочный override пропущен: профилем владеет стенд.");
                return;
            }
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
