using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Детектор «голова далеко от тела»: голова (камера) ушла от корня аватара или от таза дальше
    /// возможного, тело легло горизонтально у стоящего игрока. Так выглядел баг с высотой после
    /// телепорта (2026-10-24): голова на 3,4 м при корне ≤ 1,1 м, тело лежит. Только наблюдает и пишет
    /// <b>одну строку на эпизод</b>: Warning на входе (условие держится <see cref="EnterSeconds"/>) и Info на
    /// выходе с максимумами — не чаще одного входа за <see cref="CooldownSeconds"/> на игрока.
    ///
    /// <para>
    /// На шлеме следит за своим аватаром, на сервере — за всеми живыми телами (позы удалённых копий).
    /// Мёртвые и призраки пропускаются: их поза законно «странная».
    /// </para>
    /// </summary>
    public sealed class AvatarPoseSanityMonitor : MonoBehaviour
    {
        /// <summary>Допустимая высота камеры над корнем: сидя/на коленях — от 0,3, в прыжке на носках — до 2,3 м.</summary>
        public const float MinHeadAboveRoot = 0.3f;
        public const float MaxHeadAboveRoot = 2.3f;

        /// <summary>Камера дальше от таза — шея так не тянется (с учётом масштаба аватара).</summary>
        public const float MaxHeadToHips = 1.0f;

        /// <summary>Наклон туловища (таз → голова) от вертикали у стоящего игрока.</summary>
        public const float MaxTorsoTilt = 55f;

        /// <summary>Наклон проверяется, только если камера выше корня на столько (игрок стоит).</summary>
        public const float StandingHeadHeight = 1.0f;

        public const float EnterSeconds = 0.5f;
        public const float ExitSeconds = 1f;
        public const float CooldownSeconds = 10f;
        private const float CheckInterval = 0.1f;
        private const float ScanInterval = 1f;

        private sealed class Episode
        {
            public float BadSince = -1f;
            public float GoodSince = -1f;
            public bool Active;
            public float StartedAt;
            public float LastEnteredAt = -999f;
            public float MaxHeadAboveRoot;
            public float MaxHeadToHips;
            public float MaxTilt;
            public string Reason;
        }

        private readonly List<PlayerController> _players = new List<PlayerController>();
        private readonly Dictionary<PlayerController, Episode> _episodes = new Dictionary<PlayerController, Episode>();
        private float _nextCheck;
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject(nameof(AvatarPoseSanityMonitor));
            DontDestroyOnLoad(go);
            go.AddComponent<AvatarPoseSanityMonitor>();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextCheck) return;
            _nextCheck = now + CheckInterval;

            if (now >= _nextScan)
            {
                _nextScan = now + ScanInterval;
                Rescan();
            }

            foreach (PlayerController player in _players)
            {
                if (player == null) continue;
                Check(player, now);
            }
        }

        private void Rescan()
        {
            _players.Clear();
            bool server = NetworkServer.active;
            foreach (PlayerController player in FindObjectsByType<PlayerController>())
            {
                // Сервер видит всех; клиент — только себя (чужие копии интерполируются и врут сами по себе).
                if (server || player.isLocalPlayer) _players.Add(player);
            }

            var gone = new List<PlayerController>();
            foreach (PlayerController known in _episodes.Keys)
                if (known == null || !_players.Contains(known)) gone.Add(known);
            foreach (PlayerController known in gone) _episodes.Remove(known);
        }

        private void Check(PlayerController player, float now)
        {
            UxrAvatar avatar = player.GetComponent<UxrAvatar>();
            if (avatar == null || avatar.CameraTransform == null || !player.IsAlive) return;

            if (!_episodes.TryGetValue(player, out Episode episode))
            {
                episode = new Episode();
                _episodes.Add(player, episode);
            }

            Transform root = player.transform;
            float scale = Mathf.Max(0.01f, root.lossyScale.y);
            Vector3 head = avatar.CameraPosition;
            Transform hipsBone = PlayerBodyCenter.HipsOf(player);
            Vector3 hips = hipsBone != null ? hipsBone.position : PlayerBodyCenter.Of(player);

            float headAboveRoot = (head.y - root.position.y) / scale;
            float headToHips = Vector3.Distance(head, hips) / scale;
            float tilt = Vector3.Angle(head - hips, Vector3.up);

            string reason = null;
            if (headAboveRoot < MinHeadAboveRoot || headAboveRoot > MaxHeadAboveRoot)
                reason = $"голова над корнем {headAboveRoot:F2} м вне [{MinHeadAboveRoot:F1}; {MaxHeadAboveRoot:F1}]";
            else if (headToHips > MaxHeadToHips)
                reason = $"голова от таза {headToHips:F2} м > {MaxHeadToHips:F1}";
            else if (headAboveRoot > StandingHeadHeight && tilt > MaxTorsoTilt)
                reason = $"тело наклонено на {tilt:F0}° у стоящего игрока";

            if (reason != null)
            {
                episode.GoodSince = -1f;
                if (episode.BadSince < 0f) episode.BadSince = now;

                if (episode.Active)
                {
                    episode.MaxHeadAboveRoot = Mathf.Max(episode.MaxHeadAboveRoot, Mathf.Abs(headAboveRoot));
                    episode.MaxHeadToHips = Mathf.Max(episode.MaxHeadToHips, headToHips);
                    episode.MaxTilt = Mathf.Max(episode.MaxTilt, tilt);
                }
                else if (now - episode.BadSince >= EnterSeconds && now - episode.LastEnteredAt >= CooldownSeconds)
                {
                    episode.Active = true;
                    episode.StartedAt = now;
                    episode.LastEnteredAt = now;
                    episode.Reason = reason;
                    episode.MaxHeadAboveRoot = Mathf.Abs(headAboveRoot);
                    episode.MaxHeadToHips = headToHips;
                    episode.MaxTilt = tilt;
                    LogEnter(player, avatar, reason, head, hips, headAboveRoot, headToHips, tilt);
                }
                return;
            }

            episode.BadSince = -1f;
            if (!episode.Active) return;
            if (episode.GoodSince < 0f) episode.GoodSince = now;
            if (now - episode.GoodSince < ExitSeconds) return;

            episode.Active = false;
            GameLog.PhysicalSpace.Info(
                $"[AvatarPoseSanity] {Who(player)}: поза снова в норме через {now - episode.StartedAt:F1} с " +
                $"(было: {episode.Reason}; максимумы — голова над корнем {episode.MaxHeadAboveRoot:F2} м, " +
                $"голова от таза {episode.MaxHeadToHips:F2} м, наклон {episode.MaxTilt:F0}°).", player);
        }

        private static void LogEnter(PlayerController player, UxrAvatar avatar, string reason, Vector3 head, Vector3 hips,
                                     float headAboveRoot, float headToHips, float tilt)
        {
            Transform root = player.transform;
            Vector3 headLocal = root.InverseTransformPoint(head);
            Vector3 cameraLocal = avatar.CameraTransform.localPosition;
            string floor = FloorProbe.TryFindFloor(root.position, out float floorY, out Collider floorCollider)
                ? $"пол '{floorCollider.name}' y={floorY:F2}"
                : "пола (Ground) под корнем нет";
            string side = NetworkServer.active && !player.isLocalPlayer ? "сервер, копия" : "своё устройство";
            string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            GameLog.PhysicalSpace.Warning(
                $"[AvatarPoseSanity] {Who(player)} ({side}, карта {map}): {reason}. " +
                $"root={root.position:F2} yaw={root.eulerAngles.y:F0} scale={root.lossyScale.y:F2}, head={head:F2} " +
                $"(в корне {headLocal:F2}, камера локально {cameraLocal:F2}), hips={hips:F2}, " +
                $"голова над корнем {headAboveRoot:F2} м, голова от таза {headToHips:F2} м, наклон {tilt:F0}°, {floor}. " +
                $"Последний перенос: {LocalAvatarMoveLog.LastMove}. Трекинг: {XrTrackingEventsLog.Summary}.", player);
        }

        private static string Who(PlayerController player)
        {
            string name = player.Session != null && !string.IsNullOrEmpty(player.Session.PlayerName) ? player.Session.PlayerName : player.name;
            return $"{name}#{player.netId}";
        }
    }
}
