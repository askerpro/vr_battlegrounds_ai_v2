using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Точка связи игры с быстрыми отладочными сценариями редактора (<c>DebugOrchestrator</c> в сборке
    /// <c>VrBattlegrounds.DebugBootstrap</c>, только редактор). Код игры не знает об оркестраторе: он только
    /// сообщает, что сценарий нужно остановить (<see cref="Suppress"/>, например E2E-прогон ведёт себя сам), и
    /// читает подсказку роли (<see cref="EditorRoleOverride"/>). В сборке оркестратора нет, флаги просто не читает никто.
    /// </summary>
    public static class DebugBootstrapGate
    {
        /// <summary>Отладочный сценарий редактора остановлен до конца Play.</summary>
        public static bool IsSuppressed { get; private set; }

        /// <summary>
        /// Роль, с которой стартовать экземпляру редактора без тега Multiplayer Play Mode; null — спросить окном.
        /// Выставляет оркестратор из личных настроек разработчика.
        /// </summary>
        public static System.Func<Network.GameNetworkDiscovery.AppRole?> EditorRoleOverride { get; set; }

        /// <summary>Остановить отладочный сценарий редактора: дальше им дирижирует вызывающий.</summary>
        public static void Suppress(string reason)
        {
            if (IsSuppressed) return;

            IsSuppressed = true;
            GameLog.Debug.Info($"[DebugBootstrapGate] Отладочный сценарий редактора остановлен: {reason}");
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            // Статика переживает Play без перезагрузки домена.
            IsSuppressed = false;
            EditorRoleOverride = null;
        }
    }
}
