using System;
using System.Collections.Generic;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>Кнопка админа на «Обзоре»: команда карты или переход на экран.</summary>
    public sealed class OverviewAction
    {
        public string Label = "";

        /// <summary>Команда карты (<c>CmdAdminMapCommand</c>); null — это переход.</summary>
        public MapCommand? Command;

        /// <summary>Экран для перехода, если это не команда.</summary>
        public MenuScreenType Screen = MenuScreenType.None;

        /// <summary>Разрушительное действие (конец серии) — роль кнопки Danger.</summary>
        public bool Danger;

        /// <summary>«Матч с ботами» (T-48): запрос серверу, не команда админа и не переход.</summary>
        public bool BotMatch;
    }

    /// <summary>Главное действие ситуации и остальные доступные кнопки.</summary>
    public sealed class OverviewAdminPlan
    {
        /// <summary>Справа внизу области контента; null — главного действия нет (идёт бой).</summary>
        public OverviewAction Primary;

        /// <summary>Строкой кнопок в контенте, по порядку.</summary>
        public List<OverviewAction> Others = new List<OverviewAction>();
    }

    /// <summary>
    /// Какие кнопки админа показать на «Обзоре» (решение пользователя 2026-09-29: у админа на обзоре —
    /// все полезные кнопки ситуации). Доступность команд — правило <c>AdminMapCommands.IsAvailable</c>,
    /// передаётся функцией, поэтому план проверяется без сцены. Главное действие — то, что двигает
    /// игру вперёд: в разминке «Начать матч», на паузе «Продолжить», на итоге карты «Следующая
    /// карта» / «В лобби», в лобби — выбор серии. Во время боя главного действия нет: «Пауза» — не
    /// шаг вперёд, её нечаянное нажатие обрывает раунд. «Стоп серии» — всегда последней и Danger.
    /// </summary>
    public static class OverviewAdminActions
    {
        public static OverviewAdminPlan Plan(OverviewContext context, bool isLastMap, Func<MapCommand, bool> available)
        {
            var plan = new OverviewAdminPlan();
            if (context == OverviewContext.Offline) return plan;

            if (context == OverviewContext.Lobby)
            {
                plan.Primary = Navigate("Выбрать серию", MenuScreenType.SessionSetup);
                plan.Others.Add(Navigate("Игроки и команды", MenuScreenType.PlayersTeams));
                AddIf(plan.Others, MapCommand.Stop, isLastMap, available);
                return plan;
            }

            MapCommand? primary = null;
            switch (context)
            {
                case OverviewContext.Warmup: primary = MapCommand.GoLive; break;
                case OverviewContext.Paused: primary = MapCommand.Resume; break;
                case OverviewContext.MapFinished: primary = MapCommand.NextMap; break;
            }
            if (primary.HasValue && available != null && available(primary.Value))
                plan.Primary = Command(primary.Value, isLastMap);

            foreach (MapCommand c in new[] { MapCommand.GoLive, MapCommand.Resume, MapCommand.NextMap, MapCommand.Pause })
                if (c != primary) AddIf(plan.Others, c, isLastMap, available);

            if (context == OverviewContext.Warmup || context == OverviewContext.MapFinished)
                plan.Others.Add(Navigate("Игроки и команды", MenuScreenType.PlayersTeams));

            AddIf(plan.Others, MapCommand.Stop, isLastMap, available);
            return plan;
        }

        public static string Label(MapCommand command, bool isLastMap)
        {
            switch (command)
            {
                case MapCommand.GoLive: return "Начать матч";
                case MapCommand.Pause: return "Пауза";
                case MapCommand.Resume: return "Продолжить";
                case MapCommand.NextMap: return isLastMap ? "В лобби" : "Следующая карта";
                default: return "Стоп серии";
            }
        }

        private static void AddIf(List<OverviewAction> list, MapCommand command, bool isLastMap, Func<MapCommand, bool> available)
        {
            if (available != null && available(command)) list.Add(Command(command, isLastMap));
        }

        private static OverviewAction Command(MapCommand command, bool isLastMap) => new OverviewAction
        {
            Label = Label(command, isLastMap),
            Command = command,
            Danger = command == MapCommand.Stop
        };

        private static OverviewAction Navigate(string label, MenuScreenType screen) =>
            new OverviewAction { Label = label, Screen = screen };
    }
}
