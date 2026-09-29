using UltimateXR.Manipulation;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Что может взять выбывший (призрак, T-35): только свой планшет. Выбывший выбирает в нём
    /// команду, смотрит обзор, зовёт админа — а оружие, магазины, жетон и предметы мира ему
    /// недоступны. Живой — без ограничений этого правила (дальше решает <c>GrabRules</c>).
    ///
    /// <para>
    /// «Свой» — по построению: планшет локальный, не сетевой (<c>LocalMenuManager</c>), и чужих
    /// планшетов на машине игрока нет. Узнаётся по <see cref="MenuView"/> в родителях предмета.
    /// </para>
    /// </summary>
    public static class GhostGrabRule
    {
        public static bool IsAllowed(bool alive, UxrGrabbableObject grabbable) => alive || IsTablet(grabbable);

        public static bool IsTablet(UxrGrabbableObject grabbable) =>
            grabbable != null && grabbable.GetComponentInParent<MenuView>(true) != null;
    }
}
