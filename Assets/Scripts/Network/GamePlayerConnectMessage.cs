using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сообщение для подключения полноценного участника матча (Player).
    /// </summary>
    public struct GamePlayerConnectMessage : NetworkMessage
    {
        public string deviceToken; // Уникальный ключ
        public ClientDeviceType deviceType; // VR, ПК
        public int teamId;
        public int avatarId;
        public bool hasSavedPosition;
        public UnityEngine.Vector3 savedPosition;
        public UnityEngine.Quaternion savedRotation;

        public GamePlayerConnectMessage(string token, ClientDeviceType device, int team, int avatar)
        {
            deviceToken = token;
            deviceType = device;
            teamId = team;
            avatarId = avatar;
            hasSavedPosition = false;
            savedPosition = UnityEngine.Vector3.zero;
            savedRotation = UnityEngine.Quaternion.identity;

            if (PhysicalSpaceUtils.PhysicalSpaceSyncManager.Instance != null &&
                PhysicalSpaceUtils.PhysicalSpaceSyncManager.Instance.TryGetSavedAvatarTransform(out UnityEngine.Vector3 pos, out UnityEngine.Quaternion rot))
            {
                hasSavedPosition = true;
                savedPosition = pos;
                savedRotation = rot;
            }
        }
    }
}
