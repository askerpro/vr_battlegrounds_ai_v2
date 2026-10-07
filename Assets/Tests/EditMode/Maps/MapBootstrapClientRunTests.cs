using System;
using NUnit.Framework;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Клиентская точка «run известен локально» (<c>MapBootstrap.AcceptsClientRun</c>).
    ///
    /// <para>
    /// Что доказывает. Клиентский MapBootstrap принимает descriptor только своей сцены, только собранного запуска
    /// (CompositionReady/Ready) и только с LoadSequence больше уже принятого этим клиентом в той же сессии. Поэтому
    /// устаревший descriptor старого запуска той же сцены — повторный LoadMap или «Lobby → карта → Lobby», где
    /// SyncVar и смена сцены приходят в любом порядке, — не становится запуском нового экземпляра сцены, а значит
    /// барьер Relay не попросит снимок не того запуска. Новая сессия сервера начинает счёт заново.
    /// </para>
    /// </summary>
    public class MapBootstrapClientRunTests
    {
        private static readonly Guid Session = Guid.NewGuid();

        private static MapRunSnapshot Descriptor(string scene, ulong sequence, MapBootstrapStatus status, Guid? session = null)
        {
            var key = new MapRunKey(session ?? Session, sequence);
            var catalog = new MapRunResolverCatalog("warmup", Array.Empty<string>(),
                new[] { new MapRunMapDescription(scene, MapRunKind.Lobby, "content", 1, "arsenal", "arsenal-v1", Array.Empty<string>()) });
            MapRunResolution resolution = MapRunResolver.Resolve(new MapRunRequest(key, scene, "", "content"), catalog,
                new MapRunBindings(scene, Array.Empty<MapStationConfig>()));
            Assert.IsTrue(resolution.Passed, string.Join(", ", resolution.Errors));
            return new MapRunSnapshot(resolution.Config, 2, status, MapState.Warmup, 0, string.Empty, 0, 1, 2, string.Empty);
        }

        [Test]
        public void Собранный_запуск_своей_сцены_принимается()
        {
            Assert.IsTrue(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 1, MapBootstrapStatus.CompositionReady), "Lobby", default));
            Assert.IsTrue(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 1, MapBootstrapStatus.Ready), "Lobby", default));
        }

        [Test]
        public void Чужая_сцена_и_несобранный_запуск_не_принимаются()
        {
            Assert.IsFalse(MapBootstrap.AcceptsClientRun(Descriptor("TestMap1", 2, MapBootstrapStatus.Ready), "Lobby", default),
                "Descriptor другой сцены принят.");
            foreach (MapBootstrapStatus status in new[] { MapBootstrapStatus.Preparing, MapBootstrapStatus.Closing,
                         MapBootstrapStatus.Retiring, MapBootstrapStatus.Failed })
                Assert.IsFalse(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 1, status), "Lobby", default),
                    $"Запуск в статусе {status} принят как готовый.");
        }

        [Test]
        public void Устаревший_descriptor_той_же_сцены_не_принимается_новым_экземпляром()
        {
            // Lobby (1) → TestMap1 (2) → Lobby: новый экземпляр лобби видит запоздавший descriptor первого лобби.
            var highest = new MapRunKey(Session, 2);
            Assert.IsFalse(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 1, MapBootstrapStatus.Ready), "Lobby", highest),
                "Descriptor первого лобби принят вторым — снимок попросили бы для старого запуска.");
            Assert.IsTrue(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 3, MapBootstrapStatus.Ready), "Lobby", highest));

            // Перезагрузка той же карты: ключ уже принят прежним экземпляром.
            Assert.IsFalse(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 3, MapBootstrapStatus.Ready), "Lobby", new MapRunKey(Session, 3)),
                "Повторно принят уже принятый ключ.");
        }

        [Test]
        public void Новая_сессия_сервера_начинает_счёт_ключей_заново()
        {
            var highest = new MapRunKey(Session, 7);
            Assert.IsTrue(MapBootstrap.AcceptsClientRun(Descriptor("Lobby", 1, MapBootstrapStatus.Ready, Guid.NewGuid()), "Lobby", highest),
                "Первый запуск новой сессии отвергнут из-за ключа прежней.");
        }
    }
}
