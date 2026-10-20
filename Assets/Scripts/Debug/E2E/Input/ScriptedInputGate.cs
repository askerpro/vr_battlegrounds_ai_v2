// Разрешение на включение скриптового ввода (контракт tasks/bots-control-modes/contracts/xr-input.md).
// Компилируется во всех сборках; открывает его только E2EScriptedInput, который вырезается VRBG_NO_E2E.
namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Защита релиза для компонентов скриптового ввода в префабах аватаров. Компонент, включённый без открытого
    /// разрешения (руками в инспекторе, ошибкой префаба, в сборке с <c>VRBG_NO_E2E</c>), выключает себя сам.
    /// </summary>
    public static class ScriptedInputGate
    {
        /// <summary>Разрешение открыто владельцем включения — <c>E2EScriptedInput</c>.</summary>
        public static bool IsOpen { get; private set; }

        internal static void Open() => IsOpen = true;
        internal static void Close() => IsOpen = false;

        /// <summary>Компонент вправе работать: разрешение открыто и сборка не вырезала e2e.</summary>
        public static bool Allows
        {
            get
            {
#if VRBG_NO_E2E
                return false;
#else
                return IsOpen;
#endif
            }
        }
    }
}
