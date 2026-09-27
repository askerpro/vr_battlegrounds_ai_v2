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
    /// <remarks>
    ///     ПРАВКА ПРОЕКТА (T-12, Патч 1 в Docs/UltimateXR/sdk-patches.md).
    ///     Канал состояния (подписка на UxrManager.ComponentStateChanged, Cmd/Rpc с
    ///     byte[]-блобами и начальная синхронизация сцены) вынесен отсюда в
    ///     VrBattlegrounds.Network.NetworkStateRelay — объект уровня сессии.
    ///     Здесь остаётся только то, что действительно про аватар: инициализация,
    ///     CombineUniqueId и ownership. Ничего статического в классе быть не должно.
    /// </remarks>
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

        #region Event Trigger Methods

        public override void OnStartServer()
        {
            Avatar = GetComponent<UxrAvatar>();

            InitializeNetworkAvatar(Avatar, netIdentity.isOwned, netId.ToString(), $"Player {netId} ({(netIdentity.isOwned ? "Local" : "External")})");

            base.OnStartServer();
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
            _avatarInitialized = false;

            if (UxrGlobalSettings.Instance.LogLevelNetworking >= UxrLogLevel.Relevant)
            {
                Debug.Log($"{UxrConstants.NetworkingModule} {nameof(UxrMirrorAvatar)}.{nameof(OnDestroy)}: Is Local? {IsLocal}, Name: {AvatarName}, NetId: {(netIdentity ? netId.ToString() : "null")}");
            }

            // VR Battlegrounds (патч 9): только снять аватар с учёта, без рассылки. Объект
            // уничтожает Mirror на каждой машине сам; DestroyGameObject слал DestroyGameObjectInternal
            // другой стороне, где аватара уже нет, — UxrComponentNotFoundException на каждой смене скина.
            if (UxrInstanceManager.HasInstance && Avatar != null)
            {
                UxrInstanceManager.Instance.NotifyNetworkDespawn(Avatar.gameObject);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Server RPC requesting authority over an object.
        /// </summary>
        /// <param name="networkIdentity">Object to get authority over</param>
        [Command]
        private void CmdRequestAuthority(NetworkIdentity networkIdentity)
        {
            networkIdentity.AssignClientAuthority(netIdentity.connectionToClient);
        }

        #endregion

        #region Private Types & Data

        // Сделали переменную экземпляра вместо статичной, чтобы не блокировать повторную инициализацию для других аватаров
        private bool _avatarInitialized = false;

        private string _avatarName;

        #endregion
    }
#else
    public class UxrMirrorAvatar : MonoBehaviour
    {
    }
#endif
}