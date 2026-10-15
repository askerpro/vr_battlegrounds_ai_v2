using System.Collections.Generic;
using System.Text;
using Mirror;
using TMPro;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Bots;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.Maps.Detail;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Отладка» (<see cref="MenuScreenType.Debug"/>) — виден только в режиме отладки
    /// (<see cref="DebugMode"/>; выключили режим — контроллер уводит на раздел по умолчанию).
    ///
    /// <para>
    /// Строки собираются набором (<see cref="MenuKit"/>): статус, метки попаданий
    /// (<see cref="DebugHitMarkers"/>), перф-анализ (время кадра, детализация сцены и
    /// переход в «Перф-тесты» через <see cref="Links"/>), касание меню и
    /// телепорт к точкам карты. Категория выбирается локально, режим отладки из экрана не выключается.
    /// Экран только шлёт запросы: телепорт —
    /// <see cref="DebugModeNetwork.RequestTeleport"/>; права проверяет сервер.
    /// </para>
    ///
    /// <para>
    /// Перестраивается раз в 0,5 с и только при изменении состава — пересоздание кнопок сбивало бы
    /// наведение луча в VR. Статус и время кадра обновляются на месте.
    /// </para>
    /// </summary>
    public class MenuDebug : MenuScreen
    {
        /// <summary>Колонок в сетке кнопок телепорта.</summary>
        private const int TeleportColumns = 4;

        private enum Category { Overlays, Touch, Teleport, Performance }

        private static readonly string[] PreviewTeleports =
            { "База A", "База B", "Арсенал A", "Арсенал B", "Центр", "Крыша", "Подвал" };

        /// <summary>Вложенные перф-инструменты, доступные в режиме отладки.</summary>
        public static readonly (MenuScreenType screen, string label, bool adminOnly)[] Links =
        {
            (MenuScreenType.PerfTests, "Перф-тесты", false),
        };

        private string _lastSnapshot = "";
        private TMP_Text _status;
        private TMP_Text _frameReadout;
        private Category _category;
        private bool _preview;

        public override void Show()
        {
            base.Show();
            _preview = false;
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
            _preview = true;
            _lastSnapshot = "";
            Refresh();
        }

        private void Refresh()
        {
            bool admin = _preview || MenuPermissions.ShowAdminUi();
            string detailSnapshot = SceneDetailDebugSection.Snapshot();
            List<DebugTeleportTarget> targets = _preview ? new List<DebugTeleportTarget>() : DebugTeleportTargets.Collect();

            var snapshot = new StringBuilder();
            snapshot.Append(_category).Append('|').Append(_preview).Append('|').Append(admin)
                    .Append('|').Append(DebugHitMarkers.Visible)
                    .Append('|').Append(DebugFingerTipRays.Visible);
            foreach (DebugTeleportTarget t in targets) snapshot.Append('|').Append(t.Id).Append(t.Label);
            snapshot.Append('|').Append(detailSnapshot);

            if (snapshot.ToString() != _lastSnapshot)
            {
                _lastSnapshot = snapshot.ToString();
                Rebuild(admin, _preview ? PreviewTeleports : targets.ConvertAll(t => t.Id),
                        _preview ? PreviewTeleports : targets.ConvertAll(t => t.Label), !string.IsNullOrEmpty(detailSnapshot));
            }

            if (_status != null) _status.text = _preview
                ? "Сеть: хост · права админа: да · стресс-тест: не идёт · ботов: 0"
                : BuildStatus(admin);

            if (_frameReadout != null)
            {
                string frame = DebugPerfReadout.Format();
                _frameReadout.text = string.IsNullOrEmpty(frame) ? "Время кадра: идёт замер…" : frame;
            }
        }

        private void Rebuild(bool admin, IList<string> teleportIds, IList<string> teleportLabels, bool detailAvailable)
        {
            _frameReadout = null;
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Отладка");
            _status = MenuKit.Label(Content, "", MenuTextRole.Caption, MenuColorRole.TextSecondary);

            var categories = new List<Category> { Category.Overlays, Category.Touch, Category.Teleport, Category.Performance };
            var labels = new List<string> { "Оверлеи", "Касание", "Телепорт", "Перф-анализ" };
            MenuKit.TabStrip(Content, labels, categories.IndexOf(_category), index => SelectCategory(categories[index]));

            switch (_category)
            {
                case Category.Overlays: BuildOverlays(); break;
                case Category.Touch: BuildTouch(); break;
                case Category.Teleport: BuildTeleport(teleportIds, teleportLabels); break;
                case Category.Performance: BuildPerformance(detailAvailable); break;
            }
        }

        private void SelectCategory(Category category)
        {
            if (_category == category) return;
            _category = category;
            Refresh();
        }

        private void BuildOverlays()
        {
            // ── Оверлей ────────────────────────────────────────────────────
            MenuKit.Section(Content, "Метки попаданий");
            RectTransform overlays = MenuKit.Row(Content);
            // Метки попаданий по игрокам: урон, зона хитбокса, по кому (DebugHitMarkers).
            MenuKit.Button(overlays, DebugHitMarkers.Visible ? "Скрыть попадания" : "Показать попадания",
                           () => { DebugHitMarkers.SetVisible(!DebugHitMarkers.Visible); _lastSnapshot = ""; });
        }

        private void BuildTouch()
        {
            MenuKit.Section(Content, "Касание меню");
            MenuKit.Button(MenuKit.Row(Content), DebugFingerTipRays.Visible ? "Скрыть лучи пальцев UI" : "Показать лучи пальцев UI",
                           () => { DebugFingerTipRays.SetVisible(!DebugFingerTipRays.Visible); _lastSnapshot = ""; });
            MenuKit.Label(Content, "Включает лучи и интерактивный фон для диагностики. Жёлтый — нет попадания; зелёный — UI найден; голубой — нажатие; красный — рука блокирует UI.",
                          MenuTextRole.Caption, MenuColorRole.TextSecondary);
        }

        private void BuildPerformance(bool detailAvailable)
        {
            MenuKit.Section(Content, "Время кадра и FPS");
            _frameReadout = MenuKit.Label(Content, "Время кадра: идёт замер…");
            MenuKit.Label(Content, "FPS и время кадра — общие показатели, без разделения CPU и GPU.",
                          MenuTextRole.Caption, MenuColorRole.TextSecondary);

            MenuKit.Section(Content, "GPU — рендеринг сцены");
            MenuKit.Button(MenuKit.Row(Content), "Тест сцены", () => MenuPerfTests.Open(PerfRunMode.MapOnly));
            if (detailAvailable) SceneDetailDebugSection.Build(Content, () => { _lastSnapshot = ""; });

            MenuKit.Section(Content, "CPU — нагрузка аватаров");
            MenuKit.Button(MenuKit.Row(Content), "Перф-тесты аватаров", () => MenuPerfTests.Open(PerfRunMode.Standard));
            MenuKit.Label(Content, "Прогоны записывают показатели CPU и GPU. Выберите режим и нагрузку на экране теста; запуск — кнопкой «Старт».",
                          MenuTextRole.Caption, MenuColorRole.TextSecondary);
        }

        private void BuildTeleport(IList<string> teleportIds, IList<string> teleportLabels)
        {
            // ── Телепорт ───────────────────────────────────────────────────
            MenuKit.Section(Content, "Телепорт");
            if (teleportIds.Count == 0) MenuKit.EmptyState(Content, "На карте нет точек.");
            RectTransform grid = MenuKit.Grid(Content, TeleportColumns, 0.25f);
            for (int i = 0; i < teleportIds.Count; i++)
            {
                string id = teleportIds[i];
                MenuKit.Button(grid, teleportLabels[i], () => Teleport(id));
            }
        }

        private static string BuildStatus(bool admin)
        {
            string net = NetworkServer.active && NetworkClient.active ? "хост"
                       : NetworkClient.isConnected ? "клиент" : "нет сети";

            string stress = StressTestClientSession.IsRunning ? "идёт" : "не идёт";
            string reply = string.IsNullOrEmpty(DebugModeNetwork.LastReply) ? "" : "\nСервер: " + DebugModeNetwork.LastReply;
            return $"Сеть: {net} · права админа: {(admin ? "да" : "нет")} · стресс-тест: {stress} · ботов: {BotNetwork.CountVisibleBots()}{reply}";
        }

        private static void Teleport(string id)
        {
            if (!DebugModeNetwork.RequestTeleport(id, out string reason))
                PerfOverlay.Show("Телепорт: " + reason, 4f);
        }
    }
}
