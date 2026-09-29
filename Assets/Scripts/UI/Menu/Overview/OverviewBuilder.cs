using System.Collections.Generic;

namespace VrBattlegrounds.UI.Menu.Overview
{
    /// <summary>
    /// Стратегия экрана «Обзор»: вносит в снимок свои секции (и поля шапки) для контекстов,
    /// где она уместна. Новый режим добавляет свою стратегию в <see cref="OverviewBuilder.Default"/>,
    /// а не ветку в экране.
    /// </summary>
    public interface IOverviewSectionProvider
    {
        bool Applies(OverviewContext context, OverviewInput input);
        void Contribute(OverviewContext context, OverviewInput input, OverviewSnapshot snapshot);
    }

    /// <summary>
    /// Собирает <see cref="OverviewSnapshot"/> из <see cref="OverviewInput"/>: определяет контекст
    /// (<see cref="OverviewContextResolver"/>) и по порядку спрашивает стратегии.
    /// </summary>
    public sealed class OverviewBuilder
    {
        private readonly List<IOverviewSectionProvider> _providers;

        public OverviewBuilder(IEnumerable<IOverviewSectionProvider> providers)
        {
            _providers = new List<IOverviewSectionProvider>(providers);
        }

        public IReadOnlyList<IOverviewSectionProvider> Providers => _providers;

        /// <summary>
        /// Стратегии игры по порядку секций на экране. Новый режим: своя стратегия
        /// (<c>Applies</c> — контекст <see cref="OverviewContext.Live"/> и данные своего режима)
        /// перед <see cref="SeriesOverview"/>, и сужение <see cref="GenericLiveOverview"/>.
        /// </summary>
        public static OverviewBuilder Default => new OverviewBuilder(new IOverviewSectionProvider[]
        {
            new OfflineOverview(),
            new LobbyOverview(),
            new MapHeaderOverview(),
            new WarmupOverview(),
            new EliminationOverview(),
            new RespawnOverview(),
            new GenericLiveOverview(),
            new PausedOverview(),
            new MapFinishedOverview(),
            new SeriesOverview()
        });

        public OverviewSnapshot Build(OverviewInput input)
        {
            input = input ?? new OverviewInput();
            var snapshot = new OverviewSnapshot { Context = OverviewContextResolver.Resolve(input) };

            foreach (IOverviewSectionProvider provider in _providers)
                if (provider.Applies(snapshot.Context, input))
                    provider.Contribute(snapshot.Context, input, snapshot);

            return snapshot;
        }
    }
}
