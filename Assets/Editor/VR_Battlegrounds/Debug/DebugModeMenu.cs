using UnityEditor;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.Editor.DevTools
{
    /// <summary>
    ///     Переключение режима отладки из редактора (Play Mode) — то же, что оба стика 2 с на шлеме.
    ///     Нужен, чтобы проверить экраны «Отладка» и «Перф-тесты» без контроллеров.
    /// </summary>
    internal static class DebugModeMenu
    {
        private const string Item = "Tools/VR Battlegrounds/Debug/Toggle Debug Mode";

        [MenuItem(Item)]
        private static void Toggle() => DebugMode.Toggle("меню редактора");

        [MenuItem(Item, true)]
        private static bool CanToggle()
        {
            Menu.SetChecked(Item, DebugMode.Enabled);
            return EditorApplication.isPlaying;
        }
    }
}
