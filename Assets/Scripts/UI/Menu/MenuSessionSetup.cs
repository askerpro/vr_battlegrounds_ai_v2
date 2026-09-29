using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;
using System.Collections.Generic;
using System;

namespace VrBattlegrounds.UI.Menu
{
    public class MenuSessionSetup : MenuScreen
    {
        [Header("Data Registries")]
        [Tooltip("Реестр всех доступных игровых режимов.")]
        [SerializeField] private GameModeRegistry _gameModeRegistry;

        [Tooltip("Реестр всех доступных карт.")]
        [SerializeField] private MapRegistry _mapRegistry;

        [Header("UI References")]
        [Tooltip("Контейнер куда будут спавниться кнопки вкладок (режимов игры).")]
        [SerializeField] private Transform _tabsContainer;
        
        [Tooltip("Контейнер куда будет спавниться список карт (Vertical Layout Group).")]
        [SerializeField] private Transform _mapListContainer;

        [Tooltip("Префаб кнопки для создания вкладок.")]
        [SerializeField] private GameObject _buttonPrefab;

        [Tooltip("Префаб плитки карты (MapEntry) с картинкой и названием.")]
        [SerializeField] private GameObject _mapEntryPrefab;

        [Header("Очередь серии")]
        [Tooltip("Кнопка «Начать»: запускает серию из карт очереди в порядке очереди.")]
        [SerializeField] private Button _startButton;

        [Tooltip("Кнопка «Очистить»: опустошает очередь.")]
        [SerializeField] private Button _clearButton;

        [Tooltip("Имя дочернего TMP-текста на плитке карты — номер карты в очереди.")]
        [SerializeField] private string _queueNumberName = "QueueNumber";

        private GameModeData _selectedMode;
        private Dictionary<GameModeData, Button> _tabButtons = new Dictionary<GameModeData, Button>();

        /// <summary>
        /// Карты серии в порядке кликов (<see cref="MapQueue"/>): клик ставит карту в конец,
        /// повторный — убирает, номер на плитке — место в очереди.
        /// </summary>
        private readonly MapQueue _queue = new MapQueue();
        private readonly Dictionary<string, GameObject> _entries = new Dictionary<string, GameObject>();

        public IReadOnlyList<string> Queue => _queue.Items;

        private void Awake()
        {
            if (_startButton != null) _startButton.onClick.AddListener(OnStartPressed);
            if (_clearButton != null) _clearButton.onClick.AddListener(OnClearPressed);
        }

        private void Start()
        {
            PopulateTabs();
        }

