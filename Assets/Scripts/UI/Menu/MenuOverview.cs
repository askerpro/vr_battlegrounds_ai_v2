using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Kit;
using VrBattlegrounds.UI.Menu.Overview;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран «Обзор» — главный экран планшета, аналог TAB из CS (T-33). Показывает ситуацию:
    /// карта, раунд и фаза с часами, счёт, команды с хп / готовностью / У/С/А, серия; в лобби —
    /// блок «Вы» (команда, скин, калибровка) и зал. У админа — все кнопки ситуации: главное действие
    /// справа внизу (<see cref="MenuScreen.SetPrimary"/>), остальные — строкой кнопок под шапкой.
    /// «Матч с ботами» (<see cref="OverviewBotMatch"/>, T-48) — и обычному игроку, если он один на сервере.
    ///
    /// <para>
    /// Экран только рисует. Данные: <see cref="OverviewStateReader"/> → <see cref="OverviewBuilder"/> →
    /// <see cref="OverviewSnapshot"/>; кнопки — <see cref="OverviewAdminActions"/>. Раз в 0,5 с снимок
    /// строится заново и сравнивается с прошлым: сменилась раскладка — строки пересоздаются; сменились
    /// только значения (хп, счёт, часы) — текст правится в существующих ячейках, и луч не сбивается.
    /// </para>
    /// </summary>
    public class MenuOverview : MenuScreen
    {
        private readonly OverviewBuilder _builder = OverviewBuilder.Default;

        private OverviewSnapshot _last;
        private string _lastAdmin = "";

        private TMP_Text _title, _status, _score, _note;
        private readonly Dictionary<string, TMP_Text> _sectionTitles = new Dictionary<string, TMP_Text>();
        private readonly Dictionary<string, TMP_Text[]> _rows = new Dictionary<string, TMP_Text[]>();

        /// <summary>Показан образец (<see cref="BuildPreview"/>): живые данные его не затирают.</summary>
        private bool _preview;

        public override void Show()
        {
            base.Show();
            _preview = false;
            _last = null;
            _lastAdmin = null;
            Refresh();
        }

        /// <summary>
        /// Превью для редактора (снимки, проверка вмещаемости): тот же код рисования, но из образцового
        /// входа <see cref="OverviewSamples.Combat"/> — бой, по 6 игроков в команде, смотрит админ.
        /// </summary>
        public override void BuildPreview()
        {
            base.Show();
            _preview = true;
            _last = null;
            _lastAdmin = null;
            OverviewSnapshot snapshot = _builder.Build(OverviewSamples.Combat());
            OverviewAdminPlan admin = OverviewAdminActions.Plan(snapshot.Context, false,
                c => AdminMapCommands.IsAvailable(c, MapState.Live, true, true, true));
            Draw(snapshot, admin);
        }

        private void Update()
        {
            if (!_preview && RefreshDue(0.5f)) Refresh();
        }

        private void Refresh()
        {
            OverviewSnapshot snapshot = _builder.Build(OverviewStateReader.Read(OverviewSources.Current()));
            Draw(snapshot, AdminPlan(snapshot.Context));
        }

        private void Draw(OverviewSnapshot snapshot, OverviewAdminPlan admin)
        {
            string adminKey = Signature(admin);

            if (_last == null || adminKey != _lastAdmin || !_last.SameLayout(snapshot) || !SameHeaderShape(_last, snapshot))
            {
                Rebuild(snapshot, admin);
                _lastAdmin = adminKey;
            }
            else
            {
                UpdateInPlace(snapshot, !_last.SameContent(snapshot));
            }
            _last = snapshot;
        }

        // ── Кнопки админа ────────────────────────────────────────────────

        private static OverviewAdminPlan AdminPlan(OverviewContext context)
        {
            bool adminUi = MenuPermissions.ShowAdminUi();
            OverviewAdminPlan plan;
            if (!adminUi) plan = new OverviewAdminPlan();
            else
            {
                bool lastMap = Series.Instance != null && Series.Instance.IsLastMap;
                plan = OverviewAdminActions.Plan(context, lastMap, AdminMapCommands.IsAvailable);
            }

            // «Матч с ботами» (T-48) — и игроку без админки, если он один на сервере.
            OverviewBotMatch.Apply(plan, context, BotMatchNetwork.LocalCanRequest(adminUi));
            return plan;
        }

        private static string Signature(OverviewAdminPlan plan)
        {
            var sb = new StringBuilder();
            if (plan.Primary != null) sb.Append('!').Append(plan.Primary.Label);
            foreach (OverviewAction a in plan.Others) sb.Append('|').Append(a.Label);
            return sb.ToString();
        }

        private void ApplyActions(OverviewAdminPlan plan)
        {
            if (plan.Primary != null)
            {
                OverviewAction primary = plan.Primary;
                SetPrimary(primary.Label, () => Run(primary));
            }
            else ClearActions();

            if (plan.Others.Count == 0) return;
            RectTransform row = MenuKit.Row(Content);
            foreach (OverviewAction a in plan.Others)
            {
                OverviewAction action = a;
                MenuKit.Button(row, a.Label, () => Run(action), a.Danger ? MenuButtonRole.Danger : MenuButtonRole.Secondary);
            }
        }

        private void Run(OverviewAction action)
        {
            if (action.BotMatch)
            {
                GameLog.UI.Info("[MenuOverview] Матч с ботами.");
                BotMatchNetwork.Request();
                _last = null;
                return;
            }

            if (!action.Command.HasValue)
            {
                Push(action.Screen);
                return;
            }

            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning($"[MenuOverview] {action.Command.Value}: нет локальной сессии.");
                return;
            }

            GameLog.UI.Info($"[MenuOverview] Админ: {action.Command.Value}.");
            PlayerSession.LocalSession.CmdAdminMapCommand(action.Command.Value);
            _last = null; // следующее обновление — с нуля: набор кнопок сменится
        }

        // ── Построение ───────────────────────────────────────────────────

        private void Rebuild(OverviewSnapshot s, OverviewAdminPlan admin)
        {
            MenuKit.Clear(Content);
            _sectionTitles.Clear();
            _rows.Clear();
            _score = _note = null;

            _title = MenuKit.Title(Content, s.Header.Title);
            _status = MenuKit.Label(Content, StatusLine(s.Header), MenuTextRole.Body, MenuColorRole.TextSecondary);
            if (!string.IsNullOrEmpty(s.Header.Score))
                _score = MenuKit.Label(Content, s.Header.Score, MenuTextRole.Title, MenuColorRole.Accent);
            if (!string.IsNullOrEmpty(s.Header.ViewerNote))
                _note = MenuKit.Label(Content, s.Header.ViewerNote, MenuTextRole.Body, MenuColorRole.Accent);

            ApplyActions(admin);

            foreach (OverviewSection section in s.Sections)
            {
                if (!string.IsNullOrEmpty(section.Title))
                {
                    TMP_Text title = MenuKit.Section(Content, section.Title);
                    if (section.Tone != OverviewTone.Normal)
                        title.GetComponent<MenuThemed>().Set(ColorOf(section.Tone), MenuTextRole.Caption);
                    _sectionTitles[section.Id] = title;
                }

                float[] weights = Weights(section.Columns.Length);
                if (section.Columns.Length > 0)
                    MenuKit.TableRow(Content, section.Columns, weights, header: true);

                foreach (OverviewRow row in section.Rows)
                {
                    TMP_Text[] cells;
                    if (row.Kind == OverviewRowKind.Player)
                    {
                        RectTransform tr = MenuKit.TableRow(Content, Cells(row), weights);
                        cells = tr.GetComponentsInChildren<TMP_Text>(true);
                    }
                    else
                    {
                        cells = new[] { MenuKit.Label(Content, row.Cells.Length > 0 ? row.Cells[0] : "") };
                    }
                    Paint(cells, row.Tone);
                    _rows[RowId(section, row)] = cells;
                }
            }
        }

        private void UpdateInPlace(OverviewSnapshot s, bool contentChanged)
        {
            // Часы — каждый раз; остальное — только если что-то поменялось.
            if (_status != null) _status.text = StatusLine(s.Header);
            if (!contentChanged) return;

            if (_title != null) _title.text = s.Header.Title;
            if (_score != null) _score.text = s.Header.Score;
            if (_note != null) _note.text = s.Header.ViewerNote;

            foreach (OverviewSection section in s.Sections)
            {
                if (_sectionTitles.TryGetValue(section.Id, out TMP_Text title)) title.text = section.Title;

                foreach (OverviewRow row in section.Rows)
                {
                    if (!_rows.TryGetValue(RowId(section, row), out TMP_Text[] cells)) continue;
                    string[] texts = Cells(row);
                    for (int i = 0; i < cells.Length && i < texts.Length; i++)
                        if (cells[i].text != texts[i]) cells[i].text = texts[i];
                    Paint(cells, row.Tone);
                }
            }
        }

        // ── Мелочи ───────────────────────────────────────────────────────

        private static string RowId(OverviewSection section, OverviewRow row) => section.Id + "/" + row.Key;

        /// <summary>Своя строка — жирным: цвет занят под «внимание» и «приглушено».</summary>
        private static string[] Cells(OverviewRow row)
        {
            if (row.Tone != OverviewTone.Local) return row.Cells;
            var bold = new string[row.Cells.Length];
            for (int i = 0; i < bold.Length; i++) bold[i] = "<b>" + row.Cells[i] + "</b>";
            return bold;
        }

        private static string StatusLine(OverviewHeader h)
        {
            if (!h.ClockSeconds.HasValue) return h.Status;
            return h.Status + " · " + OverviewFormat.Clock(h.ClockSeconds.Value);
        }

        /// <summary>Пустой → непустой счёт или пометка меняют раскладку шапки — это пересборка.</summary>
        private static bool SameHeaderShape(OverviewSnapshot a, OverviewSnapshot b) =>
            string.IsNullOrEmpty(a.Header.Score) == string.IsNullOrEmpty(b.Header.Score) &&
            string.IsNullOrEmpty(a.Header.ViewerNote) == string.IsNullOrEmpty(b.Header.ViewerNote);

        /// <summary>Имя шире остальных колонок.</summary>
        private static float[] Weights(int columns)
        {
            var w = new float[columns];
            for (int i = 0; i < columns; i++) w[i] = i == 0 ? 3f : 1.2f;
            return w;
        }

        private static void Paint(TMP_Text[] cells, OverviewTone tone)
        {
            MenuColorRole color = ColorOf(tone);
            foreach (TMP_Text cell in cells)
            {
                var themed = cell.GetComponent<MenuThemed>();
                if (themed != null && themed.ColorRole != color) themed.Set(color, MenuTextRole.Body);
            }
        }

        private static MenuColorRole ColorOf(OverviewTone tone)
        {
            switch (tone)
            {
                case OverviewTone.Dimmed: return MenuColorRole.TextSecondary;
                case OverviewTone.Warning: return MenuColorRole.Accent;
                case OverviewTone.Winner:
                case OverviewTone.Success: return MenuColorRole.Success;
                default: return MenuColorRole.TextPrimary;
            }
        }
    }
}
