using UltimateXR.Core;
using UnityEngine.XR;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Моторы физических контроллеров через Unity XR <see cref="InputDevice" />. Единственный файл игры, которому
    /// разрешено писать в вибромотор (<c>HapticOwnershipTests</c>): все остальные идут через <see cref="HapticService" />.
    /// </summary>
    public sealed class UnityXRHapticDevice : IHapticDevice
    {
        private InputDevice _left, _right;
        private bool _capabilitiesLogged;

        public void Send(UxrHandSide side, float amplitude, float seconds)
        {
            if (!TryGet(side, out InputDevice device)) return;
            device.SendHapticImpulse(0u, amplitude, seconds);
        }

        public void Stop(UxrHandSide side)
        {
            if (TryGet(side, out InputDevice device)) device.StopHaptics();
        }

        /// <summary>Виден ли физический контроллер руки (для диагностики).</summary>
        public bool IsConnected(UxrHandSide side) => TryGet(side, out _);

        private bool TryGet(UxrHandSide side, out InputDevice device)
        {
            bool left = side == UxrHandSide.Left;
            device = left ? _left : _right;
            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
                if (left) _left = device;
                else _right = device;
            }
            if (!device.isValid) return false;

            if (!_capabilitiesLogged && device.TryGetHapticCapabilities(out HapticCapabilities caps))
            {
                // Закрывает гипотезы исследования п. 1.1–1.2: сколько каналов и есть ли буфер на Quest.
                _capabilitiesLogged = true;
                GameLog.Player.Info($"[Haptics] {device.name}: impulse={caps.supportsImpulse}, buffer={caps.supportsBuffer}, " +
                                    $"channels={caps.numChannels}, bufferHz={caps.bufferFrequencyHz}");
            }
            return true;
        }
    }
}
