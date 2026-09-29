using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Правило режима Elimination: новый раунд начинается с чистого пола. На фазе
    /// <c>Setup</c> убирается всё ничьё — оружие и магазины, которые никто не держит
    /// и которые не стоят ни в кобуре, ни в слоте, ни в кармане (<see cref="LooseItems"/>).
    ///
    /// <para>
    /// Во время раунда оружие на полу не трогается: ствол убитого можно подобрать.
    /// Магазины на полу уходят раньше — это <see cref="LooseItemSweeper"/> на том же префабе.
    /// </para>
    ///
    /// <para>
    /// Слушает локальный канал фазы: он приходит на каждую машину. Сетевые предметы
    /// уберёт сервер, магазины без своего <c>netId</c> каждая машина уберёт у себя.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundCleanup : MonoBehaviour
    {
        private void OnEnable()
        {
            EliminationMode.OnRoundPhaseChangedLocal += HandleRoundPhaseChanged;
        }

        private void OnDisable()
        {
            EliminationMode.OnRoundPhaseChangedLocal -= HandleRoundPhaseChanged;
        }

        private static void HandleRoundPhaseChanged(RoundPhase newState)
        {
            if (newState == RoundPhase.Setup)
                LooseItems.RemoveAll();
        }
    }
}
