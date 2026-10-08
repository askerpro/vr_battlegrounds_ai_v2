using UltimateXR.Core;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Мотор контроллера. Единственная реализация в игре — <see cref="UnityXRHapticDevice" />; в тестах — подмена.
    /// Только амплитуда и длительность: частоты в нашем стеке (Oculus XR Plugin + Unity XR) нет.
    /// </summary>
    public interface IHapticDevice
    {
        /// <summary>Вибрация с амплитудой 0..1 на <paramref name="seconds" />. Новый вызов заменяет текущую вибрацию руки.</summary>
        void Send(UxrHandSide side, float amplitude, float seconds);

        /// <summary>Погасить мотор руки.</summary>
        void Stop(UxrHandSide side);
    }
}
