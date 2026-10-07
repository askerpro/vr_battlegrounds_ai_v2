using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Первичный снимок своего игрока до спавна сессии. Все значения, включая позу корня
    /// относительно якорей, сериализуются внутри calibration. Клиент без памяти не затирает
    /// снимок сервера. Применение и нормализация принадлежат PlayerSession.
    /// </summary>
    public struct GamePlayerConnectMessage : NetworkMessage
    {
        public string deviceToken;
        public ClientDeviceType deviceType;
        public int teamId;
        public int avatarId;
        public bool hasCalibration;
        public PlayerCalibration calibration;

        // Старые входы E2E/харнесса — свойства-проекции. Mirror сериализует только calibration,
        // никакой самостоятельной mutable копии позы здесь нет.
        public bool hasAnchorPlace
        {
            get => calibration.Placement.IsAnchored;
            set
            {
                calibration = calibration.WithPlacement(value
                    ? PlayerPlacement.Anchored(anchorPlacePosition, anchorPlaceRotation, anchorPlaceMap)
                    : PlayerPlacement.None);
            }
        }
        public UnityEngine.Vector3 anchorPlacePosition
        {
            get => calibration.Placement.Position;
            set { calibration = calibration.WithPlacement(PlayerPlacement.Anchored(value, anchorPlaceRotation, anchorPlaceMap)); }
        }
        public UnityEngine.Quaternion anchorPlaceRotation
        {
            get => calibration.Placement.HasValue ? calibration.Placement.Rotation : UnityEngine.Quaternion.identity;
            set { calibration = calibration.WithPlacement(PlayerPlacement.Anchored(anchorPlacePosition, value, anchorPlaceMap)); }
        }
        public string anchorPlaceMap
        {
            get => calibration.Placement.CapturedOnMap ?? string.Empty;
            set { calibration = calibration.WithPlacement(PlayerPlacement.Anchored(anchorPlacePosition, anchorPlaceRotation, value)); }
        }

        public GamePlayerConnectMessage(string token, ClientDeviceType device, int team, int avatar)
        {
            deviceToken = token;
            deviceType = device;
            teamId = team;
            avatarId = avatar;
            hasCalibration = LocalPlayerCalibration.HasValue;
            calibration = LocalPlayerCalibration.Current;
        }
    }
}
