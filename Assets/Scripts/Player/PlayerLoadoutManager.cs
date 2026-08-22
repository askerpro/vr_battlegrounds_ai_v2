using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Manages the player's equipment and ammo when round states change.
    /// Fills the magazine pocket based on weapons currently in the primary/secondary pockets and hands.
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    public class PlayerLoadoutManager : NetworkBehaviour
    {
        private UxrAvatar _avatar;
        private UxrMagazinePocket _magazinePocket;

        private UxrGrabbableObjectAnchor _anchorHipR;
        private UxrGrabbableObjectAnchor _anchorBack;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
        }

        private void Start()
        {
            // Gather references
            _magazinePocket = GetComponentInChildren<UxrMagazinePocket>(true);

            UxrGrabbableObjectAnchor[] anchors = GetComponentsInChildren<UxrGrabbableObjectAnchor>(true);
            foreach (var anchor in anchors)
            {
                if (anchor.gameObject.name.Contains("Anchor_Hip_R")) _anchorHipR = anchor;
                else if (anchor.gameObject.name.Contains("Anchor_Back")) _anchorBack = anchor;
            }
        }

        private void OnEnable()
        {
            EliminationMode.OnRoundStateChangedLocal += HandleRoundStateChanged;
        }

        private void OnDisable()
        {
            EliminationMode.OnRoundStateChangedLocal -= HandleRoundStateChanged;
        }

        private void HandleRoundStateChanged(RoundState newState)
        {
            // We only process logic for the local player's avatar
            if (!isLocalPlayer && !isOwned) return;

            if (newState == RoundState.Countdown)
            {
                RefillMagazinesLocally();
            }
        }

        [ContextMenu("Refill Magazines")]
        public void RefillMagazinesLocally()
        {
            if (_magazinePocket == null) return;

            GameLog.Arsenal.Info($"[LoadoutManager] Scanning weapons to request magazines for '{gameObject.name}'...");

            _magazinePocket.Clear();

            HashSet<WeaponInfo> processedWeapons = new HashSet<WeaponInfo>();

            // Check anchors (holsters)
            if (_anchorHipR != null) ProcessAnchorForAmmo(_anchorHipR, processedWeapons);
            if (_anchorBack != null) ProcessAnchorForAmmo(_anchorBack, processedWeapons);

            // Check hands
            if (_avatar != null)
            {
                ProcessGrabberForAmmo(_avatar.GetGrabber(UxrHandSide.Left), processedWeapons);
                ProcessGrabberForAmmo(_avatar.GetGrabber(UxrHandSide.Right), processedWeapons);
            }

            // Only request if we found weapons
            if (processedWeapons.Count > 0)
            {
                string[] weaponIds = processedWeapons.Select(w => w.WeaponId).ToArray();
                CmdRequestMagazines(weaponIds);
            }
        }

        private void ProcessGrabberForAmmo(UxrGrabber grabber, HashSet<WeaponInfo> processed)
        {
            if (grabber == null || grabber.GrabbedObject == null) return;
            TryRegisterWeapon(grabber.GrabbedObject, processed);
        }

        private void ProcessAnchorForAmmo(UxrGrabbableObjectAnchor anchor, HashSet<WeaponInfo> processed)
        {
            if (anchor == null || anchor.CurrentPlacedObject == null) return;
            TryRegisterWeapon(anchor.CurrentPlacedObject, processed);
        }

        private void TryRegisterWeapon(UxrGrabbableObject grabObj, HashSet<WeaponInfo> processed)
        {
            var weaponComp = grabObj.GetComponent<WeaponComponent>();
            if (weaponComp == null || weaponComp.WeaponData == null) return;

            // Only register each weapon type once
            if (!processed.Contains(weaponComp.WeaponData))
            {
                processed.Add(weaponComp.WeaponData);
            }
        }

        #region Networking

        /// <summary>
        /// Sent from the Client to the Server to request magazines for equipped weapons.
        /// </summary>
        [Command]
        private void CmdRequestMagazines(string[] weaponIds)
        {
            List<NetworkIdentity> spawnedMags = new List<NetworkIdentity>();

            foreach (string wId in weaponIds)
            {
                WeaponInfo info = WeaponRegistry.Instance.GetById(wId);
                if (info == null || info.MagazinePrefab == null || info.MaxMagazineCount <= 0) continue;

                for (int i = 0; i < info.MaxMagazineCount; i++)
                {
                    // Создание и спавн ведёт сетевой слой: он гасит «Auto Anchor» до Awake
                    // и выравнивает UniqueId по netId. Без выравнивания вставка магазина
                    // не применится на другой машине — это NET-16, см. NetworkUxrIdentity.
                    GameObject magGo = NetworkUxrIdentity.CreateInstance(info.MagazinePrefab);
                    if (magGo == null) continue;

                    magGo.SetActive(true);

                    // Network Server handles giving authority back to the requesting client
                    NetworkUxrIdentity.SpawnServerObject(magGo, connectionToClient);

                    var netId = magGo.GetComponent<NetworkIdentity>();
                    if (netId != null) spawnedMags.Add(netId);
                }
            }

            if (spawnedMags.Count > 0)
            {
                GameLog.Arsenal.Info($"[LoadoutManager] Server spawned {spawnedMags.Count} magazines for client.");
                TargetReceiveMagazines(connectionToClient, spawnedMags.ToArray());
            }
        }

        /// <summary>
        /// Sent from the Server back to the Client that requested the magazines.
        /// Puts the newly spawned network magazines into the player's pockets.
        /// </summary>
        [TargetRpc]
        private void TargetReceiveMagazines(NetworkConnection target, NetworkIdentity[] magazines)
        {
            foreach (var netId in magazines)
            {
                if (netId != null)
                {
                    var grabbable = netId.GetComponent<UxrGrabbableObject>();
                    if (grabbable != null)
                    {
                        _magazinePocket.ForceStoreItem(grabbable);
                    }
                }
            }
        }

        #endregion
    }
}
