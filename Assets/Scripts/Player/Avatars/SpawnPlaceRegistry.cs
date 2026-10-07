using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Фасад захвата/разрешения места игрока (T-50). Поза живёт в PlayerSession.Calibration,
    /// реестр не хранит копий. Calibrated — координаты якорей, uncalibrated — мировое место
    /// при смене карты. Точка разрешается до Instantiate, без последующего клиентского рывка.
    /// </summary>
    public static class SpawnPlaceRegistry
    {
        /// <summary>Диагностический счётчик снимков на серверных сессиях.</summary>
        public static int Count
        {
            get
            {
                int count = 0;
                foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
                {
                    PlayerSession session = identity != null ? identity.GetComponent<PlayerSession>() : null;
                    if (session != null && session.Calibration.Placement.HasValue) count++;
                }
                return count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            MapLoader.MapLoadStarted -= OnMapLoadStarted;
            MapLoader.MapLoadStarted += OnMapLoadStarted;
        }

        private static void OnMapLoadStarted(string sceneName) => CaptureAll($"смена карты на '{sceneName}'");

        /// <summary>Старая сцена ещё существует; снимки сессий сохраняются и при временно отсутствующем теле.</summary>
        public static void CaptureAll(string reason)
        {
            if (!NetworkServer.active || PlayersManager.Instance == null) return;
            foreach (PlayerSession session in PlayersManager.Instance.Sessions)
            {
                if (session != null && session.ActiveAvatar != null)
                    Capture(session, session.ActiveAvatar.transform, reason);
            }
        }

        public static bool Capture(PlayerSession session, Transform avatar, string reason)
        {
            if (session == null || avatar == null) return false;
            if (session.IsCalibrated && !session.Calibration.Placement.IsAnchored &&
                !PhysicalSpaceAnchorFrame.TryBuildFromScene(out _, out string diagnosis))
            {
                GameLog.PhysicalSpace.Warning($"[SpawnPlaceRegistry] {session.PlayerName}: снимок для другой карты не снят: {diagnosis}.");
                return false;
            }
            return session.ServerCapturePlacement(avatar.position, avatar.rotation, reason);
        }

        /// <summary>Один и тот же путь у карты, смены тела и переподключения.</summary>
        public static bool TryResolve(PlayerSession session, out AvatarSpawnPoint point, out string diagnosis)
        {
            point = default;
            if (session == null)
            {
                diagnosis = "сессия не передана";
                return false;
            }
            PlayerPlacement place = session.Calibration.Placement;
            PhysicalSpaceAnchorFrame frame = default;
            if (place.IsAnchored) PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out _);
            string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!place.TryResolve(session.IsCalibrated, map, frame, out Vector3 position, out Quaternion rotation, out diagnosis))
                return false;
            point = new AvatarSpawnPoint(position, rotation,
                place.IsAnchored ? AvatarSpawnPointSource.CalibratedPlace : AvatarSpawnPointSource.PreviousWorldPlace,
                place.CapturedOnMap);
            return true;
        }

        /// <summary>Совместимость старого харнесса: очищает placement у владельцев, самостоятельной памяти нет.</summary>
        public static void Clear()
        {
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                PlayerSession session = identity != null ? identity.GetComponent<PlayerSession>() : null;
                if (session != null) session.ServerRememberPlacement(PlayerPlacement.None);
            }
        }

        /// <summary>Старый API — делегирование владельцу уже созданной сессии.</summary>
        public static void Remember(uint sessionNetId, Vector3 localPosition, Quaternion localRotation, string capturedOnMap)
        {
            if (!NetworkServer.spawned.TryGetValue(sessionNetId, out NetworkIdentity identity)) return;
            PlayerSession session = identity != null ? identity.GetComponent<PlayerSession>() : null;
            if (session != null)
                session.ServerRememberPlacement(PlayerPlacement.Anchored(localPosition, localRotation, capturedOnMap));
        }
    }
}
