using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.StressTest;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран «Перф-тесты» (<see cref="MenuScreenType.PerfTests"/>) — стресс-тест с планшета, только
    /// в режиме отладки. Вход — с раздела «Отладка» (<see cref="MenuDebug.Links"/>),
    /// «Назад» возвращает туда же (стек навигации).
    ///
    /// <para>
    /// <b>Режимы:</b> тип прогона (<see cref="PerfRunMode"/>: обычный, по скинам, короткий, только по
    /// карте), скин кукол (вперемешку или префаб по имени), число кукол, строка-описание — фазы и
    /// длительность по <see cref="StressTestPlan"/>. <b>Панель управления:</b> «Старт» / «Стоп»,
    /// фаза «i из N» и остаток, живой кадр (<see cref="DebugPerfReadout"/>), ответ сервера и путь
    /// perf.log последнего прогона на этой машине — для <c>adb pull</c>.
    /// </para>
    ///
    /// <para>
    /// Экран только просит: старт и стоп — <see cref="StressTestLauncher"/>, уместность (разминка,
    /// аватар) решает сервер. Кнопки пересоздаются только при смене выбора или начале/конце
    /// прогона — иначе сбивалось бы наведение луча; панель обновляется на месте раз в 0,5 с.
    /// </para>
    /// </summary>
    public class MenuPerfTests : MenuScreen
    {
        private static readonly PerfRunMode[] Modes = { PerfRunMode.Standard, PerfRunMode.PerSkin, PerfRunMode.Short, PerfRunMode.MapOnly };

        // Выбор живёт дольше экрана: планшет пересоздаётся со сценой.
        private static PerfRunMode _mode = PerfRunMode.Standard;
        private static int _puppetSkin = StressTestLayout.MixedSkins;
        private static int _puppetCount = new StressTestConfig().puppetCount;
        private static string _localMessage = string.Empty;

        private string _lastSnapshot = "";
        private TMP_Text _panel;

        public static string ModeName(PerfRunMode mode)
        {
            switch (mode)
            {
                case PerfRunMode.PerSkin: return "По скинам";
                case PerfRunMode.Short:   return "Короткий (10 с)";
                case PerfRunMode.MapOnly: return "Только по карте";
                default:                  return "Обычный";
            }
        }

        /// <summary>
        /// Следующий скин кукол по кругу: «вперемешку» (<see cref="StressTestLayout.MixedSkins"/>),
        /// затем 0..<paramref name="count"/>-1. <paramref name="step"/> — +1 или -1.
        /// </summary>
        public static int CycleSkin(int current, int count, int step)
        {
            if (count <= 0) return StressTestLayout.MixedSkins;
            int slots = count + 1;                       // «вперемешку» + каждый скин
            int slot = Mathf.Clamp(current, -1, count - 1) + 1;
            slot = ((slot + step) % slots + slots) % slots;
            return slot - 1;
        }

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

        private void Refresh()
        {
            List<GameObject> skins = StressTestServer.CollectAvatarPrefabs();
            bool running = StressTestClientSession.IsRunning;

            string snapshot = $"{_mode}|{_puppetSkin}|{_puppetCount}|{running}|{skins.Count}";
            if (snapshot != _lastSnapshot)
            {
                _lastSnapshot = snapshot;
                Rebuild(skins, running);
            }

            if (_panel != null) _panel.text = BuildPanel();
        }

        private void Rebuild(List<GameObject> skins, bool running)
        {
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Перф-тесты");

            // «Старт» / «Стоп» — главное действие экрана.
            if (running) SetPrimary("Стоп", StopRun, true, MenuButtonRole.Danger);
            else SetPrimary("Старт", StartRun);

            // ── Режимы ─────────────────────────────────────────────────────
            MenuKit.Section(Content, "Режим");
            RectTransform row = MenuKit.Row(Content);
            foreach (PerfRunMode mode in Modes)
            {
                PerfRunMode m = mode;
                MenuKit.Button(row, ModeName(mode), () => Select(m)).Selected = m == _mode;
            }

            StressTestConfig config = CurrentConfig();
            MenuKit.Label(Content, StressTestPlan.Describe(_mode, config, skins.Count), MenuTextRole.Body, MenuColorRole.TextSecondary);

            // ── Куклы ──────────────────────────────────────────────────────
            MenuKit.Section(Content, "Куклы");
            row = MenuKit.Row(Content);
            if (_mode == PerfRunMode.PerSkin)
            {
                MenuKit.Label(row, $"Скины кукол: все {skins.Count} по очереди");
            }
            else
            {
                string skinName = StressTestLayout.IsSingleSkin(skins.Count, _puppetSkin) ? skins[_puppetSkin].name : "вперемешку";
                MenuKit.Label(row, "Скин кукол: " + skinName);
                MenuKit.Button(row, "< Скин", () => ChangeSkin(skins.Count, -1));
                MenuKit.Button(row, "Скин >", () => ChangeSkin(skins.Count, +1));
            }

            row = MenuKit.Row(Content);
            MenuKit.Label(row, $"Кукол: {_puppetCount} (до {StressTestConfig.MaxPuppets})");
            MenuKit.Button(row, "− кукла", () => ChangePuppets(-1));
            MenuKit.Button(row, "+ кукла", () => ChangePuppets(+1));

            // ── Панель ─────────────────────────────────────────────────────
            MenuKit.Section(Content, "Прогон");
            _panel = MenuKit.Label(Content, BuildPanel(), MenuTextRole.Caption, MenuColorRole.TextPrimary);
        }

        private static StressTestConfig CurrentConfig() => StressTestPlan.BuildConfig(_mode, _puppetSkin, _puppetCount);

        private static string BuildPanel()
        {
            var sb = new StringBuilder();

            StressTestClientSession session = StressTestClientSession.Current;
            if (session != null)
            {
                int total = StressTestLauncher.PlannedPhaseCount;
                string of = total > 0 ? $" из {total}" : "";
                sb.Append($"Фаза {session.PhaseNumber}{of}: {session.PhaseName} · осталось {session.PhaseSecondsLeft:F0} с");
            }
            else
            {
                sb.Append("Прогон не идёт.");
            }

            string frame = DebugPerfReadout.Format();
            sb.Append("\nКадр: ").Append(string.IsNullOrEmpty(frame) ? "меряется…" : frame);

            string server = StressTestClientSession.LastServerText;
            if (!string.IsNullOrEmpty(_localMessage)) sb.Append("\nЗапрос: ").Append(_localMessage);
            if (!string.IsNullOrEmpty(server)) sb.Append("\nСервер: ").Append(server);

            string log = StressTestClientSession.LastLogPath;
            sb.Append("\nЛог: ").Append(string.IsNullOrEmpty(log) ? "прогонов с запуска не было" : log);
            return sb.ToString();
        }

        private void Select(PerfRunMode mode)
        {
            _mode = mode;
            Refresh();
        }

        private void ChangeSkin(int count, int step)
        {
            _puppetSkin = CycleSkin(_puppetSkin, count, step);
            Refresh();
        }

        private void ChangePuppets(int step)
        {
            _puppetCount = Mathf.Clamp(_puppetCount + step, 1, StressTestConfig.MaxPuppets);
            Refresh();
        }

        private void StartRun()
        {
            StressTestConfig config = CurrentConfig();
            GameLog.UI.Info($"[MenuPerfTests] Старт: {StressTestLauncher.Describe(config)}, скин {config.puppetSkin}, кукол {config.puppetCount}.");
            _localMessage = StressTestLauncher.TryStart(config, out string message) ? "отправлен серверу" : "не отправлен — " + message;
            Refresh();
        }

        private void StopRun()
        {
            StressTestLauncher.Stop();
            _localMessage = "остановка отправлена серверу";
            Refresh();
        }
    }
}
