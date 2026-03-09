// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrMirrorAvatar.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;
#if ULTIMATEXR_USE_MIRROR_SDK
using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Core.Settings;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Extensions.System;
using UltimateXR.Extensions.System.Collections;
using UltimateXR.Core.Instantiation;
using Mirror;
#endif

namespace UltimateXR.Networking.Integrations.Net.Mirror
{
#if ULTIMATEXR_USE_MIRROR_SDK
    public class UxrMirrorAvatar : NetworkBehaviour, IUxrNetworkAvatar
    {
        #region Inspector Properties/Serialized Fields

        [Tooltip("List of objects that will be disabled when the avatar is in local mode, to avoid intersections with the camera for example")][SerializeField] private List<GameObject> _localDisabledGameObjects;

        #endregion

        #region Implicit IUxrNetworkAvatar

        /// <inheritdoc />
        public IList<GameObject> LocalDisabledGameObjects => _localDisabledGameObjects;

        /// <inheritdoc />
        public bool IsLocal { get; private set; }

        /// <inheritdoc />
        public UxrAvatar Avatar { get; private set; }

        /// <inheritdoc />
        public string AvatarName
        {
            get => _avatarName;
            set
            {
                _avatarName = value;

                if (Avatar != null)
                {
                    Avatar.name = value;
                }
            }
        }

        /// <inheritdoc />
        public event Action AvatarSpawned;

        /// <inheritdoc />
        public event Action AvatarDespawned;

        /// <inheritdoc />
        public void InitializeNetworkAvatar(UxrAvatar avatar, bool isLocal, string uniqueId, string avatarName)
        {
            // Удаляем статическую проверку, чтобы позволить повторную инициализацию при смене сцены
            if (_avatarInitialized && Avatar == avatar)
            {
                // Если уже инициализирован тот же аватар, просто обновляем статус ownership
                if (IsLocal != isLocal)
                {
                    IsLocal = isLocal;
                    avatar.AvatarMode = isLocal ? UxrAvatarMode.Local : UxrAvatarMode.UpdateExternally;

                    if (isLocal)
                    {
                        LocalDisabledGameObjects.ForEach(o => o.SetActive(false));
                    }

                    if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
                    {
                        Debug.Log($"{UxrConstants.NetworkingModule} Re-initializing avatar ownership: {avatarName}, IsLocal={isLocal}, NetId={netId}, UniqueId={uniqueId}");
                    }
                }
                return;
            }

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Initializing avatar: {avatarName}, IsLocal={isLocal}, NetId={netId}, UniqueId={uniqueId}");
            }

            IsLocal = isLocal;
            Avatar = avatar;
            AvatarName = avatarName;
            avatar.AvatarMode = isLocal ? UxrAvatarMode.Local : UxrAvatarMode.UpdateExternally;

            if (isLocal)
            {
                LocalDisabledGameObjects.ForEach(o => o.SetActive(false));
            }

            avatar.CombineUniqueId(uniqueId.GetGuid(), true);

            // Вызываем событие спавна аватара
            AvatarSpawned?.Invoke();

            if (UxrInstanceManager.HasInstance)
            {
                UxrInstanceManager.Instance.NotifyNetworkSpawn(Avatar.gameObject);
            }

            _avatarInitialized = true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Request authority of the local avatar over an object.
        /// </summary>
        /// <param name="networkIdentity">The object to get authority over</param>
        public void RequestAuthority(NetworkIdentity networkIdentity)
        {
            CmdRequestAuthority(networkIdentity);
        }

        #endregion

        #region Event Handling Methods

        private void UxrManager_ComponentStateChanged(IUxrStateSync component, UxrSyncEventArgs eventArgs)
        {
            // If we are a client, we only send events for objects we own OR if we are the local player sending a command.
            // If we are the server, we send events for any object that changed its state (like damaged NPCs or environmental objects).
            
            // На клиенте: шлём только если мы - владельцы (локальный игрок).
            // На сервере: шлём если мы - "мастер" (назначенный вещатель), чтобы избежать дубликатов RPC от каждого игрока.
            if (isServer)
            {
                if (_serverBroadcaster != this)
                {
                    return;
                }
            }
            else if (!isOwned)
            {
                 return;
            }

            if (eventArgs.Options.HasFlag(UxrStateSyncOptions.Network))
            {
                byte[] serializedEvent = eventArgs.SerializeEventBinary(component);

                if (serializedEvent != null)
                {
                    if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
                    {
                        Debug.Log($"{UxrConstants.NetworkingModule} Sending state sync: {component.Component.name} ({component.UniqueId}), Event: {eventArgs.GetType().Name}, Size: {serializedEvent.Length}. IsServer: {isServer}, IsOwned: {isOwned}");
                    }

                    if (isServer)
                    {
                        // Мы на сервере (Host или Dedicated), рассылаем всем клиентам
                        RpcComponentStateChanged(serializedEvent);
                    }
                    else
                    {
                        // Мы на клиенте, отправляем команду серверу
                        CmdComponentStateChanged(serializedEvent);
                    }
                }
            }
        }

