// Только внешние порты игры; Unity Editor API берётся из установленного SDK.
namespace Mirror
{
    public static class NetworkServer { public static bool active; }
    public static class NetworkClient { public static bool active; public static bool isConnected; public static NetworkIdentity localPlayer; }
    public class NetworkIdentity : UnityEngine.MonoBehaviour { public uint netId; }
    public interface PortTransport { ushort Port { get; set; } }
    public class Transport : UnityEngine.MonoBehaviour { }
    public class NetworkManager : UnityEngine.MonoBehaviour { public static NetworkManager singleton; public Transport transport; }
}
namespace Mirror.Discovery { public class NetworkDiscovery : UnityEngine.MonoBehaviour { } }
namespace UltimateXR.Core
{
    public static class UxrManager
    {
        public static bool EditorFocusPauseEnabled { get; set; }
        public static void BindEditorFocusPauseProvider(System.Func<bool> provider) { }
        public static System.IDisposable BeginEditorFocusPauseOverride(bool value) => new Scope();
        private sealed class Scope : System.IDisposable { public void Dispose() { } }
    }
}
namespace VrBattlegrounds.Network
{
    public class GameNetworkDiscovery : UnityEngine.MonoBehaviour
    {
        public enum AppRole { Server, Host, Client }
        public AppRole? CurrentRole { get; }
        public static bool ClientManagerReady => true;
        public void RequestClientDisconnect() { }
        public void RequestClientConnect(string address) { }
    }
}
namespace VrBattlegrounds.Player { public class PlayerSession : Mirror.NetworkIdentity { public uint ActiveAvatarNetId; } }
namespace VrBattlegrounds.Managers { public class PlayersManager { public static PlayersManager Instance; public System.Collections.Generic.List<VrBattlegrounds.Player.PlayerSession> Sessions = new(); } }
namespace VrBattlegrounds.Managers
{
    // Только форма принятого API. Stub отказывает: он не подтверждает запуск карты.
    public sealed class StartupRouteHandle : System.IDisposable { public void Dispose() { } }
    public static class ServerStartupRoute
    {
        public static bool TryRequest(string scene, string mode, string owner, out StartupRouteHandle handle, out string error)
        { handle = null; error = "compile-stub"; return false; }
    }
}
namespace VrBattlegrounds.DevTools.E2E
{
    public class E2EContext { public static E2EContext Create(string scenario, string role, string path, float timeout, string map, int clients, string address, string token, bool external) => new(); }
    public class E2EResult { public string Status; public bool Passed; }
    public class E2ERunner : UnityEngine.MonoBehaviour { public bool Finished; public E2EResult Result; public static E2ERunner Begin(E2EContext context, bool quit) => null; }
}
namespace VrBattlegrounds.Maps.Runtime
{
    public struct StubRunKey { public bool IsValid => true; public override string ToString() => "stub"; }
    public class MapRoot : UnityEngine.MonoBehaviour { }
    public class MapRuntimeCatalog : UnityEngine.ScriptableObject
    {
        public VrBattlegrounds.Maps.MapRegistry Maps;
        public System.Collections.Generic.List<VrBattlegrounds.Maps.MapData> DebugMaps = new();
    }
    public static class MapRunAdmission { public static bool IsLocalPlayable => true; }
    public class MapBootstrap : UnityEngine.MonoBehaviour
    {
        public StubRunKey LocalRunKey => new StubRunKey();
        public static MapBootstrap ForScene(UnityEngine.SceneManagement.Scene scene) => null;
    }
}
namespace VrBattlegrounds.GameModes { public class GameModeData : UnityEngine.ScriptableObject { public string modeId; } }
namespace VrBattlegrounds.Maps
{
    public class MapData : UnityEngine.ScriptableObject { public string sceneName; public object kind; public VrBattlegrounds.GameModes.GameModeData[] supportedModes; }
    public class MapRegistry : UnityEngine.ScriptableObject { public MapData GetBySceneName(string name) => null; }
}
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
            public void Info(string value) { }
        }
    }
}
