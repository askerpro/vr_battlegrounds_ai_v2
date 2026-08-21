using UnityEngine;

namespace VrBattlegrounds.Core
{
    /// <summary>
    /// Реестр всех скинов игры. Хранится в Resources/SkinRegistry.asset —
    /// загружается автоматически без ссылок в сцене.
    ///
    /// Создать: ПКМ в Project -> Create -> VrBattlegrounds -> Skin Registry
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarRegistry", menuName = "VR Battlegrounds/AvatarRegistry")]
    public class AvatarRegistry : ScriptableObject
    {
        private static AvatarRegistry s_instance;

        /// <summary>
        /// Глобальный экземпляр реестра.
        /// Загружается из Resources/SkinRegistry.asset.
        /// </summary>
        public static AvatarRegistry Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = Resources.Load<AvatarRegistry>(nameof(AvatarRegistry));

                if (s_instance == null)
                    GameLog.Error("[AvatarRegistry] Файл Resources/AvatarRegistry.asset не найден. Создайте его через Create -> VrBattlegrounds -> AvatarRegistry.");

                return s_instance;
            }
        }

        [Tooltip("Все добавленные скины.")]
        public AvatarData[] avatars = new AvatarData[0];

        /// <summary>Возвращает AvatarData по skinIndex. Null если не найдено.</summary>
        public AvatarData GetByIndex(int index)
        {
            if(index >= 0 && index < avatars.Length)
                return avatars[index];

            GameLog.Player.Warning($"[AvatarRegistry] Аватар с index={index} не найден.");
            return null;
        }
    }
}
