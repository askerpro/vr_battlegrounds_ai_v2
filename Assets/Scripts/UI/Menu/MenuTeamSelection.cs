using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Двухэтапный экран выбора команды и скина.
    /// Этап 1: выбор команды (большие карточки TeamCardButton).
    /// Этап 2: выбор скина (грид аватаров выбранной команды).
    /// </summary>
    public class MenuTeamSelection : MenuScreen
    {
        [Header("UI Levels")]
        [SerializeField] private GameObject _level1TeamSelection;
        [SerializeField] private GameObject _level2AvatarSelection;

        [Header("UI Containers")]
        [SerializeField] private Transform _teamsContainer;
        [SerializeField] private Transform _avatarsListContainer;

        [Header("UI Prefabs")]
        [SerializeField] private GameObject _teamCardPrefab;
        [SerializeField] private GameObject _avatarButtonPrefab;

        private int _selectedTeamIndex = 0;
        private int _selectedAvatarIndex = 0;

        // Кэшируем созданные кнопки для обновления их визуала
        private System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button> _teamButtons
            = new System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button>();
        private System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button> _avatarButtons
            = new System.Collections.Generic.Dictionary<int, UnityEngine.UI.Button>();

        public override void Show()
        {
            base.Show();

            // Берём текущие значения из сессии игрока, если есть
            if (PlayerSession.LocalSession != null)
            {
                _selectedTeamIndex = PlayerSession.LocalSession.TeamIndex;
                _selectedAvatarIndex = PlayerSession.LocalSession.AvatarIndex;
            }

            TeamData[] teams = CurrentTeams();

            // Команда одна (лобби, или матч начался и своя команда уже есть) — выбирать
            // нечего: сразу скины этой команды. Ни одной (матч начался, а команды у игрока
            // нет) — пустой этап 1: команду выдаёт админ.
            if (teams.Length == 1)
            {
                OnTeamCardSelected(teams[0].teamIndex);
                return;
            }

            ShowTeamSelectionState();
        }

        /// <summary>
        /// Кнопка «← Назад» — возврат на этап 1. Если выбирать команду не из чего
        /// (лобби), этап 1 не показывается вовсе.
        /// </summary>
        public void BackToTeamSelection()
        {
            if (!OffersTeamChoice(CurrentTeams())) return;
            ShowTeamSelectionState();
        }

        /// <summary>
        /// Какие команды предлагать: команды активного режима этой сцены, иначе (разминка —
        /// своих команд у неё нет) команды режима, выбранного на матч, иначе все команды реестра.
        ///
        /// <para>
        /// Активный режим — первым: выбор администратора описывает <i>следующий</i> матч,
        /// и в лобби он предлагал бы команды матча, которых в лобби нет.
        /// </para>
        ///
        /// <para>
        /// Матч начался (<see cref="GameMode.TeamChoiceLocked"/>) — сам игрок команду не
        /// меняет: предлагается только его команда (для смены скина), а игроку без команды
        /// режима — ничего, её выдаёт админ.
        /// </para>
        /// </summary>
        public static TeamData[] ResolveAvailableTeams(GameMode activeMode, GameModeData selectedMode, TeamData[] allTeams,
                                                       int currentTeamIndex)
        {
            TeamData[] teams = ResolveAvailableTeams(activeMode, selectedMode, allTeams);

            if (activeMode != null && activeMode.TeamChoiceLocked)
                return System.Array.FindAll(teams, t => t.teamIndex == currentTeamIndex);

            return teams;
        }

        /// <summary>Команды без учёта закрытого выбора — активный режим, выбор матча, реестр.</summary>
        public static TeamData[] ResolveAvailableTeams(GameMode activeMode, GameModeData selectedMode, TeamData[] allTeams)
        {
            TeamData[] teams = null;

            if (activeMode != null && activeMode.Teams != null && activeMode.Teams.Length > 0)
                teams = activeMode.Teams;
            else if (selectedMode != null && selectedMode.teams != null && selectedMode.teams.Length > 0)
                teams = selectedMode.teams;
            else
                teams = allTeams;

            return teams == null ? new TeamData[0] : System.Array.FindAll(teams, t => t != null);
        }

        /// <summary>Предлагать ли выбор команды: только если команд больше одной.</summary>
        public static bool OffersTeamChoice(TeamData[] teams) => teams != null && teams.Length > 1;

        private static TeamData[] CurrentTeams()
        {
            GameMode active = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;

            // Выбор матча спрашиваем, только если режима сцены нет: геттер пишет
            // предупреждение, когда администратор ещё ничего не выбрал.
            GameModeData selected = active == null && SessionManager.Instance != null &&
                                    !string.IsNullOrEmpty(SessionManager.Instance.SelectedModeId)
                ? SessionManager.Instance.SelectedGameModeData
                : null;

            int current = PlayerSession.LocalSession != null ? PlayerSession.LocalSession.TeamIndex : 0;
            return ResolveAvailableTeams(active, selected, TeamRegistry.Instance != null ? TeamRegistry.Instance.teams : null, current);
        }

        /// <summary>
        /// Показывает этап 1 — выбор команды.
        /// </summary>
        private void ShowTeamSelectionState()
        {
            if (_level1TeamSelection) _level1TeamSelection.SetActive(true);
            if (_level2AvatarSelection) _level2AvatarSelection.SetActive(false);

            PopulateTeams();
        }

        /// <summary>
        /// Заполняет контейнер карточками команд из TeamRegistry.
        /// </summary>
        private void PopulateTeams()
        {
            ClearContainer(_teamsContainer);
            _teamButtons.Clear();

            TeamData[] availableTeams = CurrentTeams();

            if (availableTeams.Length == 0)
            {
                // Штатно: матч начался, а у игрока нет команды режима — её выдаёт админ.
                GameLog.UI.Info(
                    "[MenuTeamSelection] Выбирать нечего: матч уже идёт, команду выдаёт админ.");
                return;
            }

            foreach (var team in availableTeams)
            {
                if (team == null) continue;

                GameObject cardObj = Instantiate(_teamCardPrefab, _teamsContainer);
                cardObj.name = $"Card_Team_{team.teamIndex}";

                UnityEngine.UI.Button btn = cardObj.GetComponent<UnityEngine.UI.Button>();

                // Устанавливаем иконку на дочерний Image (Thumbnail/Icon)
                SetButtonIcon(cardObj, team.icon);

                // Устанавливаем название команды
                SetButtonText(cardObj, team.Name);

                int capturedTeamIndex = team.teamIndex;
                btn.onClick.AddListener(() => OnTeamCardSelected(capturedTeamIndex));

                _teamButtons[team.teamIndex] = btn;
            }

            // Если ранее сохраненная команда не найдена — берем первую
            if (!_teamButtons.ContainsKey(_selectedTeamIndex))
            {
                _selectedTeamIndex = availableTeams[0].teamIndex;
            }
        }

        /// <summary>
        /// Вызывается при нажатии на карточку команды. Переход на этап 2 — выбор скина.
        /// </summary>
        public void OnTeamCardSelected(int teamIndex)
        {
            _selectedTeamIndex = teamIndex;

            TeamData team = TeamRegistry.Instance?.GetByIndex(teamIndex);
            if (team == null)
            {
                GameLog.UI.Warning(
                    $"[MenuTeamSelection] Команда с teamIndex={teamIndex} не найдена в TeamRegistry");
                return;
            }

            GameLog.UI.Info(
                $"[MenuTeamSelection] Выбрана команда: {team.Name} (Index: {team.teamIndex}). Переход к скинам.");

            if (_level1TeamSelection) _level1TeamSelection.SetActive(false);
            if (_level2AvatarSelection) _level2AvatarSelection.SetActive(true);

            PopulateAvatars(team);
        }

        /// <summary>
        /// Заполняет грид аватаров для выбранной команды.
        /// </summary>
        private void PopulateAvatars(TeamData team)
        {
            ClearContainer(_avatarsListContainer);
            _avatarButtons.Clear();

            if (team.avatars == null || team.avatars.Count == 0)
            {
                GameLog.UI.Warning(
                    $"[MenuTeamSelection] У команды {team.Name} нет доступных скинов.");
                _selectedAvatarIndex = 0;
                return;
            }

            for (int i = 0; i < team.avatars.Count; i++)
            {
                AvatarData avatarData = team.avatars[i];
                if (avatarData == null) continue;

                GameObject btnObj = Instantiate(_avatarButtonPrefab, _avatarsListContainer);
                btnObj.name = $"Btn_Avatar_{i}";

                UnityEngine.UI.Button btn = btnObj.GetComponent<UnityEngine.UI.Button>();

                SetButtonText(btnObj, !string.IsNullOrEmpty(avatarData.displayName)
                    ? avatarData.displayName : avatarData.name);

                // Иконка скина 
                SetButtonIcon(btnObj, avatarData.icon);

                int capturedAvatarIndex = i;
                btn.onClick.AddListener(() => OnAvatarSelected(capturedAvatarIndex));

                _avatarButtons[i] = btn;
            }
        }

        /// <summary>
        /// Вызывается при нажатии на кнопку скина — применяет выбор и закрывает меню.
        /// </summary>
        public void OnAvatarSelected(int avatarIndex)
        {
            _selectedAvatarIndex = avatarIndex;
            GameLog.UI.Info(
                $"[MenuTeamSelection] Выбран скин индекс: {avatarIndex}. Применяем и закрываем меню.");

            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning(
                    "[MenuTeamSelection] Локальный PlayerSession не найден. Невозможно отправить запрос.");
                return;
            }

            PlayerSession.LocalSession.CmdRequestTeamChange(_selectedTeamIndex, _selectedAvatarIndex);
            Hide();
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

        private void SetButtonIcon(GameObject btnObj, Sprite iconSprite)
        {
            if (iconSprite == null) return;

            // Сначала ищем по новому шаблону: Thumbnail_Container/Thumbnail или просто Thumbnail
            Transform thumbObj = btnObj.transform.Find("Thumbnail_Container/Thumbnail") 
                                 ?? btnObj.transform.Find("Thumbnail")
                                 ?? btnObj.transform.Find("Icon");

            if (thumbObj != null)
            {
                var img = thumbObj.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.sprite = iconSprite;
                    return;
                }
            }

            // Если ничего не нашли, попробуем найти первый Image, у которого имя не совпадает с корнем и не Background
            var images = btnObj.GetComponentsInChildren<UnityEngine.UI.Image>();
            foreach (var img in images)
            {
                if (img.gameObject == btnObj) continue; // Пропускаем корень (рамку кнопки)
                if (img.gameObject.name.Contains("BG") || img.gameObject.name.Contains("Background")) continue; // Пропускаем фоны
                
                img.sprite = iconSprite;
                return;
            }
        }
    }
}
