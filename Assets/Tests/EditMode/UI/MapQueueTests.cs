using NUnit.Framework;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Очередь карт серии в меню админа (<see cref="MapQueue"/>): клик по карточке ставит
    /// карту в конец очереди, повторный — убирает, номера остальных пересчитываются.
    /// </summary>
    public class MapQueueTests
    {
        [Test]
        public void Клики_ставят_карты_в_очередь_по_порядку()
        {
            var queue = new MapQueue();

            Assert.IsTrue(queue.Toggle("B"));
            Assert.IsTrue(queue.Toggle("A"));
            Assert.IsTrue(queue.Toggle("C"));

            CollectionAssert.AreEqual(new[] { "B", "A", "C" }, queue.Items, "Порядок серии не совпадает с порядком кликов.");
            Assert.AreEqual(1, queue.NumberOf("B"));
            Assert.AreEqual(2, queue.NumberOf("A"));
            Assert.AreEqual(3, queue.NumberOf("C"));
            Assert.AreEqual(0, queue.NumberOf("D"), "Карта вне очереди получила номер.");
        }

        [Test]
        public void Повторный_клик_убирает_карту_и_пересчитывает_номера()
        {
            var queue = new MapQueue();
            queue.Toggle("A");
            queue.Toggle("B");
            queue.Toggle("C");

            Assert.IsFalse(queue.Toggle("A"), "Повторный клик не убрал карту.");

            CollectionAssert.AreEqual(new[] { "B", "C" }, queue.Items);
            Assert.AreEqual(1, queue.NumberOf("B"), "Номер не пересчитан после удаления.");
            Assert.AreEqual(2, queue.NumberOf("C"));
            Assert.AreEqual(0, queue.NumberOf("A"));
        }

        [Test]
        public void Очистить_опустошает_очередь()
        {
            var queue = new MapQueue();
            queue.Toggle("A");
            queue.Toggle("B");

            queue.Clear();

            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, queue.NumberOf("A"));
        }
    }
}
