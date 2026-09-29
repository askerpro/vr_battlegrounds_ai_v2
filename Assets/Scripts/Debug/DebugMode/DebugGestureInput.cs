using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;
using UnityEngine.XR;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Жест режима отладки: <b>оба стика нажаты 2 секунды</b> (таймер — <see cref="DebugHoldGesture"/>)
    /// включают или выключают <see cref="DebugMode"/>. Других комбинаций нет: жест стресс-теста
    /// (оба стика, стики + грипы) убран, прогоны запускаются с планшета, экран «Отладка».
    ///
    /// <para>
    /// Стики читаются напрямую из XR-ввода: пока шлем ждёт сервер, аватара ещё нет и взять
    /// ввод UltimateXR не у кого. В редакторе (симулятор без XR-устройств) — через ввод аватара.
    /// В игре оба стика одновременно не зажимают. Подтверждение — вибрация обоих контроллеров
    /// и табличка <see cref="PerfOverlay"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Шлем без сервера.</b> Планшета в ожидании сервера нет, поэтому здесь единственное
    /// действие без меню: жест при <b>уже включённом</b> режиме и отсутствии сети делает шлем
    /// хостом (<see cref="GameNetworkDiscovery.RestartAsHost"/>) — прежний путь стресс-теста
    /// «вне студии, без ПК». Дальше — планшет.
    /// </para>
    /// </summary>
    public sealed class DebugGestureInput : MonoBehaviour
    {
        private readonly DebugHoldGesture _gesture = new DebugHoldGesture();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Серверу без графики нажимать нечем.
            if (Application.isBatchMode) return;

            var go = new GameObject("DebugMode");
            DontDestroyOnLoad(go);
            go.AddComponent<DebugGestureInput>();
            go.AddComponent<DebugClientSync>();
            go.AddComponent<DebugPerfReadout>();
        }

        private void Update()
        {
            if (_gesture.Update(BothSticksPressed(), Time.unscaledTime)) OnGesture();
        }

        private void OnGesture()
        {
            bool offline = !NetworkClient.isConnected && !NetworkServer.active;

            if (DebugMode.Enabled && offline)
            {
                BecomeHost();
                return;
            }

            DebugMode.Toggle("оба стика 2 с");

            if (DebugMode.Enabled)
            {
                Haptic(0.8f, 0.25f);
                PerfOverlay.Show(offline
                    ? "Режим отладки включён.\nСервера нет: ещё раз оба стика 2 с — шлем станет хостом."
                    : "Режим отладки включён.\nПланшет, раздел «Отладка».", 5f);
            }
            else
            {
                Haptic(0.3f, 0.1f);
                PerfOverlay.Show("Режим отладки выключен.", 3f);
            }
        }

        private static void BecomeHost()
        {
            GameNetworkDiscovery discovery = FindAnyObjectByType<GameNetworkDiscovery>();
            if (discovery == null)
            {
                PerfOverlay.Show("Режим отладки: не найден GameNetworkDiscovery — не могу стать хостом.", 5f);
                return;
            }

            GameLog.Debug.Info("[DebugMode] Сервера нет — шлем становится хостом по жесту.");
            PerfOverlay.Show("Шлем становится хостом.\nПосле загрузки лобби — планшет, раздел «Отладка».", 6f);
            discovery.RestartAsHost();
        }

        private bool BothSticksPressed()
        {
            if (XrPressed(InputDeviceCharacteristics.Left) && XrPressed(InputDeviceCharacteristics.Right))
                return true;

            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar == null || avatar.ControllerInput == null) return false;

            UxrControllerInput input = avatar.ControllerInput;
            return input.GetButtonsPress(UxrHandSide.Left,  UxrInputButtons.Joystick)
                && input.GetButtonsPress(UxrHandSide.Right, UxrInputButtons.Joystick);
        }

        private bool XrPressed(InputDeviceCharacteristics side)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | side, _devices);
            foreach (InputDevice device in _devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool pressed) && pressed) return true;
            }
            return false;
        }

        private void Haptic(float amplitude, float seconds)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _devices);
            foreach (InputDevice device in _devices)
            {
                if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                    device.SendHapticImpulse(0u, amplitude, seconds);
            }
        }
    }
}
