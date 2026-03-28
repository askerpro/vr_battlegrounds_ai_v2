using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Данные скина (аватара) игрока — название, иконка, префаб.
    /// Создать: ПКМ в Project -> Create -> VrBattlegrounds -> AvatarData
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarData", menuName = "VrBattlegrounds/AvatarData")]
    public class AvatarData : ScriptableObject
    {
        [Tooltip("Отображаемое название скина в UI")]
        public string displayName;

        [Tooltip("Иконка/Превью скина для UI")]
        public Sprite icon;

        [Tooltip("Префаб физического VR-аватара для инстанцирования")]
        public GameObject prefab;
    }
}
