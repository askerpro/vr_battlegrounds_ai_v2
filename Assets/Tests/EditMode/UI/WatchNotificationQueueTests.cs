using NUnit.Framework;
using VrBattlegrounds.UI.HUD;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Очередь нотификаций часов (<see cref="WatchNotificationQueue"/>, T-46): часы показывают статус,
    /// нотификация прерывает его на свой срок. Важное перебивает неважное, очередь не отстаёт от игры,
    /// устаревшее не всплывает. Время — параметр, без Unity.
    /// </summary>
    public class WatchNotificationQueueTests
    {
        private static WatchNotification N(string text, WatchPriority priority = WatchPriority.Normal,
                                           float duration = 3f, string key = null) =>
            new WatchNotification(text, priority, WatchSound.Beep, duration, key);

        private static string Started(WatchNotificationQueue queue, float now) =>
            queue.Tick(now, out WatchNotification started) ? started.Text : null;

        [Test]
        public void Пустая_очередь_показывает_статус()
        {
            var queue = new WatchNotificationQueue();
            Assert.IsNull(Started(queue, 0f));
            Assert.IsFalse(queue.IsShowing);
        }

        [Test]
        public void Нотификация_стартует_один_раз_и_держится_свой_срок()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("a", duration: 3f), 0f);

            Assert.AreEqual("a", Started(queue, 0f), "старт сообщается — по нему звук и вибрация");
            Assert.IsNull(Started(queue, 0.1f), "повторного старта нет — иначе звук каждый кадр");
            Assert.IsTrue(queue.IsShowing);
            Assert.AreEqual("a", queue.Current.Text);

            Assert.IsNull(Started(queue, 2.9f));
            Assert.IsTrue(queue.IsShowing, "срок не вышел");

            Assert.IsNull(Started(queue, 3.01f));
            Assert.IsFalse(queue.IsShowing, "срок вышел — часы вернулись к статусу");
        }

        [Test]
        public void Ожидающая_укорачивает_текущую_до_минимума()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("a", duration: 5f), 0f);
            Started(queue, 0f);
            queue.Post(N("b"), 0.5f);

            Assert.IsNull(Started(queue, WatchNotificationQueue.MinShowSeconds - 0.01f), "минимум показа ещё не вышел");
            Assert.AreEqual("b", Started(queue, WatchNotificationQueue.MinShowSeconds + 0.01f),
                            "при очереди текущая держится минимум, а не весь срок — иначе часы отстают от игры");
        }

        [Test]
        public void Важная_прерывает_текущую_сразу()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("убит противник", WatchPriority.Low), 0f);
            Started(queue, 0f);

            queue.Post(N("вы погибли", WatchPriority.Critical), 0.2f);
            Assert.AreEqual("вы погибли", Started(queue, 0.2f));
        }

        [Test]
        public void Неважная_не_прерывает_важную()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("раунд выигран", WatchPriority.High, duration: 3f), 0f);
            Started(queue, 0f);

            queue.Post(N("убит противник", WatchPriority.Low), 0.2f);
            Assert.IsNull(Started(queue, 0.3f));
            Assert.AreEqual("раунд выигран", queue.Current.Text);
        }

        [Test]
        public void Одного_приоритета_по_порядку()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("a", duration: 1f), 0f);
            queue.Post(N("b", duration: 1f), 0f);
            queue.Post(N("c", duration: 1f), 0f);

            Assert.AreEqual("a", Started(queue, 0f));
            Assert.AreEqual("b", Started(queue, 1.01f));
            Assert.AreEqual("c", Started(queue, 2.02f));
        }

        [Test]
        public void Важная_обгоняет_ожидающих()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("current", WatchPriority.High, duration: 1f), 0f);
            Started(queue, 0f);
            queue.Post(N("low1", WatchPriority.Low, duration: 1f), 0.1f);
            queue.Post(N("low2", WatchPriority.Low, duration: 1f), 0.1f);
            queue.Post(N("high", WatchPriority.High, duration: 1f), 0.2f);

            Assert.AreEqual("high", Started(queue, 1.01f));
            Assert.AreEqual("low1", Started(queue, 2.02f));
        }

        [Test]
        public void Переполнение_выбрасывает_самую_неважную()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("current", WatchPriority.Critical, duration: 1f), 0f);
            Started(queue, 0f);

            queue.Post(N("low", WatchPriority.Low, duration: 1f), 0.1f);
            for (int i = 0; i < WatchNotificationQueue.MaxWaiting; i++)
                queue.Post(N("normal" + i, duration: 1f), 0.1f);

            Assert.AreEqual(WatchNotificationQueue.MaxWaiting, queue.WaitingCount);
            float t = 0f;
            for (int i = 0; i < WatchNotificationQueue.MaxWaiting; i++)
            {
                t += 1.01f;
                Assert.AreEqual("normal" + i, Started(queue, t));
            }

            Assert.IsNull(Started(queue, t + 1.01f), "low выброшена при переполнении");
        }

        [Test]
        public void Тот_же_ключ_заменяет_ожидающую()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("current", duration: 1f), 0f);
            Started(queue, 0f);
            queue.Post(N("вернитесь в зону", WatchPriority.Low, 1f, key: "reminder"), 0.1f);
            queue.Post(N("выберите команду", WatchPriority.Low, 1f, key: "reminder"), 0.2f);

            Assert.AreEqual(1, queue.WaitingCount, "напоминание одно — свежее");
            Assert.AreEqual("выберите команду", Started(queue, 1.01f));
        }

        [Test]
        public void Тот_же_ключ_не_повторяет_показанную()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("вернитесь в зону", WatchPriority.Low, 3f, key: "reminder"), 0f);
            Started(queue, 0f);
            queue.Post(N("вернитесь в зону", WatchPriority.Low, 3f, key: "reminder"), 1f);

            Assert.AreEqual(0, queue.WaitingCount, "то же самое уже на экране — не ставить в очередь второй раз");
        }

        [Test]
        public void Устаревшая_ожидающая_не_всплывает()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("старое", duration: 1f), 0f);

            // Часов не было (смена аватара) — никто не тикал.
            Assert.IsNull(Started(queue, WatchNotificationQueue.MaxWaitSeconds + 0.1f),
                          "событие старше срока ожидания — уже неправда, не показывать");
            Assert.IsFalse(queue.IsShowing);
        }

        [Test]
        public void Пустая_нотификация_игнорируется()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N(null), 0f);
            queue.Post(N(""), 0f);
            Assert.AreEqual(0, queue.WaitingCount);
            Assert.IsNull(Started(queue, 0f));
        }

        [Test]
        public void Clear_возвращает_к_статусу()
        {
            var queue = new WatchNotificationQueue();
            queue.Post(N("a"), 0f);
            queue.Post(N("b"), 0f);
            Started(queue, 0f);

            queue.Clear();
            Assert.IsFalse(queue.IsShowing);
            Assert.AreEqual(0, queue.WaitingCount);
        }
    }
}
