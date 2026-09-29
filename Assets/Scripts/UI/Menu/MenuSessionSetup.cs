using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран админа «Новая серия» (<see cref="MenuScreenType.SessionSetup"/>, вложенный в раздел
    /// «Админ»): режим матча — ряд кнопок, карты режима — плитки. Клик по плитке ставит карту в конец
    /// очереди серии, повторный — убирает (<see cref="MapQueue"/>); номер в очереди — в подписи
    /// плитки, плитка в очереди выделена. «Начать» — главное действие (справа внизу), «Очистить» —
    /// рядом. Смена режима очищает очередь. Сервер отбрасывает несовместимые карты и проверяет
    /// право админа (<c>AdminMapCommands.ServerStartSeries</c>).
    /// </summary>
    public class MenuSessionSetup : MenuScreen
    {
        [Header("Data Registries")]
        [Tooltip("Реестр всех доступных игровых режимов.")]
        [SerializeField] private GameModeRegistry _gameModeRegistry;

        [Tooltip("Реестр всех доступных карт.")]
        [SerializeField] private MapRegistry _mapRegistry;

        [Tooltip("Колонок в сетке карт.")]
        [SerializeField] private int _mapColumns = 3;

        private GameModeData _selectedMode;
        private readonly Dictionary<GameModeData, KitButton> _modeButtons = new Dictionary<GameModeData, KitButton>();
        private readonly Dictionary<string, (KitButton tile, string name)> _tiles = new Dictionary<string, (KitButton, string)>();
        private TMPro.TMP_Text _queueLabel;

        /// <summary>
        /// Карты серии в порядке кликов (<see cref="MapQueue"/>): клик ставит карту в конец,
        /// повторный — убирает, номер на плитке — место в очереди.
        /// </summary>
        private readonly MapQueue _queue = new MapQueue();

        public IReadOnlyList<string> Queue => _queue.Items;

        public override void Show()
        {
            base.Show();
            SetPrimary("Начать", OnStartPressed, _queue.Count > 0);
            SetSecondary("Очистить", OnClearPressed, _queue.Count > 0);
            Build();
        }

        /// <summary>
        /// Вкладки режимов: только режимы матча. Разминку не выбирают — её включает карта сама
        /// (<see cref="GameModeRegistry.Warmup"/>).
        /// </summary>
        public static List<GameModeData> TabModes(GameModeRegistry registry)
        {
            return registry != null ? new List<GameModeData>(registry.MatchModes) : new List<GameModeData>();
        }

        /// <summary>
        /// Карты под режим матча: совместимые с ним по <c>MapData.supportedModes</c>. У лобби
        /// режимов матча нет, поэтому в выбор карт матча оно не попадает.
        /// </summary>
        public static List<MapData> MapsForMode(MapRegistry registry, GameModeData mode)
        {
            var result = new List<MapData>();
            if (registry == null || registry.maps == null) return result;

            foreach (MapData map in registry.maps)
                if (map != null && (mode == null || MapModeRules.IsCompatible(map, mode))) result.Add(map);

            return result;
        }

        /// <summary>Подпись плитки: номер в очереди перед названием.</summary>
        public static string TileLabel(string displayName, int queueNumber) =>
            queueNumber > 0 ? queueNumber + " · " + displayName : displayName;

        private void Build()
        {
            MenuKit.Clear(Content);
            _modeButtons.Clear();
            _tiles.Clear();

            MenuKit.Title(Content, "Новая серия");

            List<GameModeData> modes = TabModes(_gameModeRegistry);
            if (modes.Count == 0)
            {
                GameLog.UI.Warning("[MenuSessionSetup] В реестре нет режимов матча или он не назначен!");
                MenuKit.EmptyState(Content, "Нет режимов матча — реестр режимов не назначен.");
                return;
            }
            if (_selectedMode == null || !modes.Contains(_selectedMode)) _selectedMode = modes[0];

            MenuKit.Section(Content, "Режим");
            RectTransform modeRow = MenuKit.Row(Content);
            foreach (GameModeData mode in modes)
            {
                GameModeData captured = mode;
                KitButton button = MenuKit.Button(modeRow, mode.displayName, () => SelectMode(captured));
                button.Selected = mode == _selectedMode;
                _modeButtons[mode] = button;
            }

            MenuKit.Section(Content, "Карты — в порядке кликов");
            RectTransform grid = MenuKit.Grid(Content, _mapColumns, 0.62f);
            List<MapData> maps = MapsForMode(_mapRegistry, _selectedMode);
            if (maps.Count == 0) MenuKit.EmptyState(Content, "Нет карт, совместимых с режимом.");
            foreach (MapData map in maps)
            {
                string scene = map.sceneName;
                KitButton tile = MenuKit.Tile(grid, map.displayName, map.preview, () => OnMapClicked(scene));
                tile.name = "Tile_" + scene;
                _tiles[scene] = (tile, map.displayName);
            }

            _queueLabel = MenuKit.Label(Content, "", MenuTextRole.Body, MenuColorRole.TextSecondary);
            RefreshQueueView();
        }

        private void SelectMode(GameModeData mode)
        {
            if (_selectedMode == mode) return;
            // Очередь — карты одного режима: другой режим — другой набор совместимых карт.
            _queue.Clear();
            _selectedMode = mode;
            Build();
        }

        /// <summary>Клик по плитке: карта в конец очереди или из очереди вон.</summary>
        private void OnMapClicked(string sceneName)
        {
            bool added = _queue.Toggle(sceneName);
            GameLog.UI.Info($"[MenuSessionSetup] {(added ? "В очередь" : "Из очереди")}: {sceneName}. " +
                            $"Серия: {string.Join(" → ", _queue.Items)}");
            RefreshQueueView();
        }

        /// <summary>«Начать»: серия из очереди — выбранный режим, карты по порядку.</summary>
        private void OnStartPressed()
        {
            if (_queue.Count == 0 || _selectedMode == null) return;

            var maps = new string[_queue.Count];
            for (int i = 0; i < maps.Length; i++) maps[i] = _queue.Items[i];

            GameLog.UI.Info($"[MenuSessionSetup] Начать серию: {_selectedMode.displayName}, {string.Join(" → ", maps)}");

            if (Player.PlayerSession.LocalSession != null)
                Player.PlayerSession.LocalSession.CmdAdminStartSeries(_selectedMode.modeId, maps);

            _queue.Clear();
            RefreshQueueView();
        }

        private void OnClearPressed()
        {
            _queue.Clear();
            RefreshQueueView();
        }

        /// <summary>Номера и выделение плиток, строка серии, доступность «Начать»/«Очистить».</summary>
        private void RefreshQueueView()
        {
            var names = new List<string>();
            foreach (string scene in _queue.Items)
                names.Add(_tiles.TryGetValue(scene, out var t) ? t.name : scene);

            foreach (KeyValuePair<string, (KitButton tile, string name)> entry in _tiles)
            {
                int number = _queue.NumberOf(entry.Key);
                entry.Value.tile.Text = TileLabel(entry.Value.name, number);
                entry.Value.tile.Selected = number > 0;
            }

            if (_queueLabel != null)
                _queueLabel.text = _queue.Count > 0 ? "Серия: " + string.Join(" · ", names) : "Выберите карты — они сыграются по порядку.";

            SetPrimaryInteractable(_queue.Count > 0);
            SetSecondaryInteractable(_queue.Count > 0);
        }
    }
}
