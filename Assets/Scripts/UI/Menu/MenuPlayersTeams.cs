using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран админа «Игроки и команды»: список подключённых игроков и выдача команды
    /// каждому (этап Б). Кнопки команд — команды активного режима сцены; кнопка
    /// «Распределить автоматически» — разовый автобаланс игроков без команды режима.
    ///
    /// <para>
    /// Экран только отправляет запросы (<c>PlayerSession.CmdAdminAssignTeam</c>,
    /// <c>CmdAdminAutoBalance</c>); право админа проверяет сервер
    /// (<see cref="TeamChangeRules.IsAdmin"/>). Не-админу экран показывает только
    /// пояснение. Список игроков — сетевые <see cref="PlayerSession"/> этой машины:
    /// они есть и у клиента, <c>PlayersManager</c> для этого не нужен.
    /// </para>
    /// </summary>
    public class MenuPlayersTeams : MenuScreen
    {
        [Tooltip("Контейнер строк игроков (VerticalLayoutGroup).")]
        [SerializeField] private Transform _rowsContainer;

        [Tooltip("Префаб кнопки команды: Button + TMP-текст в детях (SlimButton_IconText).")]
        [SerializeField] private GameObject _teamButtonPrefab;

        [Tooltip("Кнопка «Распределить автоматически».")]
        [SerializeField] private Button _autoBalanceButton;

        [Tooltip("Как часто перестраивать список, пока экран открыт, секунды.")]
        [Min(0.1f)]
        [SerializeField] private float _refreshInterval = 0.5f;

        private float _refreshTimer;
        private string _lastSnapshot = "";

        private void Awake()
        {
            if (_autoBalanceButton != null)
                _autoBalanceButton.onClick.AddListener(OnAutoBalancePressed);
        }

        private void OnDestroy()
        {
            if (_autoBalanceButton != null)
                _autoBalanceButton.onClick.RemoveListener(OnAutoBalancePressed);
        }

        public override void Show()
        {
            base.Show();
            _lastSnapshot = "";
            Rebuild();
        }

        private void Update()
        {
            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer < _refreshInterval) return;
            _refreshTimer = 0f;
            Rebuild();
        }

        /// <summary>Эта машина — админ: хост или сессия с флагом админа.</summary>
        public static bool IsLocalAdmin()
        {
            if (NetworkServer.active) return true;
            if (LocalClientProfile.IsLocalAdmin) return true;
            return PlayerSession.LocalSession != null && PlayerSession.LocalSession.IsAdmin;
        }

        /// <summary>Команды, которые админ выдаёт: активного режима сцены.</summary>
        public static TeamData[] AssignableTeams()
        {
            GameMode mode = GameMode.Current;
            return mode != null ? System.Array.FindAll(mode.Teams, t => t != null) : new TeamData[0];
        }

        private void Rebuild()
        {
            if (_rowsContainer == null) return;

            bool admin = IsLocalAdmin();
            TeamData[] teams = AssignableTeams();
            List<PlayerSession> players = CollectPlayers();

            // Перестраиваем, только если что-то поменялось: список живёт в VR-меню,
            // и пересоздание кнопок каждые полсекунды сбивало бы наведение луча.
            string snapshot = admin + "|" + string.Join(",", System.Array.ConvertAll(teams, t => t.teamIndex.ToString()));
            foreach (PlayerSession p in players) snapshot += "|" + p.netId + ":" + p.PlayerName + ":" + p.TeamIndex;
            if (snapshot == _lastSnapshot) return;
            _lastSnapshot = snapshot;

            for (int i = _rowsContainer.childCount - 1; i >= 0; i--)
                Destroy(_rowsContainer.GetChild(i).gameObject);

            if (_autoBalanceButton != null)
                _autoBalanceButton.gameObject.SetActive(admin && teams.Length > 0);

            if (!admin)
            {
                CreateLabel(_rowsContainer, "Экран администратора: выдавать команды может только админ.");
                return;
            }

            if (teams.Length == 0)
                CreateLabel(_rowsContainer, "Режим сцены не запущен — выдавать нечего.");

            foreach (PlayerSession player in players)
            {
                GameObject row = CreateRow(_rowsContainer);
                TeamData current = TeamRegistry.Instance != null && player.TeamIndex != 0
                    ? TeamRegistry.Instance.GetByIndex(player.TeamIndex)
                    : null;
                CreateLabel(row.transform, $"{player.PlayerName}: {(current != null ? current.displayName : "без команды")}");

                foreach (TeamData team in teams)
                {
                    uint targetNetId = player.netId;
                    int teamIndex = team.teamIndex;
                    CreateTeamButton(row.transform, team.displayName, () => OnAssignPressed(targetNetId, teamIndex));
                }
            }
        }

        private static List<PlayerSession> CollectPlayers()
        {
            var result = new List<PlayerSession>();
            foreach (PlayerSession s in FindObjectsByType<PlayerSession>(FindObjectsSortMode.None))
            {
                if (s != null && s.Role == GameRole.Player) result.Add(s);
            }
            result.Sort((a, b) => a.netId.CompareTo(b.netId));
            return result;
        }

        private void OnAssignPressed(uint targetNetId, int teamIndex)
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info($"[MenuPlayersTeams] Админ выдаёт команду {teamIndex} игроку netId={targetNetId}.");
            PlayerSession.LocalSession.CmdAdminAssignTeam(targetNetId, teamIndex);
            _lastSnapshot = "";
        }

        private void OnAutoBalancePressed()
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info("[MenuPlayersTeams] Админ распределяет игроков без команды автоматически.");
            PlayerSession.LocalSession.CmdAdminAutoBalance();
            _lastSnapshot = "";
        }

        // ── Сборка строк ─────────────────────────────────────────────────────

        private static GameObject CreateRow(Transform parent)
        {
            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var element = row.AddComponent<LayoutElement>();
            element.preferredHeight = 80f;
            return row;
        }

        private static void CreateLabel(Transform parent, string text)
        {
            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(parent, false);

            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 36f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            var element = label.AddComponent<LayoutElement>();
            element.preferredWidth = 600f;
            element.preferredHeight = 70f;
        }

        private void CreateTeamButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            if (_teamButtonPrefab == null) return;

            GameObject go = Instantiate(_teamButtonPrefab, parent);
            go.name = "Btn_Team_" + text;

            TMP_Text label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;

            Button button = go.GetComponent<Button>();
            if (button != null) button.onClick.AddListener(onClick);
        }
    }
}
