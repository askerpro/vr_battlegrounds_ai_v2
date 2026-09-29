using UnityEngine;
using VrBattlegrounds.Network;

using VrBattlegrounds.Core;
namespace VrBattlegrounds.Player.Avatars
{
    [CreateAssetMenu(fileName = "TeamAvatarStrategy", menuName = "VR Battlegrounds/Core/Strategies/Team Avatar")]
    public class TeamAvatarStrategy : AvatarSpawnStrategy
    {
        [Tooltip("Префаб, который будет выдан, если у команды нет скина или команда не выбрана.")]
        [SerializeField] private GameObject fallbackPrefab;

        [Tooltip("Аватар выбывшего — призрак (T-35). Тот же, что AvatarRegistry.ghost (GhostAvatarTests).")]
        [SerializeField] private GameObject ghostPrefab;

        /// <summary>Аватар выбывшего игрока — призрак (T-35).</summary>
        public GameObject GhostPrefab => ghostPrefab;

        /// <summary>
        /// Префаб тела по состоянию игрока: выбывший — призрак (T-35), живой — скин своей команды,
        /// без команды — запасной аватар. Одно правило для спавна, смены скина и смены тела
        /// на смерти и возрождении (<c>AvatarManager.ServerReconcileBody</c>).
        /// </summary>
        public override GameObject GetPrefab(PlayerSession session, GameObject globalFallback)
        {
            if (session.IsEliminated && ghostPrefab != null) return ghostPrefab;

            TeamData teamData = TeamRegistry.Instance.GetByIndex(session.TeamIndex);
            if (teamData != null)
            {
                GameObject avatarPrefab = teamData.GetAvatarPrefab(session.AvatarIndex);
                if (avatarPrefab != null)
                {
                    GameLog.Player.Info($"[AvatarManager/TeamAvatarStrategy] Выбран скин '{avatarPrefab.name}' для команды '{teamData.Name}' (teamId: {session.TeamIndex}, avatarId: {session.AvatarIndex})");
                    return avatarPrefab;
                }
                else
                {
                    GameLog.Player.Warning($"[AvatarManager/TeamAvatarStrategy] Не удалось найти скин с ID {session.AvatarIndex} для команды {session.TeamIndex}!");
                }
            }
            else
            {
                GameLog.Player.Warning($"[AvatarManager/TeamAvatarStrategy] Команда с ID {session.TeamIndex} не найдена в TeamRegistry!");
            }

            return fallbackPrefab != null ? fallbackPrefab : globalFallback;
        }
    }
}
