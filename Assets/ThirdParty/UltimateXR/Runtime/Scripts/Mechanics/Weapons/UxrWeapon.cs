// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrWeapon.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UltimateXR.Core.Components.Composite;
using UltimateXR.Manipulation;
using UnityEngine;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     Base class for weapons. Weapons are used by <see cref="UxrActor" /> components to inflict damage to other actor
    ///     components.
    /// </summary>
    public abstract partial class UxrWeapon : UxrGrabbableObjectComponent<UxrWeapon>
    {
        #region Public Types & Data

        /// <summary>
        ///     Gets who is in possession of the weapon, to attribute the inflicted damage to.
        /// </summary>
        public UxrActor Owner
        {
            get => TryResolveOwnerOverride(out UxrActor owner) ? owner : _owner;
            protected set => _owner = value;
        }

        #endregion

        #region Unity

        /// <summary>
        ///     Initializes the component.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            UxrActor.GlobalUnregistering += UxrActor_GlobalUnregistering;

            Owner = GetComponentInParent<UxrActor>();
        }

        /// <summary>
        ///     Called when it's going to be destroyed.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            UxrActor.GlobalUnregistering -= UxrActor_GlobalUnregistering;
        }

        #endregion

        #region Event Handling Methods

        /// <summary>
        ///     Called whenever an actor is about to be destroyed.
        /// </summary>
        /// <param name="actor"></param>
        private void UxrActor_GlobalUnregistering(UxrActor actor)
        {
            // VR Battlegrounds patch 72: provider не должен скрывать уничтожение legacy владельца.
            if (_owner == actor)
            {
                _owner = null;
            }
        }

        #endregion

        #region Event Trigger Methods

        /// <summary>
        ///     Called when the object was grabbed. It is used to set the weapon owner.
        /// </summary>
        /// <param name="e">Event parameters</param>
        protected override void OnObjectGrabbed(UxrManipulationEventArgs e)
        {
            base.OnObjectGrabbed(e);

            if (e.IsGrabbedStateChanged && UxrGrabManager.Instance.GetGrabbingHand(e.GrabbableObject, e.GrabPointIndex, out UxrGrabber grabber))
            {
                Owner = grabber.Avatar.GetComponentInChildren<UxrActor>();
            }
        }

        #endregion

        #region Protected Overrides UxrGrabbableObjectComponent<UxrWeapon>

        /// <inheritdoc />
        protected override bool IsGrabbableObjectRequired => false;

        #endregion

        #region Protected Methods

        /// <summary>VR Battlegrounds patch 72: предметный SDK root read-only owner provider.</summary>
        protected virtual UxrGrabbableObject OwnerProviderRoot => GrabbableObject;

        /// <summary>SDK72: основная точка обычного SDK-оружия; firearm использует точки своих спусков.</summary>
        protected virtual bool HasRawOwnerMainGrip =>
            UxrGrabManager.HasInstance && GrabbableObject != null &&
            UxrGrabManager.Instance.GetGrabbingHand(GrabbableObject, 0, out UxrGrabber _);

        #endregion

        #region Private Methods

        /// <summary>
        ///     SDK72: true означает override, в том числе невалидный. Ни отказ, ни reentry
        ///     не разрешают fallback к legacy owner; provider и actor не кэшируются.
        /// </summary>
        private bool TryResolveOwnerOverride(out UxrActor owner)
        {
            owner = null;
            if (_ownerProviderReadInProgress)
            {
                _ownerProviderReadRejected = true;
                return true;
            }

            _ownerProviderReadInProgress = true;
            _ownerProviderReadRejected = false;
            try
            {
                UxrGrabbableObject root = OwnerProviderRoot;
                if (root == null) return _ownerProviderReadRejected;
                root.GetComponents(_ownerProviders);

                bool active = false;
                bool invalid = false;
                bool selected = false;
                UxrActor candidateOwner = null;
                for (int index = 0; index < _ownerProviders.Count; index++)
                {
                    MonoBehaviour component = _ownerProviders[index];
                    if (component == null || !(component is IUxrWeaponOwnerProvider provider)) continue;
                    // Disabled provider также представляет активный equipment claim.
                    if (!provider.TryGetOwner(out UxrActor candidate)) continue;
                    active = true;
                    if (candidate == null || candidate.IsBeingDestroyed || candidate.IsDead)
                    {
                        invalid = true;
                    }
                    else if (selected && candidateOwner != candidate)
                    {
                        invalid = true;
                    }
                    else
                    {
                        selected = true;
                        candidateOwner = candidate;
                    }
                }

                if (_ownerProviderReadRejected) return true;
                if (!active) return false;
                if (invalid || candidateOwner == null || candidateOwner.IsBeingDestroyed ||
                    candidateOwner.IsDead) return true;
                if (root == null || root.IsBeingDestroyed || root != OwnerProviderRoot ||
                    HasRawOwnerMainGrip || _ownerProviderReadRejected) return true;
                owner = candidateOwner;
                return true;
            }
            catch (Exception)
            {
                // SDK72: неисправный provider/root не делает устаревшего VR owner допустимым.
                return true;
            }
            finally
            {
                _ownerProviderReadInProgress = false;
                _ownerProviderReadRejected = false;
            }
        }

        #endregion

        #region Private Types & Data

        private UxrActor _owner;
        private readonly List<MonoBehaviour> _ownerProviders = new List<MonoBehaviour>(4);
        private bool _ownerProviderReadInProgress;
        private bool _ownerProviderReadRejected;

        #endregion
    }
}
