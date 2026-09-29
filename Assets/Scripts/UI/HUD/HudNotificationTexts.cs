using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>Каким звуком сопроводить сообщение информационного HUD.</summary>
    public enum HudSound
    {
        None,
        /// <summary>Короткий «пик»: смена фазы, итог раунда, чужое убийство.</summary>
        Beep,
        /// <summary>Тревога: погиб сам игрок.</summary>
        Alert
    }

    /// <summary>Сообщение информационного HUD: текст, сколько держать, каким звуком.</summary>
    public readonly struct HudMessage
    {
        public readonly string Text;
        public readonly float Duration;
        public readonly HudSound Sound;

        public HudMessage(string text, float duration, HudSound sound)
        {
            Text = text;
            Duration = duration;
            Sound = sound;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Text) && Sound == HudSound.None;
    }

    /// <summary>
    /// Тексты и звуки информационного HUD (<see cref="HUDWidget_GameNotification"/>). Чистый класс:
    /// режим строк не генерирует, виджет спрашивает здесь — и тест проверяет без префабов.
    /// </summary>
    public static class HudNotificationTexts
    {
        /// <summary>
        /// Смена фазы раунда. Итог боя (<see cref="RoundState.Resolution"/>) — только звук: победителя
        /// тем же моментом сообщает <c>OnRoundEndedLocal</c>, и текст фазы затёр бы его.
        /// </summary>
        public static HudMessage Phase(RoundState state, string score)
        {
            switch (state)
            {
                case RoundState.Setup:      return new HudMessage("Подготовка раунда", 2f, HudSound.Beep);
                case RoundState.Equipment:  return new HudMessage("Закупка: возьмите оружие и жетон готовности", 3f, HudSound.Beep);
                case RoundState.Countdown:  return new HudMessage("Приготовьтесь!", 3f, HudSound.Beep);
                case RoundState.Combat:     return new HudMessage("В бой!", 2f, HudSound.Beep);
                case RoundState.Resolution: return new HudMessage(null, 0f, HudSound.Beep);
                case RoundState.Scoreboard: return new HudMessage(string.IsNullOrEmpty(score) ? "Итоги раунда" : "Счёт раундов: " + score, 4f, HudSound.None);
                default:                    return new HudMessage(null, 0f, HudSound.None);
            }
        }

        /// <summary>
        /// Просить ли игрока вернуться на свою базу: он мёртв (погиб или выбыл в конце боя) и вне
        /// своей зоны. Живым становятся только на своей базе, а закупка ждёт, пока на базе все.
        /// </summary>
        public static bool AskReturnToBase(bool alive, bool inOwnZone) => !alive && !inOwnZone;

        public static HudMessage ReturnToBase() =>
            new HudMessage("Вернитесь в свою зону, чтобы вернуться в игру", 3f, HudSound.Beep);

        /// <summary>
        /// Живой вышел из своей зоны на обратном отсчёте: отсчёт стоит, пока он не вернётся,
        /// и начнётся сначала (<c>RoundManager.CountdownHeld</c>).
        /// </summary>
        public static bool AskReturnForCountdown(bool alive, bool inOwnZone, RoundState state) =>
            alive && !inOwnZone && state == RoundState.Countdown;

        public static HudMessage ReturnForCountdown() =>
            new HudMessage("Вернитесь в свою зону — отсчёт начнётся заново", 2f, HudSound.Beep);

        /// <summary>Вторая половина карты: команды меняются сторонами — игрок идёт на другую базу.</summary>
        public static HudMessage SidesSwapped() =>
            new HudMessage("Смена сторон! Ваша база теперь на другой стороне", 4f, HudSound.Beep);

        /// <param name="kill">Кто кого убил.</param>
        /// <param name="localSessionNetId">netId сессии этого игрока (0 — нет).</param>
        /// <param name="localTeam">Команда этого игрока.</param>
        public static HudMessage Kill(in KillNotice kill, uint localSessionNetId, int localTeam)
        {
            bool iDied = localSessionNetId != 0 && kill.VictimNetId == localSessionNetId;
            bool iKilled = localSessionNetId != 0 && kill.HasKiller && kill.KillerNetId == localSessionNetId;

            if (iDied)
            {
                return new HudMessage(kill.HasKiller ? $"Вы погибли — вас убил {kill.KillerName}" : "Вы погибли", 3f, HudSound.Alert);
            }

            if (iKilled)
            {
                return new HudMessage($"Вы убили {kill.VictimName}", 2f, HudSound.Beep);
            }

            string who = kill.VictimTeam == localTeam ? "Союзник" : "Противник";
            string by = kill.HasKiller ? $" ({kill.KillerName})" : string.Empty;
            return new HudMessage($"{who} {kill.VictimName} убит{by}", 2f, HudSound.Beep);
        }
    }
}
