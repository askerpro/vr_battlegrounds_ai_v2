using VrBattlegrounds.Managers;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Поиск <see cref="GameModeData"/> по <c>modeId</c> на любой машине.
    ///
    /// <para>
    /// По сети режим сообщает только строку <c>modeId</c> (<see cref="GameMode.ModeData"/>).
    /// Искать её в одном <c>GameModeRegistry</c> нельзя: лобби-режима там нет намеренно —
    /// реестр описывает выбор матча. Поэтому сначала спрашивается режим сцены
    /// (<c>GameplayManager.SceneGameMode</c> — он есть в сцене у каждой машины), потом реестр.
    /// </para>
    /// </summary>
    public static class GameModeCatalog
    {
        public static GameModeData Find(string modeId)
        {
            if (string.IsNullOrEmpty(modeId)) return null;

            GameplayManager manager = GameplayManager.Instance;
            if (manager != null && manager.SceneGameMode != null && manager.SceneGameMode.modeId == modeId)
                return manager.SceneGameMode;

            return SessionManager.Instance != null ? SessionManager.Instance.FindModeData(modeId) : null;
        }
    }
}
