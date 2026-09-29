using TMPro;
using UnityEngine;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Экран «Статистика» (<see cref="MenuScreenType.Statistics"/>) — у всех игроков: сквозной
    /// счёт серии по картам и TOTAL — раунды и карты команд, убийства / смерти / ассисты игроков.
    ///
    /// <para>
    /// Данные — реплицированные строки <see cref="Series"/> (есть и у клиента), таблицу
    /// собирает <see cref="SeriesStatsTable"/>. Текст перестраивается раз в
    /// <see cref="_refreshInterval"/> и только когда что-то поменялось.
    /// </para>
    /// </summary>
    public class MenuStatistics : MenuScreen
    {
        [Tooltip("Текст таблицы (TMP, rich text).")]
        [SerializeField] private TMP_Text _table;

        [Min(0.1f)]
        [SerializeField] private float _refreshInterval = 0.5f;

        private float _timer;
        private string _last = "";

        public override void Show()
        {
            base.Show();
            _last = "";
            Rebuild();
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < _refreshInterval) return;
            _timer = 0f;
            Rebuild();
        }

        private void Rebuild()
        {
            if (_table == null) return;

            string text = BuildText(Series.Instance);
            if (text == _last) return;

            _last = text;
            _table.text = text;
        }

        /// <summary>Текст таблицы для серии; без серии — пояснение.</summary>
        public static string BuildText(Series series)
        {
            if (series == null || series.Maps.Count == 0)
                return "Серия ещё не начиналась — статистики нет.";

            var sections = SeriesStatsTable.Build(series.Maps, series.TeamStats, series.PlayerStats, series.Results);
            string table = SeriesStatsTable.Format(sections, TeamName);

            // Карта загружена напрямую, без серии: та же таблица из одной карты.
            return series.IsAdHoc ? "<i>Карта без серии</i>\n\n" + table : table;
        }

        private static string TeamName(int teamIndex)
        {
            TeamData team = TeamRegistry.Instance != null ? TeamRegistry.Instance.GetByIndex(teamIndex) : null;
            return team != null ? team.Name : "Команда " + teamIndex;
        }
    }
}
