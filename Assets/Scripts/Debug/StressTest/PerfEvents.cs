using System.Collections.Generic;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Что произошло в кадре. Кадр в 40 мс сам по себе ничего не объясняет, а «40 мс,
    /// и в этом кадре 9 выстрелов и 3 спавна» — уже след.
    /// </summary>
    public enum PerfEventKind
    {
        Spawn,
        Despawn,
        Shot,
        Explosion,
        Grab,
        Release,
    }

    /// <summary>
    /// Точка, куда стресс-тест сообщает о своих действиях. Счётчики за кадр уходят
    /// колонками в CSV, текстовые заметки — строками в лог событий.
    ///
    /// <para>
    /// Статический, потому что источники событий (кукла, спавнер, оружие) не должны
    /// знать о рекордере. Когда запись не идёт, вызовы почти бесплатны: счётчик
    /// инкрементируется и сбрасывается при следующем старте.
    /// </para>
    /// </summary>
    public static class PerfEvents
    {
        private static readonly int[] Counters = new int[System.Enum.GetValues(typeof(PerfEventKind)).Length];
        private static readonly Queue<string> Notes = new Queue<string>();

        public static int KindCount => Counters.Length;

        public static void Count(PerfEventKind kind, int amount = 1)
        {
            Counters[(int)kind] += amount;
        }

        /// <summary>Текстовая заметка в лог событий — смена фазы, спавн пачки и т. п.</summary>
        public static void Note(string text)
        {
            if (Notes.Count < 1024) Notes.Enqueue(text);
        }

        /// <summary>Забирает счётчики кадра и обнуляет их.</summary>
        internal static void DrainCounters(int[] into)
        {
            for (int i = 0; i < Counters.Length; i++)
            {
                into[i] = Counters[i];
                Counters[i] = 0;
            }
        }

        internal static bool TryDequeueNote(out string note)
        {
            if (Notes.Count == 0)
            {
                note = null;
                return false;
            }

            note = Notes.Dequeue();
            return true;
        }

        internal static void Reset()
        {
            for (int i = 0; i < Counters.Length; i++) Counters[i] = 0;
            Notes.Clear();
        }
    }
}
