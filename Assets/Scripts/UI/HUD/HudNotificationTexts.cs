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
    /// <summary>Повторяющееся напоминание игроку в матче (<see cref="HudNotificationTexts.Reminder"/>).</summary>
    public enum HudReminder
    {
        None,
        /// <summary>Нет команды матча — своей зоны нет, нужна команда.</summary>
        ChooseTeam,
        ReturnForCountdown,
        ReturnToBase
    }

    public static class HudNotificationTexts
    {
        /// <summary>
        /// Смена фазы раунда. Итог боя (<see cref="RoundPhase.Resolution"/>) — только звук: победителя
        /// тем же моментом сообщает <c>RoundEndedLocal</c>, и текст фазы затёр бы его.
        /// </summary>
        public static HudMessage Phase(RoundPhase state, string score)
        {
            switch (state)
            {
                case RoundPhase.Setup:      return new HudMessage("Подготовка раунда", 2f, HudSound.Beep);
                case RoundPhase.Equipment:  return new HudMessage("Закупка: возьмите оружие и жетон готовности", 3f, HudSound.Beep);
                case RoundPhase.Countdown:  return new HudMessage("Приготовьтесь!", 3f, HudSound.Beep);
                case RoundPhase.Combat:     return new HudMessage("В бой!", 2f, HudSound.Beep);
                case RoundPhase.Resolution: return new HudMessage(null, 0f, HudSound.Beep);
                case RoundPhase.Scoreboard: return new HudMessage(string.IsNullOrEmpty(score) ? "Итоги раунда" : "Счёт раундов: " + score, 4f, HudSound.None);
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
        /// и начнётся сначала (<c>RoundPhases.CountdownHeld</c>).
        /// </summary>
        public static bool AskReturnForCountdown(bool alive, bool inOwnZone, RoundPhase state) =>
            alive && !inOwnZone && state == RoundPhase.Countdown;

        public static HudMessage ReturnForCountdown() =>
            new HudMessage("Вернитесь в свою зону — отсчёт начнётся заново", 2f, HudSound.Beep);

        /// <summary>
        /// Какое напоминание показать игроку в матче. Без команды матча у игрока нет своей зоны:
        /// «вернитесь в свою зону» его никуда не ведёт — ему нужна команда.
        /// </summary>
        public static HudReminder Reminder(bool hasModeTeam, bool alive, bool inOwnZone, RoundPhase phase)
        {
            if (!hasModeTeam) return HudReminder.ChooseTeam;
            if (AskReturnForCountdown(alive, inOwnZone, phase)) return HudReminder.ReturnForCountdown;
            if (AskReturnToBase(alive, inOwnZone)) return HudReminder.ReturnToBase;
            return HudReminder.None;
        }

        public static HudMessage ChooseTeam() =>
            new HudMessage("Выберите команду в планшете, чтобы войти в матч", 3f, HudSound.Beep);

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
