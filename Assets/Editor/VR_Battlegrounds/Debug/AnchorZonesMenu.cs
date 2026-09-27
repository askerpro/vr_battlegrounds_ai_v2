using UnityEditor;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Editor.DevTools
{
    /// <summary>
    ///     Переключатель <see cref="AnchorZonesDebugView" /> — зон досягаемости карманов в шлеме.
    ///     Состояние хранится в EditorPrefs и применяется при каждом входе в Play Mode, так что
    ///     включить можно заранее, до запуска игры.
    /// </summary>
    [InitializeOnLoad]
    internal static class AnchorZonesMenu
    {
        private const string MenuPath = "Tools/VR Battlegrounds/Debug/Anchor Zones In Headset";
        private const string PrefKey  = "VrBattlegrounds.AnchorZonesInHeadset";

        static AnchorZonesMenu()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    AnchorZonesDebugView.Enabled = EditorPrefs.GetBool(PrefKey, false);
                }
            };
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool value = !EditorPrefs.GetBool(PrefKey, false);
            EditorPrefs.SetBool(PrefKey, value);

            if (EditorApplication.isPlaying)
            {
                AnchorZonesDebugView.Enabled = value;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(PrefKey, false));
            return true;
        }
    }
}
