using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;
using UnityEngine.XR;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Запуск стресс-теста из шлема без ПК: удерживать <b>оба стика нажатыми</b>
    /// <see cref="HoldSeconds"/> секунд. Что произойдёт, зависит от состояния:
    ///
    /// <list type="bullet">
    /// <item><b>Подключён к серверу</b> — запрос серверу, куклы пойдут за этим игроком.</item>
    /// <item><b>Идёт прогон</b> — остановить.</item>
    /// <item><b>Ждёт сервер</b> (шлем в билде всегда стартует клиентом) — шлем становится
    ///       хостом, и прогон стартует сам, как только появится аватар.</item>
    /// </list>
    ///
    /// <para>
    /// Стики читаются напрямую из XR-ввода: пока шлем ждёт сервер, аватара ещё нет, и
    /// ввод UltimateXR взять не у кого. В редакторе (симулятор без XR-устройств) — через
    /// ввод аватара. В игре оба стика одновременно не нажимаются, случайно не запустить.
    /// </para>
    /// </summary>
    public sealed class StressTestLauncher : MonoBehaviour
    {
        public const float HoldSeconds = 2f;

        /// <summary>Пауза после появления аватара на свежем хосте: догрузка сцены, первые спавны.</summary>
        private const float AutoStartDelay = 3f;

        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private float _heldSince = -1f;
        private bool  _fired;
        private bool  _autoStartPending;
        private float _avatarSeenAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Серверу без графики нажимать нечем.
            if (Application.isBatchMode) return;

            var go = new GameObject(nameof(StressTestLauncher));
            DontDestroyOnLoad(go);
            go.AddComponent<StressTestLauncher>();
        }

        private void Update()
        {
            TryAutoStart();

            if (!BothSticksPressed())
            {
                _heldSince = -1f;
                _fired = false;
                return;
            }

            if (_heldSince < 0f) _heldSince = Time.unscaledTime;
            if (_fired || Time.unscaledTime - _heldSince < HoldSeconds) return;

            _fired = true;
            OnCombo();
        }

        private void OnCombo()
        {
            if (StressTestClientSession.IsRunning)
            {
                StressTestNetwork.RequestStop();
                return;
            }

            if (NetworkClient.isConnected)
            {
                if (!StressTestNetwork.RequestStart(new StressTestConfig(), out string reason))
                    PerfOverlay.Show("Стресс-тест не запущен:\n" + reason, 5f);
                return;
            }

            if (NetworkServer.active)
            {
                PerfOverlay.Show("Стресс-тест: выделенный сервер без клиента — запускать с шлема.", 5f);
                return;
            }

            // Шлем ждёт сервер, которого нет: становимся хостом сами.
            GameNetworkDiscovery discovery = FindFirstObjectByType<GameNetworkDiscovery>();
            if (discovery == null)
            {
                PerfOverlay.Show("Стресс-тест: не найден GameNetworkDiscovery — не могу стать хостом.", 5f);
                return;
            }

            GameLog.Perf.Info("[StressTest] Сервера нет — шлем становится хостом, прогон стартует после загрузки.");
            PerfOverlay.Show("Шлем становится хостом.\nСтресс-тест начнётся после загрузки лобби.", 8f);
            _autoStartPending = true;
            _avatarSeenAt = -1f;
            discovery.RestartAsHost();
        }

        private void TryAutoStart()
        {
            if (!_autoStartPending) return;

            if (!NetworkClient.isConnected || !NetworkClient.ready || UxrAvatar.LocalAvatar == null)
            {
                _avatarSeenAt = -1f;
                return;
            }

            if (_avatarSeenAt < 0f) _avatarSeenAt = Time.unscaledTime;
            if (Time.unscaledTime - _avatarSeenAt < AutoStartDelay) return;

            _autoStartPending = false;
            if (!StressTestNetwork.RequestStart(new StressTestConfig(), out string reason))
                PerfOverlay.Show("Стресс-тест не запущен:\n" + reason, 5f);
        }

        private bool BothSticksPressed()
        {
            if (XrStickPressed(InputDeviceCharacteristics.Left) && XrStickPressed(InputDeviceCharacteristics.Right))
                return true;

            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar == null) return false;

            UxrControllerInput input = avatar.ControllerInput;
            return input.GetButtonsPress(UxrHandSide.Left,  UxrInputButtons.Joystick)
                && input.GetButtonsPress(UxrHandSide.Right, UxrInputButtons.Joystick);
        }

        private bool XrStickPressed(InputDeviceCharacteristics side)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | side, _devices);
            foreach (InputDevice device in _devices)
            {
                if (device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool pressed) && pressed) return true;
            }
            return false;
        }
    }
}
