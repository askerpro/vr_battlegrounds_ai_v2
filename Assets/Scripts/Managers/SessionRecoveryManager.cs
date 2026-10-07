using UnityEngine;
using System;
using System.Collections.Generic;
using VrBattlegrounds.Player;
using VrBattlegrounds.Core;
using Mirror;

namespace VrBattlegrounds.Managers
{
    public class SessionSnapshot
    {
        public string DeviceToken;
        public string PlayerName;
        public int TeamIndex;
        public int AvatarIndex;

        /// <summary>
        /// Калибровка игрока целиком (T-50): пол, рост глаз, признак и поза корня трекинга. Такая же
        /// характеристика игрока, как команда и скин, и переживать отключение обязана так же: без
        /// признака вернувшийся откалиброванный игрок выглядел бы для сервера новичком, чьё место
        /// можно назначить (CAL-02), а без пола и роста вернулся бы стандартным — и переподключение
        /// в бою уже не дало бы их поменять.
        /// </summary>
        public PhysicalSpaceUtils.PlayerCalibration Calibration;

        /// <summary>Признак калибровки по якорям из <see cref="Calibration" />.</summary>
        public bool IsCalibrated => Calibration.IsCalibrated;

        /// <summary>
        /// Момент сохранения, секунды от старта процесса. По нему считается возраст записи:
        /// ждать вернувшегося игрока имеет смысл в пределах матча, а не бесконечно.
        /// </summary>
        public double SavedAtSeconds;

        // Stats
        public int Kills;
        public int Deaths;
        public int Score;

        // Physical state to restore
        public float Health;
        /// <summary>Совместимость старого харнесса: проекция одной позы, отдельного поля хранения нет.</summary>
        public Vector3 Position
        {
            get => Calibration.Placement.Position;
            set => Calibration = Calibration.WithPlacement(PhysicalSpaceUtils.PlayerPlacement.World(value, Rotation, CapturedOnMap));
        }
        public Quaternion Rotation
        {
            get => Calibration.Placement.HasValue ? Calibration.Placement.Rotation : Quaternion.identity;
            set => Calibration = Calibration.WithPlacement(PhysicalSpaceUtils.PlayerPlacement.World(Position, value, CapturedOnMap));
        }
        public bool NeedsPhysicalRestore;

        /// <summary>
        /// Карта отключения и контекст восстановления живого тела. Пространство координат
        /// самой позы определяется Calibration.Placement; мировая поза годится для подключения
        /// на той же карте, anchor-pose переводится в систему якорей новой карты.
        /// </summary>
        public string CapturedOnMap = string.Empty;

