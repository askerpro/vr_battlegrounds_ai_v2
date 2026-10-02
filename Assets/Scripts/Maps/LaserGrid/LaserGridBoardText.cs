using System;
using System.Collections.Generic;
using System.Text;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.UI.HUD;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.Maps
{
    /// <summary>Что сейчас идёт — глазами табло лазерной сетки.</summary>
    public enum LaserGridBoardPhase
    {
        /// <summary>Режима нет (пауза, лобби): сетка и так не видна.</summary>
        None,
        WaitingForPlayers,
        Setup,
        Equipment,
        Countdown,
        Combat,
        /// <summary>Победа в раунде и экран итогов (<c>Resolution</c>, <c>Scoreboard</c>).</summary>
        RoundOver,
        /// <summary>Карта сыграна.</summary>
        MapOver,
        /// <summary>Режим без раундов, рисующий зоны (сейчас таких нет): время и счёт.</summary>
        OtherMode
    }

    /// <summary>
    /// Состояние раунда для табло зоны — всё, от чего зависит текст. Сравнивается целиком: табло
    /// пересобирает строку только когда снимок изменился (TMP перестраивает меш на каждое присвоение).
    /// Собирает <see cref="LaserGridScreens"/> из тех же источников, что часы (<see cref="RoundClock"/>,
    /// <see cref="WatchScore"/>) и обзор планшета.
    /// </summary>
    public struct LaserGridRoundInfo : IEquatable<LaserGridRoundInfo>
    {
        public LaserGridBoardPhase Phase;
        /// <summary>Остаток фазы, целые секунды вверх; −1 — показывать нечего.</summary>
        public int Seconds;
        /// <summary>Обратный отсчёт стоит: кто-то из живых вне своей зоны.</summary>
        public bool CountdownHeld;
        public int Round;
        public int TotalRounds;
        public bool HasScore;
        public int Own;
        public int Enemy;
        /// <summary>Закупка кончается таймером, готовность не спрашивается.</summary>
        public bool ByTimer;
        /// <summary>Игроков в команде зоны.</summary>
        public int TeamPlayers;
        /// <summary>Из них раунд ещё ждёт (не объявили готовность).</summary>
        public int TeamPending;
        /// <summary>Сколько раунд ждёт у соперника.</summary>
        public int EnemyPending;
        /// <summary>Отпечаток состава ожидаемых своей команды: сменился — пересобрать имена.</summary>
        public int PendingKey;

        public bool Equals(LaserGridRoundInfo o) =>
            Phase == o.Phase && Seconds == o.Seconds && CountdownHeld == o.CountdownHeld && Round == o.Round &&
            TotalRounds == o.TotalRounds && HasScore == o.HasScore && Own == o.Own && Enemy == o.Enemy &&
            ByTimer == o.ByTimer && TeamPlayers == o.TeamPlayers && TeamPending == o.TeamPending &&
            EnemyPending == o.EnemyPending && PendingKey == o.PendingKey;

        public override bool Equals(object obj) => obj is LaserGridRoundInfo o && Equals(o);

        public override int GetHashCode() => (int)Phase * 397 ^ Seconds * 31 ^ Round ^ TeamPending * 7 ^ PendingKey;
    }

    /// <summary>Хозяин стены персонального куска — что о нём нужно табло.</summary>
    public struct LaserGridOwnerInfo : IEquatable<LaserGridOwnerInfo>
    {
        /// <summary>Стена закреплена за игроком (T-45). Нет — стена команды (экономика выключена).</summary>
        public bool HasOwner;
        public string Name;
        /// <summary>Это игрок этой машины.</summary>
        public bool IsLocal;
        /// <summary>Раунд ждёт его готовности.</summary>
        public bool Pending;
        public bool InZone;
        public bool Alive;

        public bool Equals(LaserGridOwnerInfo o) =>
            HasOwner == o.HasOwner && Name == o.Name && IsLocal == o.IsLocal && Pending == o.Pending &&
            InZone == o.InZone && Alive == o.Alive;

        public override bool Equals(object obj) => obj is LaserGridOwnerInfo o && Equals(o);

        public override int GetHashCode() => (Name != null ? Name.GetHashCode() : 0) ^ (Pending ? 1 : 0) ^ (InZone ? 2 : 0);
    }

    /// <summary>Три строки табло: заголовок, крупное (отсчёт, время, счёт) и пояснение.</summary>
    public readonly struct LaserGridBoardText
    {
        public readonly string Title;
        public readonly string Big;
        public readonly string Detail;

        public LaserGridBoardText(string title, string big, string detail)
        {
            Title = title ?? string.Empty;
            Big = big ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Одна строка TMP с размерами частей — один меш на табло вместо трёх.</summary>
        public string ToRichText()
        {
            var sb = new StringBuilder(96);
            sb.Append("<size=45%>").Append(Title).Append("</size>\n");
            sb.Append(Big.Length > 0 ? Big : " ").Append('\n');
            sb.Append("<size=32%>").Append(Detail).Append("</size>");
            return sb.ToString();
        }

        public override string ToString() => Title + " | " + Big + " | " + Detail;
    }

    /// <summary>
    /// Что написать на табло лазерной сетки (T-47) в какой фазе — чистые правила, под юнит-тест.
    ///
    /// <para>
    /// Табло — для крупного обратного отсчёта, готовности раунда и хода фаз. Нотификаций часов
    /// (<see cref="WatchNotifications"/>) здесь нет: убийства, деньги, итоги — на часах.
    /// </para>
    /// </summary>
    public static class LaserGridBoardRules
    {
        /// <summary>Сколько имён ожидаемых помещается на общем табло; остальные — «и ещё N».</summary>
        public const int MaxNames = 3;

        public const string ReadyColor = "#4FD17A";   // MenuTheme.Success
        public const string WarnColor = "#FFAF00";    // MenuTheme.Accent

        /// <summary>Фаза табло по режиму. Не Elimination, но рисует зоны — <see cref="LaserGridBoardPhase.OtherMode"/>.</summary>
        public static LaserGridBoardPhase Classify(bool hasMode, bool isElimination, EliminationState state, RoundPhase phase)
        {
            if (!hasMode) return LaserGridBoardPhase.None;
            if (!isElimination) return LaserGridBoardPhase.OtherMode;

            switch (state)
            {
                case EliminationState.WaitingForPlayers: return LaserGridBoardPhase.WaitingForPlayers;
                case EliminationState.Finished: return LaserGridBoardPhase.MapOver;
            }

            switch (phase)
            {
                case RoundPhase.Setup: return LaserGridBoardPhase.Setup;
                case RoundPhase.Equipment: return LaserGridBoardPhase.Equipment;
                case RoundPhase.Countdown: return LaserGridBoardPhase.Countdown;
                case RoundPhase.Combat: return LaserGridBoardPhase.Combat;
                default: return LaserGridBoardPhase.RoundOver;
            }
        }

        /// <summary>Общее табло. <paramref name="pendingNames"/> — кого ждём в команде зоны.</summary>
        public static LaserGridBoardText Common(LaserGridRoundInfo r, IReadOnlyList<string> pendingNames)
        {
            return new LaserGridBoardText(Title(r), Big(r), CommonDetail(r, pendingNames));
        }

        /// <summary>Персональный кусок стены: чья стена, тот же крупный отсчёт, готовность лично хозяина.</summary>
        public static LaserGridBoardText Personal(LaserGridRoundInfo r, LaserGridOwnerInfo owner)
        {
            return new LaserGridBoardText(OwnerTitle(owner), Big(r), PersonalDetail(r, owner));
        }

        public static string Title(LaserGridRoundInfo r)
        {
            switch (r.Phase)
            {
                case LaserGridBoardPhase.WaitingForPlayers: return "ЖДЁМ ИГРОКОВ";
                case LaserGridBoardPhase.Countdown: return "ПРИГОТОВЬТЕСЬ!";
                case LaserGridBoardPhase.RoundOver: return "ИТОГ РАУНДА";
                case LaserGridBoardPhase.MapOver: return "КАРТА СЫГРАНА";
                case LaserGridBoardPhase.OtherMode: return "МАТЧ";
                case LaserGridBoardPhase.Setup: return RoundTitle(r, RoundPhase.Setup);
                case LaserGridBoardPhase.Equipment: return RoundTitle(r, RoundPhase.Equipment);
                case LaserGridBoardPhase.Combat: return RoundTitle(r, RoundPhase.Combat);
                default: return string.Empty;
            }
        }

        private static string RoundTitle(LaserGridRoundInfo r, RoundPhase phase)
        {
            string name = OverviewFormat.PhaseName(phase).ToUpperInvariant();
            if (r.Round <= 0) return name;
            return r.TotalRounds > 0 ? $"РАУНД {r.Round} ИЗ {r.TotalRounds} · {name}" : $"РАУНД {r.Round} · {name}";
        }

        /// <summary>
        /// Крупное: в отсчёте — секунды числом («3»), в закупке и бою — «м:сс», после раунда и карты — счёт.
        /// </summary>
        public static string Big(LaserGridRoundInfo r)
        {
            switch (r.Phase)
            {
                case LaserGridBoardPhase.Countdown:
                    return r.Seconds >= 0 ? r.Seconds.ToString() : string.Empty;
                case LaserGridBoardPhase.Equipment:
                case LaserGridBoardPhase.Combat:
                case LaserGridBoardPhase.OtherMode:
                    return r.Seconds >= 0 ? OverviewFormat.Clock(r.Seconds) : string.Empty;
                case LaserGridBoardPhase.RoundOver:
                case LaserGridBoardPhase.MapOver:
                    return r.HasScore ? WristDisplayFace.FormatScore(r.Own, r.Enemy) : string.Empty;
                default:
                    return string.Empty;
            }
        }

        public static string CommonDetail(LaserGridRoundInfo r, IReadOnlyList<string> pendingNames)
        {
            switch (r.Phase)
            {
                case LaserGridBoardPhase.WaitingForPlayers:
                    return "Матч начнётся, когда в обеих командах будут игроки";
                case LaserGridBoardPhase.Setup:
                    return "Все в свою зону — откроется закупка";
                case LaserGridBoardPhase.Equipment:
                    return Readiness(r, pendingNames);
                case LaserGridBoardPhase.Countdown:
                    return r.CountdownHeld
                        ? Colored("Отсчёт стоит: все живые — в свою зону", WarnColor)
                        : "Арсенал закрыт — по местам!";
                case LaserGridBoardPhase.Combat:
                case LaserGridBoardPhase.OtherMode:
                    return Score(r);
                case LaserGridBoardPhase.RoundOver:
                    return r.Round > 0 && r.TotalRounds > 0 ? $"Сыграно раундов: {r.Round} из {r.TotalRounds}" : string.Empty;
                default:
                    return string.Empty;
            }
        }

        /// <summary>Готовность в закупке: сколько своих готово, кого ждём, ждём ли соперника.</summary>
        public static string Readiness(LaserGridRoundInfo r, IReadOnlyList<string> pendingNames)
        {
            if (r.ByTimer) return "Старт по таймеру — берите оружие со своей стены";

            int ready = Math.Max(0, r.TeamPlayers - r.TeamPending);
            if (r.TeamPending <= 0)
            {
                if (r.TeamPlayers <= 0) return string.Empty;
                return r.EnemyPending > 0
                    ? Colored("Ваша команда готова", ReadyColor) + $" — ждём соперника ({r.EnemyPending})"
                    : Colored("Все готовы — сейчас отсчёт", ReadyColor);
            }

            var sb = new StringBuilder();
            sb.Append("Готовы ").Append(ready).Append(" из ").Append(r.TeamPlayers);

            if (pendingNames != null && pendingNames.Count > 0)
            {
                sb.Append(" · ждём: ");
                int shown = Math.Min(MaxNames, pendingNames.Count);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(string.IsNullOrEmpty(pendingNames[i]) ? "Игрок" : pendingNames[i]);
                }
                if (pendingNames.Count > shown) sb.Append(" и ещё ").Append(pendingNames.Count - shown);
            }

            sb.Append("\nГотовность — жетон на своей стене");
            return sb.ToString();
        }

        public static string OwnerTitle(LaserGridOwnerInfo owner)
        {
            if (!owner.HasOwner) return "СТЕНА КОМАНДЫ";
            string name = string.IsNullOrEmpty(owner.Name) ? "ИГРОК" : owner.Name.ToUpperInvariant();
            return owner.IsLocal ? name + " · ВЫ" : name;
        }

        public static string PersonalDetail(LaserGridRoundInfo r, LaserGridOwnerInfo owner)
        {
            if (!owner.HasOwner) return CommonDetail(r, null);

            switch (r.Phase)
            {
                case LaserGridBoardPhase.Equipment:
                    if (r.ByTimer) return "Закупка до конца таймера";
                    if (!owner.Pending) return Colored("ГОТОВ", ReadyColor);
                    if (!owner.Alive) return Colored("Выбыл — дойдите до своей зоны", WarnColor);
                    if (!owner.InZone) return Colored("Вне зоны — вернитесь", WarnColor);
                    return "Закупитесь и возьмите жетон";

                case LaserGridBoardPhase.Countdown:
                    if (r.CountdownHeld && owner.Alive && !owner.InZone)
                        return Colored("Вернитесь в зону — отсчёт стоит", WarnColor);
                    return r.CountdownHeld ? Colored("Отсчёт стоит", WarnColor) : "Приготовьтесь";

                default:
                    return CommonDetail(r, null);
            }
        }

        private static string Score(LaserGridRoundInfo r) =>
            r.HasScore ? "Счёт " + WristDisplayFace.FormatScore(r.Own, r.Enemy) : string.Empty;

        private static string Colored(string text, string color) => "<color=" + color + ">" + text + "</color>";
    }
}
