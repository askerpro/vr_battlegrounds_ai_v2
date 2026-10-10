using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UltimateXR.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>Provider checkout профиля и замороженного launch request; принадлежит Editor-сборке Bootstrap.</summary>
    [InitializeOnLoad]
    public static class PlayLaunchSettings
    {
        private static Func<PlayLaunchConfiguration> _resolver;
        private static Func<bool> _requestActive;
        private static PlayLaunchConfiguration _cached;
        private static long _stamp = long.MinValue;
        private static string _cachedPath;
        static PlayLaunchSettings() { UxrManager.BindEditorFocusPauseProvider(() => Effective.PauseOnFocusLoss); }
        public static bool RequestActive => _requestActive != null && _requestActive();
        public static bool HasLaunchConfiguration => _resolver?.Invoke() != null;
        public static PlayLaunchConfiguration Effective => (_resolver?.Invoke() ?? ReadProfile()).Copy();
        public static string CheckoutRoot
        {
            get { string path = Path.GetFullPath(Application.dataPath).Replace('\\', '/'); int clone = path.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase);
                return clone >= 0 ? path.Substring(0, clone) : Path.GetDirectoryName(path); }
        }
        public static PlayLaunchConfiguration Decode(string json)
        {
            var configuration = new PlayLaunchConfiguration();
            JsonUtility.FromJsonOverwrite(json, configuration);
            return configuration;
        }
        private static PlayLaunchProfileStore Store => new PlayLaunchProfileStore(CheckoutRoot,
            Decode, config => JsonUtility.ToJson(config, true), () => RequestActive, Environment.GetEnvironmentVariable("VRBG_LAUNCH_CONFIG"));
        public static string ProfilePath => Store.FilePath;
        public static void BindActiveLaunch(Func<PlayLaunchConfiguration> configuration, Func<bool> active)
        {
            if (_resolver != null) throw new PlayLaunchException("ResolverOwnerBusy", "Launch resolver уже зарегистрирован.");
            _resolver = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _requestActive = active ?? throw new ArgumentNullException(nameof(active));
        }
        public static PlayLaunchConfiguration ReadProfile()
        {
            var store = Store; long stamp = File.Exists(store.FilePath) ? File.GetLastWriteTimeUtc(store.FilePath).Ticks : 0;
            if (_cached == null || stamp != _stamp || _cachedPath != store.FilePath) { _cached = store.Read(); _stamp = stamp; _cachedPath = store.FilePath; }
            return _cached.Copy();
        }
        public static void SaveProfile(PlayLaunchConfiguration configuration)
        {
            var store = Store; store.Save(configuration); _cached = configuration.Copy(); _stamp = File.GetLastWriteTimeUtc(store.FilePath).Ticks; _cachedPath = store.FilePath;
        }
        public static void Change(Action<PlayLaunchConfiguration> change)
        {
            var previous = ReadProfile(); var next = previous.Copy(); change(next);
            if (JsonUtility.ToJson(previous) != JsonUtility.ToJson(next)) SaveProfile(next);
        }
        /// <summary>Явный импорт человека; общие ключи не изменяются.</summary>
        public static void ImportLegacyProfile()
        {
            string prefix = DebugBootstrapSettings.KeyPrefix;
            var config = new PlayLaunchConfiguration {
                Enabled = EditorPrefs.GetBool(prefix + "Enabled", true), HostIsAdmin = EditorPrefs.GetBool(prefix + "HostIsAdmin", true),
                AutoGoLive = EditorPrefs.GetBool(prefix + "AutoGoLive", true), MinPlayers = Math.Max(0, EditorPrefs.GetInt(prefix + "MinPlayersOverride", 1)),
                BotCount = Math.Max(0, EditorPrefs.GetInt(prefix + "BotCount", 0)), ModeId = EditorPrefs.GetString(prefix + "AutoGameModeId", "elimination"),
                ScreenshotOnButtonB = EditorPrefs.GetBool(prefix + "ScreenshotOnButtonB", false),
                PauseOnFocusLoss = EditorPrefs.GetBool("VrBattlegrounds.PauseXrWhenEditorUnfocused", true) };
            config.Role = EditorPrefs.GetBool(prefix + "AutoStartFallbackRole", true)
                ? ((VrBattlegrounds.Network.GameNetworkDiscovery.AppRole)EditorPrefs.GetInt(prefix + "FallbackRole", 1)).ToString().ToLowerInvariant() : "ask";
            SaveProfile(config);
        }
    }
}
