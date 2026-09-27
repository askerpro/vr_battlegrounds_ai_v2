using System.Collections.Generic;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Очередь карт серии, которую админ собирает в меню выбора сессии (<see cref="MenuSessionSetup"/>).
    /// Клик по карточке ставит карту в конец очереди, повторный — убирает; номер карты —
    /// её место в очереди с единицы, после удаления номера остальных сдвигаются сами.
    /// Чистая логика без UI — проверяет <c>MapQueueTests</c>.
    /// </summary>
    public sealed class MapQueue
    {
        private readonly List<string> _items = new List<string>();

        /// <summary>Сцены карт в порядке серии.</summary>
        public IReadOnlyList<string> Items => _items;

        public int Count => _items.Count;

        /// <summary>Добавляет карту в конец или убирает, если она уже в очереди.</summary>
        /// <returns>true — карта добавлена; false — убрана (или пустое имя).</returns>
        public bool Toggle(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return false;

            if (_items.Remove(scene)) return false;

            _items.Add(scene);
            return true;
        }

        /// <summary>Номер карты в очереди, начиная с 1; 0 — карты в очереди нет.</summary>
        public int NumberOf(string scene) => _items.IndexOf(scene) + 1;

        public void Clear() => _items.Clear();
    }
}
