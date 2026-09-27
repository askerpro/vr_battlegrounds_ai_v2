using Mirror;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Правило лобби-режима: бесконечный карман. Сервер держит в кармане каждого игрока
    /// по магазину к каждому его оружию (в руках и в кобурах). Достал — через интервал
    /// лежит новый.
    ///
    /// <para>
    /// Лежит на префабе <see cref="LobbyMode"/>, как <see cref="RoundMagazineRefill"/> —
    /// на префабе Elimination: <see cref="PlayerLoadoutManager"/> выдаёт магазины и не знает,
    /// когда это нужно (это проверяет <c>MagazineRefillPlannerTests</c>). Правило решает
    /// только «сколько» и «когда».
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyMagazineSupply : MonoBehaviour
    {
        [Tooltip("Как часто сервер досыпает магазины в карманы игроков, секунды.")]
        [Min(0.05f)]
        [SerializeField] private float _refillInterval = 0.5f;

        private float _timer;

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Один шаг правила. Отдельно от <c>Update</c> — для EditMode-теста.</summary>
        internal void Tick(float deltaTime)
        {
            if (!NetworkServer.active) return;

            _timer += deltaTime;
            if (_timer < _refillInterval) return;
            _timer = 0f;

            foreach (PlayerLoadoutManager loadout in PlayerLoadoutManager.ServerInstances)
            {
                if (loadout != null)
                    loadout.ServerEnsureMagazines(1);
            }
        }
    }
}
