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

        private GameModeData _selectedMode;
        private Dictionary<GameModeData, Button> _tabButtons = new Dictionary<GameModeData, Button>();

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
        /// Вкладки режимов: только режимы матча. Разминка лежит в том же реестре
        /// (<see cref="GameModeData.isWarmup"/>), но её не выбирают — с неё стартует любая карта.
        /// </summary>
        public static List<GameModeData> TabModes(GameModeRegistry registry)
        {
            return registry != null ? new List<GameModeData>(registry.MatchModes) : new List<GameModeData>();
        }

        /// <summary>
        /// Карты под режим матча: совместимые с ним по <c>MapData.supportedModes</c>. Лобби
        /// совместимо только с разминкой, поэтому в выбор карт матча не попадает.
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

                TextMeshProUGUI txtUGUI = mapBtnObj.GetComponentInChildren<TextMeshProUGUI>();
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
            }
        }

        private void OnMapClicked(string sceneName)
        {
            // Серия из одной карты: UI выбора нескольких карт пока нет — данные и серверная
            // логика серии (SessionManager.SetSeries, MatchSeries) уже умеют список.
            SessionManager.Instance?.SetSession(sceneName, _selectedMode?.modeId);
            GameLog.UI.Info($"[MenuSessionSetup] Режим: {_selectedMode?.displayName}, Выбрана карта: {sceneName}");

            // Сразу начинаем серию: карта загрузится и стартует в разминке, матч — кнопкой «Начать матч».
            SessionManager.Instance?.StartSession();
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
