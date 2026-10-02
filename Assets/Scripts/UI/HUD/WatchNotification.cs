namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Насколько нотификация важна. Важная прерывает показ менее важной и обгоняет её в очереди
    /// (<see cref="WatchNotificationQueue"/>); от приоритета же сила вибрации.
    /// </summary>
    public enum WatchPriority
    {
        /// <summary>Фон: чужие убийства, напоминания, своя покупка.</summary>
        Low = 0,
        /// <summary>Ход раунда: смена фазы, своё убийство, доход, мало времени.</summary>
        Normal = 1,
        /// <summary>Перелом: итог раунда, пауза, смена сторон, старт матча.</summary>
        High = 2,
        /// <summary>Своя смерть, итог карты.</summary>
        Critical = 3
    }

    /// <summary>Каким звуком часы сопровождают нотификацию. Звук есть у каждой.</summary>
    public enum WatchSound
    {
        /// <summary>Короткий «пик».</summary>
        Beep,
        /// <summary>Тревога: своя смерть.</summary>
        Alert,
        /// <summary>Касса: деньги пришли или ушли.</summary>
        Money
    }

    /// <summary>
    /// Нотификация наручных часов (T-46): текст, приоритет, звук, срок показа. Часы показывают её
    /// вместо статуса, со звуком и вибрацией контроллера той руки, на которой часы, и возвращаются к статусу.
    /// Поднимается через <see cref="WatchNotifications.Post(WatchNotification)"/>.
    /// </summary>
    public readonly struct WatchNotification
    {
        public const float DefaultDuration = 3f;

        public readonly string Text;
        public readonly WatchPriority Priority;
        public readonly WatchSound Sound;

        /// <summary>Сколько секунд держать, если никто не ждёт (при очереди — меньше, см. очередь).</summary>
        public readonly float Duration;

        /// <summary>
        /// Ключ замены: ожидающая нотификация с тем же ключом заменяется новой, а такая же уже на экране
        /// не повторяется (напоминания, повторяющиеся статусы). <c>null</c> — без замены.
        /// </summary>
        public readonly string Key;

        public WatchNotification(string text, WatchPriority priority = WatchPriority.Normal,
                                 WatchSound sound = WatchSound.Beep, float duration = DefaultDuration, string key = null)
        {
            Text = text;
            Priority = priority;
            Sound = sound;
            Duration = duration;
            Key = key;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Text);

        public override string ToString() => $"[{Priority}] {Text}";
    }
}
