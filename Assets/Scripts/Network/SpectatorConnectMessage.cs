using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сообщение для подключения зрителей или администраторов (Spectator).
    /// </summary>
    public struct SpectatorConnectMessage : NetworkMessage
    {
        public string deviceToken; // Уникальный ключ
        public ClientDeviceType deviceType; // Tablet, PC, VR
        public bool isAdmin;
    }
}
