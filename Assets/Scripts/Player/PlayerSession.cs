using Mirror;
using System;
using VrBattlegrounds.Managers;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

using VrBattlegrounds.Player.Avatars;
namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Сохраняемая сессия игрока. В отличие от аватара (куклы), этот объект не уничтожается
    /// при смене скина (горячей замене префаба). Он хранит счет, ID команды, никнейм и токен устройства.
    /// Когда игрок переподключается, сервер может восстановить эту сессию по deviceToken.
    /// </summary>
    public class PlayerSession : NetworkBehaviour
    {
        public static PlayerSession LocalSession { get; private set; }

        // ── События сервера ───────────────────────────────────────────────────
        public event Action<PlayerSession> OnSessionReady;

        // ── Сетевые данные (Хранятся на сервере, синхронизируются всем) ───────

        [SyncVar] public string DeviceToken = string.Empty;

        [SyncVar] public ClientDeviceType DeviceType;
        [SyncVar] public bool IsAdmin;
        [SyncVar] public GameRole Role;

        [SyncVar(hook = nameof(OnPlayerNameChanged))]
        public string PlayerName = "Player";

        [SyncVar(hook = nameof(OnTeamIndexChanged))]
        public int TeamIndex = 0;

        [SyncVar] public int AvatarIndex = 0;

        [SyncVar] public int Kills = 0;
        [SyncVar] public int Deaths = 0;
        [SyncVar] public int Score = 0;

        // ── Статус готовности (Round State) ───────────────────────────────────
        
        [SyncVar] public bool IsInSpawnZone = false;
        [SyncVar] public bool HasGrabbedDogTag = false;

        public bool IsReadyForRound => IsInSpawnZone && HasGrabbedDogTag;

        // Ссылка на текущий физический аватар (куклу).
        // Может на клиенте быть null, если скин ещё не заспавнился.
        public PlayerController ActiveAvatar { get; set; }

        public TeamData Team => TeamRegistry.Instance?.GetByIndex(TeamIndex);

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {netId} started on server for {PlayerName}.");
            OnSessionReady?.Invoke(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (isLocalPlayer)
            {
                LocalSession = this;
            }
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {netId} started on client for {PlayerName}.");
        }

        // ── SyncVar Hooks ─────────────────────────────────────────────────────

        private void OnPlayerNameChanged(string oldName, string newName)
        {
            gameObject.name = $"PlayerSession_{newName}";
            GameLog.Info(GameSettings.Instance.LogLevelDebug, $"[PlayerSession] {netId} name changed → {newName}");
        }

        private void OnTeamIndexChanged(int oldIndex, int newIndex)
        {
            TeamData team = TeamRegistry.Instance?.GetByIndex(newIndex);
            GameLog.Info(GameSettings.Instance.LogLevelDebug,
                $"[PlayerSession] {PlayerName} команда изменена → {(team != null ? team.displayName : "нет")}");
        }

        // ── Клиентские команды ────────────────────────────────────────────────

        [Command]
        public void CmdRequestAvatarChange(int newAvatarId)
        {
            if (AvatarManager.Instance != null)
            {
                GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {PlayerName}: Клиент запросил смену скина на ID {newAvatarId}");
                AvatarManager.Instance.ChangeAvatar(connectionToClient, this, this.TeamIndex, newAvatarId);
            }
        }

        [Command]
        public void CmdRequestTeamChange(int newTeamId, int newAvatarId)
        {
            if (GameplayManager.Instance != null)
            {
                GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {PlayerName}: Клиент запросил смену команды на {newTeamId} и скина на {newAvatarId}");
                GameplayManager.Instance.ProcessTeamChangeRequest(this, newTeamId, newAvatarId);
            }
            else if (AvatarManager.Instance != null)
            {
                // Если матч не идет, меняем напрямую
                AvatarManager.Instance.ChangeAvatar(connectionToClient, this, newTeamId, newAvatarId);
            }
        }

        [Command]
        public void CmdSetDogTagGrabbed(bool state)
        {
            HasGrabbedDogTag = state;
            GameLog.Verbose(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {PlayerName} dog tag grabbed set to {state}");
        }

        [Server]
        public void ServerSetInSpawnZone(bool state)
        {
            IsInSpawnZone = state;
        }

        [Server]
        public void ServerResetRoundReadiness()
        {
            HasGrabbedDogTag = false;
            IsInSpawnZone = false;
        }
    }
}
