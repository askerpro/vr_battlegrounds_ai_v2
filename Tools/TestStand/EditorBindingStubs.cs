// Только внешние порты игры; Unity Editor API берётся из установленного SDK.
namespace Mirror
{
    public static class NetworkServer { public static bool active; }
    public static class NetworkClient { public static bool active; public static bool isConnected; }
    public interface PortTransport { ushort Port { get; set; } }
    public class Transport : UnityEngine.MonoBehaviour { }
    public class NetworkManager : UnityEngine.MonoBehaviour { public Transport transport; }
}
namespace Mirror.Discovery { public class NetworkDiscovery : UnityEngine.MonoBehaviour { } }
namespace UltimateXR.Core { public static class UxrManager { public static bool EditorFocusPauseEnabled { get; set; } } }
namespace VrBattlegrounds.DevTools { public static class DebugBootstrapGate { public static void Suppress(string value) { } } }
namespace VrBattlegrounds.Core
{
    public static class GameLog
    {
        public static readonly Channel Debug = new Channel();
        public sealed class Channel
        {
            public void Warning(string value) { }
            public void Verbose(string value) { }
            public void Error(string value) { }
        }
    }
}
