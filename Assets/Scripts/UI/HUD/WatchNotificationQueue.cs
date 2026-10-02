using System.Collections.Generic;

namespace VrBattlegrounds.UI.HUD
{
    /// <summary>
    /// Очередь нотификаций часов (T-46) — чистые правила без Unity: время приходит параметром,
    /// поэтому всё проверяется юнит-тестом (<c>WatchNotificationQueueTests</c>).
    ///
    /// <list type="bullet">
    /// <item>Свободно — нотификация показывается на ближайшем <see cref="Tick"/>; старт сообщается один раз
    /// (по нему звук и вибрация).</item>
    /// <item>Ожидающая важнее показанной — прерывает её сразу; прерванная не возвращается (уже неактуальна).</item>
    /// <item>Ожидающие — по убыванию приоритета, внутри приоритета по порядку.</item>
    /// <item>Пока кто-то ждёт, показанная держится не весь срок, а <see cref="MinShowSeconds"/>: последний
    /// выстрел раунда даёт «вы погибли», «раунд проигран» и «+1900» разом, и часы не должны отставать от игры.</item>
    /// <item>Больше <see cref="MaxWaiting"/> ожидающих — выбрасывается самая неважная (из равных — последняя).</item>
    /// <item>Ключ (<see cref="WatchNotification.Key"/>): новая заменяет ожидающую с тем же ключом, а такая же
    /// уже на экране не встаёт в очередь повторно.</item>
    /// <item>Ожидающая старше <see cref="MaxWaitSeconds"/> выбрасывается: часов не было (смена аватара,
    /// призрак без часов) — событие уже неправда.</item>
    /// </list>
    /// </summary>
    public sealed class WatchNotificationQueue
    {
        public const int MaxWaiting = 4;
        public const float MinShowSeconds = 1.5f;
        public const float MaxWaitSeconds = 8f;

        private readonly struct Waiting
        {
            public readonly WatchNotification Notification;
            public readonly float PostedAt;

            public Waiting(WatchNotification notification, float postedAt)
            {
                Notification = notification;
                PostedAt = postedAt;
            }
        }

        private readonly List<Waiting> _waiting = new List<Waiting>();
        private WatchNotification _current;
        private float _shownAt;

        /// <summary>Часы показывают нотификацию (иначе — статус).</summary>
        public bool IsShowing { get; private set; }

        /// <summary>Показанная нотификация; осмысленна при <see cref="IsShowing"/>.</summary>
        public WatchNotification Current => _current;

        public int WaitingCount => _waiting.Count;

        public void Post(WatchNotification notification, float now)
        {
            if (notification.IsEmpty) return;

            if (!string.IsNullOrEmpty(notification.Key))
            {
                if (IsShowing && _current.Key == notification.Key && _current.Text == notification.Text) return;
                _waiting.RemoveAll(w => w.Notification.Key == notification.Key);
            }

            // После всех ожидающих не ниже по приоритету — FIFO внутри приоритета.
            int index = _waiting.Count;
            for (int i = 0; i < _waiting.Count; i++)
            {
                if (_waiting[i].Notification.Priority < notification.Priority)
                {
                    index = i;
                    break;
                }
            }

            _waiting.Insert(index, new Waiting(notification, now));

            if (_waiting.Count > MaxWaiting)
                _waiting.RemoveAt(_waiting.Count - 1);
        }

        /// <summary>
        /// Продвигает очередь. <c>true</c> — в этот момент началась нотификация <paramref name="started"/>
        /// (сыграть звук, дать вибрацию).
        /// </summary>
        public bool Tick(float now, out WatchNotification started)
        {
            started = default;
            _waiting.RemoveAll(w => now - w.PostedAt > MaxWaitSeconds);

            if (IsShowing)
            {
                bool someoneWaits = _waiting.Count > 0;
                bool preempted = someoneWaits && _waiting[0].Notification.Priority > _current.Priority;
                float hold = someoneWaits && _current.Duration > MinShowSeconds ? MinShowSeconds : _current.Duration;

                if (!preempted && now - _shownAt < hold) return false;
                IsShowing = false;
                _current = default;
            }

            if (_waiting.Count == 0) return false;

            _current = _waiting[0].Notification;
            _waiting.RemoveAt(0);
            _shownAt = now;
            IsShowing = true;
            started = _current;
            return true;
        }

        /// <summary>Снять показанную и забыть ожидающих — часы сразу к статусу (смена карты).</summary>
        public void Clear()
        {
            _waiting.Clear();
            _current = default;
            IsShowing = false;
        }
    }
}
