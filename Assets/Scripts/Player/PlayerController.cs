using UnityEngine;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Контроллер игрока. Управляет командой и состоянием (жив/мёртв).
    /// Использует физическое перемещение по арене (телепортация не используется).
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public Team Team { get; set; } = Team.None;
        public bool IsAlive { get; private set; } = true;

        public void Die()
        {
            IsAlive = false;
            // TODO: деактивация, уведомление RoundManager
        }

        public void Respawn(Transform spawnPoint)
        {
            IsAlive = true;
            transform.position = spawnPoint.position;
            transform.rotation = spawnPoint.rotation;
        }
    }
}