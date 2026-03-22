using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сообщение, которое клиент отправляет серверу при подключении, 
    /// чтобы запросить нужный префаб (VR игрок, планшет админа и т.д.).
    /// </summary>
    public struct PlayerJoinMessage : NetworkMessage
    {
        public ClientDeviceRole role;
        
        // Опциональные параметры для сохранения позиции между сценами
        public bool hasSavedPosition;
        public UnityEngine.Vector3 savedPosition;
        public UnityEngine.Quaternion savedRotation;
    }
}
