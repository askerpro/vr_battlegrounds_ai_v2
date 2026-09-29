using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Контейнер данных префаба планшета для <see cref="MenuController"/>: что включать и
    /// переносить, каркас (<see cref="MenuFrame"/>), раздел при открытии.
    /// </summary>
    public class MenuView : MonoBehaviour
    {
        [Tooltip("The root object of the menu that will be detached/attached (the Canvas/Screens holder).")]
        public GameObject MenuRoot;

        [Tooltip("The screen to show when the menu is opened.")]
        public MenuScreenType DefaultScreen = MenuScreenType.Main;

        [Tooltip("Каркас: левая колонка, прокручиваемая область контента, полоса главного действия.")]
        public MenuFrame Frame;

        /// <summary>
        /// Разделы колонки вне режима (офлайн, режим без своего списка): игровые по умолчанию +
        /// системные. Список разделов объявляет режим (<c>GameModeData.menuTabs</c>), см. <see cref="MenuTabs"/>.
        /// </summary>
        public static List<MenuTab> DefaultTabs() => MenuTabs.Resolve(null);
    }
}
