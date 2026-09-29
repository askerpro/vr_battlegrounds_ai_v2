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
        /// <summary>
        /// Место, заданное калибровкой физического пространства: игрок стоит в комнате,
        /// и игра его не двигает (<see cref="CalibratedSpawnRegistry"/>, T-30).
        /// </summary>
        CalibratedPlace,

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
                case AvatarSpawnPointSource.CalibratedPlace:
                    return $"место, заданное калибровкой (снято на карте '{SourceName}'), в {Position}";
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
    /// <item><b>Место, заданное калибровкой</b> (<see cref="CalibratedSpawnRegistry"/>, T-30).
    ///       Бьёт всё остальное и по единственной причине: до калибровки игра не знает,
    ///       где игрок находится внутри арены, и вправе поставить его куда угодно;
    ///       после — его место задано физически, он стоит в комнате, и переносить его
    ///       в базу значит расклеить картинку с телом. Ветка достижима только для
    ///       игрока, объявившего <c>PlayerSession.IsCalibrated</c>, и только там,
    ///       где сессия передана вызывающим.</item>
    /// <item><see cref="TeamSpawnZone"/> нужной команды. Единственная точка на карте,
    ///       которая знает, чья она: <c>TeamSpawnZone.Team</c>. Только точка создания
    ///       аватара: респавн и смена команды никого не двигают (этап Б) — в зону
    ///       игрок приходит сам.</item>
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
    /// вниз ровно настолько же, насколько вверх.
    /// </para>
    /// </summary>
    public static class AvatarSpawnPointResolver
    {
        /// <summary>
        /// Ищет точку спавна для команды. <paramref name="team"/> может быть <c>null</c> —
        /// это «команда ещё не выбрана», и тогда зоны не спрашиваются вовсе.
        /// </summary>
        /// <param name="team">Команда игрока или <c>null</c>.</param>
        /// <param name="calibratedSession">
        ///     Сессия, чьё <b>откалиброванное</b> место обязано победить зону, или <c>null</c>,
        ///     если восстанавливать место не нужно. Разделение не косметическое: место
        ///     восстанавливают только там, где аватара не осталось (смена карты). При смене
        ///     скина или команды аватар жив, и резолвер не спрашивается вовсе (этап Б).
        /// </param>
        public static AvatarSpawnPoint Resolve(TeamData team, PlayerSession calibratedSession = null)
        {
            if (calibratedSession != null)
            {
                AvatarSpawnPoint calibrated;
                string diagnosis;

                if (CalibratedSpawnRegistry.TryResolve(calibratedSession, out calibrated, out diagnosis))
                    return calibrated;

                if (calibratedSession.IsCalibrated)
                {
                    GameLog.PhysicalSpace.Warning(
                        $"[AvatarSpawnPointResolver] {calibratedSession.PlayerName} откалиброван, но вернуть его " +
                        $"на своё место нечем: {diagnosis}. Ставим в зону команды — игрок увидит, что его сдвинули.");
                }
            }

            // Своей зоны на сцене нет, но идёт разминка — общая зона разминки (лобби: зона одна,
            // нейтральная, а игрок уже выбрал команду серии).
            TeamSpawnZone zone = FindZone(team) ?? FindWarmupZone(team);
            if (zone != null)
            {
                Transform zoneTransform = zone.SpawnPoint;
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
        /// <summary>
        /// Зона общей команды разминки (первая команда режима разминки), если идёт разминка и у
        /// <paramref name="team"/> своей зоны на сцене нет. Команду серии игрок выбирает в лобби,
        /// а зона лобби одна — нейтральная; без этого он появлялся бы в начале координат.
        /// </summary>
        private static TeamSpawnZone FindWarmupZone(TeamData team)
        {
            if (team == null) return null;

            GameModes.GameMode mode = Managers.GameplayManager.Instance != null ? Managers.GameplayManager.Instance.ActiveGameMode : null;
            if (mode == null || !mode.IsWarmup || mode.Teams == null || mode.Teams.Length == 0) return null;

            TeamData neutral = mode.Teams[0];
            return neutral != null && neutral != team ? FindZone(neutral) : null;
        }

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
