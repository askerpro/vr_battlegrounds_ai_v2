using UnityEditor;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Настройки отладочных инструментов Play в редакторе (<see cref="DebugOrchestrator"/>, <see cref="VRScreenshotCapture"/>). Личные настройки
    /// разработчика: UserSettings этого checkout, а разовый запрос — замороженный launch snapshot. В git и в
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
            get => PlayLaunchSettings.Effective.Enabled;
            set => PlayLaunchSettings.Change(c => c.Enabled = value);
        }

        /// <summary>Хост в редакторе получает профиль VR-игрока с правами админа.</summary>
        public static bool HostIsAdmin
        {
            get => PlayLaunchSettings.Effective.HostIsAdmin;
            set => PlayLaunchSettings.Change(c => c.HostIsAdmin = value);
        }

        /// <summary>Запустить матч, как только подключится достаточно игроков.</summary>
        public static bool AutoGoLive
        {
            get => PlayLaunchSettings.Effective.AutoGoLive;
            set => PlayLaunchSettings.Change(c => c.AutoGoLive = value);
        }

        /// <summary>Минимум игроков для автостарта; 0 — из данных режима.</summary>
        public static int MinPlayersOverride
        {
            get => PlayLaunchSettings.Effective.MinPlayers;
            set => PlayLaunchSettings.Change(c => c.MinPlayers = System.Math.Max(0, value));
        }

        /// <summary>Сколько ботов-противников сервер добавит сам при старте.</summary>
        public static int BotCount
        {
            get => PlayLaunchSettings.Effective.BotCount;
            set => PlayLaunchSettings.Change(c => c.BotCount = System.Math.Max(0, value));
        }

        /// <summary>
        /// Карта, которую загрузить после старта сервера; пусто — не грузить. Только на текущую сессию редактора:
        /// её пишет <c>PlayModeStartFromOffline</c>, когда Play жмут на открытой карте.
        /// </summary>
        public static string AutoLoadMapScene
        {
            get => PlayLaunchSettings.Effective.SceneSource == "scene" ? System.IO.Path.GetFileNameWithoutExtension(PlayLaunchSettings.Effective.ScenePath)
                : PlayLaunchSettings.Effective.SceneSource == "lobby" ? "" : SessionState.GetString(KeyPrefix + nameof(AutoLoadMapScene), "");
            set { if (PlayLaunchSettings.RequestActive) throw new PlayLaunchException("RequestOwnerBusy", "Карта принадлежит launch request."); SessionState.SetString(KeyPrefix + nameof(AutoLoadMapScene), value ?? ""); }
        }

        /// <summary>modeId режима для автозагруженной карты; пусто — не выставлять режим.</summary>
        public static string AutoGameModeId
        {
            get => PlayLaunchSettings.Effective.ModeId;
            set => PlayLaunchSettings.Change(c => c.ModeId = value ?? "");
        }

        /// <summary>Сразу стартовать с ролью <see cref="FallbackRole"/>, без окна выбора роли.</summary>
        public static bool AutoStartFallbackRole
        {
            get => PlayLaunchSettings.Effective.Role != "ask";
            set => PlayLaunchSettings.Change(c => { if (value != (c.Role != "ask")) c.Role = value ? "host" : "ask"; });
        }

        /// <summary>Роль экземпляра редактора без тега Multiplayer Play Mode.</summary>
        public static GameNetworkDiscovery.AppRole FallbackRole
        {
            get => PlayLaunchSettings.Effective.Role == "server" ? GameNetworkDiscovery.AppRole.Server : PlayLaunchSettings.Effective.Role == "client" ? GameNetworkDiscovery.AppRole.Client : GameNetworkDiscovery.AppRole.Host;
            set => PlayLaunchSettings.Change(c => c.Role = value.ToString().ToLowerInvariant());
        }

        /// <summary>Скриншот по кнопке B правого контроллера (<see cref="VRScreenshotCapture"/>). По умолчанию выключен.</summary>
        public static bool ScreenshotOnButtonB
        {
            get => PlayLaunchSettings.Effective.ScreenshotOnButtonB;
            set => PlayLaunchSettings.Change(c => c.ScreenshotOnButtonB = value);
        }

        /// <summary>Вернуть всё к значениям по умолчанию.</summary>
        public static void ResetToDefaults()
        {
            PlayLaunchSettings.SaveProfile(new PlayLaunchConfiguration());

            SessionState.EraseString(KeyPrefix + nameof(AutoLoadMapScene));
        }
    }
}
