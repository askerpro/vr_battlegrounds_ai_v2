// --------------------------------------------------------------------------------------------------------------------
// <copyright file="UxrProjectileSource.cs" company="VRMADA">
//   Copyright (c) VRMADA, All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using UltimateXR.Core.Caching;
using UltimateXR.Core.Components;
using UnityEngine;
using UltimateXR.Networking;

namespace UltimateXR.Mechanics.Weapons
{
    /// <summary>
    ///     Component that has the ability to fire shots.
    /// </summary>
    public class UxrProjectileSource : UxrComponent<UxrProjectileSource>, IUxrPrecacheable
    {
        #region Inspector Properties/Serialized Fields

        [SerializeField] private Animator                _weaponAnimator;
        [SerializeField] private List<UxrShotDescriptor> _shotTypes;

        #endregion

        #region Public Types & Data

        /// <summary>
        ///     The different shots that can be fired using the component.
        /// </summary>
        public IReadOnlyList<UxrShotDescriptor> ShotTypes => _shotTypes;

        /// <summary>
        ///     VR Battlegrounds patch 23: выстрел произведён — на каждой машине, и у стрелка, и при повторе
        ///     синхронизированного <see cref="Shoot(int, Vector3, Quaternion)" /> по сети. Параметр — индекс типа выстрела.
        /// </summary>
        public event System.Action<int> ShotFired;

        #endregion

        #region Implicit IUxrPrecacheable

