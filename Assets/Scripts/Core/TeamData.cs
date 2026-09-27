using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds
{
    /// <summary>
    /// Данные команды игроков — название, иконка, цвет.
    /// Создать: ПКМ в Project -> Create -> VrBattlegrounds -> Team Data
    ///
    /// По сети передаётся только <see cref="teamIndex"/> (int через SyncVar).
    /// TeamData получается из <see cref="TeamRegistry"/> по индексу на всех клиентах.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TeamData",
        menuName = "VR Battlegrounds/Team Data")]
    public class TeamData : ScriptableObject
    {
        [Tooltip("Уникальный числовой идентификатор команды. Используется в SyncVar и для поиска в TeamRegistry.\n0 = зарезервировано для 'нет команды'.")]
        [Min(1)]
        public int teamIndex;

        [Tooltip("Отображаемое название команды в UI.")]
        public string displayName;

        [Tooltip("Иконка команды для UI.")]
        public Sprite icon;

        [Tooltip("Цвет команды для выделения игроков и UI.")]
        public Color color = Color.white;

        [Tooltip("Список скинов (аватаров), доступных для этой команды")]
        public List<Core.AvatarData> avatars = new List<Core.AvatarData>();

        /// <summary>
        /// Возвращает данные скина по индексу в списке команды.
        /// Обеспечивает безопасный доступ к массиву avatars.
        /// </summary>
        public Core.AvatarData GetAvatar(int listIndex)
        {
            if (listIndex >= 0 && listIndex < avatars.Count)
            {
                return avatars[listIndex];
            }
            else if (avatars.Count > 0)
            {
                // Fallback на первый скин
                return avatars[0];
            }
            return null;
        }

        /// <summary>
        /// Индекс скина в списке команды или 0 (первый скин), если его там нет.
        /// Нужен при смене команды: игрок, перешедший из лобби в матч и обратно,
        /// сохраняет свой скин, если он есть и в новой команде.
        /// </summary>
        public int IndexOfAvatar(Core.AvatarData avatar)
        {
            if (avatar == null) return 0;
            int index = avatars.IndexOf(avatar);
            return index >= 0 ? index : 0;
        }

        /// <summary>
        /// Возвращает префаб скина (для обратной совместимости).
        /// </summary>
        public GameObject GetAvatarPrefab(int listIndex)
        {
            Core.AvatarData avatar = GetAvatar(listIndex);
            return avatar != null ? avatar.prefab : null;
        }

        public override string ToString() => displayName;
    }
}