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
        private static ManagedLaunchScope _managed;
        public static bool ManagedLaunchActive => _managed != null;
        public static System.IDisposable BeginManagedLaunch()
        {
            if (_managed != null) throw new System.InvalidOperationException("У launch уже есть локальный владелец.");
            _managed = new ManagedLaunchScope(); return _managed;
        }
        private sealed class ManagedLaunchScope : System.IDisposable
        {
            public void Dispose() { if (ReferenceEquals(_managed, this)) _managed = null; }
        }

        /// <summary>
        /// Frozen роль управляемого запуска; вне него — fallback редактора без MPP-тега.
        /// Участник managed request связывает роль до Start сети; legacy оркестратор предоставляет fallback.
        /// </summary>
        public static System.Func<Network.GameNetworkDiscovery.AppRole?> EditorRoleOverride { get; set; }
        public static System.Func<string> EditorClientAddressOverride { get; set; }
        /// <summary>Подготовка собственного маршрута до StartServer/StartHost; код отказа запрещает старт.</summary>
        public static System.Func<string> EditorBeforeServerStart { get; set; }

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
            EditorClientAddressOverride = null;
            EditorBeforeServerStart = null;
        }
    }
}
