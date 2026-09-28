using System.Collections.Generic;
using System.Text;
using Mirror;
using TMPro;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.DevTools.StressTest;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран «Отладка» (<see cref="MenuScreenType.Debug"/>) — только в режиме отладки
    /// (<see cref="DebugMode"/>, кнопка на главном экране под <see cref="DebugOnlyElements"/>).
    ///
    /// <para>
    /// Строки собираются кодом (<see cref="MenuRowBuilder"/>): статус, оверлей кадра, переходы на
    /// экраны админа, телепорт к точкам карты, выключение режима. Стресс-тест — отдельный экран
    /// «Перф-тесты» (<see cref="MenuPerfTests"/>), переход — кнопка <c>Btn_PerfTests</c> в префабе.
    /// Экран только шлёт запросы: телепорт — <see cref="DebugModeNetwork.RequestTeleport"/>;
    /// права проверяет сервер.
    /// </para>
    ///
    /// <para>
    /// Перестраивается раз в <see cref="_refreshInterval"/> и только при изменении состава —
    /// пересоздание кнопок сбивало бы наведение луча в VR. Строка статуса обновляется на месте.
    /// </para>
    /// </summary>
    public class MenuDebug : MenuScreen
    {
        /// <summary>Сколько кнопок телепорта в строке: шире экран не вмещает.</summary>
        private const int TeleportPerRow = 4;

        [Tooltip("Контейнер строк (VerticalLayoutGroup).")]
        [SerializeField] private Transform _rowsContainer;

        [Tooltip("Префаб кнопки: Button + TMP-текст в детях (SlimButton_IconText).")]
        [SerializeField] private GameObject _buttonPrefab;

        [Min(0.1f)]
        [SerializeField] private float _refreshInterval = 0.5f;

        private float _timer;
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
            if (!DebugMode.Enabled)
            {
                // Режим выключили, пока экран открыт: экрана больше нет.
                if (MenuController.Instance != null) MenuController.Instance.SwitchTo(MenuScreenType.Main);
                return;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < _refreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            if (_rowsContainer == null) return;

            bool admin = MenuPlayersTeams.IsLocalAdmin();
            List<DebugTeleportTarget> targets = DebugTeleportTargets.Collect();

            var snapshot = new StringBuilder();
            snapshot.Append(admin).Append('|').Append(DebugPerfReadout.Visible);
            foreach (DebugTeleportTarget t in targets) snapshot.Append('|').Append(t.Id).Append(t.Label);

            if (snapshot.ToString() != _lastSnapshot)
            {
                _lastSnapshot = snapshot.ToString();
                Rebuild(admin, targets);
            }

            if (_status != null) _status.text = BuildStatus(admin);
        }

        private void Rebuild(bool admin, List<DebugTeleportTarget> targets)
        {
            for (int i = _rowsContainer.childCount - 1; i >= 0; i--)
                Destroy(_rowsContainer.GetChild(i).gameObject);

            _status = MenuRowBuilder.Label(_rowsContainer, "", 1700f, 110f);

            // ── Оверлей ────────────────────────────────────────────────────
            Transform row = MenuRowBuilder.Row(_rowsContainer);
            MenuRowBuilder.Label(row, "Оверлей", 300f);
            MenuRowBuilder.Button(_buttonPrefab, row, DebugPerfReadout.Visible ? "Скрыть кадр" : "Показать кадр",
                                  () => { DebugPerfReadout.SetVisible(!DebugPerfReadout.Visible); _lastSnapshot = ""; });

            // ── Админ ──────────────────────────────────────────────────────
            row = MenuRowBuilder.Row(_rowsContainer);
            MenuRowBuilder.Label(row, "Админ", 300f);
            if (admin)
            {
                MenuRowBuilder.Button(_buttonPrefab, row, "Матч", () => SwitchTo(MenuScreenType.MatchManager));
                MenuRowBuilder.Button(_buttonPrefab, row, "Игроки и команды", () => SwitchTo(MenuScreenType.PlayersTeams));
                MenuRowBuilder.Button(_buttonPrefab, row, "Выбор серии", () => SwitchTo(MenuScreenType.SessionSetup));
            }
            else
            {
                MenuRowBuilder.Label(row, "Сервер прав не выдал — см. строку статуса.", 1300f);
            }

            // ── Телепорт ───────────────────────────────────────────────────
            row = MenuRowBuilder.Row(_rowsContainer);
            MenuRowBuilder.Label(row, "Телепорт", 300f);
            if (targets.Count == 0) MenuRowBuilder.Label(row, "На карте нет точек.", 1300f);
            for (int i = 0; i < targets.Count; i++)
            {
                if (i > 0 && i % TeleportPerRow == 0)
                {
                    row = MenuRowBuilder.Row(_rowsContainer);
                    MenuRowBuilder.Label(row, "", 300f);
                }

                string id = targets[i].Id;
                MenuRowBuilder.Button(_buttonPrefab, row, targets[i].Label, () => Teleport(id));
            }

            // ── Выход ──────────────────────────────────────────────────────
            row = MenuRowBuilder.Row(_rowsContainer);
            MenuRowBuilder.Button(_buttonPrefab, row, "Выключить режим отладки", () => DebugMode.Set(false, "кнопка планшета"));
        }

        private static string BuildStatus(bool admin)
        {
            string net = NetworkServer.active && NetworkClient.active ? "хост"
                       : NetworkClient.isConnected ? "клиент" : "нет сети";

            string stress = StressTestClientSession.IsRunning ? "идёт" : "не идёт";
            string reply = string.IsNullOrEmpty(DebugModeNetwork.LastReply) ? "" : "\nСервер: " + DebugModeNetwork.LastReply;
            return $"Сеть: {net} · права админа: {(admin ? "да" : "нет")} · стресс-тест: {stress}{reply}";
        }

        private static void SwitchTo(MenuScreenType screen)
        {
            if (MenuController.Instance != null) MenuController.Instance.SwitchTo(screen);
        }

        private static void Teleport(string id)
        {
            if (!DebugModeNetwork.RequestTeleport(id, out string reason))
                PerfOverlay.Show("Телепорт: " + reason, 4f);
        }
    }
}
