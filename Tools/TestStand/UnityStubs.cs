// Только порт для проверки владельцев конфигурации; это не эмуляция Unity runtime.
namespace UnityEngine
{
    public static class Application
    {
        public static bool isEditor = true;
        public static string dataPath = "test-project/Assets";
        // Условия сборки и путь данных для E2EResult (артефакты, buildGuid): значения теста, не эмуляция платформы.
        public static string persistentDataPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrbg-harness-persistent");
        public static string buildGUID = "";
        public static string version = "0.0.0-harness";
        public static string unityVersion = "harness";
        public static RuntimePlatform platform = RuntimePlatform.WindowsEditor;
    }
    public enum RuntimePlatform { WindowsEditor = 7, WindowsPlayer = 2, Android = 11 }
    public static class Debug
    {
        public static bool isDebugBuild = true;
    }
    public static class PlayerPrefs
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> Values = new();
        public static int SaveCount;
        public static string GetString(string key, string fallback = "") => Values.TryGetValue(key, out var value) ? value : fallback;
        public static void SetString(string key, string value) => Values[key] = value;
        public static void Save() => SaveCount++;
    }
}
namespace VrBattlegrounds.Core
{
    public static class GameLog
    {
        public static void Error(string text) { }
        public static readonly Channel Debug = new();
        public sealed class Channel
        {
            public void Warning(string text) { }
            public void Verbose(string text) { }
        }
    }
}
