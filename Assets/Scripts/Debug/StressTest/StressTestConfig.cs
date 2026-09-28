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

        /// <summary>Потолок кукол: сервер обрезает запрос по нему (<c>StressTestRequestMessage.ToConfig</c>).</summary>
        public const int MaxPuppets = 20;

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

        /// <summary>
        /// Скин кукол: -1 (<see cref="StressTestLayout.MixedSkins"/>) — все префабы
        /// <c>TeamRegistry</c> вперемешку; 0..N-1 — только этот префаб из упорядоченного списка
        /// (команды по порядку реестра, внутри — аватары по порядку, без повторов).
        /// В прогоне по скинам не действует.
        /// </summary>
        public int puppetSkin = StressTestLayout.MixedSkins;

        /// <summary>
        /// Прогон по скинам: после «базы» — по фазе на каждый скин («куклы: &lt;префаб&gt;»),
        /// куклы предыдущего скина убираются. Хлама и фазы «по карте» нет.
        /// </summary>
        public bool perSkinPhases;

        /// <summary>
        /// Последняя фаза обычного прогона — «куклы: по карте»: хлам и ряды убираются, куклы
        /// встают кольцом вокруг игрока, в поле зрения обычно 1–3.
        /// </summary>
        public bool mapSpreadPhase = true;

        /// <summary>
        /// Только «по карте»: после «базы» сразу кукол кольцом, без рядов и хлама — одна измеряемая
        /// фаза нагрузки, как в живом матче. Действует только в обычном прогоне.
        /// </summary>
        public bool mapOnly;

        /// <summary>Кольцо «по карте»: ближний радиус, м.</summary>
        public float mapMinRadius = 8f;

        /// <summary>Кольцо «по карте»: дальний радиус, м.</summary>
        public float mapMaxRadius = 15f;
    }
}
