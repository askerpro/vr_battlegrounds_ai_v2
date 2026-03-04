using UnityEngine;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Абстрактный базовый класс игрового режима.
    /// Новые режимы наследуют этот класс и переопределяют
    /// OnRoundEnd, CanRespawn, CheckWinCondition.
    /// </summary>
    public abstract class GameMode : MonoBehaviour
    {
        /// <summary>Вызывается при завершении раунда.</summary>
        public abstract void OnRoundEnd();

        /// <summary>Может ли игрок возродиться в текущем режиме.</summary>
        public abstract bool CanRespawn();

        /// <summary>Проверяет условие победы и возвращает победившую команду (или None).</summary>
        public abstract Team CheckWinCondition();
    }
}