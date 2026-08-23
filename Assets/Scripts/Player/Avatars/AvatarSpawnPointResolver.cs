using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Откуда взялась точка спавна. Нужен ровно для одного — чтобы строка в логе
    /// отвечала на вопрос «почему аватар оказался здесь», а не только «где он».
    /// </summary>
    public enum AvatarSpawnPointSource
    {
        /// <summary>Зона спавна команды на карте (<see cref="TeamSpawnZone"/>).</summary>
        TeamSpawnZone,

        /// <summary>Точка спавна Mirror (<c>NetworkStartPosition</c>), команды не знает.</summary>
        NetworkStartPosition,

        /// <summary>Ничего не нашлось — начало координат. Всегда повод для строки в логе.</summary>
        WorldOrigin
    }

    /// <summary>Готовая точка спавна: куда ставить и откуда она взялась.</summary>
    public readonly struct AvatarSpawnPoint
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly AvatarSpawnPointSource Source;

        /// <summary>Имя объекта, давшего точку. Пусто для <see cref="AvatarSpawnPointSource.WorldOrigin"/>.</summary>
        public readonly string SourceName;

        public AvatarSpawnPoint(Vector3 position, Quaternion rotation,
                                AvatarSpawnPointSource source, string sourceName)
        {
            Position   = position;
            Rotation   = rotation;
            Source     = source;
            SourceName = sourceName;
        }

        public override string ToString()
        {
            switch (Source)
            {
                case AvatarSpawnPointSource.TeamSpawnZone:
                    return $"зона спавна '{SourceName}' в {Position}";
                case AvatarSpawnPointSource.NetworkStartPosition:
                    return $"точка Mirror '{SourceName}' в {Position}";
                default:
                    return "начало координат (точек спавна не нашлось)";
            }
        }
    }

    /// <summary>
    /// Отвечает на единственный вопрос: <b>где</b> создавать аватар. Что именно
    /// создавать — дело <see cref="AvatarSpawnStrategy"/>, кому создавать — дело
    /// <see cref="AvatarManager"/>.
    ///
    /// <para>
    /// Порядок поиска — от знающего про команды к не знающему:
    /// </para>
    /// <list type="number">
    /// <item><see cref="TeamSpawnZone"/> нужной команды. Единственная точка на карте,
    ///       которая знает, чья она: <c>TeamSpawnZone.Team</c>. Той же точкой
    ///       пользуются <c>EliminationMode.PrepareNextRound</c> и <c>DebugOrchestrator</c>,
    ///       то есть спавн, респавн и отладочная расстановка сходятся в одном месте.</item>
    /// <item><c>NetworkStartPosition</c> из Mirror. Про команды не знает и раздаёт точки
    ///       по кругу — годится как запасной вариант для карт без зон.</item>
    /// <item>Начало координат. Это отказ, а не решение: раньше сюда молча попадали все
    ///       аватары после смены карты (находка <b>WPN-03</b>), поэтому теперь случай
    ///       именуется явно и вызывающий пишет о нём в лог.</item>
    /// </list>
    ///
    /// <para>
    /// Точка зоны берётся как <c>transform.position</c> — это уровень пола: коробка
    /// триггера в префабе <c>TeamSpawnZone</c> центрирована на самом объекте и уходит
    /// вниз ровно настолько же, насколько вверх. Той же трактовки держится
    /// <c>PlayerController.Respawn</c>: он передаёт <c>zone.transform</c> в
    /// <c>UxrManager.MoveAvatarTo</c>, а тот ждёт <i>floor position</i>.
    /// </para>
    /// </summary>
    public static class AvatarSpawnPointResolver
    {
        /// <summary>
        /// Ищет точку спавна для команды. <paramref name="team"/> может быть <c>null</c> —
        /// это «команда ещё не выбрана», и тогда зоны не спрашиваются вовсе.
        /// </summary>
        public static AvatarSpawnPoint Resolve(TeamData team)
        {
            TeamSpawnZone zone = FindZone(team);
            if (zone != null)
            {
                Transform zoneTransform = zone.transform;
                return new AvatarSpawnPoint(zoneTransform.position, zoneTransform.rotation,
                                            AvatarSpawnPointSource.TeamSpawnZone, zone.name);
            }

            Transform startPosition = NetworkManager.singleton != null
                ? NetworkManager.singleton.GetStartPosition()
                : null;

            if (startPosition != null)
            {
                return new AvatarSpawnPoint(startPosition.position, startPosition.rotation,
                                            AvatarSpawnPointSource.NetworkStartPosition, startPosition.name);
            }

            return new AvatarSpawnPoint(Vector3.zero, Quaternion.identity,
                                        AvatarSpawnPointSource.WorldOrigin, string.Empty);
        }

        /// <summary>
        /// Зона спавна заданной команды или <c>null</c>.
        ///
        /// <para>
        /// Порядок обхода задан явно (<see cref="FindObjectsSortMode.InstanceID"/>):
        /// если художник поставил на карту две зоны одной команды, выбор обязан быть
        /// одинаковым от прогона к прогону, иначе спавн станет случайным.
        /// </para>
        /// </summary>
        public static TeamSpawnZone FindZone(TeamData team)
        {
            if (team == null) return null;

            TeamSpawnZone[] zones = Object.FindObjectsByType<TeamSpawnZone>(FindObjectsSortMode.InstanceID);
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null && zones[i].Team == team)
                    return zones[i];
            }

            return null;
        }
    }
}