        #endregion

        #region Event Trigger Methods

        public override void OnStartServer()
        {
            Avatar = GetComponent<UxrAvatar>();

            InitializeNetworkAvatar(Avatar, netIdentity.isOwned, netId.ToString(), $"Player {netId} ({(netIdentity.isOwned ? "Local" : "External")})");

            // На сервере подписываемся на события изменений, чтобы транслировать их клиентам (для NPC и прочего)
            UxrManager.ComponentStateChanged += UxrManager_ComponentStateChanged;
            
            if (_serverBroadcaster == null)
            {
                _serverBroadcaster = this;
            }

            base.OnStartServer();
        }

        public override void OnStopServer()
        {
            UxrManager.ComponentStateChanged -= UxrManager_ComponentStateChanged;
            
            if (_serverBroadcaster == this)
            {
                _serverBroadcaster = null;
            }
            
            base.OnStopServer();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} OnStartLocalPlayer: NetId={netId}, isOwned={netIdentity.isOwned}");
            }

            // Принудительная инициализация как локального аватара
            if (!_avatarInitialized || !IsLocal)
            {
                Avatar = GetComponent<UxrAvatar>();
                InitializeNetworkAvatar(Avatar, true, netId.ToString(), $"Player {netId} (Local)");
            }

            UxrManager.ComponentStateChanged += UxrManager_ComponentStateChanged;

            if (!netIdentity.isServer)
            {
                byte[] localAvatarState = UxrManager.Instance.SaveStateChanges(new List<GameObject> { Avatar.gameObject }, null, UxrStateSaveLevel.ChangesSinceBeginning, UxrGlobalSettings.Instance.NetFormatInitialState);

                if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
                {
                    Debug.Log($"{UxrConstants.NetworkingModule} Requesting global state and sending local avatar state in {localAvatarState.Length} bytes.");
                }

                // Send the initial avatar state to the server and request the current scene state.  
                // Call after AvatarSpawned() in case any event handler changes the avatar state.
                CmdNewAvatarJoined(localAvatarState);
            }
            else
            {
                // Server creates the session and doesn't need to send the initial state.
                _initialStateLoaded = true;
                
                // На Хосте OnStartServer уже подписал нас на события, но OnStartLocalPlayer вызывается тоже.
                // Нам не нужно подписываться дважды.
            }

