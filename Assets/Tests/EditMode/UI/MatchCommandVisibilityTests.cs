using NUnit.Framework;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Кнопки админа на карте видны только там, где имеют смысл
    /// (<see cref="AdminMatchCommands.IsAvailable(MatchCommand, GameplayState, bool, bool, bool)"/>):
    /// «Пауза» — во время матча, «Продолжить» — на паузе, «Начать матч» — когда матча нет
    /// и карта его допускает, «Стоп» — пока идёт серия. То же правило сервер проверяет
    /// перед исполнением.
    /// </summary>
    public class MatchCommandVisibilityTests
    {
        private static bool Available(MatchCommand c, GameplayState s, bool pause = true, bool mapHasMatch = true, bool series = true)
            => AdminMatchCommands.IsAvailable(c, s, pause, mapHasMatch, series);

        [Test]
        public void В_разминке_только_Начать_матч_и_Стоп()
        {
            Assert.IsTrue(Available(MatchCommand.StartMatch, GameplayState.NotActive));
            Assert.IsFalse(Available(MatchCommand.Pause, GameplayState.NotActive), "«Пауза» видна без матча.");
            Assert.IsFalse(Available(MatchCommand.Resume, GameplayState.NotActive), "«Продолжить» видна без паузы.");
            Assert.IsTrue(Available(MatchCommand.Stop, GameplayState.NotActive));
        }

        [Test]
        public void Во_время_матча_Пауза_и_Стоп()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, GameplayState.Active), "«Начать матч» видна во время матча.");
            Assert.IsTrue(Available(MatchCommand.Pause, GameplayState.Active));
            Assert.IsFalse(Available(MatchCommand.Pause, GameplayState.Active, pause: false), "«Пауза» у режима без паузы.");
            Assert.IsFalse(Available(MatchCommand.Resume, GameplayState.Active));
        }

        [Test]
        public void На_паузе_только_Продолжить_и_Стоп()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, GameplayState.Paused), "На паузе «Начать матч» начал бы матч заново.");
            Assert.IsFalse(Available(MatchCommand.Pause, GameplayState.Paused));
            Assert.IsTrue(Available(MatchCommand.Resume, GameplayState.Paused));
            Assert.IsTrue(Available(MatchCommand.Stop, GameplayState.Paused));
        }

        [Test]
        public void В_лобби_без_серии_кнопок_нет()
        {
            Assert.IsFalse(Available(MatchCommand.StartMatch, GameplayState.NotActive, mapHasMatch: false, series: false),
                "В лобби видна «Начать матч».");
            Assert.IsFalse(Available(MatchCommand.Stop, GameplayState.NotActive, mapHasMatch: false, series: false),
                "«Стоп» без серии.");
        }
    }
}