        private void PopulateTabs()
        {
            ClearContainer(_tabsContainer);
            _tabButtons.Clear();

            List<GameModeData> tabs = TabModes(_gameModeRegistry);
            if (tabs.Count == 0)
            {
                GameLog.UI.Warning("[MenuSessionSetup] В реестре нет режимов матча или он не назначен!");
                return;
            }

            foreach (var mode in tabs)
            {
                GameObject tabBtnObj = Instantiate(_buttonPrefab, _tabsContainer);
                tabBtnObj.name = $"Tab_{mode.modeId}";

                Button btn = tabBtnObj.GetComponent<Button>();
                
                TextMeshProUGUI txtUGUI = tabBtnObj.GetComponentInChildren<TextMeshProUGUI>();
                if (txtUGUI != null) txtUGUI.text = mode.displayName;
                else
                {
                    TextMeshPro txtTMP = tabBtnObj.GetComponentInChildren<TextMeshPro>();
                    if (txtTMP != null) txtTMP.text = mode.displayName;
                    else
                    {
                        Text txtStd = tabBtnObj.GetComponentInChildren<Text>();
                        if (txtStd != null) txtStd.text = mode.displayName;
                    }
                }
                
                // Captured variable for lambda
                GameModeData capturedMode = mode;
                btn.onClick.AddListener(() => SelectTab(capturedMode));
                
                _tabButtons[mode] = btn;
            }

            // Выбираем первый по-умолчанию
            SelectTab(tabs[0]);
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

        private void SelectTab(GameModeData mode)
        {
            // Очередь — карты одного режима: другая вкладка — другой набор совместимых карт.
            if (_selectedMode != mode) _queue.Clear();
            _selectedMode = mode;
            
            foreach (var kvp in _tabButtons)
            {
                if (kvp.Value != null)
                {
                    // Кнопка активной вкладки становится некликабельной (выделенной визуально)
                    kvp.Value.interactable = (kvp.Key != mode);
                }
            }

            PopulateMapList();
        }

        private void PopulateMapList()
        {
            ClearContainer(_mapListContainer);
            _entries.Clear();

            if (_mapRegistry == null || _mapRegistry.maps == null)
            {
                GameLog.UI.Warning("[MenuSessionSetup] MapRegistry не назначен или пуст!");
                return;
            }

            GameObject mapPrefabToUse = _mapEntryPrefab != null ? _mapEntryPrefab : _buttonPrefab;

            foreach (var mapDef in MapsForMode(_mapRegistry, _selectedMode))
            {

                GameObject mapBtnObj = Instantiate(mapPrefabToUse, _mapListContainer);
                mapBtnObj.name = $"BtnMap_{mapDef.sceneName}";

                Button btn = mapBtnObj.GetComponent<Button>();
                
                // Если есть превью картинки, находим Image на корневом объекте (MapEntry)
                if (mapDef.preview != null)
                {
                    Image img = mapBtnObj.GetComponent<Image>();
                    if (img != null) img.sprite = mapDef.preview;
                }

                TextMeshProUGUI txtUGUI = NameLabel(mapBtnObj);
                if (txtUGUI != null) txtUGUI.text = mapDef.displayName;
                else
                {
                    TextMeshPro txtTMP = mapBtnObj.GetComponentInChildren<TextMeshPro>();
                    if (txtTMP != null) txtTMP.text = mapDef.displayName;
                    else
                    {
                        Text txtStd = mapBtnObj.GetComponentInChildren<Text>();
                        if (txtStd != null) txtStd.text = mapDef.displayName;
                    }
                }

                string capturedScene = mapDef.sceneName;
                btn.onClick.AddListener(() => OnMapClicked(capturedScene));
                _entries[capturedScene] = mapBtnObj;
            }

            RefreshQueueView();
        }

        /// <summary>Клик по плитке: карта в конец очереди или из очереди вон.</summary>
        private void OnMapClicked(string sceneName)
        {
            bool added = _queue.Toggle(sceneName);
            GameLog.UI.Info($"[MenuSessionSetup] {(added ? "В очередь" : "Из очереди")}: {sceneName}. " +
                            $"Серия: {string.Join(" → ", _queue.Items)}");
            RefreshQueueView();
        }

        /// <summary>«Начать»: серия из очереди — режим вкладки, карты по порядку.</summary>
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

        /// <summary>Номера на плитках и доступность «Начать»/«Очистить».</summary>
        private void RefreshQueueView()
        {
            foreach (KeyValuePair<string, GameObject> entry in _entries)
            {
                if (entry.Value == null) continue;
                Transform numberTransform = FindDeep(entry.Value.transform, _queueNumberName);
                if (numberTransform == null) continue;

                int number = _queue.NumberOf(entry.Key);
                numberTransform.gameObject.SetActive(number > 0);
                TMP_Text label = numberTransform.GetComponent<TMP_Text>();
                if (label != null) label.text = number > 0 ? number.ToString() : "";
            }

            if (_startButton != null) _startButton.interactable = _queue.Count > 0;
            if (_clearButton != null) _clearButton.interactable = _queue.Count > 0;
        }

        /// <summary>Название карты на плитке — первый TMP-текст, кроме номера в очереди.</summary>
        private TextMeshProUGUI NameLabel(GameObject entry)
        {
            foreach (TextMeshProUGUI text in entry.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.name != _queueNumberName) return text;
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }
    }
}
