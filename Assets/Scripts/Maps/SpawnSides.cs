using System;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Смена сторон карты. Команда — это состав (Военные, Повстанцы): она постоянна всю серию,
    /// к ней привязаны цвет, аватар, счёт и статистика. Меняется только то, какой зоной спавна
    /// команда пользуется: карта несимметрична, и первую половину состав играет с одной стороны,
    /// вторую — с другой, как в CS.
    ///
    /// <para>
    /// <b>Единственная точка.</b> Зона спавна (<see cref="TeamSpawnZone"/>) хранит «домашнюю»
    /// команду — чья она в первой половине. Чья она сейчас, отвечает <see cref="Resolve"/>. Через
    /// <see cref="TeamSpawnZone.Team"/> это видят все: точка спавна, возрождение в зоне, «игрок
    /// в своей зоне», готовность, подсветка, боты.
    /// </para>
    ///
    /// <para>
    /// Состояние задаёт режим матча (<c>EliminationMode</c>) — на сервере и из хука SyncVar
    /// на клиентах. Вне матча стороны не поменяны.
    /// </para>
    /// </summary>
    public static class SpawnSides
    {
        private static TeamData _first;
        private static TeamData _second;

        /// <summary>Стороны поменяны: зоны первой команды принадлежат второй и наоборот.</summary>
        public static bool Swapped { get; private set; }

        /// <summary>Стороны поменялись (или вернулись). Зоны пересчитывают хозяина.</summary>
        public static event Action Changed;

        /// <summary>
        /// Задаёт пару команд, меняющихся сторонами, и признак смены. Повторный вызов с теми же
        /// значениями событие не поднимает.
        /// </summary>
        public static void Set(TeamData first, TeamData second, bool swapped)
        {
            bool effective = swapped && first != null && second != null && first != second;
            if (_first == first && _second == second && Swapped == effective) return;

            _first = first;
            _second = second;
            Swapped = effective;
            Changed?.Invoke();
        }

        /// <summary>Стороны на месте (матч кончился, режим сменился).</summary>
        public static void Reset() => Set(null, null, false);

        /// <summary>Чья сейчас зона с домашней командой <paramref name="homeTeam"/>.</summary>
        public static TeamData Resolve(TeamData homeTeam)
        {
            if (!Swapped || homeTeam == null) return homeTeam;
            if (homeTeam == _first) return _second;
            if (homeTeam == _second) return _first;
            return homeTeam;
        }
    }
}
