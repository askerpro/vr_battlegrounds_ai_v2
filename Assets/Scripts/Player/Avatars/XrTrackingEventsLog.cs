using System.Collections.Generic;
using UltimateXR.Avatar;
using UnityEngine;
using UnityEngine.XR;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// События трекинга шлема, которые сдвигают голову относительно корня аватара без участия игры:
    /// recenter и смена пола/начала координат (<see cref="XRInputSubsystem.trackingOriginUpdated"/>),
    /// смена границы Guardian (<see cref="XRInputSubsystem.boundaryChanged"/>), снял/надел шлем
    /// (<see cref="CommonUsages.userPresence"/>), потеря трекинга головы, пауза и фокус приложения.
    /// Одна строка на событие, камера локально (в пространстве трекинга) до/после — чтобы понять,
    /// откуда голова «уехала» от тела (<see cref="AvatarPoseSanityMonitor"/>). Только наблюдает.
    /// </summary>
    public sealed class XrTrackingEventsLog : MonoBehaviour
    {
        private const float PollInterval = 0.5f;

        private static int s_originUpdates;
        private static int s_boundaryChanges;
        private static float s_lastOriginUpdate = -1f;
        private static string s_presence = "неизвестно";

        private readonly List<XRInputSubsystem> _subsystems = new List<XRInputSubsystem>();
        private readonly HashSet<XRInputSubsystem> _subscribed = new HashSet<XRInputSubsystem>();
        private float _nextPoll;
        private Vector3 _lastCameraLocal;
        private bool _hasCameraLocal;
        private bool? _presence;
        private InputTrackingState? _headTracking;

        /// <summary>Сводка для строк других детекторов.</summary>
        public static string Summary =>
            $"recenter/смена начала координат {s_originUpdates}" +
            (s_lastOriginUpdate >= 0f ? $" (последняя на {s_lastOriginUpdate:F1} с)" : "") +
            $", смена границы {s_boundaryChanges}, шлем на голове: {s_presence}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_originUpdates = 0;
            s_boundaryChanges = 0;
            s_lastOriginUpdate = -1f;
            s_presence = "неизвестно";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Серверу без шлема наблюдать нечего.
            if (Application.isBatchMode) return;

            var go = new GameObject(nameof(XrTrackingEventsLog));
            DontDestroyOnLoad(go);
            go.AddComponent<XrTrackingEventsLog>();
        }

        private void OnDestroy()
        {
            foreach (XRInputSubsystem subsystem in _subscribed)
            {
                if (subsystem == null) continue;
                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                subsystem.boundaryChanged -= OnBoundaryChanged;
            }
            _subscribed.Clear();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextPoll) return;
            _nextPoll = now + PollInterval;

            SubscribeNewSubsystems();
            PollHead();

            if (TryGetCameraLocal(out Vector3 cameraLocal))
            {
                _lastCameraLocal = cameraLocal;
                _hasCameraLocal = true;
            }
        }

        private void SubscribeNewSubsystems()
        {
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (XRInputSubsystem subsystem in _subsystems)
            {
                if (subsystem == null || !_subscribed.Add(subsystem)) continue;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                subsystem.boundaryChanged += OnBoundaryChanged;
                GameLog.PhysicalSpace.Info(
                    $"[XrTracking] Подсистема ввода {subsystem.subsystemDescriptor?.id}: начало координат " +
                    $"{subsystem.GetTrackingOriginMode()} (поддерживается {subsystem.GetSupportedTrackingOriginModes()}).", this);
            }
        }

        private void PollHead()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return;

            if (head.TryGetFeatureValue(CommonUsages.userPresence, out bool present) && present != _presence)
            {
                bool first = _presence == null;
                _presence = present;
                s_presence = present ? "да" : "нет";
                if (!first)
                    GameLog.PhysicalSpace.Info($"[XrTracking] Шлем {(present ? "надет" : "снят")}. Камера локально {CameraLocalText()}.", this);
            }

            if (head.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) && state != _headTracking)
            {
                InputTrackingState? previous = _headTracking;
                _headTracking = state;
                bool lost = previous.HasValue && (previous.Value & InputTrackingState.Position) != 0 && (state & InputTrackingState.Position) == 0;
                bool regained = previous.HasValue && (previous.Value & InputTrackingState.Position) == 0 && (state & InputTrackingState.Position) != 0;
                if (lost) GameLog.PhysicalSpace.Warning($"[XrTracking] Трекинг позиции головы потерян ({state}). Камера локально {CameraLocalText()}.", this);
                else if (regained) GameLog.PhysicalSpace.Info($"[XrTracking] Трекинг позиции головы восстановлен ({state}). Камера локально {CameraLocalText()}.", this);
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            s_originUpdates++;
            s_lastOriginUpdate = Time.unscaledTime;
            string before = _hasCameraLocal ? $"{_lastCameraLocal:F2}" : "неизвестно";
            GameLog.PhysicalSpace.Warning(
                $"[XrTracking] Начало координат трекинга обновлено (recenter/пол/Guardian): режим {subsystem.GetTrackingOriginMode()}, " +
                $"камера локально до {before} (≤{PollInterval:F1} с назад) → сейчас {CameraLocalText()}.", this);
        }

        private void OnBoundaryChanged(XRInputSubsystem subsystem)
        {
            s_boundaryChanges++;
            var points = new List<Vector3>();
            string boundary = subsystem.TryGetBoundaryPoints(points) ? $"{points.Count} точек" : "точки недоступны";
            GameLog.PhysicalSpace.Info($"[XrTracking] Граница Guardian изменилась: {boundary}. Камера локально {CameraLocalText()}.", this);
        }

        private void OnApplicationPause(bool paused) =>
            GameLog.PhysicalSpace.Info($"[XrTracking] Приложение {(paused ? "на паузе" : "снова активно")}. Камера локально {CameraLocalText()}.", this);

        private void OnApplicationFocus(bool focused) =>
            GameLog.PhysicalSpace.Info($"[XrTracking] Фокус приложения {(focused ? "получен" : "потерян")}.", this);

        private static bool TryGetCameraLocal(out Vector3 cameraLocal)
        {
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            Transform camera = avatar != null ? avatar.CameraTransform : null;
            cameraLocal = camera != null ? camera.localPosition : Vector3.zero;
            return camera != null;
        }

        private static string CameraLocalText() => TryGetCameraLocal(out Vector3 local) ? $"{local:F2}" : "нет аватара";
    }
}
