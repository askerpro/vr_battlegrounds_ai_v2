using System.Collections.Generic;
using System.Text;
using Mirror;
using TMPro;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.Bots;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Отладка» (<see cref="MenuScreenType.Debug"/>) — виден только в режиме отладки
    /// (<see cref="DebugMode"/>; выключили режим — контроллер уводит на раздел по умолчанию).
    ///
    /// <para>
    /// Строки собираются набором (<see cref="MenuKit"/>): статус, оверлей кадра, переходы на экраны
    /// админа и «Перф-тесты» (<see cref="Links"/>), боты-противники (<see cref="BotNetwork"/>),
    /// телепорт к точкам карты, выключение режима. Экран только шлёт запросы: телепорт —
    /// <see cref="DebugModeNetwork.RequestTeleport"/>; права проверяет сервер.
    /// </para>
    ///
    /// <para>
    /// Перестраивается раз в 0,5 с и только при изменении состава — пересоздание кнопок сбивало бы
    /// наведение луча в VR. Строка статуса обновляется на месте.
    /// </para>
    /// </summary>
    public class MenuDebug : MenuScreen
    {
        /// <summary>Колонок в сетке кнопок телепорта.</summary>
        private const int TeleportColumns = 4;

        /// <summary>Вложенные экраны: «Перф-тесты» — всем в режиме отладки, остальные — админу.</summary>
        public static readonly (MenuScreenType screen, string label, bool adminOnly)[] Links =
        {
            (MenuScreenType.PerfTests, "Перф-тесты", false),
            (MenuScreenType.MatchManager, "Матч", true),
            (MenuScreenType.PlayersTeams, "Игроки и команды", true),
            (MenuScreenType.SessionSetup, "Новая серия", true),
        };

        private string _lastSnapshot = "";
        private TMP_Text _status;

        public override void Show()
        {
            base.Show();
            _lastSnapshot = "";
            Refresh();
        }

        private void Update()
        {
            if (RefreshDue()) Refresh();
        }

        public override void BuildPreview()
        {
            base.Show();
            string[] labels = { "База A", "База B", "Арсенал A", "Арсенал B", "Центр", "Крыша", "Подвал" };
            Rebuild(true, labels, labels);
            if (_status != null) _status.text = "Сеть: хост · права админа: да · стресс-тест: не идёт · ботов: 0";
        }

        private void Refresh()
        {
            bool admin = MenuPermissions.ShowAdminUi();
            List<DebugTeleportTarget> targets = DebugTeleportTargets.Collect();

            var snapshot = new StringBuilder();
            snapshot.Append(admin).Append('|').Append(DebugPerfReadout.Visible);
            foreach (DebugTeleportTarget t in targets) snapshot.Append('|').Append(t.Id).Append(t.Label);

            if (snapshot.ToString() != _lastSnapshot)
            {
                _lastSnapshot = snapshot.ToString();
                Rebuild(admin, targets.ConvertAll(t => t.Id), targets.ConvertAll(t => t.Label));
            }

            if (_status != null) _status.text = BuildStatus(admin);
        }

        private void Rebuild(bool admin, IList<string> teleportIds, IList<string> teleportLabels)
        {
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Отладка");
            _status = MenuKit.Label(Content, "", MenuTextRole.Caption, MenuColorRole.TextSecondary);

            // ── Оверлей ────────────────────────────────────────────────────
            MenuKit.Section(Content, "Оверлей кадра");
            MenuKit.Button(MenuKit.Row(Content), DebugPerfReadout.Visible ? "Скрыть кадр" : "Показать кадр",
                           () => { DebugPerfReadout.SetVisible(!DebugPerfReadout.Visible); _lastSnapshot = ""; });

            // ── Разделы ────────────────────────────────────────────────────
            MenuKit.Section(Content, "Разделы");
            RectTransform links = MenuKit.Row(Content);
            foreach ((MenuScreenType screen, string label, bool adminOnly) in Links)
            {
                if (adminOnly && !admin) continue;
                MenuScreenType target = screen;
                MenuKit.Button(links, label, () => Push(target));
            }
            if (!admin) MenuKit.Label(Content, "Сервер прав админа не выдал — см. строку статуса.", MenuTextRole.Caption, MenuColorRole.TextSecondary);

            // ── Боты ───────────────────────────────────────────────────────
            // Только админу: сервер всё равно проверит права (BotNetwork).
            if (admin)
            {
                MenuKit.Section(Content, "Боты");
                RectTransform row = MenuKit.Row(Content);
                MenuKit.Button(row, "Добавить бота", () => RequestBots(add: true));
                MenuKit.Button(row, "Убрать всех", () => RequestBots(add: false), MenuButtonRole.Danger);
            }

            // ── Телепорт ───────────────────────────────────────────────────
            MenuKit.Section(Content, "Телепорт");
            if (teleportIds.Count == 0) MenuKit.EmptyState(Content, "На карте нет точек.");
            RectTransform grid = MenuKit.Grid(Content, TeleportColumns, 0.25f);
            for (int i = 0; i < teleportIds.Count; i++)
            {
                string id = teleportIds[i];
                MenuKit.Button(grid, teleportLabels[i], () => Teleport(id));
            }

            // ── Выход ──────────────────────────────────────────────────────
            MenuKit.Section(Content, "Режим");
            MenuKit.Button(MenuKit.Row(Content), "Выключить режим отладки", () => DebugMode.Set(false, "кнопка планшета"), MenuButtonRole.Danger);
        }

        private static string BuildStatus(bool admin)
        {
            string net = NetworkServer.active && NetworkClient.active ? "хост"
                       : NetworkClient.isConnected ? "клиент" : "нет сети";

            string stress = StressTestClientSession.IsRunning ? "идёт" : "не идёт";
            string reply = string.IsNullOrEmpty(DebugModeNetwork.LastReply) ? "" : "\nСервер: " + DebugModeNetwork.LastReply;
            return $"Сеть: {net} · права админа: {(admin ? "да" : "нет")} · стресс-тест: {stress} · ботов: {BotNetwork.CountVisibleBots()}{reply}";
        }

        private static void RequestBots(bool add)
        {
            if (!BotNetwork.Request(add, out string reason))
                PerfOverlay.Show("Боты: " + reason, 4f);
        }

        private static void Teleport(string id)
        {
            if (!DebugModeNetwork.RequestTeleport(id, out string reason))
                PerfOverlay.Show("Телепорт: " + reason, 4f);
        }
    }
}
