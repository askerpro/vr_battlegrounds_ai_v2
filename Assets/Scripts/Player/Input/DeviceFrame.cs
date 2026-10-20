// Ввод устройства для симуляции шлема и контроллеров (контракты tasks/bots-control-modes/contracts/xr-input.md,
// tasks/bots-input/contracts/bot-input-design.md). Тип компилируется во всех сборках: им пользуются устройство аватара
// (AvatarInputDevice) и компоненты ScriptedControllerInput/Tracking в префабах аватаров — скриптовый клиент и боты.
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>Состояние одного контроллера: поза и органы управления. По умолчанию всё отпущено.</summary>
    public struct DeviceHand
    {
        /// <summary>Поза контроллера в пространстве трекинга (локально к родителю камеры аватара).</summary>
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>Нажатые кнопки (<see cref="UxrInputButtons"/>): Grip, Trigger, Button1, Button2, Joystick, Menu.</summary>
        public UxrInputButtons Pressed;

        /// <summary>Оси курка и хвата 0..1. Отрицательное значение — взять из нажатия кнопки (0 или 1).</summary>
        public float Trigger1D;
        public float Grip1D;

        /// <summary>Отклонение стика.</summary>
        public Vector2 Joystick;

        public bool IsPressed(UxrInputButtons button) => (Pressed & button) == button && button != UxrInputButtons.None;
    }

    /// <summary>
    /// Кадр устройства — то же, что за кадр отдают шлем и контроллеры: поза головы и двух контроллеров в пространстве
    /// трекинга и органы управления. Источник ввода (<see cref="IAvatarInputSource"/>) заполняет кадр, устройство
    /// применяет его штатными путями UltimateXR; поведения у получателя нет.
    /// </summary>
    public struct DeviceFrame
    {
        /// <summary>Поза головы (камеры) в пространстве трекинга.</summary>
        public Vector3 HeadPosition;
        public Quaternion HeadRotation;

        public DeviceHand Left;
        public DeviceHand Right;

        /// <summary>Кадр «стоит на месте, руки опущены, всё отпущено» для роста глаз <paramref name="eyeHeight"/>.</summary>
        public static DeviceFrame Rest(float eyeHeight)
        {
            DeviceFrame frame = default;
            frame.HeadPosition = new Vector3(0f, eyeHeight, 0f);
            frame.HeadRotation = Quaternion.identity;
            frame.Left = RestHand(UxrHandSide.Left, eyeHeight);
            frame.Right = RestHand(UxrHandSide.Right, eyeHeight);
            return frame;
        }

        public DeviceHand this[UxrHandSide side]
        {
            get => side == UxrHandSide.Left ? Left : Right;
            set { if (side == UxrHandSide.Left) Left = value; else Right = value; }
        }

        private static DeviceHand RestHand(UxrHandSide side, float eyeHeight)
        {
            float sign = side == UxrHandSide.Left ? -1f : 1f;
            return new DeviceHand
            {
                Position = new Vector3(0.22f * sign, Mathf.Max(0.5f, eyeHeight - 0.72f), 0.12f),
                Rotation = Quaternion.Euler(60f, 0f, 0f),
                Trigger1D = -1f,
                Grip1D = -1f
            };
        }
    }

    /// <summary>
    /// Источник ввода аватара: человек со шлемом, Blaze или сценарий. Раз за кадр заполняет кадр устройства;
    /// false — ввода в этом кадре нет (устройство держит «всё отпущено» и прежние позы).
    /// </summary>
    public interface IAvatarInputSource
    {
        bool TryRead(ref DeviceFrame frame);
    }
}
