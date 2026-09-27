using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Component that manages grabbing permissions for a player.
    /// It sets a validation delegate on all UxrGrabber components found in the player hierarchy.
    /// </summary>
    public class PlayerGrabManager : MonoBehaviour
    {
        private List<UxrGrabber> _grabbers = new List<UxrGrabber>();
        private readonly Dictionary<UxrGrabber, Func<UxrGrabbableObject, int, bool>> _delegates = new Dictionary<UxrGrabber, Func<UxrGrabbableObject, int, bool>>();
        private PlayerController _playerController;
        private UxrAvatar _avatar;

        #region Unity

        private void Awake()
        {
            _playerController = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
            _grabbers.AddRange(GetComponentsInChildren<UxrGrabber>(true));

            if (_playerController != null)
            {
                _playerController.PlayerDied += OnPlayerDied;
            }
        }

        private void OnEnable()
        {
            _delegates.Clear();

            foreach (var grabber in _grabbers)
            {
                // Делегат на каждый граббер: политике двух рук нужно знать, какая рука спрашивает.
                UxrGrabber owner = grabber;
                Func<UxrGrabbableObject, int, bool> canGrab = (grabbable, point) => IsGrabAllowed(owner, grabbable, point);
                _delegates[grabber] = canGrab;
                grabber.CanGrabDelegate = canGrab;
            }
        }

        private void OnDisable()
        {
            foreach (var grabber in _grabbers)
            {
                if (_delegates.TryGetValue(grabber, out var canGrab) && grabber.CanGrabDelegate == canGrab)
                {
                    grabber.CanGrabDelegate = null;
                }
            }

            _delegates.Clear();
        }

        private void OnDestroy()
        {
            if (_playerController != null)
            {
                _playerController.PlayerDied -= OnPlayerDied;
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Validation hook for UxrGrabber.
        /// </summary>
        private bool IsGrabAllowed(UxrGrabber grabber, UxrGrabbableObject grabbable, int grabPointIndex)
        {
            if (_playerController != null)
            {
                // Block grabbing if player is dead
                if (!_playerController.IsAlive)
                {
                    return false;
                }
            }

            return GrabRules.IsGrabAllowed(grabber, grabbable, grabPointIndex);
        }

        private void OnPlayerDied(PlayerController controller)
        {
            ReleaseAllGrabbedObjects();
        }

        public void ReleaseAllGrabbedObjects()
        {
            if (_avatar == null) return;

            var grabManager = UxrGrabManager.Instance;
            if (grabManager == null) return;

            // Release objects from each grabber
            foreach (var grabber in _grabbers)
            {
                if (grabber.GrabbedObject != null)
                {
                    grabManager.ReleaseObject(grabber, grabber.GrabbedObject, true);
                }
            }
        }

        #endregion
    }
}
