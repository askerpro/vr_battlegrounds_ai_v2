using System;
using Mirror;
using UnityEngine;
using VrBattlegrounds;

namespace VrBattlegrounds.GameModes
{
    /// <summary>
    /// Абстрактный базовый класс игрового режима.
    ///
    /// Каждый режим самостоятельно управляет своей внутренней структурой —
    /// сетами, раундами, таймерами или любой другой логикой.
    ///
    /// MatchManager инстанцирует префаб режима через NetworkServer.Spawn,
    /// вызывает Initialize() → StartMatch(), и ждёт события MatchEnded.
    /// При StopMatch() вызывает StopMatch() на режиме и уничтожает инстанс.
    /// </summary>
    public abstract class GameMode : NetworkBehaviour
    {
        /// <summary>
        /// Срабатывает когда режим определил победителя матча.
        /// Null = ничья. Подписывается MatchManager.
        /// </summary>
        public event Action<TeamData> MatchEnded;

        // Команды передаются через Initialize() — не хранятся в Inspector
        private TeamData[] _teams = new TeamData[0];

        /// <summary>Команды, участвующие в матче (только чтение).</summary>
        public TeamData[] Teams => _teams;

        /// <summary>
        /// Инициализирует режим командами перед стартом.
        /// Вызывается MatchManager-ом сразу после NetworkServer.Spawn.
        /// </summary>
        public void Initialize(TeamData[] teams)
        {
            _teams = teams ?? new TeamData[0];
        }

        /// <summary>Запускает матч. Вызывается MatchManager-ом на сервере.</summary>
        public abstract void StartMatch();

        /// <summary>
        /// Принудительно останавливает матч без определения победителя.
        /// Вызывается MatchManager-ом при StopMatch() администратора.
        /// </summary>
        public abstract void StopMatch();

        /// <summary>
        /// Возвращает текущий счёт команды (фраги, раунды, сеты — зависит от режима).
        /// Используется UI для отображения счёта.
        /// </summary>
        public abstract int GetScore(TeamData team);

        /// <summary>Может ли игрок возродиться в текущем режиме.</summary>
        public abstract bool CanRespawn();

        /// <summary>
        /// Вызвать из конкретного режима когда определён победитель матча.
        /// </summary>
        protected void RaiseMatchEnded(TeamData winner) => MatchEnded?.Invoke(winner);
    }
}

