using System;
using System.Linq;
using Mirror;
using NUnit.Framework;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Запуск карты для EditMode-тестов судьи и режимов. В игре судью запускает <see cref="MapBootstrap"/> из сцены
    /// с MapRoot; в EditMode сцены нет, поэтому тест собирает тот же поток вручную: настоящий
    /// <see cref="MapRunAuthority"/> (единственный писатель descriptor), настоящий <see cref="MapRunResolver"/>
    /// (режим матча согласуется один раз при загрузке), CompositionReady и <c>ServerStartRun</c>.
    /// Неуправляемого судьи больше нет — тесты проверяют тот же путь, что игра.
    /// </summary>
    public sealed class TestMapRun : IMapRunHost
    {
        public MapRunAuthority Authority { get; }
        public MapRunScope Scope { get; }
        public MapRunConfig Config { get; }
        public MapReferee Referee { get; private set; }

        public MapMatchIntent MatchIntent => Config.MatchIntent;

        public bool IsServerReady => Scope != null && !Scope.IsDisposed && Authority != null &&
                                     Authority.Current.IsReady && Authority.Current.Key == Scope.Key;

        /// <summary>Сколько раз судья опубликовал режим.</summary>
        public int ModeCommits { get; private set; }

        private TestMapRun(MapRunAuthority authority, MapRunScope scope, MapRunConfig config)
        {
            Authority = authority;
            Scope = scope;
            Config = config;
        }

        public bool CommitMode(MapState state, GameMode mode)
        {
            if (mode == null || mode.ModeData == null) return false;
            bool committed = Authority.CommitMode(Scope, Authority.Current.Revision, state, mode.ModeData.modeId, mode.netId);
            if (committed) ModeCommits++;
            return committed;
        }

        /// <summary>
        /// Начать запуск карты <paramref name="scene"/> на заспавненном <paramref name="authority"/>: resolver с
        /// захваченным режимом серии <paramref name="capturedModeId"/>. Бой — <paramref name="matchModes"/> не пуст,
        /// лобби — пуст.
        /// </summary>
        public static TestMapRun Begin(MapRunAuthority authority, string scene, string warmupModeId,
                                       string capturedModeId, params string[] matchModes)
        {
            MapRunKind kind = matchModes.Length > 0 ? MapRunKind.Combat : MapRunKind.Lobby;
            var catalog = new MapRunResolverCatalog(warmupModeId, matchModes.Concat(AllModesFallback(capturedModeId, matchModes)),
                new[] { new MapRunMapDescription(scene, kind, "content-test", 1, "arsenal-test", "arsenal-test-v1", matchModes) });
            var request = new MapRunRequest(authority.NextKey, scene, capturedModeId, "content-test");
            MapRunResolution resolution = MapRunResolver.Resolve(request, catalog, new MapRunBindings(scene, Array.Empty<MapStationConfig>()));
            Assert.IsTrue(resolution.Passed, "Тестовый запуск не разрешился: " + string.Join(", ", resolution.Errors));
            Assert.IsTrue(authority.BeginRun(resolution, out MapRunScope scope), "MapRunAuthority не принял тестовый запуск.");
            return new TestMapRun(authority, scope, resolution.Config);
        }

        /// <summary>
        /// Связать судью с запуском и пройти CompositionReady → ServerStartRun, как <c>MapBootstrap.Compose</c>.
        /// Судья к этому моменту заспавнен; <paramref name="coordinator"/> — любой заспавненный объект вместо
        /// координатора развёртывания (descriptor требует оба netId).
        /// </summary>
        public bool Compose(MapReferee referee, NetworkIdentity coordinator)
        {
            Referee = referee;
            Assert.IsTrue(Authority.CommitPrepared(Scope, Authority.Current.Revision, referee.netId, coordinator.netId),
                "CompositionReady тестового запуска не принят.");
            return referee.ServerStartRun();
        }

        /// <summary>
        /// Опубликовать состояние карты и режим прямо в descriptor, без смены режима судьёй, — для тестов читателей
        /// состояния (обзор, HUD). <paramref name="modeIdentity"/> — заспавненный объект на месте режима.
        /// </summary>
        public void Publish(MapReferee referee, NetworkIdentity coordinator, NetworkIdentity modeIdentity, MapState state, string modeId)
        {
            Referee = referee;
            Assert.IsTrue(Authority.CommitPrepared(Scope, Authority.Current.Revision, referee.netId, coordinator.netId),
                "CompositionReady тестового запуска не принят.");
            Assert.IsTrue(Authority.CommitMode(Scope, Authority.Current.Revision, state, modeId, modeIdentity.netId),
                "Режим тестового запуска не опубликован.");
        }

        // Режим, захваченный серией, обязан быть известен каталогу, даже если карта его не поддерживает.
        private static string[] AllModesFallback(string captured, string[] matchModes) =>
            !string.IsNullOrEmpty(captured) && !matchModes.Contains(captured) ? new[] { captured } : Array.Empty<string>();
    }
}
