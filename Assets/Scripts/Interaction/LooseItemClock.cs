using System.Collections.Generic;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Сколько каждый предмет пролежал ничьим подряд. Чистая логика: ключ — любой
    /// целочисленный id предмета, время — любое монотонное.
    ///
    /// <para>
    /// Отсчёт начинается с первого замера «ничей» и обнуляется, как только предмет
    /// перестал быть ничьим (подняли, повесили в кобуру). Предметы, не попавшие в замер,
    /// забываются — иначе словарь рос бы на каждом уничтоженном магазине.
    /// </para>
    /// </summary>
    public sealed class LooseItemClock
    {
        private readonly Dictionary<int, float> _looseSince = new Dictionary<int, float>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _stale = new List<int>();

        /// <summary>Начинает замер: всё, что не будет отмечено до <see cref="EndPass"/>, забудется.</summary>
        public void BeginPass()
        {
            _seen.Clear();
        }

        /// <summary>
        /// Отмечает предмет и возвращает, сколько он уже лежит ничьим (0 — только что стал
        /// ничьим или не ничей).
        /// </summary>
        public float Observe(int id, bool loose, float now)
        {
            _seen.Add(id);

            if (!loose)
            {
                _looseSince.Remove(id);
                return 0f;
            }

            if (!_looseSince.TryGetValue(id, out float since))
            {
                _looseSince[id] = now;
                return 0f;
            }

            return now - since;
        }

        /// <summary>Предмет убран — отсчёт для него больше не нужен.</summary>
        public void Forget(int id)
        {
            _looseSince.Remove(id);
        }

        public void EndPass()
        {
            _stale.Clear();

            foreach (int id in _looseSince.Keys)
            {
                if (!_seen.Contains(id)) _stale.Add(id);
            }

            foreach (int id in _stale) _looseSince.Remove(id);
        }

        /// <summary>Сколько предметов сейчас на отсчёте.</summary>
        public int Tracked => _looseSince.Count;
    }
}
