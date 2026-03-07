using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Глобальные настройки игры. Хранится в Resources/GameSettings.asset —
    /// загружается автоматически без ссылок в сцене.
    ///
    /// Создать: ПКМ в Project → Create → VrBattlegrounds → Game Settings
    /// Открыть:  Tools → VrBattlegrounds → Game Settings
    /// </summary>
    [CreateAssetMenu(
        fileName = "GameSettings",
        menuName  = "VrBattlegrounds/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        #region Singleton

        private static GameSettings s_instance;

        /// <summary>
        /// Глобальный экземпляр настроек.
        /// Загружается из Resources/GameSettings.asset.
        /// Если файл не найден — используются значения по умолчанию.
        /// </summary>
        public static GameSettings Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = Resources.Load<GameSettings>(nameof(GameSettings));

                    if (s_instance == null)
                        s_instance = CreateInstance<GameSettings>();
                }

                return s_instance;
            }
        }

        #endregion

        #region Inspector Fields

        [Header("Логи — Сеть")]
        [Tooltip("GameNetworkManager, MapManager, GameNetworkDiscovery")]
        [SerializeField] private LogLevel _logLevelNetwork = LogLevel.Info;

        [Header("Логи — Игрок")]
        [Tooltip("PlayerController, PlayerController")]
        [SerializeField] private LogLevel _logLevelPlayer = LogLevel.Info;

        [Header("Логи — Матч")]
        [Tooltip("GameplayManager, SetManager, RoundManager")]
        [SerializeField] private LogLevel _logLevelMatch = LogLevel.Info;

        [Header("Логи — Отладка")]
        [Tooltip("DebugOrchestrator и другие DevTools")]
        [SerializeField] private LogLevel _logLevelDebug = LogLevel.Verbose;

        #endregion

        #region Properties

        /// <summary>Уровень логов сетевых систем (NetworkManager, MapManager, Discovery).</summary>
        public LogLevel LogLevelNetwork  => _logLevelNetwork;

        /// <summary>Уровень логов игрока (PlayerController).</summary>
        public LogLevel LogLevelPlayer   => _logLevelPlayer;

        /// <summary>Уровень логов матча (GameplayManager, SetManager, RoundManager).</summary>
        public LogLevel LogLevelMatch    => _logLevelMatch;

        /// <summary>Уровень логов инструментов отладки (DebugOrchestrator).</summary>
        public LogLevel LogLevelDebug    => _logLevelDebug;

        #endregion
    }
}
