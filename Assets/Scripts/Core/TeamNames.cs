using System;
using System.Collections.Generic;
using System.Text;

namespace VrBattlegrounds
{
    /// <summary>
    /// Названия команд, которые админ задал на эту серию (<c>TeamNameService</c> реплицирует их
    /// всем машинам). Показ любой команды — через <see cref="TeamData.Name"/>: переименованная
    /// команда меняется сразу на табло, в HUD, статистике и меню. Без переименования — имя ассета.
    ///
    /// <para>
    /// Ассет не трогается: запись в <c>TeamData.displayName</c> во время игры в редакторе
    /// сохранилась бы на диск.
    /// </para>
    /// </summary>
    public static class TeamNames
    {
        /// <summary>Длиннее не помещается на табло и в строке HUD.</summary>
        public const int MaxLength = 24;

        private static readonly Dictionary<int, string> Overrides = new Dictionary<int, string>();

        /// <summary>Названия поменялись — UI перерисовывается.</summary>
        public static event Action Changed;

        public static string Resolve(TeamData team)
        {
            if (team == null) return string.Empty;
            return Overrides.TryGetValue(team.teamIndex, out string name) && !string.IsNullOrEmpty(name) ? name : team.displayName;
        }

        /// <summary>Полная замена переопределений (с сервера). Пустое имя — вернуть имя ассета.</summary>
        public static void SetAll(IEnumerable<KeyValuePair<int, string>> names)
        {
            Overrides.Clear();
            if (names != null)
            {
                foreach (KeyValuePair<int, string> pair in names)
                {
                    if (!string.IsNullOrEmpty(pair.Value)) Overrides[pair.Key] = pair.Value;
                }
            }
            Changed?.Invoke();
        }

        public static void Clear() => SetAll(null);

        /// <summary>
        /// Имя из ввода админа: без пробелов по краям и повторов, без тегов разметки TextMeshPro
        /// (<c>&lt;</c>, <c>&gt;</c> — иначе имя меняло бы вид табло), не длиннее <paramref name="maxLength"/>.
        /// Пустая строка — «вернуть по умолчанию».
        /// </summary>
        public static string Sanitize(string raw, int maxLength = MaxLength)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var sb = new StringBuilder(raw.Length);
            bool space = false;
            foreach (char c in raw.Trim())
            {
                if (c == '<' || c == '>' || char.IsControl(c)) continue;

                if (char.IsWhiteSpace(c))
                {
                    if (space) continue;
                    space = true;
                    sb.Append(' ');
                    continue;
                }

                space = false;
                sb.Append(c);
            }

            string result = sb.ToString().Trim();
            return result.Length > maxLength ? result.Substring(0, maxLength).TrimEnd() : result;
        }
    }
}