        /// <summary>
        /// Живой snapshot той же карты имеет приоритет перед клиентским воспоминанием.
        ///
        /// <para>
        /// Условий два, и второе появилось вместе с CAL-02: игрок был жив (иначе
        /// восстанавливать здоровье нельзя) <b>и</b> сервер всё ещё на той же карте.
        /// Саму физическую привязку откалиброванного игрока смерть не сбрасывает:
        /// этот случай независимо решает PlayersManager.InitialCalibration.
        /// </para>
        /// </summary>
        /// <param name="currentMap">Имя активной сцены сервера сейчас.</param>
        public bool CanRestorePlaceOn(string currentMap)
        {
            return NeedsPhysicalRestore
                   && !string.IsNullOrEmpty(CapturedOnMap)
                   && string.Equals(CapturedOnMap, currentMap, System.StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Серверный менеджер для сохранения состояния игроков при отключении и восстановления при переподключении.
    /// Работает поверх Mirror, идентифицируя игроков по deviceToken.
    /// </summary>
    [DefaultExecutionOrder(ManagerOrder.SessionRecoveryManager)]
    public class SessionRecoveryManager : MonoBehaviour
    {
        public static SessionRecoveryManager Instance { get; private set; }

        [Header("Время жизни снимков")]
        [Tooltip("Сколько минут ждать вернувшегося игрока. Старше — снимок выбрасывается, " +
                 "и игрок заходит как новый. Ноль и меньше отключают срок годности.")]
        [SerializeField] private float _snapshotLifetimeMinutes = 7f;

        [Tooltip("Потолок числа хранимых снимков. При переполнении выбрасывается самый старый.")]
        [SerializeField] private int _maxStoredSnapshots = 32;

        private Dictionary<string, SessionSnapshot> _disconnectedSessions = new Dictionary<string, SessionSnapshot>();

        /// <summary>
        /// Часы менеджера. Отдельным полем, потому что иначе срок годности проверяется
        /// только реальным ожиданием — в тесте это семь минут простоя.
        /// </summary>
        private Func<double> _timeSource = () => Time.realtimeSinceStartupAsDouble;

        /// <summary>Записей в хранилище. Нужен, чтобы за ростом словаря можно было следить снаружи.</summary>
        public int StoredSnapshotsCount => _disconnectedSessions.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        [Server]
        public void SaveDisconnectedSession(string deviceToken, PlayerSession session, PlayerController avatar)
        {
            if (string.IsNullOrEmpty(deviceToken) || session == null) return;

            if (avatar != null)
                session.ServerCapturePlacement(avatar.transform.position, avatar.transform.rotation, "отключение");

            var snapshot = new SessionSnapshot
            {
                DeviceToken = deviceToken,
                PlayerName = session.PlayerName,
                TeamIndex = session.TeamIndex,
                AvatarIndex = session.AvatarIndex,
                Kills = session.Kills,
                Deaths = session.Deaths,
                Score = session.Score,
                Calibration = session.Calibration,
                NeedsPhysicalRestore = false,
                CapturedOnMap = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                SavedAtSeconds = _timeSource()
            };

            if (avatar != null)
            {
                snapshot.Health = avatar.Health;
                // Спавним на месте только если игрок был жив
                if (avatar.IsAlive)
                {
                    snapshot.NeedsPhysicalRestore = true;
                }
            }

            _disconnectedSessions[deviceToken] = snapshot;
            GameLog.Player.Info($"[SessionRecoveryManager] Saved session for {session.PlayerName} (Token: {deviceToken}).");

            DropExpiredSnapshots();
            DropOldestWhileOverLimit();
        }

        [Server]
        public SessionSnapshot GetAndRemoveSavedSession(string deviceToken)
        {
            if (string.IsNullOrEmpty(deviceToken)) return null;

            // Чистим перед выдачей, а не по таймеру: так протухший снимок не вернётся
            // даже в тот единственный кадр, когда уборка ещё не подоспела.
            DropExpiredSnapshots();

            if (_disconnectedSessions.TryGetValue(deviceToken, out SessionSnapshot snapshot))
            {
                _disconnectedSessions.Remove(deviceToken);
                GameLog.Player.Info($"[SessionRecoveryManager] Restoring session for {snapshot.PlayerName} (Token: {deviceToken}).");
                return snapshot;
            }

            return null;
        }

        // ── Срок годности ─────────────────────────────────────────────────────

        /// <summary>
        /// Выбрасывает снимки старше <see cref="_snapshotLifetimeMinutes"/>.
        ///
        /// Зачем вообще. Запись удалялась только при удачном переподключении того же
        /// устройства (находка NET-10). Игрок, ушедший насовсем, оставлял её навсегда,
        /// и хранилище росло всё время жизни сервера.
        /// </summary>
        private void DropExpiredSnapshots()
        {
            if (_snapshotLifetimeMinutes <= 0f) return; // срок годности отключён
            if (_disconnectedSessions.Count == 0) return;

            double deadline = _timeSource() - _snapshotLifetimeMinutes * 60.0;

            List<string> expired = null;
            foreach (KeyValuePair<string, SessionSnapshot> entry in _disconnectedSessions)
            {
                if (entry.Value.SavedAtSeconds > deadline) continue;

                if (expired == null) expired = new List<string>();
                expired.Add(entry.Key);
            }

            if (expired == null) return;

            foreach (string token in expired)
            {
                GameLog.Player.Info(
                    $"[SessionRecoveryManager] Снимок {_disconnectedSessions[token].PlayerName} (Token: {token}) " +
                    $"старше {_snapshotLifetimeMinutes} мин — выброшен.");
                _disconnectedSessions.Remove(token);
            }
        }

        /// <summary>
        /// Держит хранилище в пределах <see cref="_maxStoredSnapshots"/>, выбрасывая самые
        /// старые записи. Страховка на случай, когда отключений больше, чем успевает
        /// съесть срок годности.
        /// </summary>
        private void DropOldestWhileOverLimit()
        {
            if (_maxStoredSnapshots <= 0) return; // лимит отключён

            while (_disconnectedSessions.Count > _maxStoredSnapshots)
            {
                string oldestToken = null;
                double oldestTime = double.MaxValue;

                foreach (KeyValuePair<string, SessionSnapshot> entry in _disconnectedSessions)
                {
                    if (entry.Value.SavedAtSeconds >= oldestTime) continue;

                    oldestTime = entry.Value.SavedAtSeconds;
                    oldestToken = entry.Key;
                }

                if (oldestToken == null) return; // защита от зацикливания

                GameLog.Player.Info(
                    $"[SessionRecoveryManager] Хранилище переполнено ({_maxStoredSnapshots}) — " +
                    $"выброшен самый старый снимок (Token: {oldestToken}).");
                _disconnectedSessions.Remove(oldestToken);
            }
        }
    }
}
