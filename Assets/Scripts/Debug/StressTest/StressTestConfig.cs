using System;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Параметры прогона стресс-теста. Значения по умолчанию — «прод на 10 человек»:
    /// шлем игрока плюс 9 кукол.
    /// </summary>
    [Serializable]
    public sealed class StressTestConfig
    {
        /// <summary>Сколько remote-аватаров повторяют за локальным.</summary>
        public int puppetCount = 9;

        /// <summary>Сколько лежащих предметов (оружие, магазины) высыпать в фазе «хлам».</summary>
        public int clutterCount = 40;

        /// <summary>Разгон до первого замера: прогрев шейдеров, первый GC.</summary>
        public float warmupSeconds = 5f;

        /// <summary>Длительность каждой измеряемой фазы.</summary>
        public float phaseSeconds = 40f;

        /// <summary>Успокоение после спавна — пишется в лог, в сводку не идёт.</summary>
        public float settleSeconds = 4f;

        /// <summary>Наибольшая задержка повтора: куклы повторяют от 0,1 с до этого значения.</summary>
        public float maxDelaySeconds = 1.5f;

        /// <summary>Сколько кукол в ряду перед игроком.</summary>
        public int puppetsPerRow = 5;

        /// <summary>Шаг между куклами в ряду и между рядами, м.</summary>
        public float puppetSpacing = 1.2f;

        /// <summary>Расстояние от игрока до первого ряда, м.</summary>
        public float firstRowDistance = 2.5f;
    }
}
