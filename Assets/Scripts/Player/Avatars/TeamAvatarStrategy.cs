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

        public override GameObject GetPrefab(PlayerSession session, GameObject globalFallback)
        {
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
