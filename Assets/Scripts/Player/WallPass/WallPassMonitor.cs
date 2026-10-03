using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Серверный автор T-40: читает реплицированную камеру, публикует состояние на сессии
    /// и применяет урон обычным конвейером актора. Скины историю опоры не сбрасывают.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallPassMonitor : MonoBehaviour
    {
        private const double SampleInterval = 0.05;
        private readonly WallPassTracker _tracker = new WallPassTracker();
        private PlayerSession _session;
        private PlayerController _avatar;
        private UxrAvatar _uxrAvatar;
        private NetworkTransformBase _headSync;
        private WallPassGeometry _geometry;
        private GameMode _mode;
        private Scene _scene;
        private float _floorY;
        private double _nextSample;
        private double _lastObservation = double.NaN;
        private bool _hasNetworkPose;
        private string _waitingReason;
        private Vector3? _serverTeleportTarget;
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;

        private void Awake() => _session = GetComponent<PlayerSession>();

        private void OnDisable()
        {
            if (_session != null && _session.isServer && NetworkServer.active)
                ResetHistory("компонент отключён");
        }

        private void LateUpdate()
        {
            if (_session == null || !_session.isServer || !NetworkServer.active ||
                !StateEventAuthority.IsWorldAuthority) return;

            double now = NetworkTime.time;
            if (now < _nextSample) return;
            _nextSample = now + SampleInterval;

            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            if (mode != _mode)
            {
                ResetHistory("смена режима");
                _mode = mode;
            }

            PlayerController avatar = _session.ActiveAvatar;
            if (_session.Role != GameRole.Player || _session.IsEliminated ||
                (avatar != null && !avatar.IsAlive) || mode == null || !mode.WallPassDetectionEnabled)
            {
                ResetHistory("нет боевого тела или фаза без обнаружения");
                return;
            }

            // Между уничтожением и спавном скина состояние сохраняется на сессии.
            if (avatar == null)
            {
                ReportWaiting("аватар ещё не появился");
                return;
            }

            if (!_session.InitialCalibrationReady)
            {
                ReportWaiting("первичный снимок калибровки");
                return;
            }

            if (avatar != _avatar)
            {
                _avatar = avatar;
                _uxrAvatar = avatar.GetComponent<UxrAvatar>();
                _headSync = _uxrAvatar != null && _uxrAvatar.CameraComponent != null
                    ? _uxrAvatar.CameraComponent.GetComponent<NetworkTransformBase>() : null;
                _hasNetworkPose = false;
                _rootPosition = avatar.transform.position;
                _rootRotation = avatar.transform.rotation;
            }

            if (_uxrAvatar == null || _uxrAvatar.CameraComponent == null)
            {
                ReportWaiting("нет камеры UxrAvatar");
                return;
            }

            // Первая поза камеры в префабе — ещё не место шлема владельца.
            bool needsClientPose = avatar.connectionToClient != null && !avatar.isOwned;
            if (needsClientPose && !_hasNetworkPose)
            {
                _hasNetworkPose = _headSync != null && _headSync.serverSnapshots.Count > 0;
                if (!_hasNetworkPose)
                {
                    ReportWaiting("первая сетевая поза шлема");
                    return;
                }
            }

            if (_geometry == null || _scene != avatar.gameObject.scene)
            {
                ResetHistory("смена физической сцены");
                _scene = avatar.gameObject.scene;
                // Арена плоская; корень аватара задаёт уровень виртуального пола.
                // Поза камеры и калибровочный пивот не могут стать новым уровнем пола.
                _floorY = avatar.transform.position.y;
                _geometry = new WallPassGeometry(avatar.gameObject.scene.GetPhysicsScene(), _floorY);
            }

            Vector3 head = _uxrAvatar.CameraComponent.transform.position;
            if (!Finite(head))
            {
                ReportWaiting("некорректная поза шлема");
                return;
            }

            // RPC переноса и поза головы едут разными каналами: не заводим опору
            // на старой позиции в промежутке между разрешением и исполнением переноса.
            if (_serverTeleportTarget.HasValue)
            {
                Vector3 delta = head - _serverTeleportTarget.Value;
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.01f)
                {
                    ReportWaiting("исполнение серверного переноса");
                    return;
                }
                _floorY = _serverTeleportTarget.Value.y;
                _geometry = new WallPassGeometry(avatar.gameObject.scene.GetPhysicsScene(), _floorY);
                _serverTeleportTarget = null;
            }

            if (head.y < _floorY + 0.1f || head.y > _floorY + 3.5f)
            {
                ReportWaiting("некорректная высота шлема");
                return;
            }

            // Калибровка меняет систему координат, а физический шаг — только камеру.
            // Разрешённое выравнивание вне боя начинает новую историю на новом месте.
            if (mode.PhysicalCalibrationEnabled &&
                ((avatar.transform.position - _rootPosition).sqrMagnitude > 0.0001f ||
                 Quaternion.Angle(avatar.transform.rotation, _rootRotation) > 0.1f))
                ResetHistory("выравнивание пространства вне боя");
            _rootPosition = avatar.transform.position;
            _rootRotation = avatar.transform.rotation;

            if (!double.IsNaN(_lastObservation) && now - _lastObservation > 0.15)
                _tracker.SuspendObservation();
            _lastObservation = now;

            WallPassObservation observation = _geometry.Observe(head, _tracker.HasSupport ? _tracker.Support : (Vector3?)null);
            if (!_tracker.HasSupport && !observation.HasOriginalSupport)
                ReportWaiting("нет исходной законной опоры");
            else if (_waitingReason != null)
            {
                GameLog.Player.Info($"[WallPass] {_session.PlayerName}: обнаружение готово, head={head:F3}.", this);
                _waitingReason = null;
            }

            bool punitive = mode.PlayersTakeDamage && !mode.IsWarmup;
            WallPassDecision decision = _tracker.Evaluate(observation, now, punitive, mode.WallPassSettings);
            WallPassStatus previous = _session.WallPassStatus;
            _session.ServerSetWallPassStatus(decision.Status);

            if (decision.Status.Stage != previous.Stage || decision.Kill)
                GameLog.Player.Info($"[WallPass] {_session.PlayerName}: {previous.Stage} → {decision.Status.Stage}, " +
                    $"cause={decision.Status.Cause}, head={head:F3}, depth={observation.HeadDepth:F3}, " +
                    $"lean={observation.LeanDistance:F3}, support={observation.HasOriginalSupport}, " +
                    $"return={decision.Status.ReturnPoint:F3}, punitive={punitive}, kill={decision.Kill}.", this);

            // ReceiveDamage может синхронно заменить тело на призрака. После вызова
            // больше не обращаться к прежнему аватару и не наносить второй штраф.
            if (decision.Kill && punitive)
            {
                avatar._actor.ReceiveDamage(Mathf.Max(avatar.Health, 1f));
                return;
            }
            if (decision.DealContactDamage && punitive)
                avatar._actor.ReceiveDamage(mode.WallPassSettings.SafeContactDamage);
        }

        /// <summary>Разрешённый серверный перенос стенда; физический шаг головы сюда не приходит.</summary>
        public void ResetForServerTeleport(Vector3 position)
        {
            if (_session != null && _session.isServer && StateEventAuthority.IsWorldAuthority)
            {
                ResetHistory("разрешённый серверный перенос");
                _serverTeleportTarget = position;
            }
        }

        private void ResetHistory(string reason)
        {
            if (_tracker.HasSupport || _session.WallPassStatus.Stage != WallPassStage.Clear)
                GameLog.Player.Verbose($"[WallPass] {_session.PlayerName}: история сброшена — {reason}.", this);
            _tracker.Reset();
            _lastObservation = double.NaN;
            _serverTeleportTarget = null;
            _session.ServerSetWallPassStatus(default);
        }

        private void ReportWaiting(string reason)
        {
            _tracker.SuspendObservation();
            _lastObservation = double.NaN;
            if (_waitingReason == reason) return;
            _waitingReason = reason;
            GameLog.Player.Info($"[WallPass] {_session.PlayerName}: ожидание — {reason}.", this);
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    }
}
