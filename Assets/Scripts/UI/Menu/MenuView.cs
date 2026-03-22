using UnityEngine;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// A pure data container (View) for menu prefabs. 
    /// Used by the global MenuController to know what to toggle and teleport.
    /// </summary>
    public class MenuView : MonoBehaviour
    {
        [Tooltip("The root object of the menu that will be detached/attached (the Canvas/Screens holder).")]
        public GameObject MenuRoot;

        [Tooltip("The screen to show when the menu is opened.")]
        public MenuScreenType DefaultScreen = MenuScreenType.Main;
    }
}
