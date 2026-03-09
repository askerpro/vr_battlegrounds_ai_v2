using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
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
        private PlayerController _playerController;

        #region Unity

        private void Awake()
        {
            _playerController = GetComponent<PlayerController>();
            _grabbers.AddRange(GetComponentsInChildren<UxrGrabber>(true));
        }

        private void OnEnable()
        {
            foreach (var grabber in _grabbers)
            {
                grabber.CanGrabDelegate = IsGrabAllowed;
            }
        }

        private void OnDisable()
        {
            foreach (var grabber in _grabbers)
            {
                if (grabber.CanGrabDelegate == IsGrabAllowed)
                {
                    grabber.CanGrabDelegate = null;
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Validation hook for UxrGrabber.
        /// </summary>
        private bool IsGrabAllowed(UxrGrabbableObject grabbable, int grabPointIndex)
        {
            if (_playerController != null)
            {
                // Block grabbing if player is dead
                if (!_playerController.IsAlive)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
