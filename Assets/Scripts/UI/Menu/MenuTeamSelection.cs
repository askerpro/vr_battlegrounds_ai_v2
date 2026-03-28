using Mirror;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Единый экран выбора команды и скина для игрока.
    /// Перенесен из устаревшего PlayerMenuController в новую архитектуру MenuScreen.
    /// Автоматически заполняет список доступных команд и их скинов.
    /// </summary>
    public class MenuTeamSelection : MenuScreen
    {
        [Header("UI Containers")]
        [SerializeField] private Transform _tabsContainer;
        [SerializeField] private Transform _avatarsListContainer;

        [Header("UI Prefabs")]
        [SerializeField] private GameObject _tabButtonPrefab;
        [SerializeField] private GameObject _avatarButtonPrefab;

        private int _selectedTeamIndex = 0;
        private int _selectedAvatarIndex = 0;

        // Кэшируем созданные кнопки для обновления их визуала (interactable)
        private System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button> _teamButtons = new System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button>();
        private System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button> _avatarButtons = new System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button>();

        public override void Show()
        {
            base.Show();

            // Если игрок уже имеет установленные значения, берем их
            if (PlayerSession.LocalSession != null)
            {
                _selectedTeamIndex = PlayerSession.LocalSession.TeamIndex;
                _selectedAvatarIndex = PlayerSession.LocalSession.AvatarIndex;
            }

            PopulateTeams();
        }

        private void PopulateTeams()
        {
            ClearContainer(_tabsContainer);
            _teamButtons.Clear();

            // Пытаемся получить команды для текущего режима
            TeamData[] availableTeams = null;
            if (VrBattlegrounds.Managers.SessionManager.Instance != null &&
                VrBattlegrounds.Managers.SessionManager.Instance.SelectedGameModeData != null)
            {
                availableTeams = VrBattlegrounds.Managers.SessionManager.Instance.SelectedGameModeData.teams;
            }

            // Если режима нет или список пуст, берем все команды из реестра
            if (availableTeams == null || availableTeams.Length == 0)
            {
                availableTeams = TeamRegistry.Instance.teams;
            }

            if (availableTeams == null || availableTeams.Length == 0)
            {
                Debug.LogWarning("[MenuTeamSelection] Нет доступных команд для отображения!");
                return;
            }

            foreach (var team in availableTeams)
            {
                if (team == null) continue;

                GameObject btnObj = Instantiate(_tabButtonPrefab, _tabsContainer);
                btnObj.name = $"Tab_Team_{team.teamIndex}";

                UnityEngine.UI.Button btn = btnObj.GetComponent<UnityEngine.UI.Button>();
                SetButtonText(btnObj, team.displayName);

                UnityEngine.UI.Image btnImage = btnObj.GetComponent<UnityEngine.UI.Image>();
                if (btnImage != null && team.icon != null)
                {
                    btnImage.sprite = team.icon;
                }

                int capturedTeamIndex = team.teamIndex;
                btn.onClick.AddListener(() => OnTeamTabSelected(capturedTeamIndex));

                _teamButtons[team.teamIndex] = btn;
            }

            // Если ранее сохраненная команда не найдена в доступных, берем первую
            if (!_teamButtons.ContainsKey(_selectedTeamIndex))
            {
                _selectedTeamIndex = availableTeams[0].teamIndex;
            }

            // Автоматически выбираем активную команду
            OnTeamTabSelected(_selectedTeamIndex);
        }

        /// <summary>
        /// Вызывается при нажатии на вкладку/кнопку команды.
        /// </summary>
        public void OnTeamTabSelected(int teamIndex)
        {
            _selectedTeamIndex = teamIndex;

            TeamData team = TeamRegistry.Instance?.GetByIndex(teamIndex);
            if (team == null)
            {
                Debug.LogWarning($"[MenuTeamSelection] Команда с teamIndex={teamIndex} не найдена в TeamRegistry");
                return;
            }

            Debug.Log($"[MenuTeamSelection] Выбрана вкладка команды: {team.name} (Index: {team.teamIndex})");

            // Обновляем визуал кнопок команд
            foreach (var kvp in _teamButtons)
            {
                if (kvp.Value != null)
                    kvp.Value.interactable = (kvp.Key != teamIndex);
            }

            PopulateAvatars(team);
        }

        private void PopulateAvatars(TeamData team)
        {
            ClearContainer(_avatarsListContainer);
            _avatarButtons.Clear();

            if (team.avatars == null || team.avatars.Count == 0)
            {
                Debug.LogWarning($"[MenuTeamSelection] У команды {team.displayName} нет доступных скинов.");
                _selectedAvatarIndex = 0; // fallback
                return;
            }

            for (int i = 0; i < team.avatars.Count; i++)
            {
                AvatarData avatarData = team.avatars[i];
                if (avatarData == null) continue;

                GameObject btnObj = Instantiate(_avatarButtonPrefab, _avatarsListContainer);
                btnObj.name = $"Btn_Avatar_{i}";

                UnityEngine.UI.Button btn = btnObj.GetComponent<UnityEngine.UI.Button>();

                SetButtonText(btnObj, !string.IsNullOrEmpty(avatarData.displayName) ? avatarData.displayName : avatarData.name);

                // Устанавливаем иконку на корневой компонент Image кнопки, как просил пользователь
                UnityEngine.UI.Image btnImage = btnObj.GetComponent<UnityEngine.UI.Image>();
                if (btnImage != null && avatarData.icon != null)
                {
                    btnImage.sprite = avatarData.icon;
                }

                int capturedAvatarIndex = i;
                btn.onClick.AddListener(() => OnAvatarSelected(capturedAvatarIndex));

                _avatarButtons[i] = btn;
            }

            // Сбрасываем выбранный скин на 0 по умолчанию при смене команды (если только этот скин уже не 0)
            OnAvatarSelected(0);
        }

        /// <summary>
        /// Вызывается при нажатии на кнопку скина из списка.
        /// </summary>
        public void OnAvatarSelected(int avatarIndex)
        {
            _selectedAvatarIndex = avatarIndex;
            Debug.Log($"[MenuTeamSelection] Выбран скин индекс: {avatarIndex}. Ожидание подтверждения...");

            // Обновляем визуал кнопок скинов
            foreach (var kvp in _avatarButtons)
            {
                if (kvp.Value != null)
                    kvp.Value.interactable = (kvp.Key != avatarIndex);
            }
        }

        /// <summary>
        /// Вызывается при нажатии на кнопку Применить / Выбрать.
        /// Отправляет финальный запрос на сервер.
        /// </summary>
        public void OnApplyPressed()
        {
            if (PlayerSession.LocalSession == null)
            {
                Debug.LogWarning("[MenuTeamSelection] Локальный PlayerSession не найден. Невозможно отправить запрос на сервер.");
                return;
            }

            PlayerSession.LocalSession.CmdRequestTeamChange(_selectedTeamIndex, _selectedAvatarIndex);
            Debug.Log($"[MenuTeamSelection] Запрошена смена команды на {_selectedTeamIndex} и скина на {_selectedAvatarIndex}");
        }

        private void ClearContainer(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }

        private void SetButtonText(GameObject btnObj, string textValue)
        {
            var txtUGUI = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (txtUGUI != null) { txtUGUI.text = textValue; return; }

            var txtTMP = btnObj.GetComponentInChildren<TMPro.TextMeshPro>();
            if (txtTMP != null) { txtTMP.text = textValue; return; }

            var txtStd = btnObj.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txtStd != null) { txtStd.text = textValue; }
        }
    }
}