        /// <inheritdoc />
        public IEnumerable<GameObject> PrecachedInstances
        {
            get
            {
                foreach (UxrShotDescriptor shotType in _shotTypes)
                {
                    if (shotType.PrefabInstantiateOnImpact)
                    {
                        yield return shotType.PrefabInstantiateOnImpact;
                    }

                    if (shotType.PrefabInstantiateOnTipWhenShot)
                    {
                        yield return shotType.PrefabInstantiateOnTipWhenShot;
                    }

                    if (shotType.PrefabScenarioImpactDecal && shotType.PrefabScenarioImpactDecal.gameObject)
                    {
                        yield return shotType.PrefabScenarioImpactDecal.gameObject;
                    }

                    if (shotType.ProjectilePrefab)
                    {
                        yield return shotType.ProjectilePrefab;
                    }
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Tries to get the <see cref="UxrActor" /> that holds the <see cref="UxrWeapon" /> that has the
        ///     <see cref="UxrProjectileSource" /> component.
        /// </summary>
        /// <returns>Actor component or null if it wasn't found</returns>
        public UxrActor TryGetWeaponOwner()
        {
            UxrWeapon weapon = GetComponentInParent<UxrWeapon>();

            if (weapon)
            {
                return weapon.Owner;
            }

            return GetComponentInParent<UxrActor>();
        }

        /// <summary>
        ///     Shoots a round.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to fire</param>
        public void Shoot(int shotTypeIndex)
        {
            if (shotTypeIndex >= 0 && shotTypeIndex < _shotTypes.Count)
            {
                Shoot(shotTypeIndex, _shotTypes[shotTypeIndex].ShotSource.position, _shotTypes[shotTypeIndex].ShotSource.rotation);
            }
        }

        /// <summary>
        ///     Shoots a round, overriding the source position and orientation.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to fire</param>
        /// <param name="projectileSource">Source shot position</param>
        /// <param name="projectileOrientation">Shot source orientation. The shot will be fired in the z (forward) direction</param>
        public void Shoot(int shotTypeIndex, Vector3 projectileSource, Quaternion projectileOrientation)
        {
            UxrFirearmShotEmissionOutcome outcome;
            Exception failure;
            TryShootWithOutcome(shotTypeIndex, projectileSource, projectileOrientation, out outcome, out failure);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>VR Battlegrounds: известный emission outcome и balanced scope, включая отказ FX/подписчика.</summary>
        public bool TryShootWithOutcome(int shotTypeIndex, Vector3 projectileSource, Quaternion projectileOrientation,
            out UxrFirearmShotEmissionOutcome outcome, out Exception failure)
        {
            outcome = UxrFirearmShotEmissionOutcome.NotEmitted; failure = null;
            if (shotTypeIndex < 0 || shotTypeIndex >= _shotTypes.Count) return false;
            bool endAttempted = false;
            BeginSync();
            try
            {
                var shot = _shotTypes[shotTypeIndex];
                if (shot.PrefabInstantiateOnTipWhenShot)
                {
                    GameObject newInstance = Instantiate(shot.PrefabInstantiateOnTipWhenShot, shot.Tip.position, shot.Tip.rotation);
                    newInstance.transform.parent = shot.PrefabInstantiateOnTipParent ? transform : null;
                    if (shot.PrefabInstantiateOnTipLife >= 0f) Destroy(newInstance, shot.PrefabInstantiateOnTipLife);
                }
                // Register может начать создание projectile и затем упасть: такой частичный результат нельзя retry.
                outcome = UxrFirearmShotEmissionOutcome.Indeterminate;
                UxrWeaponManager.Instance.RegisterNewProjectileShot(this, shot, projectileSource, projectileOrientation);
                outcome = UxrFirearmShotEmissionOutcome.Emitted;
                if (_weaponAnimator != null && !string.IsNullOrEmpty(shot.ShotAnimationVarName)) _weaponAnimator.SetTrigger(shot.ShotAnimationVarName);
                ShotFired?.Invoke(shotTypeIndex);
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (outcome == UxrFirearmShotEmissionOutcome.Emitted)
                {
                    // Порядок и имя legacy event сохранены; End уже потребил scope даже если подписчик бросил.
                    endAttempted = true;
                    try { EndSyncMethod(new object[] { shotTypeIndex, projectileSource, projectileOrientation }, nameof(Shoot)); }
                    catch (Exception exception) { if (failure == null) failure = exception; }
                }
                if (!endAttempted) CancelSync();
            }
            return outcome == UxrFirearmShotEmissionOutcome.Emitted && failure == null;
        }

        /// <summary>
        ///     Shoots a round pointing to the given target.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to fire</param>
        /// <param name="target">Position where the shot will be going towards</param>
        public void ShootTo(int shotTypeIndex, Vector3 target)
        {
            if (shotTypeIndex >= 0 && shotTypeIndex < _shotTypes.Count)
            {
                Vector3 direction = (target - _shotTypes[shotTypeIndex].ShotSource.position).normalized;
                Shoot(shotTypeIndex, _shotTypes[shotTypeIndex].ShotSource.position, Quaternion.LookRotation(direction));
            }
        }

        /// <summary>
        ///     Gets the distance where a shot using the current position and orientation will impact.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to use</param>
        /// <returns>Shot distance or a negative value telling the current target is out of range</returns>
        public float ShotRaycastDistance(int shotTypeIndex)
        {
            if (shotTypeIndex >= 0 && shotTypeIndex < _shotTypes.Count)
            {
                if (Physics.Raycast(_shotTypes[shotTypeIndex].ShotSource.position,
                                    _shotTypes[shotTypeIndex].ShotSource.forward,
                                    out RaycastHit raycastHit,
                                    _shotTypes[shotTypeIndex].ProjectileMaxDistance,
                                    _shotTypes[shotTypeIndex].CollisionLayerMask,
                                    QueryTriggerInteraction.Ignore))
                {
                    return raycastHit.distance;
                }
            }

            return -1.0f;
        }

        /// <summary>
        ///     Gets the current world-space origin of projectiles fired using the given shot type.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to use</param>
        /// <returns>Projectile world-space source</returns>
        public Vector3 GetShotOrigin(int shotTypeIndex)
        {
            if (shotTypeIndex >= 0 && shotTypeIndex < _shotTypes.Count)
            {
                return _shotTypes[shotTypeIndex].ShotSource.position;
            }

            return Vector3.zero;
        }

        /// <summary>
        ///     Gets the current world-space direction of projectiles fired using the given shot type.
        /// </summary>
        /// <param name="shotTypeIndex">Index in <see cref="ShotTypes" />, telling which shot type to use</param>
        /// <returns>Projectile world-space direction</returns>
        public Vector3 GetShotDirection(int shotTypeIndex)
        {
            if (shotTypeIndex >= 0 && shotTypeIndex < _shotTypes.Count)
            {
                return _shotTypes[shotTypeIndex].ShotSource.forward;
            }

            return Vector3.zero;
        }

        #endregion
    }
}
