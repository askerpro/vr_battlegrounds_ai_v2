using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Правило режима Elimination: к обратному отсчёту раунда карман магазинов каждого
    /// игрока собирается заново — по <c>MaxMagazineCount</c> к каждому оружию, которое
    /// игрок взял в фазе закупки.
    ///
    /// <para>
    /// Лежит на префабе режима, а не в <see cref="PlayerLoadoutManager"/>: менеджер
    /// выдаёт магазины и не знает, когда это нужно. Режим решает это сам, так же как
    /// разминка решает своё (<see cref="WarmupMagazineSupply"/> на префабе <see cref="WarmupMode"/>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundMagazineRefill : MonoBehaviour
    {
        private void OnEnable()
        {
            EliminationMode.OnRoundPhaseChangedServer += HandleRoundPhaseChanged;
        }

        private void OnDisable()
        {
            EliminationMode.OnRoundPhaseChangedServer -= HandleRoundPhaseChanged;
        }

        private static void HandleRoundPhaseChanged(RoundPhase newState)
        {
            if (newState != RoundPhase.Countdown) return;

            foreach (PlayerLoadoutManager loadout in PlayerLoadoutManager.ServerInstances)
            {
                if (loadout == null) continue;

                loadout.ServerClearMagazines();
                loadout.ServerEnsureMagazines(0);
            }
        }
    }
}
