using UnityEngine;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Реестр всех доступных игровых режимов.
    /// Создать: ПКМ в Project → Create → VrBattlegrounds → Game Mode Registry
    /// Один asset на проект — назначить в Inspector SessionManager и AdminMenuController.
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameModeRegistry",
        menuName  = "VR Battlegrounds/Game Mode Registry")]
    public class GameModeRegistry : ScriptableObject
    {
        [Tooltip("Все режимы, доступные для выбора в меню администратора.")]
        public GameModeData[] modes = new GameModeData[0];

        /// <summary>Найти режим по идентификатору. Возвращает null если не найден.</summary>
        public GameModeData GetById(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return null;

            foreach (GameModeData mode in modes)
            {
                if (mode != null && mode.modeId == modeId)
                    return mode;
            }
            return null;
        }
    }
}
