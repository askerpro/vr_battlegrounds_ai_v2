// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 65: единая точка перехвата вибрации контроллеров.
// --------------------------------------------------------------------------------------------------------------------
using System;
using UltimateXR.Core;
using UltimateXR.Devices;

namespace UltimateXR.Haptics
{
    /// <summary>VR Battlegrounds patch 65: вид запроса вибрации, перехваченного у контроллера.</summary>
    public enum UxrHapticRequestKind
    {
        /// <summary>Клип <see cref="UxrHapticClip" /> (с формой или без).</summary>
        Clip,

        /// <summary>Процедурный тип SDK <see cref="UxrHapticClipType" /> с силой и длительностью.</summary>
        ClipType,

        /// <summary>Сырой запрос: частота, сила, длительность.</summary>
        Raw,

        /// <summary>Остановить вибрацию руки.</summary>
        Stop
    }

    /// <summary>
    ///     VR Battlegrounds patch 65: запрос вибрации к контроллеру — что просили, у какого компонента и для какой руки.
    ///     Поля, не относящиеся к виду запроса, имеют значения по умолчанию.
    /// </summary>
    public readonly struct UxrHapticRequest
    {
        /// <summary>Вид запроса.</summary>
        public readonly UxrHapticRequestKind Kind;

        /// <summary>Компонент ввода, которому адресован запрос (по нему находится аватар).</summary>
        public readonly UxrControllerInput Source;

        /// <summary>Рука.</summary>
        public readonly UxrHandSide Side;

        /// <summary>Клип; только для <see cref="UxrHapticRequestKind.Clip" />.</summary>
        public readonly UxrHapticClip Clip;

        /// <summary>Тип процедурного клипа; только для <see cref="UxrHapticRequestKind.ClipType" />.</summary>
        public readonly UxrHapticClipType ClipType;

        /// <summary>Частота, Гц; только для <see cref="UxrHapticRequestKind.Raw" />.</summary>
        public readonly float Frequency;

        /// <summary>Сила 0..1 (<see cref="UxrHapticRequestKind.ClipType" /> и <see cref="UxrHapticRequestKind.Raw" />).</summary>
        public readonly float Amplitude;

        /// <summary>Длительность, с; для ClipType значение не больше 0 — длительность SDK по умолчанию.</summary>
        public readonly float DurationSeconds;

        /// <summary>Режим SDK Mix/Replace.</summary>
        public readonly UxrHapticMode Mode;

        private UxrHapticRequest(UxrHapticRequestKind kind,
                                 UxrControllerInput   source,
                                 UxrHandSide          side,
                                 UxrHapticClip        clip,
                                 UxrHapticClipType    clipType,
                                 float                frequency,
                                 float                amplitude,
                                 float                durationSeconds,
                                 UxrHapticMode        mode)
        {
            Kind            = kind;
            Source          = source;
            Side            = side;
            Clip            = clip;
            ClipType        = clipType;
            Frequency       = frequency;
            Amplitude       = amplitude;
            DurationSeconds = durationSeconds;
            Mode            = mode;
        }

        /// <summary>Запрос клипа.</summary>
        public static UxrHapticRequest ForClip(UxrControllerInput source, UxrHandSide side, UxrHapticClip clip)
        {
            return new UxrHapticRequest(UxrHapticRequestKind.Clip,
                                        source,
                                        side,
                                        clip,
                                        UxrHapticClipType.None,
                                        0.0f,
                                        0.0f,
                                        0.0f,
                                        clip != null ? clip.HapticMode : UxrHapticMode.Mix);
        }

        /// <summary>Запрос процедурного типа SDK.</summary>
        public static UxrHapticRequest ForClipType(UxrControllerInput source,
                                                   UxrHandSide        side,
                                                   UxrHapticClipType  clipType,
                                                   float              amplitude,
                                                   float              durationSeconds,
                                                   UxrHapticMode      mode)
        {
            return new UxrHapticRequest(UxrHapticRequestKind.ClipType, source, side, null, clipType, 0.0f, amplitude, durationSeconds, mode);
        }

        /// <summary>Сырой запрос.</summary>
        public static UxrHapticRequest ForRaw(UxrControllerInput source,
                                              UxrHandSide        side,
                                              float              frequency,
                                              float              amplitude,
                                              float              durationSeconds,
                                              UxrHapticMode      mode)
        {
            return new UxrHapticRequest(UxrHapticRequestKind.Raw, source, side, null, UxrHapticClipType.None, frequency, amplitude, durationSeconds, mode);
        }

        /// <summary>Запрос остановки.</summary>
        public static UxrHapticRequest ForStop(UxrControllerInput source, UxrHandSide side)
        {
            return new UxrHapticRequest(UxrHapticRequestKind.Stop, source, side, null, UxrHapticClipType.None, 0.0f, 0.0f, 0.0f, UxrHapticMode.Mix);
        }
    }

    /// <summary>
    ///     VR Battlegrounds patch 65: перехват вибрации. Методы вибрации контроллера первой строкой отдают запрос
    ///     <see cref="Router" />; если он вернул true, SDK устройство не трогает. Так у мотора остаётся один писатель —
    ///     игровой сервис вибрации (на Quest каждый вызов мотора обрывает текущую вибрацию).
    ///     <see cref="Router" /> == null — SDK работает как без патча.
    /// </summary>
    public static class UxrHapticRouting
    {
        /// <summary>
        ///     Обработчик запросов. true — запрос принят игрой, SDK устройство не трогает. null — SDK работает как без патча.
        ///     Ставит и снимает игровой сервис вибрации.
        /// </summary>
        public static Func<UxrHapticRequest, bool> Router;

        /// <summary>Отдать запрос обработчику. false — обработчика нет или он отказался, SDK выполняет запрос сам.</summary>
        public static bool TryRoute(in UxrHapticRequest request)
        {
            Func<UxrHapticRequest, bool> router = Router;
            return router != null && router(request);
        }
    }
}
