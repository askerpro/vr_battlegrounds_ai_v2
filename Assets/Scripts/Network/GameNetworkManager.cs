using Mirror;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сетевой менеджер игры на базе Mirror.
    /// Host = администратор арены, управляет матчем через MatchManager.
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            base.OnServerAddPlayer(conn);
            // TODO: назначить команду, зарегистрировать в MatchManager
        }
    }
}