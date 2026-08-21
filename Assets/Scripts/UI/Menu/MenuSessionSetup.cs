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

            if (_gameModeRegistry == null || _gameModeRegistry.modes == null || _gameModeRegistry.modes.Length == 0)
            {
                GameLog.UI.Warning("[MenuSessionSetup] Реестр режимов пуст или не назначен!");
                return;
            }

            foreach (var mode in _gameModeRegistry.modes)
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
            SelectTab(_gameModeRegistry.modes[0]);
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

            foreach (var mapDef in _mapRegistry.maps)
            {
                if (!IsMapSupportedByMode(mapDef, _selectedMode)) continue;

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

        private bool IsMapSupportedByMode(MapData mapDef, GameModeData targetMode)
        {
            if (targetMode == null || mapDef.supportedModes == null || mapDef.supportedModes.Length == 0) 
            {
                return true; 
            }

            foreach (var mode in mapDef.supportedModes)
            {
                if (mode != null && mode.modeId == targetMode.modeId)
                    return true;
            }
            return false;
        }

        private void OnMapClicked(string sceneName)
        {
            // Сохраняем логику, как было в Armada
            SessionManager.Instance?.SetSession(sceneName, _selectedMode?.modeId);
            GameLog.UI.Info($"[MenuSessionSetup] Режим: {_selectedMode?.displayName}, Выбрана карта: {sceneName}");
            
            // Сразу запускаем старт после выбора (или можно сделать отдельную кнопку 'Start')
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
