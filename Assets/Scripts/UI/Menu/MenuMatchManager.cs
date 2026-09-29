using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Админ» (<see cref="MenuScreenType.MatchManager"/>, виден только админу): команды матча
    /// и входы в экраны админа.
    ///
    /// <para>
    /// Каждая команда видна только когда имеет смысл (<see cref="AdminMapCommands.IsAvailable(MapCommand)"/>
    /// по реплицированному состоянию). Главная команда ситуации («Начать матч», «Продолжить»,
    /// «Следующая карта» / «В лобби») — главное действие справа внизу; остальные — строкой в
    /// содержимом, «Стоп» — опасная. Нажатие уходит на сервер командой сессии
    /// (<c>PlayerSession.CmdAdminMapCommand</c>), право и уместность сервер проверяет ещё раз.
    /// </para>
    /// </summary>
    public class MenuMatchManager : MenuScreen
    {
        /// <summary>Приоритет главного действия: первая доступная из списка.</summary>
        public static readonly MapCommand[] PrimaryOrder = { MapCommand.GoLive, MapCommand.Resume, MapCommand.NextMap };

        /// <summary>Вложенные экраны раздела (проверяет <c>MatchMenuWiringTests</c>).</summary>
        public static readonly (MenuScreenType screen, string label)[] Links =
        {
            (MenuScreenType.PlayersTeams, "Игроки и команды"),
            (MenuScreenType.SessionSetup, "Новая серия"),
        };

        private string _lastSnapshot = "";

        public override void Show()
        {
            base.Show();
            _lastSnapshot = "";
            Rebuild(forced: false);
        }

        private void Update()
        {
            if (RefreshDue()) Rebuild(forced: false);
        }

        public override void BuildPreview()
        {
            base.Show();
            Render(true, new HashSet<MapCommand> { MapCommand.GoLive, MapCommand.Stop }, false);
        }

        public static string Label(MapCommand command, bool lastMap)
        {
            switch (command)
            {
                case MapCommand.GoLive: return "Начать матч";
                case MapCommand.Pause: return "Пауза";
                case MapCommand.Resume: return "Продолжить";
                case MapCommand.NextMap: return lastMap ? "В лобби" : "Следующая карта";
                case MapCommand.Stop: return "Стоп (конец серии)";
                default: return command.ToString();
            }
        }

        /// <summary>Главное действие: первая доступная команда из <see cref="PrimaryOrder"/>; нет — <c>null</c>.</summary>
        public static MapCommand? PrimaryCommand(ICollection<MapCommand> available)
        {
            foreach (MapCommand c in PrimaryOrder)
                if (available.Contains(c)) return c;
            return null;
        }

        private void Rebuild(bool forced)
        {
            bool admin = MenuPermissions.ShowAdminUi();
            bool lastMap = Series.Instance != null && Series.Instance.IsLastMap;
            var available = new HashSet<MapCommand>();
            if (admin)
                foreach (MapCommand c in System.Enum.GetValues(typeof(MapCommand)))
                    if (AdminMapCommands.IsAvailable(c)) available.Add(c);

            string snapshot = admin + "|" + lastMap + "|" + string.Join(",", available);
            if (!forced && snapshot == _lastSnapshot) return;
            _lastSnapshot = snapshot;
            Render(admin, available, lastMap);
        }

        private void Render(bool admin, HashSet<MapCommand> available, bool lastMap)
        {
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Админ");

            if (!admin)
            {
                ClearActions();
                MenuKit.EmptyState(Content, "Управлять матчем может только админ.");
                return;
            }

            MapCommand? primary = PrimaryCommand(available);
            if (primary.HasValue)
            {
                MapCommand p = primary.Value;
                SetPrimary(Label(p, lastMap), () => Send(p));
            }
            else ClearActions();

            MenuKit.Section(Content, "Матч");
            var rest = new List<MapCommand>();
            foreach (MapCommand c in available)
                if (c != primary) rest.Add(c);

            if (available.Count == 0)
            {
                MenuKit.EmptyState(Content, "Сейчас управлять нечем: серия не идёт.");
            }
            else if (rest.Count > 0)
            {
                RectTransform row = MenuKit.Row(Content);
                foreach (MapCommand c in rest)
                {
                    MapCommand command = c;
                    MenuKit.Button(row, Label(c, lastMap), () => Send(command),
                                   c == MapCommand.Stop ? MenuButtonRole.Danger : MenuButtonRole.Secondary);
                }
            }

            MenuKit.Section(Content, "Разделы администратора");
            RectTransform links = MenuKit.Row(Content);
            foreach ((MenuScreenType screen, string label) in Links)
            {
                MenuScreenType target = screen;
                MenuKit.Button(links, label, () => Push(target));
            }
        }

        private void Send(MapCommand command)
        {
            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning($"[MenuMatchManager] {command}: нет локальной сессии.");
                return;
            }

            GameLog.UI.Info($"[MenuMatchManager] Админ: {command}.");
            PlayerSession.LocalSession.CmdAdminMapCommand(command);
            _lastSnapshot = "";
        }
    }
}
