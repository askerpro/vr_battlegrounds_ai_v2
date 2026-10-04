using System.Collections.Generic;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Правило «стена ↔ игрок» — чистая функция без сцены и сети.
    ///
    /// <para>
    /// Каждая стена арсенала связана с зоной через <c>ArsenalStationAnchor.Zone</c>, а зона
    /// отвечает, чья она сейчас (с учётом смены сторон). Стены зоны делятся между игроками
    /// команды, которой зона принадлежит: по одной на игрока.
    /// </para>
    /// <list type="bullet">
    /// <item><b>Стабильно.</b> Владелец, который всё ещё в команде этой зоны, свою стену
    ///       сохраняет: переподключение, смерть, новый раунд не пересаживают игроков.</item>
    /// <item><b>Детерминированно.</b> Свободные стены (по возрастанию id) отдаются игрокам без
    ///       стены (по возрастанию id) — сервер один, но порядок не зависит от обхода сцены.</item>
    /// <item><b>Смена сторон.</b> Стены зоны после смены сторон принадлежат другой команде —
    ///       прежние владельцы им больше не подходят, и стены раздаются заново.</item>
    /// <item>Игроков больше, чем стен в зоне, — лишние остаются без стены (покупать им негде).</item>
    /// </list>
    /// </summary>
    public static class ArsenalOwnership
    {
        /// <summary>Стена: стабильный id (сетевой), команда её зоны сейчас (−1 — нейтральная/нет зоны), текущий владелец (0 — нет).</summary>
        public readonly struct Wall
        {
            public readonly uint Id;
            public readonly int Team;
            public readonly uint Owner;

            public Wall(uint id, int team, uint owner)
            {
                Id = id;
                Team = team;
                Owner = owner;
            }
        }

        /// <summary>Игрок: id сессии и индекс команды.</summary>
        public readonly struct Player
        {
            public readonly uint Session;
            public readonly int Team;

            public Player(uint session, int team)
            {
                Session = session;
                Team = team;
            }
        }

        /// <summary>
        /// Раздача стен. Результат — владелец для каждой стены (0 — без владельца).
        /// </summary>
        public static Dictionary<uint, uint> Assign(IReadOnlyList<Wall> walls, IReadOnlyList<Player> players)
        {
            var result = new Dictionary<uint, uint>();
            var teamOf = new Dictionary<uint, int>();
            foreach (Player player in players)
                if (player.Session != 0) teamOf[player.Session] = player.Team;

            var seated = new HashSet<uint>();

            var ordered = new List<Wall>(walls);
            ordered.Sort((a, b) => a.Id.CompareTo(b.Id));

            // Проход 1 — сохраняем тех, кто сидит на своей стене по праву.
            foreach (Wall wall in ordered)
            {
                bool keeps = wall.Owner != 0 && wall.Team >= 0 && !seated.Contains(wall.Owner) &&
                             teamOf.TryGetValue(wall.Owner, out int team) && team == wall.Team;

                result[wall.Id] = keeps ? wall.Owner : 0u;
                if (keeps) seated.Add(wall.Owner);
            }

            // Проход 2 — свободные стены игрокам без стены той же команды.
            var waiting = new List<Player>();
            foreach (Player player in players)
                if (player.Session != 0 && !seated.Contains(player.Session)) waiting.Add(player);
            waiting.Sort((a, b) => a.Session.CompareTo(b.Session));

            foreach (Wall wall in ordered)
            {
                if (result[wall.Id] != 0 || wall.Team < 0) continue;

                int index = waiting.FindIndex(p => p.Team == wall.Team);
                if (index < 0) continue;

                result[wall.Id] = waiting[index].Session;
                waiting.RemoveAt(index);
            }

            return result;
        }
    }
}
