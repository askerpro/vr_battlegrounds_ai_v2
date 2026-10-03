using UnityEngine;

namespace VrBattlegrounds.LevelDesign
{
    /// <summary>Подтверждение защиты конкретного маркера на карте, отдельно от prefab арены.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalObstacleProtection : MonoBehaviour
    {
        public string arenaId, markerId, confirmedFingerprint;
        public Bounds protectedVolume;
        public GameObject chosenReplacement;
        [Tooltip("Устойчивый ID выбранной формы общего реестра. chosenReplacement сохранён для миграции прежнего выбора.")]
        public string chosenDefinitionId;
    }
}
