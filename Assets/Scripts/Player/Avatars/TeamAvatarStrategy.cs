using UnityEngine;
using VrBattlegrounds.Network;

using VrBattlegrounds.Core;
namespace VrBattlegrounds.Player.Avatars
{
    [CreateAssetMenu(fileName = "TeamAvatarStrategy", menuName = "VrBattlegrounds/Core/Strategies/Team Avatar")]
    public class TeamAvatarStrategy : AvatarSpawnStrategy
    {
        [Tooltip("Префаб, который будет выдан, если у команды нет скина или команда не выбрана.")]
        [SerializeField] private GameObject fallbackPrefab;

        public override GameObject GetPrefab(GamePlayerConnectMessage msg, GameObject globalFallback)
        {
            TeamData teamData = TeamRegistry.Instance.GetByIndex(msg.teamId);
            if (teamData != null)
            {
                GameObject avatarPrefab = teamData.GetAvatarPrefab(msg.avatarId);
                if (avatarPrefab != null)
                {
                    GameLog.Info(GameSettings.Instance.LogLevelPlayer, $"[AvatarManager/TeamAvatarStrategy] Выбран скин '{avatarPrefab.name}' для команды '{teamData.displayName}' (teamId: {msg.teamId}, avatarId: {msg.avatarId})");
                    return avatarPrefab;
                }
                else
                {
                    GameLog.Warning(GameSettings.Instance.LogLevelPlayer, $"[AvatarManager/TeamAvatarStrategy] Не удалось найти скин с ID {msg.avatarId} для команды {msg.teamId}!");
                }
            }
            else
            {
                GameLog.Warning(GameSettings.Instance.LogLevelPlayer, $"[AvatarManager/TeamAvatarStrategy] Команда с ID {msg.teamId} не найдена в TeamRegistry!");
            }

            return fallbackPrefab != null ? fallbackPrefab : globalFallback;
        }
    }
}
