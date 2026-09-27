using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Правило режима: режим начинается с чистого пола. Когда экземпляр режима появляется
    /// на машине, всё ничьё (оружие и магазины, которые никто не держит и которые не стоят
    /// в кобуре, слоте или кармане — <see cref="LooseItems"/>) убирается.
    ///
    /// <para>
    /// Зачем. Режим на карте меняется на месте (разминка → матч → разминка), и мусор
    /// разминки не должен дожить до первого раунда, а мусор матча — до разминки.
    /// Уборщики самих режимов (<see cref="LooseItemSweeper"/>, <see cref="RoundCleanup"/>)
    /// уходят вместе со своим экземпляром.
    /// </para>
    ///
    /// <para>
    /// Лежит на префабе каждого режима и срабатывает на каждой машине, где режим
    /// заспавнился: сетевые предметы убирает сервер, магазины без своего <c>netId</c> —
    /// каждая машина у себя (тот же разбор, что у <see cref="RoundCleanup"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ModeStartCleanup : MonoBehaviour
    {
        private void OnEnable()
        {
            int removed = LooseItems.RemoveAll();
            if (removed > 0)
                GameLog.Match.Info($"[ModeStartCleanup] Новый режим — с пола убрано предметов: {removed}.", this);
        }
    }
}
