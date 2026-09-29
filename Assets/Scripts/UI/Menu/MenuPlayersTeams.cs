using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Player;
using VrBattlegrounds.UI.Menu.Kit;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран админа «Игроки и команды» (вложенный в раздел «Админ»): список подключённых игроков и
    /// выдача команды каждому (этап Б). Кнопки команд — команды активного режима сцены; главное
    /// действие — «Распределить автоматически» (разовый автобаланс игроков без команды режима).
    /// Здесь же админ задаёт названия команд на серию и ники игрокам — полем ввода (на планшете —
    /// системная клавиатура); сервер чистит ввод (<c>AdminNaming</c>).
    ///
    /// <para>
    /// Экран только отправляет запросы (<c>PlayerSession.CmdAdminAssignTeam</c>,
    /// <c>CmdAdminAutoBalance</c>); право админа проверяет сервер
    /// (<see cref="SessionPermissions.IsAdmin"/>). Список игроков — сетевые <see cref="PlayerSession"/>
    /// этой машины: они есть и у клиента. Перестраивается раз в 0,5 с и только при изменении
    /// состава, и не пока админ печатает — пересоздание сбивало бы луч и стирало ввод.
    /// </para>
    /// </summary>
    public class MenuPlayersTeams : MenuScreen
    {
        /// <summary>Строка игрока — данные, из которых строится экран (настоящие или превью).</summary>
        public struct PlayerRow
        {
            public uint NetId;
            public string Name;
            public int TeamIndex;
        }

        private string _lastSnapshot = "";

        public override void Show()
        {
            base.Show();
            _lastSnapshot = "";
            Rebuild();
        }

        private void Update()
        {
            if (RefreshDue()) Rebuild();
        }

        /// <summary>Команды, которые админ выдаёт: активного режима сцены.</summary>
        public static TeamData[] AssignableTeams()
        {
            GameMode mode = MapReferee.Instance != null ? MapReferee.Instance.ActiveGameMode : null;
            return mode != null ? System.Array.FindAll(mode.Teams, t => t != null) : new TeamData[0];
        }

        public override void BuildPreview()
        {
            base.Show();
            TeamData[] teams = TeamRegistry.Instance != null ? System.Array.FindAll(TeamRegistry.Instance.teams, t => t != null) : new TeamData[0];
            var players = new List<PlayerRow>();
            string[] names = { "Асланбек", "Марат", "Игрок_07", "ОченьДлинныйНикИгрока", "Залим", "Рустам", "Тимур", "Кантемир", "Ислам", "Азамат", "Бетал", "Хасан" };
            for (int i = 0; i < names.Length; i++)
                players.Add(new PlayerRow { NetId = (uint)(i + 1), Name = names[i], TeamIndex = teams.Length > 0 ? (i % 3 == 2 ? 0 : teams[i % teams.Length].teamIndex) : 0 });
            Render(true, teams, players);
        }

        private void Rebuild()
        {
            // Пока админ печатает, не перестраиваем: пересоздание стёрло бы ввод.
            if (IsTyping()) return;

            bool admin = MenuPermissions.ShowAdminUi();
            TeamData[] teams = AssignableTeams();
            List<PlayerRow> players = CollectPlayers();

            string snapshot = admin + "|" + string.Join(",", System.Array.ConvertAll(teams, t => t.teamIndex + ":" + t.Name));
            foreach (PlayerRow p in players) snapshot += "|" + p.NetId + ":" + p.Name + ":" + p.TeamIndex;
            if (snapshot == _lastSnapshot) return;
            _lastSnapshot = snapshot;

            Render(admin, teams, players);
        }

        private void Render(bool admin, TeamData[] teams, List<PlayerRow> players)
        {
            MenuKit.Clear(Content);
            MenuKit.Title(Content, "Игроки и команды");

            if (admin && teams.Length > 0) SetPrimary("Распределить автоматически", OnAutoBalancePressed);
            else ClearActions();

            if (!admin)
            {
                MenuKit.EmptyState(Content, "Экран администратора: выдавать команды может только админ.");
                return;
            }

            if (teams.Length == 0)
                MenuKit.EmptyState(Content, "Режим сцены не запущен — выдавать нечего.");

            // ── Названия команд на серию ─────────────────────────────────────
            if (teams.Length > 0) MenuKit.Section(Content, "Названия команд на серию");
            foreach (TeamData team in teams)
            {
                RectTransform row = MenuKit.Row(Content);
                int renamedIndex = team.teamIndex;
                TMP_InputField input = MenuKit.Input(row, team.Name, TeamNames.MaxLength);
                MenuKit.Button(row, "Сохранить", () => OnRenameTeamPressed(renamedIndex, input.text));
            }

            // ── Игроки ───────────────────────────────────────────────────────
            MenuKit.Section(Content, $"Игроки ({players.Count})");
            if (players.Count == 0) MenuKit.EmptyState(Content, "Игроков пока нет.");

            foreach (PlayerRow player in players)
            {
                RectTransform row = MenuKit.Row(Content);
                uint netId = player.NetId;
                TMP_InputField nick = MenuKit.Input(row, player.Name, TeamNames.MaxLength);
                MenuKit.Button(row, "Ник", () => OnRenamePlayerPressed(netId, nick.text));

                if (player.TeamIndex == 0 || teams.Length == 0)
                    MenuKit.Label(row, player.TeamIndex == 0 ? "без команды" : TeamName(player.TeamIndex), MenuTextRole.Caption, MenuColorRole.TextSecondary);

                foreach (TeamData team in teams)
                {
                    int teamIndex = team.teamIndex;
                    KitButton button = MenuKit.Button(row, team.Name, () => OnAssignPressed(netId, teamIndex));
                    button.Selected = player.TeamIndex == teamIndex;
                }
            }
        }

        private static string TeamName(int teamIndex)
        {
            TeamData team = TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamIndex) : null;
            return team != null ? team.Name : "команда " + teamIndex;
        }

        private static List<PlayerRow> CollectPlayers()
        {
            var result = new List<PlayerRow>();
            foreach (PlayerSession s in FindObjectsByType<PlayerSession>(FindObjectsSortMode.None))
            {
                if (s != null && s.Role == GameRole.Player)
                    result.Add(new PlayerRow { NetId = s.netId, Name = s.PlayerName, TeamIndex = s.TeamIndex });
            }
            result.Sort((a, b) => a.NetId.CompareTo(b.NetId));
            return result;
        }

        private void OnAssignPressed(uint targetNetId, int teamIndex)
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info($"[MenuPlayersTeams] Админ выдаёт команду {teamIndex} игроку netId={targetNetId}.");
            PlayerSession.LocalSession.CmdAdminAssignTeam(targetNetId, teamIndex);
            _lastSnapshot = "";
        }

        private void OnRenameTeamPressed(int teamIndex, string name)
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info($"[MenuPlayersTeams] Админ переименовывает команду {teamIndex} в '{name}'.");
            PlayerSession.LocalSession.CmdAdminRenameTeam(teamIndex, name);
            _lastSnapshot = "";
        }

        private void OnRenamePlayerPressed(uint targetNetId, string name)
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info($"[MenuPlayersTeams] Админ даёт ник '{name}' игроку netId={targetNetId}.");
            PlayerSession.LocalSession.CmdAdminRenamePlayer(targetNetId, name);
            _lastSnapshot = "";
        }

        private void OnAutoBalancePressed()
        {
            if (PlayerSession.LocalSession == null) return;

            GameLog.UI.Info("[MenuPlayersTeams] Админ распределяет игроков без команды автоматически.");
            PlayerSession.LocalSession.CmdAdminAutoBalance();
            _lastSnapshot = "";
        }

        /// <summary>Какое-то поле ввода экрана в фокусе — админ печатает.</summary>
        private bool IsTyping()
        {
            foreach (TMP_InputField field in Content.GetComponentsInChildren<TMP_InputField>())
            {
                if (field.isFocused) return true;
            }
            return false;
        }
    }
}
