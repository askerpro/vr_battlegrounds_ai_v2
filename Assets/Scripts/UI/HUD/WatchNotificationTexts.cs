using VrBattlegrounds.Economy;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>Повторяющееся напоминание игроку в матче (<see cref="WatchNotificationTexts.Reminder"/>).</summary>
    public enum WatchReminder
    {
        None,
        /// <summary>Нет команды матча — своей зоны нет, нужна команда.</summary>
        ChooseTeam,
        ReturnForCountdown,
        ReturnToBase
    }

    /// <summary>
    /// Что, как важно и каким звуком говорят часы на события игры (T-46). Чистый класс: события
    /// переводит <see cref="WatchGameEvents"/>, тексты и правила — здесь, и тест проверяет их без префабов.
    /// Раньше — <c>HudNotificationTexts</c> информационного HUD.
    /// </summary>
    public static class WatchNotificationTexts
    {
        /// <summary>Ключ напоминаний: свежее заменяет ожидающее, одинаковое на экране не повторяется.</summary>
        public const string ReminderKey = "reminder";

        /// <summary>Пороги «мало времени», секунды.</summary>
        private static readonly int[] LowTimeThresholds = { 30, 10 };

        // ── Ход раунда ────────────────────────────────────────

        /// <summary>
        /// Смена фазы раунда. Итог боя (<see cref="RoundPhase.Resolution"/>) — пустая: победителя тем же
        /// моментом сообщает <see cref="RoundEnded"/>, и фаза его перебила бы.
        /// </summary>
        public static WatchNotification Phase(RoundPhase state, string score)
        {
            switch (state)
            {
                case RoundPhase.Setup:      return new WatchNotification("Подготовка раунда", WatchPriority.Normal, duration: 2f);
                case RoundPhase.Equipment:  return new WatchNotification("Закупка\nвозьмите оружие и жетон готовности", WatchPriority.Normal);
                case RoundPhase.Countdown:  return new WatchNotification("Приготовьтесь!", WatchPriority.Normal);
                case RoundPhase.Combat:     return new WatchNotification("В бой!", WatchPriority.Normal, duration: 2f);
                case RoundPhase.Scoreboard: return new WatchNotification(string.IsNullOrEmpty(score) ? "Итоги раунда" : "Счёт раундов\n" + score,
                                                                         WatchPriority.Normal, duration: 4f);
                default:                    return default;
            }
        }

        public static WatchNotification ModeStarted() =>
            new WatchNotification("Матч начался!\nВ бой!", WatchPriority.High, duration: 4f);

        /// <summary>Вторая половина карты: команды меняются сторонами — игрок идёт на другую базу.</summary>
        public static WatchNotification SidesSwapped() =>
            new WatchNotification("Смена сторон!\nВаша база — на другой стороне", WatchPriority.High, duration: 4f);

        /// <param name="winnerTeam">Индекс команды-победителя; <c>null</c> — ничья.</param>
        public static WatchNotification RoundEnded(int? winnerTeam, string winnerName, int localTeam)
        {
            if (winnerTeam == null) return new WatchNotification("Раунд — ничья", WatchPriority.High);
            return winnerTeam.Value == localTeam
                ? new WatchNotification("Раунд выигран!", WatchPriority.High)
                : new WatchNotification($"Раунд проигран\nпобедили {winnerName}", WatchPriority.High);
        }

        /// <param name="winnerTeam">Индекс команды-победителя карты; <c>null</c> — ничья.</param>
        public static WatchNotification MapFinished(int? winnerTeam, string winnerName, int localTeam)
        {
            if (winnerTeam == null) return new WatchNotification("Карта сыграна вничью", WatchPriority.Critical, duration: 5f);
            return winnerTeam.Value == localTeam
                ? new WatchNotification($"Победа!\nкарта за {winnerName}", WatchPriority.Critical, duration: 5f)
                : new WatchNotification($"Поражение\nкарта за {winnerName}", WatchPriority.Critical, duration: 5f);
        }

        public static WatchNotification Paused(bool paused) => paused
            ? new WatchNotification("Пауза", WatchPriority.High)
            : new WatchNotification("Игра продолжается", WatchPriority.High, duration: 2f);

        /// <summary>
        /// Какой порог «мало времени» пройден между двумя замерами остатка: 0 — никакой. Остаток вырос
        /// (новая фаза) или первый замер (<c>NaN</c>) — не пересечение.
        /// </summary>
        public static int CrossedLowTime(float before, float now)
        {
            if (float.IsNaN(before) || float.IsNaN(now) || now >= before) return 0;
            foreach (int threshold in LowTimeThresholds)
                if (before > threshold && now <= threshold) return threshold;
            return 0;
        }

        public static WatchNotification LowTime(int seconds) =>
            new WatchNotification($"Осталось {seconds} секунд", WatchPriority.Normal, duration: 2f);

        /// <summary>Respawn: игрок снова жив после смерти.</summary>
        public static WatchNotification Respawned() =>
            new WatchNotification("Вы снова в игре", WatchPriority.Normal, duration: 2f);

        // ── Смерти ────────────────────────────────────────────

        /// <param name="kill">Кто кого убил.</param>
        /// <param name="localSessionNetId">netId сессии этого игрока (0 — нет).</param>
        /// <param name="localTeam">Команда этого игрока.</param>
        public static WatchNotification Kill(in KillNotice kill, uint localSessionNetId, int localTeam)
        {
            bool iDied = localSessionNetId != 0 && kill.VictimNetId == localSessionNetId;
            bool iKilled = localSessionNetId != 0 && kill.HasKiller && kill.KillerNetId == localSessionNetId;

            if (iDied)
            {
                return new WatchNotification(kill.HasKiller ? $"Вы погибли\nвас убил {kill.KillerName}" : "Вы погибли",
                                             WatchPriority.Critical, WatchSound.Alert);
            }

            if (iKilled)
                return new WatchNotification($"Вы убили {kill.VictimName}", WatchPriority.Normal, duration: 2f);

            string who = kill.VictimTeam == localTeam ? "Союзник" : "Противник";
            string by = kill.HasKiller ? $"\n({kill.KillerName})" : string.Empty;
            return new WatchNotification($"{who} {kill.VictimName} убит{by}", WatchPriority.Low, duration: 2f);
        }

        // ── Деньги ────────────────────────────────────────────

        /// <summary>
        /// Денежная операция своего игрока: «+$3250 / победа в раунде», «-$2900 / TR15». Покупку игрок
        /// сделал сам у стены — она неважная и не перебивает ход раунда.
        /// </summary>
        public static WatchNotification Money(in EconomyTransaction t)
        {
            string amount = (t.Delta >= 0 ? "+$" : "-$") + System.Math.Abs(t.Delta);
            string detail = string.IsNullOrEmpty(t.Detail) ? string.Empty : " " + t.Detail;

            switch (t.Reason)
            {
                case EconomyReason.RoundWin:  return Cash($"{amount}\nпобеда в раунде", WatchPriority.Normal);
                case EconomyReason.RoundLoss: return Cash($"{amount}\nза проигранный раунд", WatchPriority.Normal);
                case EconomyReason.Kill:      return Cash($"{amount}\nза убийство{detail}", WatchPriority.Normal);
                case EconomyReason.TeamKill:  return Cash($"{amount}\nубийство союзника", WatchPriority.Normal);
                case EconomyReason.Purchase:  return Cash(string.IsNullOrEmpty(t.Detail) ? amount : $"{amount}\n{t.Detail}", WatchPriority.Low);
                case EconomyReason.Refund:    return Cash($"{amount}\nвозврат{detail}", WatchPriority.Low);
                case EconomyReason.HalfReset: return Cash($"${t.Money}\nновая половина", WatchPriority.Normal);
                default:                      return Cash($"{amount}\nденьги", WatchPriority.Low);
            }
        }

        private static WatchNotification Cash(string text, WatchPriority priority) =>
            new WatchNotification(text, priority, WatchSound.Money, duration: 2.5f);

        // ── Напоминания ───────────────────────────────────────

        /// <summary>
        /// Просить ли игрока вернуться на свою базу: он мёртв (погиб или выбыл в конце боя) и вне
        /// своей зоны. Живым становятся только на своей базе, а закупка ждёт, пока на базе все.
        /// </summary>
        public static bool AskReturnToBase(bool alive, bool inOwnZone) => !alive && !inOwnZone;

        /// <summary>
        /// Живой вышел из своей зоны на обратном отсчёте: отсчёт стоит, пока он не вернётся,
        /// и начнётся сначала (<c>RoundPhases.CountdownHeld</c>).
        /// </summary>
        public static bool AskReturnForCountdown(bool alive, bool inOwnZone, RoundPhase state) =>
            alive && !inOwnZone && state == RoundPhase.Countdown;

        /// <summary>
        /// Какое напоминание показать игроку в матче. Без команды матча у игрока нет своей зоны:
        /// «вернитесь в свою зону» его никуда не ведёт — ему нужна команда.
        /// </summary>
        public static WatchReminder Reminder(bool hasModeTeam, bool alive, bool inOwnZone, RoundPhase phase)
        {
            if (!hasModeTeam) return WatchReminder.ChooseTeam;
            if (AskReturnForCountdown(alive, inOwnZone, phase)) return WatchReminder.ReturnForCountdown;
            if (AskReturnToBase(alive, inOwnZone)) return WatchReminder.ReturnToBase;
            return WatchReminder.None;
        }

        public static WatchNotification ChooseTeam() =>
            new WatchNotification("Выберите команду\nв планшете", WatchPriority.Low, key: ReminderKey);

        public static WatchNotification ReturnToBase() =>
            new WatchNotification("Вернитесь в свою зону", WatchPriority.Low, key: ReminderKey);

        public static WatchNotification ReturnForCountdown() =>
            new WatchNotification("Вернитесь в зону —\nотсчёт начнётся заново", WatchPriority.Low, duration: 2f, key: ReminderKey);
    }
}
