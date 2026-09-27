using NUnit.Framework;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// Быстрый старт из редактора не перезагружает уже открытую сцену. Хост поднимается
    /// прямо в <c>onlineScene</c> (Lobby), и <c>autoLoadMapScene: Lobby</c> в
    /// <c>DebugBootstrapConfig</c> перезагружал лобби второй раз.
    /// </summary>
    public class DebugAutoLoadMapTests
    {
        [Test]
        public void Открытая_сцена_второй_раз_не_грузится()
        {
            Assert.IsTrue(VrBattlegrounds.DevTools.DebugOrchestrator.IsAlreadyLoaded("Lobby", "Lobby"),
                "Хост уже в Lobby — автозагрузка Lobby перезагрузила бы её второй раз.");
        }

        [Test]
        public void Другая_карта_грузится()
        {
            Assert.IsFalse(VrBattlegrounds.DevTools.DebugOrchestrator.IsAlreadyLoaded("TestMap1", "Lobby"));
            Assert.IsFalse(VrBattlegrounds.DevTools.DebugOrchestrator.IsAlreadyLoaded("", "Lobby"));
        }
    }
}
