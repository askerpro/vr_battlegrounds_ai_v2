using UnityEngine;
using VrBattlegrounds;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Абстрактный базовый класс игрового режима.
    /// Новые режимы наследуют этот класс и переопределяют
    /// OnRoundEnd, CanRespawn, CheckWinCondition.
    ///
    /// Список команд задаётся в Inspector через поле <see cref="teams"/>.
    /// Это позволяет одному режиму работать с любым набором команд из TeamRegistry.
    /// </summary>
    public abstract class GameMode : MonoBehaviour
    {
        [Header("Команды")]
        [Tooltip("Команды, участвующие в этом режиме. Назначить TeamData assets из TeamRegistry.")]
        [SerializeField] protected TeamData[] teams = new TeamData[0];

        /// <summary>Команды, участвующие в этом режиме (только чтение).</summary>
        public TeamData[] Teams => teams;

        /// <summary>Вызывается при завершении раунда.</summary>
        public abstract void OnRoundEnd();

        /// <summary>Может ли игрок возродиться в текущем режиме.</summary>
        public abstract bool CanRespawn();

        /// <summary>
        /// Проверяет условие победы.
        /// Возвращает победившую TeamData, или null если раунд продолжается / ничья.
        /// </summary>
        public abstract TeamData CheckWinCondition();
    }
}