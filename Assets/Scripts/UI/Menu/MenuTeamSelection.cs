using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Раздел «Команда» — два этапа на одном экране: выбор команды (плитки команд) и выбор скина
    /// (плитки скинов выбранной команды). «Назад» со скинов возвращает к командам
    /// (<see cref="HandleBack"/>), если команд больше одной. Выбор скина отправляет запрос серверу и
    /// закрывает меню.
    /// </summary>
    public class MenuTeamSelection : MenuScreen
    {
        [Tooltip("Колонок в сетке плиток.")]
        [SerializeField] private int _columns = 3;

        private int _selectedTeamIndex = 0;
        private bool _choosingSkin;

        public override void Show()
        {
            base.Show();

            // Берём текущие значения из сессии игрока, если есть
            if (PlayerSession.LocalSession != null) _selectedTeamIndex = PlayerSession.LocalSession.TeamIndex;

            TeamData[] teams = CurrentTeams();

            // Команда одна (лобби, или матч начался и своя команда уже есть) — выбирать
            // нечего: сразу скины этой команды. Ни одной (матч начался, а команды у игрока
            // нет) — этап 1 с пояснением: команду выдаёт админ.
            if (teams.Length == 1) ShowSkins(teams[0].teamIndex);
            else ShowTeams();
        }

        public override bool HasInnerBack => _choosingSkin && OffersTeamChoice(CurrentTeams());

        /// <summary>«Назад» со скинов — к командам (если выбирать есть из чего).</summary>
        public override bool HandleBack()
        {
            if (!HasInnerBack) return false;
            ShowTeams();
            return true;
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

        /// <summary>Подпись скина: имя для людей, без технического префикса ассета.</summary>
        public static string SkinLabel(AvatarData avatar)
        {
            if (avatar == null) return "";
            string name = !string.IsNullOrEmpty(avatar.displayName) ? avatar.displayName : avatar.name;
            return name.StartsWith("Avatar_") ? name.Substring("Avatar_".Length) : name;
        }

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

        /// <summary>Этап 1 — выбор команды.</summary>
        private void ShowTeams()
        {
            _choosingSkin = false;
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Выберите команду");

            TeamData[] teams = CurrentTeams();
            if (teams.Length == 0)
            {
                // Штатно: матч начался, а у игрока нет команды режима — её выдаёт админ.
                GameLog.UI.Info("[MenuTeamSelection] Выбирать нечего: матч уже идёт, команду выдаёт админ.");
                MenuKit.EmptyState(Content, "Матч уже идёт — команду выдаёт администратор.");
                RefreshNavigation();
                return;
            }

            RectTransform grid = MenuKit.Grid(Content, _columns, 0.75f);
            foreach (TeamData team in teams)
            {
                int index = team.teamIndex;
                KitButton tile = MenuKit.Tile(grid, team.Name, team.icon, () => ShowSkins(index));
                tile.name = $"Card_Team_{team.teamIndex}";
                tile.Selected = team.teamIndex == _selectedTeamIndex;
            }
            RefreshNavigation();
        }

        /// <summary>Этап 2 — скины выбранной команды.</summary>
        private void ShowSkins(int teamIndex)
        {
            _selectedTeamIndex = teamIndex;
            TeamData team = TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamIndex) : null;
            if (team == null)
            {
                GameLog.UI.Warning($"[MenuTeamSelection] Команда с teamIndex={teamIndex} не найдена в TeamRegistry");
                return;
            }

            GameLog.UI.Info($"[MenuTeamSelection] Выбрана команда: {team.Name} (Index: {team.teamIndex}). Переход к скинам.");

            _choosingSkin = true;
            MenuKit.Clear(Content);
            MenuKit.Title(Content, team.Name + ": выберите скин");

            if (team.avatars == null || team.avatars.Count == 0)
            {
                GameLog.UI.Warning($"[MenuTeamSelection] У команды {team.Name} нет доступных скинов.");
                MenuKit.EmptyState(Content, "У команды нет скинов.");
                RefreshNavigation();
                return;
            }

            // То же правило, что проверит сервер (TeamChangeRules): скин своей команды —
            // только в разминке. Плитки, которые сервер всё равно отклонит, не показываем.
            GameMode active = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            int currentTeam = PlayerSession.LocalSession != null ? PlayerSession.LocalSession.TeamIndex : 0;
            if (!TeamChangeRules.CanPlayerChoose(active, currentTeam, teamIndex, out string refusal))
            {
                GameLog.UI.Info($"[MenuTeamSelection] Скин не выбрать: {refusal}.");
                MenuKit.EmptyState(Content, char.ToUpper(refusal[0]) + refusal.Substring(1) + ".");
                RefreshNavigation();
                return;
            }

            int currentAvatar = PlayerSession.LocalSession != null && PlayerSession.LocalSession.TeamIndex == teamIndex
                ? PlayerSession.LocalSession.AvatarIndex : -1;

            RectTransform grid = MenuKit.Grid(Content, _columns, 0.9f);
            for (int i = 0; i < team.avatars.Count; i++)
            {
                AvatarData avatar = team.avatars[i];
                if (avatar == null) continue;

                int avatarIndex = i;
                KitButton tile = MenuKit.Tile(grid, SkinLabel(avatar), avatar.icon, () => OnAvatarSelected(avatarIndex));
                tile.name = $"Btn_Avatar_{i}";
                tile.Selected = i == currentAvatar;
            }
            RefreshNavigation();
        }

        /// <summary>Скин выбран — запрос серверу и меню закрывается (раньше скрывался только экран, планшет оставался пустым).</summary>
        public void OnAvatarSelected(int avatarIndex)
        {
            GameLog.UI.Info($"[MenuTeamSelection] Выбран скин индекс: {avatarIndex}. Применяем и закрываем меню.");

            if (PlayerSession.LocalSession == null)
            {
                GameLog.UI.Warning("[MenuTeamSelection] Локальный PlayerSession не найден. Невозможно отправить запрос.");
                return;
            }

            PlayerSession.LocalSession.CmdRequestTeamChange(_selectedTeamIndex, avatarIndex);
            CloseMenu();
        }
    }
}
