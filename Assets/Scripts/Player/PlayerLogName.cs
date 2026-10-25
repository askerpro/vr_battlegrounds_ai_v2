using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Единый формат игрока в строках лога: <c>Имя#netId сессии</c>.
    ///
    /// <para>
    /// У одного игрока в логе встречается до трёх имён: имя сессии, UXR-имя аватара до хука
    /// <c>SyncVar</c> (<c>Player 41 (External)</c>) и имя объекта аватара (<c>… (Remote)</c>).
    /// netId сессии переживает смену аватара и карты, поэтому связывает строки разных систем.
    /// Старые строки массово не переделываются — формат для новых и исправляемых.
    /// </para>
    /// </summary>
    public static class PlayerLogName
    {
        /// <summary>Игрок сессии; <c>null</c> — «нет сессии».</summary>
        public static string Of(PlayerSession session) =>
            session != null ? $"{session.PlayerName}#{session.netId}" : "нет сессии";

        /// <summary>Игрок аватара; аватар без сессии (тест, кукла до связи) — имя объекта.</summary>
        public static string Of(PlayerController player)
        {
            if (player == null) return "нет аватара";
            PlayerSession session = player.Session;
            return session != null ? Of(session) : player.name;
        }

        /// <summary>
        /// Источник урона: игрок — <c>Имя#netId</c>, иной актор (мишень, предмет) — имя объекта,
        /// урон без источника (падение, проход сквозь стену, отладка) — «мир».
        /// </summary>
        public static string OfSource(UxrActor actor)
        {
            if (actor == null) return "мир";
            PlayerController player = actor.GetComponent<PlayerController>();
            return player != null ? Of(player) : actor.name;
        }
    }
}
