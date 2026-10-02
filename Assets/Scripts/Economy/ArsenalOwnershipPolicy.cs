using System.Collections.Generic;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Economy
{
    /// <summary>
    /// Закрепляет стены арсенала за игроками (T-45): стена в зоне спавна команды — одному игроку этой
    /// команды. Правило — <see cref="ArsenalOwnership"/> (чистая функция), здесь только сбор данных
    /// со сцены и запись владельца в стену (<see cref="ArsenalWallController.ServerSetOwner"/>).
    ///
    /// <para>
    /// Сервер, раз в <see cref="Interval"/> секунд: состав команд, смена сторон (зона отвечает, чья она
    /// сейчас — <see cref="TeamSpawnZone.Team"/>) и подключения подхватываются сами, без подписок.
    /// Запись в стену — только при изменении. Режим ушёл (разминка) — владельцы снимаются: без
    /// экономики стена снова общая.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArsenalOwnershipPolicy : MonoBehaviour
    {
        private const float Interval = 0.5f;

        private GameMode _mode;
        private float _nextUpdate;
        private readonly List<ArsenalWallController> _walls = new List<ArsenalWallController>();

        private void Awake()
        {
            _mode = GetComponent<GameMode>();
        }

        private void Update()
        {
            if (!NetworkServer.active || Time.unscaledTime < _nextUpdate) return;
            _nextUpdate = Time.unscaledTime + Interval;
            ServerAssign();
        }

        private void OnDisable()
        {
            if (!NetworkServer.active) return;

            foreach (ArsenalWallController wall in FindWalls())
                if (wall != null && wall.isServer) wall.ServerSetOwner(0);
        }

        /// <summary>Раздаёт стены. Для тестов — напрямую.</summary>
        public void ServerAssign()
        {
            if (_mode == null) _mode = GetComponent<GameMode>();
            if (_mode == null) return;

            List<ArsenalWallController> walls = FindWalls();
            TeamSpawnZone[] zones = FindObjectsByType<TeamSpawnZone>(FindObjectsInactive.Exclude);
            var seats = new List<ArsenalOwnership.Wall>(walls.Count);
            foreach (ArsenalWallController wall in walls)
            {
                if (wall == null || !wall.isServer) continue;
                seats.Add(new ArsenalOwnership.Wall(wall.netId, TeamOf(wall, zones), wall.OwnerSessionNetId));
            }

            var players = new List<ArsenalOwnership.Player>();
            foreach (TeamRuntimeData state in _mode.TeamStates.Values)
            {
                if (state == null || state.Team == null) continue;
                foreach (PlayerSession session in state.Sessions)
                    if (session != null) players.Add(new ArsenalOwnership.Player(session.netId, state.Team.teamIndex));
            }

            Dictionary<uint, uint> owners = ArsenalOwnership.Assign(seats, players);

            foreach (ArsenalWallController wall in walls)
            {
                if (wall != null && wall.isServer && owners.TryGetValue(wall.netId, out uint owner))
                    wall.ServerSetOwner(owner);
            }
        }

        /// <summary>
        /// Команда зоны, в которой стоит стена сейчас; −1 — стена вне командной зоны. Зону ищет
        /// <see cref="SpawnZoneMembership"/>: на картах стена — сосед зоны, а не дочерний объект (T-47).
        /// </summary>
        public static int TeamOf(ArsenalWallController wall)
        {
            TeamSpawnZone zone = ExplicitZoneOf(wall);
            if (zone == null && wall != null) zone = SpawnZoneMembership.ZoneOf(wall.transform);
            return TeamOfZone(zone);
        }

        /// <summary>То же среди уже найденных зон — раздача зовёт это дважды в секунду на каждую стену.</summary>
        public static int TeamOf(ArsenalWallController wall, IEnumerable<TeamSpawnZone> zones)
        {
            TeamSpawnZone zone = ExplicitZoneOf(wall);
            if (zone == null && wall != null) zone = SpawnZoneMembership.ZoneOf(wall.transform, zones);
            return TeamOfZone(zone);
        }

        // Новая станция стоит снаружи зоны; её команда задаётся разметкой, а не текущими bounds.
        private static TeamSpawnZone ExplicitZoneOf(ArsenalWallController wall)
        {
            ArsenalStationAnchor anchor = wall != null ? wall.GetComponent<ArsenalStationAnchor>() : null;
            return anchor != null ? anchor.Zone : null;
        }

        private static int TeamOfZone(TeamSpawnZone zone)
        {
            TeamData team = zone != null ? zone.Team : null;
            return team != null ? team.teamIndex : -1;
        }

        private List<ArsenalWallController> FindWalls()
        {
            _walls.RemoveAll(w => w == null);
            if (_walls.Count == 0)
                _walls.AddRange(FindObjectsByType<ArsenalWallController>());
            return _walls;
        }
    }
}
