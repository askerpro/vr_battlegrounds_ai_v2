using System;
using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>Один источник идентичности подключения; тестовый токен не записывается в PlayerPrefs.</summary>
    public static class ClientDeviceIdentity
    {
        private static TemporaryToken _temporary;
        public static bool HasTemporaryToken => _temporary != null;

        public static string BaseToken
        {
            get
            {
                if (_temporary != null) return _temporary.Value;
                string token = PlayerPrefs.GetString("DeviceToken", "");
                if (!string.IsNullOrEmpty(token)) return token;
                token = Guid.NewGuid().ToString();
                PlayerPrefs.SetString("DeviceToken", token);
                PlayerPrefs.Save();
                return token;
            }
        }

        /// <summary>Существующий Editor-суффикс остаётся частью сетевой, а не управляющей идентичности.</summary>
        public static string ConnectionToken
        {
            get
            {
                string token = BaseToken;
#if UNITY_EDITOR
                token += "_editor_" + Application.dataPath.GetHashCode();
#endif
                return token;
            }
        }

        public static IDisposable BeginTemporaryToken(string token)
        {
            if (string.IsNullOrEmpty(token)) throw new ArgumentException("Тестовый токен не может быть пустым.");
            if (_temporary != null) throw new InvalidOperationException("У идентичности уже есть временный владелец.");
            var scope = new TemporaryToken(token);
            _temporary = scope;
            return scope;
        }

        private sealed class TemporaryToken : IDisposable
        {
            public readonly string Value;
            public TemporaryToken(string value) { Value = value; }
            public void Dispose() { if (ReferenceEquals(_temporary, this)) _temporary = null; }
        }
    }
}
