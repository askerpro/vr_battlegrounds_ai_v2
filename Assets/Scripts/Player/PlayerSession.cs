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

        /// <summary>
        /// Аватар локального игрока появился, сменился (скин, карта) или исчез (null).
        /// Замена опроса в <c>Update</c>: клиентскому коду больше не нужно каждый кадр
        /// спрашивать «мой аватар уже заспавнился?».
        /// На выделенном сервере не срабатывает — там нет локальной сессии.
        /// </summary>
        public static event Action<PlayerController> LocalAvatarChanged;

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

        // ── Связь с аватаром ──────────────────────────────────────────────────

        /// <summary>
        /// netId текущего физического аватара (куклы) — единственный источник правды о связи.
        /// Реплицируется, поэтому «где мой аватар» может спросить и клиент, а не только сервер.
        /// Ноль означает «аватара нет»: ещё не заспавнен или уничтожен при смене карты.
        /// </summary>
        [SyncVar(hook = nameof(OnActiveAvatarNetIdChanged))]
        public uint ActiveAvatarNetId;

        /// <summary>Разрешённая ссылка на аватар. Кэш, источник правды — <see cref="ActiveAvatarNetId"/>.</summary>
        private PlayerController _activeAvatar;

        /// <summary>
        /// Текущий физический аватар (кукла). Доступен и на сервере, и на клиенте.
        ///
        /// Ссылка кэшируется. Если кэш пуст — аватар уничтожен либо netId приехал раньше,
        /// чем сам объект заспавнился, — выполняется отложенное разрешение по netId.
        /// Присваивать имеет смысл только на сервере: он владеет <see cref="ActiveAvatarNetId"/>.
        /// </summary>
        public PlayerController ActiveAvatar
        {
            get
            {
                // Сравнение с null по-Unity ловит и уничтоженный аватар (смена скина, смена карты).
                if (_activeAvatar == null) ResolveActiveAvatar();
                return _activeAvatar;
            }
            set
            {
                if (value != null && value.netId == 0)
                {
                    GameLog.Warning(GameSettings.Instance.LogLevelPlayer,
                        $"[PlayerSession] {PlayerName}: аватар назначен до NetworkServer.Spawn — netId ещё 0, клиенты связь не получат.");
                }

                ActiveAvatarNetId = value != null ? value.netId : 0u;

                // На выделенном сервере хук SyncVar не вызывается (Mirror зовёт его только
                // в host-режиме), поэтому связь проставляем здесь же. Повторный вызов
                // из хука безвреден — LinkAvatar идемпотентен.
                LinkAvatar(value);
            }
        }

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

                // Хук ActiveAvatarNetId мог отработать раньше: Mirror применяет SyncVar
                // до вызова OnStartClient, и тогда LocalSession ещё не был назначен,
                // а событие ушло «в никуда». Досылаем текущее состояние подписчикам.
                LocalAvatarChanged?.Invoke(ActiveAvatar);
            }
            GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[PlayerSession] {netId} started on client for {PlayerName}.");
        }

        /// <summary>
        /// Клиент отключился или сессию деспавнили. Снимаем статическую ссылку, иначе она
        /// указывает на уничтоженный объект: <see cref="LocalSession"/> — единственный путь
        /// клиентского кода к своему аватару (T-11), и висящая ссылка выглядит как живая.
        ///
        /// Сравнение с <c>this</c> обязательно. При переподключении новая сессия успевает
        /// встать в <see cref="LocalSession"/> раньше, чем Mirror доберётся до деспавна
        /// старой, — безусловное обнуление стёрло бы ссылку на актуальную сессию.
        /// </summary>
        public override void OnStopClient()
        {
            base.OnStopClient();

            if (LocalSession == this)
            {
                LocalSession = null;
            }
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

        /// <summary>
        /// Пришёл новый netId аватара. Разрешаем его в ссылку на каждой машине.
        /// Если объекта ещё нет в spawned (порядок доставки спавнов не гарантирован),
        /// связь закроется с другой стороны — из <c>PlayerController.OnStartClient</c>.
        /// </summary>
        private void OnActiveAvatarNetIdChanged(uint oldNetId, uint newNetId)
        {
            ResolveActiveAvatar();
        }

        // ── Разрешение связи ──────────────────────────────────────────────────

        /// <summary>Ищет аватар по <see cref="ActiveAvatarNetId"/> и обновляет кэш.</summary>
        private void ResolveActiveAvatar()
        {
            if (ActiveAvatarNetId == 0)
            {
                LinkAvatar(null);
                return;
            }

            NetworkIdentity identity = Mirror.Utils.GetSpawnedInServerOrClient(ActiveAvatarNetId);
            LinkAvatar(identity != null ? identity.GetComponent<PlayerController>() : null);
        }

        /// <summary>
        /// Ставит ссылку с обеих сторон и оповещает подписчиков, если это локальный игрок.
        /// Идемпотентен: повторный вызов с той же ссылкой ничего не делает.
        /// </summary>
        private void LinkAvatar(PlayerController avatar)
        {
            // Именно ReferenceEquals, а не ==: уничтоженный аватар по-Unity равен null,
            // и переход «был аватар → его больше нет» иначе остался бы незамеченным.
            if (ReferenceEquals(_activeAvatar, avatar)) return;

            _activeAvatar = avatar;

            if (avatar != null) avatar.LinkSession(this);

            if (LocalSession == this) LocalAvatarChanged?.Invoke(avatar);
        }

        /// <summary>
        /// Аватар сообщает, что он заспавнился. Закрывает гонку «netId приехал раньше объекта».
        /// Чужой аватар игнорируется: источник правды о связи — <see cref="ActiveAvatarNetId"/>,
        /// который ставит только сервер.
        /// </summary>
        internal void NotifyAvatarSpawned(PlayerController avatar)
        {
            if (avatar == null || avatar.netId != ActiveAvatarNetId) return;
            LinkAvatar(avatar);
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
