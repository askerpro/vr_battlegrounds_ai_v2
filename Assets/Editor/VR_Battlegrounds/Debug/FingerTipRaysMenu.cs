using UnityEditor;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Editor.DevTools
{
    /// <summary>Лучи пальцев для UI: настройка текущей сессии редактора, без изменения префабов.</summary>
    internal static class FingerTipRaysMenu
    {
        private const string Item = "Tools/VR Battlegrounds/Debug/UI FingerTip Rays";

        [MenuItem(Item)]
        private static void Toggle()
        {
            bool current = EditorApplication.isPlaying ? DebugFingerTipRays.Visible
                : SessionState.GetBool(DebugFingerTipRays.EditorSessionKey, false);
            DebugFingerTipRays.SetVisible(!current);
        }

        [MenuItem(Item, true)]
        private static bool Validate()
        {
            Menu.SetChecked(Item, EditorApplication.isPlaying ? DebugFingerTipRays.Visible
                : SessionState.GetBool(DebugFingerTipRays.EditorSessionKey, false));
            return true;
        }
    }
}
