using System.Collections.Generic;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>Контекст экрана «Обзор»: от него зависит набор секций.</summary>
    public enum OverviewContext
    {
        /// <summary>Нет сетевой сессии.</summary>
        Offline,
        /// <summary>Лобби: матча нет, счёта нет.</summary>
        Lobby,
        /// <summary>Боевая карта до «Начать матч».</summary>
        Warmup,
        /// <summary>Матч идёт.</summary>
        Live,
        /// <summary>Матч на паузе.</summary>
        Paused,
        /// <summary>Итог карты записан, ждём «Следующая карта» / «В лобби».</summary>
        MapFinished
    }

    public enum OverviewRowKind
    {
        /// <summary>Строка игрока — ячейки по колонкам секции.</summary>
        Player,
        /// <summary>Пояснение одной ячейкой на всю ширину.</summary>
        Info
    }

    /// <summary>Как View выделяет строку. Цвета — токены дизайн-системы T-32.</summary>
    public enum OverviewTone
    {
        Normal,
        /// <summary>Своя строка.</summary>
        Local,
        /// <summary>Выбыл, нет аватара.</summary>
        Dimmed,
        /// <summary>Требует внимания: не выбрал команду, не откалиброван, отсчёт стоит.</summary>
        Warning,
        /// <summary>Победитель.</summary>
        Winner,
        /// <summary>Всё в порядке (калибровка пройдена).</summary>
        Success
    }

    /// <summary>Шапка экрана. Часы отдельно от остального: тикают каждую секунду, а таблицу не трогают.</summary>
    public sealed class OverviewHeader
    {
        /// <summary>Главная строка: карта или «Лобби».</summary>
        public string Title = "";

        /// <summary>Что происходит: «Раунд 5 из 12 · Бой», «Разминка», «Пауза».</summary>
        public string Status = "";

        /// <summary>Счёт карты: «Военные 3 : 2 Повстанцы»; пусто — счёта нет.</summary>
        public string Score = "";

        /// <summary>Про смотрящего: «Вы выбыли — наблюдение до конца раунда».</summary>
        public string ViewerNote = "";

        /// <summary>Подпись часов («Бой», «Закупка»); пусто — часов нет.</summary>
        public string ClockLabel = "";

        /// <summary>Остаток в целых секундах (округление вверх); null — часов нет.</summary>
        public int? ClockSeconds;

        internal bool SameExceptClock(OverviewHeader o) =>
            o != null && Title == o.Title && Status == o.Status && Score == o.Score &&
            ViewerNote == o.ViewerNote && ClockLabel == o.ClockLabel && ClockSeconds.HasValue == o.ClockSeconds.HasValue;
    }

    public sealed class OverviewRow
    {
        /// <summary>Стабильный ключ: у игрока — ключ сессии. View переиспользует строку по нему.</summary>
        public string Key = "";
        public OverviewRowKind Kind;
        public OverviewTone Tone;

        /// <summary>Ячейки по <see cref="OverviewSection.Columns"/>; у <see cref="OverviewRowKind.Info"/> — одна.</summary>
        public string[] Cells = new string[0];

        internal bool SameContent(OverviewRow o)
        {
            if (o == null || Key != o.Key || Kind != o.Kind || Tone != o.Tone || Cells.Length != o.Cells.Length) return false;
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i] != o.Cells[i]) return false;
            return true;
        }
    }

    public sealed class OverviewSection
    {
        /// <summary>Стабильный идентификатор: «team:1», «series», «plan».</summary>
        public string Id = "";
        public string Title = "";
        public OverviewTone Tone;

        /// <summary>Заголовки колонок строк игроков; пусто — в секции только пояснения.</summary>
        public string[] Columns = new string[0];

        public List<OverviewRow> Rows = new List<OverviewRow>();

        public OverviewRow AddInfo(string key, string text, OverviewTone tone = OverviewTone.Normal)
        {
            var row = new OverviewRow { Key = key, Kind = OverviewRowKind.Info, Tone = tone, Cells = new[] { text } };
            Rows.Add(row);
            return row;
        }
    }

    /// <summary>
    /// Что показать на экране «Обзор» в данный момент. Чистые данные: View только рисует.
    ///
    /// <para>
    /// Три уровня сравнения — чтобы перестраивать как можно меньше и не сбивать наведение луча:
    /// часы меняются каждую секунду — View правит одну надпись; <see cref="SameContent"/> ложно,
    /// а <see cref="SameLayout"/> истинно (хп, счёт) — правит текст ячеек на месте;
    /// <see cref="SameLayout"/> ложно (кто-то пришёл, порядок сменился) — строит строки заново.
    /// </para>
    /// </summary>
    public sealed class OverviewSnapshot
    {
        public OverviewContext Context;
        public OverviewHeader Header = new OverviewHeader();
        public List<OverviewSection> Sections = new List<OverviewSection>();

        public OverviewSection Find(string id) => Sections.Find(s => s.Id == id);

        /// <summary>Те же секции и те же строки в том же порядке (по ключам) — строки можно не пересоздавать.</summary>
        public bool SameLayout(OverviewSnapshot o)
        {
            if (o == null || Context != o.Context || Sections.Count != o.Sections.Count) return false;
            for (int s = 0; s < Sections.Count; s++)
            {
                OverviewSection a = Sections[s], b = o.Sections[s];
                if (a.Id != b.Id || a.Rows.Count != b.Rows.Count || a.Columns.Length != b.Columns.Length) return false;
                for (int r = 0; r < a.Rows.Count; r++)
                    if (a.Rows[r].Key != b.Rows[r].Key || a.Rows[r].Kind != b.Rows[r].Kind) return false;
            }
            return true;
        }

        /// <summary>Всё совпадает, кроме значения часов, — перерисовывать нечего, кроме них.</summary>
        public bool SameContent(OverviewSnapshot o)
        {
            if (!SameLayout(o) || !Header.SameExceptClock(o.Header)) return false;
            for (int s = 0; s < Sections.Count; s++)
            {
                OverviewSection a = Sections[s], b = o.Sections[s];
                if (a.Title != b.Title || a.Tone != b.Tone) return false;
                for (int c = 0; c < a.Columns.Length; c++)
                    if (a.Columns[c] != b.Columns[c]) return false;
                for (int r = 0; r < a.Rows.Count; r++)
                    if (!a.Rows[r].SameContent(b.Rows[r])) return false;
            }
            return true;
        }
    }
}
