using UnityEditor;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Настройки отладочных инструментов Play в редакторе (<see cref="DebugOrchestrator"/>, <see cref="VRScreenshotCapture"/>). Личные настройки
    /// разработчика: EditorPrefs этой машины, а карта автозапуска — SessionState текущей сессии редактора. В git и в
    /// сборку не попадают (сборка <c>VrBattlegrounds.DebugBootstrap</c> компилируется только в редакторе).
    /// Меню — <c>Tools/VR Battlegrounds/Debug/Bootstrap Settings…</c>; тесты выставляют значения из кода и
    /// возвращают после себя.
    /// </summary>
    public static class DebugBootstrapSettings
    {
        public const string KeyPrefix = "VrBattlegrounds.DebugBootstrap.";

        /// <summary>Быстрая инициализация включена. Выключить — обычный старт игры.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(KeyPrefix + nameof(Enabled), true);
            set => EditorPrefs.SetBool(KeyPrefix + nameof(Enabled), value);
        }

        /// <summary>Хост в редакторе получает профиль VR-игрока с правами админа.</summary>
        public static bool HostIsAdmin
        {
            get => EditorPrefs.GetBool(KeyPrefix + nameof(HostIsAdmin), true);
            set => EditorPrefs.SetBool(KeyPrefix + nameof(HostIsAdmin), value);
        }

        /// <summary>Запустить матч, как только подключится достаточно игроков.</summary>
        public static bool AutoGoLive
        {
            get => EditorPrefs.GetBool(KeyPrefix + nameof(AutoGoLive), true);
            set => EditorPrefs.SetBool(KeyPrefix + nameof(AutoGoLive), value);
        }

        /// <summary>Минимум игроков для автостарта; 0 — из данных режима.</summary>
        public static int MinPlayersOverride
        {
            get => EditorPrefs.GetInt(KeyPrefix + nameof(MinPlayersOverride), 1);
            set => EditorPrefs.SetInt(KeyPrefix + nameof(MinPlayersOverride), value < 0 ? 0 : value);
        }

        /// <summary>Сколько ботов-противников сервер добавит сам при старте.</summary>
        public static int BotCount
        {
            get => EditorPrefs.GetInt(KeyPrefix + nameof(BotCount), 0);
            set => EditorPrefs.SetInt(KeyPrefix + nameof(BotCount), value < 0 ? 0 : value);
        }

        /// <summary>
        /// Карта, которую загрузить после старта сервера; пусто — не грузить. Только на текущую сессию редактора:
        /// её пишет <c>PlayModeStartFromOffline</c>, когда Play жмут на открытой карте.
        /// </summary>
        public static string AutoLoadMapScene
        {
            get => SessionState.GetString(KeyPrefix + nameof(AutoLoadMapScene), "");
            set => SessionState.SetString(KeyPrefix + nameof(AutoLoadMapScene), value ?? "");
        }

        /// <summary>modeId режима для автозагруженной карты; пусто — не выставлять режим.</summary>
        public static string AutoGameModeId
        {
            get => EditorPrefs.GetString(KeyPrefix + nameof(AutoGameModeId), "elimination");
            set => EditorPrefs.SetString(KeyPrefix + nameof(AutoGameModeId), value ?? "");
        }

        /// <summary>Сразу стартовать с ролью <see cref="FallbackRole"/>, без окна выбора роли.</summary>
        public static bool AutoStartFallbackRole
        {
            get => EditorPrefs.GetBool(KeyPrefix + nameof(AutoStartFallbackRole), true);
            set => EditorPrefs.SetBool(KeyPrefix + nameof(AutoStartFallbackRole), value);
        }

        /// <summary>Роль экземпляра редактора без тега Multiplayer Play Mode.</summary>
        public static GameNetworkDiscovery.AppRole FallbackRole
        {
            get => (GameNetworkDiscovery.AppRole)EditorPrefs.GetInt(KeyPrefix + nameof(FallbackRole), (int)GameNetworkDiscovery.AppRole.Host);
            set => EditorPrefs.SetInt(KeyPrefix + nameof(FallbackRole), (int)value);
        }

        /// <summary>Скриншот по кнопке B правого контроллера (<see cref="VRScreenshotCapture"/>). По умолчанию выключен.</summary>
        public static bool ScreenshotOnButtonB
        {
            get => EditorPrefs.GetBool(KeyPrefix + nameof(ScreenshotOnButtonB), false);
            set => EditorPrefs.SetBool(KeyPrefix + nameof(ScreenshotOnButtonB), value);
        }

        /// <summary>Вернуть всё к значениям по умолчанию.</summary>
        public static void ResetToDefaults()
        {
            foreach (string name in new[]
                     {
                         nameof(Enabled), nameof(HostIsAdmin), nameof(AutoGoLive), nameof(MinPlayersOverride),
                         nameof(BotCount), nameof(AutoGameModeId), nameof(AutoStartFallbackRole), nameof(FallbackRole), nameof(ScreenshotOnButtonB),
                     })
            {
                EditorPrefs.DeleteKey(KeyPrefix + name);
            }

            SessionState.EraseString(KeyPrefix + nameof(AutoLoadMapScene));
        }
    }
}