            Debug.Log($"{UxrConstants.NetworkingModule} {nameof(UxrMirrorAvatar)}.{nameof(OnStartLocalPlayer)}: Is Local? {IsLocal}, Name: {AvatarName}. NetId: {netId}, UniqueId: {Avatar.UniqueId}.");
        }

        /// <inheritdoc />
        public override void OnStartClient()
        {
            Avatar = GetComponent<UxrAvatar>();

            InitializeNetworkAvatar(Avatar, netIdentity.isOwned, netId.ToString(), $"Player {netId} ({(netIdentity.isOwned ? "Local" : "External")})");

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} {nameof(UxrMirrorAvatar)}.{nameof(OnStartClient)}: Is Local? {IsLocal}, Name: {AvatarName}. NetId: {netId}, UniqueId: {Avatar.UniqueId}.");
            }

            base.OnStartClient();
        }

        /// <inheritdoc />
        public override void OnStopClient()
        {
            if (Avatar && netIdentity.isOwned)
            {
                UxrManager.ComponentStateChanged -= UxrManager_ComponentStateChanged;
            }

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} {nameof(UxrMirrorAvatar)}.{nameof(OnStopClient)}: Is Local? {IsLocal}, Name: {AvatarName}");
            }

            AvatarDespawned?.Invoke();

            base.OnStopClient();
        }

        /// <summary>
        /// Вызывается при уничтожении объекта для корректной очистки ресурсов
        /// </summary>
        private void OnDestroy()
        {
            if (netIdentity && netIdentity.isOwned)
            {
                UxrManager.ComponentStateChanged -= UxrManager_ComponentStateChanged;
            }

            _avatarInitialized = false;

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} {nameof(UxrMirrorAvatar)}.{nameof(OnDestroy)}: Is Local? {IsLocal}, Name: {AvatarName}, NetId: {(netIdentity ? netId.ToString() : "null")}");
            }

            if (UxrInstanceManager.HasInstance)
            {
                UxrInstanceManager.Instance.DestroyGameObject(Avatar.gameObject);
            }
        }

        /// <summary>
        /// Обработка события изменения сцены в Mirror
        /// </summary>
        /// <param name="sceneName">Имя новой сцены</param>
        public void OnNetworkSceneChanged(string sceneName)
        {
            // Сбрасываем флаг загрузки начального состояния, так как мы в новой сцене
            _initialStateLoaded = false;

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Scene changed to {sceneName}, reset initial state loading flag.");
            }

            // NetworkBehaviour не имеет базовой реализации OnNetworkSceneChanged,
            // поэтому мы не вызываем здесь base метод
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Server RPC to request the current global state upon joining.
        /// </summary>
        /// <param name="avatarState">The initial state of the avatar that joined</param>
        /// <param name="sender">Information filled by Mirror with information about the sender</param>
        [Command]
        private void CmdNewAvatarJoined(byte[] avatarState, NetworkConnectionToClient sender = null)
        {
            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Verbose)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Received request for global state from client {sender.identity.netId.ToString()}.");
            }

            // First load the avatar state
            UxrManager.Instance.LoadStateChanges(avatarState);

            // Now export the scenario state, except for the new avatar, and send it back
            byte[] serializedState = UxrManager.Instance.SaveStateChanges(null, new List<GameObject> { gameObject }, UxrStateSaveLevel.ChangesSinceBeginning, UxrGlobalSettings.Instance.NetFormatInitialState);

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Verbose)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Sending global state in {serializedState.Length} bytes to client {sender.identity.netId.ToString()}. Broadcasting {avatarState.Length} bytes to sync new avatar.");
            }

            // Send global state to new user.
            TargetLoadGlobalState(sender, serializedState);

            // Broadcast initial state of new avatar.
            RpcLoadAvatarState(avatarState);
        }

        /// <summary>
        ///     Server RPC to propagate state change events to all other clients.
        /// </summary>
        /// <param name="serializedEventData">The serialized state change data</param>
        [Command]
        private void CmdComponentStateChanged(byte[] serializedEventData)
        {
            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Server received CmdComponentStateChanged. Size: {serializedEventData.Length}. Applying and broadcasting.");
            }

            // Сервер применяет состояние у себя
            UxrManager.Instance.ExecuteStateSyncEvent(serializedEventData);

            // И рассылает остальным клиентам
            RpcComponentStateChanged(serializedEventData);
        }

        /// <summary>
        ///     Server RPC requesting authority over an object.
        /// </summary>
        /// <param name="networkIdentity">Object to get authority over</param>
        [Command]
        private void CmdRequestAuthority(NetworkIdentity networkIdentity)
        {
            networkIdentity.AssignClientAuthority(netIdentity.connectionToClient);
        }

        /// <summary>
        ///     Targeted client RPC to client that joined to sync to the current state.
        /// </summary>
        /// <param name="target">Target of the RPC</param>
        /// <param name="serializedStateData">The serialized state data</param>
        [TargetRpc]
        private void TargetLoadGlobalState(NetworkConnectionToClient target, byte[] serializedStateData)
        {
            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Verbose)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Receiving {serializedStateData.Length} bytes of global state data.");
            }

            UxrManager.Instance.LoadStateChanges(serializedStateData);
            _initialStateLoaded = true;
        }

        /// <summary>
        ///     Client RPC to sync the state of a new avatar that joined.
        /// </summary>
        /// <param name="serializedStateData">The serialized state data</param>
        [ClientRpc]
        private void RpcLoadAvatarState(byte[] serializedStateData)
        {
            if (netIdentity.isOwned)
            {
                // Don't execute on the source of the event, we don't want to load our own avatar data.
                return;
            }

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Verbose)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Receiving {serializedStateData.Length} bytes of avatar state data.");
            }

            UxrManager.Instance.LoadStateChanges(serializedStateData);
        }

        /// <summary>
        ///     Client RPC to execute a state change event. It will execute on all clients except the one that generated it,
        ///     which can be identified because it's the one with ownership.
        /// </summary>
        /// <param name="serializedEventData">The serialized state change data</param>
        [ClientRpc]
        private void RpcComponentStateChanged(byte[] serializedEventData)
        {
            if (isServer)
            {
                // Сервер (включая Хост) игнорирует RPC, так как он либо сам его породил, либо уже применил в Cmd.
                return;
            }

            if (isOwned)
            {
                // Владелец объекта (тот кто послал Cmd) игнорирует RPC.
                return;
            }

            if (!_avatarInitialized || _initialStateLoaded == false)
            {
                // Ignore sync events until the initial state is sent, to make sure the syncs are only processed after the initial state.
                return;
            }

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Receiving state sync on {AvatarName}: {serializedEventData.Length} bytes");
            }

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Verbose)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} Receiving {serializedEventData.Length} bytes of data on {AvatarName}. Base64: {Convert.ToBase64String(serializedEventData)}");
            }

            UxrManager.Instance.ExecuteStateSyncEvent(serializedEventData);
        }

        #endregion

        #region Private Types & Data

        // Сделали переменную экземпляра вместо статичной, чтобы не блокировать повторную инициализацию для других аватаров
        private bool _avatarInitialized = false;

        private static bool _initialStateLoaded;
        private static UxrMirrorAvatar _serverBroadcaster;

        private string _avatarName;

        #endregion
    }
#else
    public class UxrMirrorAvatar : MonoBehaviour
    {
    }
#endif
}