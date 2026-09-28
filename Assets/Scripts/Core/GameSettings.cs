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
        menuName  = "VR Battlegrounds/Game Settings")]
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

        [Header("Логи — Weapon System")]
        [Tooltip("Система оружия и механики стрельбы/перезарядки")]
        [SerializeField] private LogLevel _logLevelWeaponSystem = LogLevel.Info;

        [Header("Логи — Интерфейс")]
        [Tooltip("UI менеджеры, меню, кнопки, HUD")]
        [SerializeField] private LogLevel _logLevelUI = LogLevel.Info;

        [Header("Логи — Физическое пространство")]
        [Tooltip("Скрытые настройки калибровки (PhysicalSpaceSyncManager, HUD)")]
        [SerializeField] private LogLevel _logLevelPhysicalSpace = LogLevel.Info;

        [Header("Логи — Арсенал")]
        [Tooltip("ArsenalWallController, WeaponSlotController, DogTagController")]
        [SerializeField] private LogLevel _logLevelArsenal = LogLevel.Info;

        [Header("Логи — Производительность")]
        [Tooltip("Стресс-тест: изменения метрик, всплески кадров, итоги фаз")]
        [SerializeField] private LogLevel _logLevelPerf = LogLevel.Info;

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

        /// <summary>Уровень логов системы оружия.</summary>
        public LogLevel LogLevelWeaponSystem => _logLevelWeaponSystem;

        /// <summary>Уровень логов интерфейса.</summary>
        public LogLevel LogLevelUI       => _logLevelUI;

        /// <summary>Уровень логов физического пространства.</summary>
        public LogLevel LogLevelPhysicalSpace => _logLevelPhysicalSpace;

        /// <summary>Уровень логов арсенала (ArsenalWallController, WeaponSlotController, DogTagController).</summary>
        public LogLevel LogLevelArsenal => _logLevelArsenal;

        /// <summary>Уровень логов производительности (стресс-тест).</summary>
        public LogLevel LogLevelPerf => _logLevelPerf;

        #endregion
    }
}
