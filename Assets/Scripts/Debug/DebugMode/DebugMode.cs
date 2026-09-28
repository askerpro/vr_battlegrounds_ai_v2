using System;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Скрытый режим отладки на этом устройстве. Включается жестом «оба стика 2 с»
    /// (<see cref="DebugGestureInput"/>), показывает на планшете экран «Отладка» (стресс-тест,
    /// телепорт, оверлей кадра) и просит у сервера права админа (<see cref="DebugClientSync"/>).
    ///
    /// <para>
    /// Отдельная статическая служба, а не флаг на существующем синглтоне: режим живёт всё время
    /// работы приложения, независимо от сцены и сети, а меню, оверлей и сетевая синхронизация
    /// только подписываются на <see cref="Changed"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Не сохраняется между запусками</b> (PlayerPrefs не используется): шлемы общие, и режим,
    /// включённый разработчиком, не должен достаться следующему игроку после перезапуска.
    /// Включить заново — тот же жест.
    /// </para>
    /// </summary>
    public static class DebugMode
    {
        public static bool Enabled { get; private set; }

        /// <summary>Режим включили (true) или выключили (false).</summary>
        public static event Action<bool> Changed;

        public static void Set(bool enabled, string reason)
        {
            if (Enabled == enabled) return;
            Enabled = enabled;

            GameLog.Debug.Info($"[DebugMode] Режим отладки {(enabled ? "включён" : "выключен")}: {reason}.");
            Changed?.Invoke(enabled);
        }

        public static void Toggle(string reason) => Set(!Enabled, reason);
    }
}
