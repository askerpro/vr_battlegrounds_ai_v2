using NUnit.Framework;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Кнопки админа на карте видны только там, где имеют смысл
    /// (<see cref="AdminMatchCommands.IsAvailable(MatchCommand, MapState, bool, bool, bool)"/>):
    /// «Пауза» — во время матча, «Продолжить» — на паузе, «Начать матч» — когда матча нет
    /// и карта его допускает, «Стоп» — пока идёт серия. То же правило сервер проверяет
    /// перед исполнением.
    /// </summary>
    public class MatchCommandVisibilityTests
    {
        private static bool Available(MatchCommand c, MapState s, bool pause = true, bool mapHasMatch = true, bool series = true)
            => AdminMatchCommands.IsAvailable(c, s, pause, mapHasMatch, series);

        [Test]
        public void В_разминке_только_Начать_матч_и_Стоп()
        {
            Assert.IsTrue(Available(MatchCommand.StartMatch, MapState.Warmup));
            Assert.IsFalse(Available(MatchCommand.Pause, MapState.Warmup), "«Пауза» видна без матча.");
            Assert.IsFalse(Available(MatchCommand.Resume, MapState.Warmup), "«Продолжить» видна без паузы.");
            Assert.IsTrue(Available(MatchCommand.Stop, MapState.Warmup));
        }

        [Test]
        public void Во_время_матча_Пауза_и_Стоп()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, MapState.Live), "«Начать матч» видна во время матча.");
            Assert.IsTrue(Available(MatchCommand.Pause, MapState.Live));
            Assert.IsFalse(Available(MatchCommand.Pause, MapState.Live, pause: false), "«Пауза» у режима без паузы.");
            Assert.IsFalse(Available(MatchCommand.Resume, MapState.Live));
        }

        [Test]
        public void На_паузе_только_Продолжить_и_Стоп()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, MapState.Paused), "На паузе «Начать матч» начал бы матч заново.");
            Assert.IsFalse(Available(MatchCommand.Pause, MapState.Paused));
            Assert.IsTrue(Available(MatchCommand.Resume, MapState.Paused));
            Assert.IsTrue(Available(MatchCommand.Stop, MapState.Paused));
        }

        [Test]
        public void В_лобби_без_серии_кнопок_нет()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, MapState.Warmup, mapHasMatch: false, series: false),
                "В лобби видна «Начать матч».");
            Assert.IsFalse(Available(MatchCommand.Stop, MapState.Warmup, mapHasMatch: false, series: false),
                "«Стоп» без серии.");
            Assert.IsFalse(Available(MatchCommand.NextMap, MapState.Warmup, mapHasMatch: false, series: false),
                "«Следующая карта» без серии.");
        }

        /// <summary>
        /// «Следующая карта» — только в разминке идущей серии: во время матча и на паузе
        /// переход оборвал бы игру на карте. Решение о переходе принимает только админ.
        /// </summary>
        [Test]
        public void Следующая_карта_только_в_разминке_серии()
        {
            Assert.IsTrue(Available(MatchCommand.NextMap, MapState.Warmup));
            Assert.IsFalse(Available(MatchCommand.NextMap, MapState.Live), "«Следующая карта» посреди матча.");
            Assert.IsFalse(Available(MatchCommand.NextMap, MapState.Paused), "«Следующая карта» на паузе — снимок матча потерялся бы.");
            Assert.IsFalse(Available(MatchCommand.NextMap, MapState.Warmup, series: false), "«Следующая карта» без серии.");
        }
    }
}
