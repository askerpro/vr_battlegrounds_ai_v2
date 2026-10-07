// Только порт для проверки владельцев конфигурации; это не эмуляция Unity runtime.
namespace UnityEngine
{
    public static class Application
    {
        public static bool isEditor = true;
        public static string dataPath = "test-project/Assets";
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
