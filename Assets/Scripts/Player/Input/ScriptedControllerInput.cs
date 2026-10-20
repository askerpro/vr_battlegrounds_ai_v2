// Скриптовые контроллеры для симуляции ввода (контракты tasks/bots-control-modes/contracts/xr-input.md,
// tasks/bots-input/contracts/bot-input-design.md). Компонент лежит выключенным в префабах аватаров и компилируется во
// всех сборках; включает его только устройство аватара (AvatarInputDevice) по своей заявке, иначе он выключает себя сам.
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Пара контроллеров, чьи кнопки и оси берутся из кадра устройства (<see cref="DeviceFrame"/>), а не из XR.
    /// Дальше ввод читает штатный <c>UxrStandardAvatarController</c>: хват по Grip, курок по Trigger, жесты — как у
    /// человека. Флаги кнопок выставляются штатным <c>SetButtonFlags</c> в <see cref="UpdateInput"/>, поэтому
    /// PressDown/PressUp считает база SDK.
    /// </summary>
    public sealed class ScriptedControllerInput : UxrControllerInput
    {
        private const UxrControllerElements Elements =
            UxrControllerElements.Joystick | UxrControllerElements.Trigger | UxrControllerElements.Grip |
            UxrControllerElements.Button1 | UxrControllerElements.Button2 | UxrControllerElements.Menu;

        private static readonly UxrInputButtons[] Buttons =
        {
            UxrInputButtons.Joystick, UxrInputButtons.Trigger, UxrInputButtons.Grip,
            UxrInputButtons.Button1, UxrInputButtons.Button2, UxrInputButtons.Menu
        };

        private DeviceFrame _frame;

        public override UxrControllerSetupType SetupType => UxrControllerSetupType.Dual;
        public override bool IsHandednessSupported => true;
        public override string LeftControllerName => "Scripted Left";
        public override string RightControllerName => "Scripted Right";

        public override bool IsControllerEnabled(UxrHandSide handSide) => isActiveAndEnabled;

        public override bool HasControllerElements(UxrHandSide handSide, UxrControllerElements controllerElement) =>
            controllerElement != UxrControllerElements.None && (Elements & controllerElement) == controllerElement;

        public override UxrControllerInputCapabilities GetControllerCapabilities(UxrHandSide handSide) => 0;

        public override float GetInput1D(UxrHandSide handSide, UxrInput1D input1D, bool getIgnoredInput = false)
        {
            if (ShouldIgnoreInput(handSide, getIgnoredInput)) return 0f;

            DeviceHand hand = _frame[handSide];
            switch (input1D)
            {
                case UxrInput1D.Grip: return Axis(hand.Grip1D, hand.IsPressed(UxrInputButtons.Grip));
                case UxrInput1D.Trigger: return Axis(hand.Trigger1D, hand.IsPressed(UxrInputButtons.Trigger));
                default: return 0f;
            }
        }

        public override Vector2 GetInput2D(UxrHandSide handSide, UxrInput2D input2D, bool getIgnoredInput = false)
        {
            if (ShouldIgnoreInput(handSide, getIgnoredInput)) return Vector2.zero;
            return input2D == UxrInput2D.Joystick ? Vector2.ClampMagnitude(_frame[handSide].Joystick, 1f) : Vector2.zero;
        }

        /// <summary>Компонент заявлен устройством аватара (<see cref="AvatarInputDevice"/>) — только тогда он вправе работать.</summary>
        internal bool Claimed { get; set; }

        /// <summary>Кадр, который прочитает ближайший <see cref="UpdateInput"/>. Пишет только <see cref="AvatarInputDevice"/>.</summary>
        internal void Apply(in DeviceFrame frame) => _frame = frame;

        /// <summary>Подключение устройства: штатное событие SDK включает парный <see cref="ScriptedControllerTracking"/>.</summary>
        internal void Connect()
        {
            _frame = default;
            enabled = true;
            if (enabled) OnDeviceConnected(new UxrDeviceConnectEventArgs(true));
        }

        /// <summary>Отключение: кнопки отпускаются, трекинг выключается тем же событием.</summary>
        internal void Disconnect()
        {
            _frame = default;
            if (!enabled) return;
            OnDeviceConnected(new UxrDeviceConnectEventArgs(false));
            enabled = false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Claimed) return;

            // База уже зарегистрировала компонент: выключение пройдёт штатным OnDisable.
            enabled = false;
            if (Application.isPlaying)
                GameLog.Error($"[ScriptedInput] {name}: скриптовый ввод включён без устройства аватара — выключен.");
        }

        protected override void UpdateInput()
        {
            for (int i = 0; i < Buttons.Length; i++)
            {
                UxrInputButtons button = Buttons[i];
                SetButtonFlags(ButtonFlags.PressFlagsLeft, button, _frame.Left.IsPressed(button));
                SetButtonFlags(ButtonFlags.PressFlagsRight, button, _frame.Right.IsPressed(button));
            }
        }

        private static float Axis(float value, bool pressed) => value >= 0f ? Mathf.Clamp01(value) : pressed ? 1f : 0f;
    }
}
