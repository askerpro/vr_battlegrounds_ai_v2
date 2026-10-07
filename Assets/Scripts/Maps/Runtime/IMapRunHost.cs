using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>
    /// Запуск карты, которому принадлежит <see cref="MapReferee"/>. Судья без запуска режим не начинает:
    /// неуправляемого пути нет. В игре это <see cref="MapBootstrap"/>; EditMode-тесты подставляют свой
    /// запуск поверх настоящего <see cref="MapRunAuthority"/>, чтобы проверять тот же единственный писатель.
    /// </summary>
    internal interface IMapRunHost
    {
        /// <summary>Режим матча, согласованный один раз при загрузке; пустой — матча на карте нет.</summary>
        MapMatchIntent MatchIntent { get; }

        /// <summary>Сервер: первый режим закоммичен, gameplay карты открыт.</summary>
        bool IsServerReady { get; }

        /// <summary>Единственная публикация режима: descriptor получает состояние карты и режим.</summary>
        bool CommitMode(MapState state, GameMode mode);
    }
}
