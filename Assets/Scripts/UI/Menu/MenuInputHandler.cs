using UnityEngine;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;

namespace VrBattlegrounds.UI.Menu
{
    public class MenuInputHandler : MonoBehaviour
    {
        [Tooltip("The hand used to trigger the menu. Usually Left.")]
        [SerializeField] private UxrHandSide _menuHand = UxrHandSide.Left;
        
        [Tooltip("The button used to trigger the menu.")]
        [SerializeField] private UxrInputButtons _menuButton = UxrInputButtons.Menu;

        private void Update()
        {
            // Ensure avatar and input are initialized
            if (UxrAvatar.LocalAvatar != null && UxrAvatar.LocalAvatarInput != null)
            {
                // Check if the assigned menu button was pressed this frame on either controller
                if (UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Left, _menuButton) ||
                    UxrAvatar.LocalAvatarInput.GetButtonsPressDown(UxrHandSide.Right, _menuButton))
                {
                    if (MenuController.Instance != null)
                    {
                        MenuController.Instance.ToggleMenu();
                    }
                }
            }
        }
    }
}
