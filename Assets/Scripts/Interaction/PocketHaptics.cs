using UltimateXR.Avatar;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Устаревший компонент без поведения. Вибрацию готовности карманов теперь даёт общая
    /// <see cref="VrBattlegrounds.Haptics.InteractionHaptics" /> по ролям якорей (<c>tasks/haptics-system</c>). Компонент
    /// остаётся на префабах аватаров только до этапа <c>pocket-removal</c>: снятие с префабов и правка
    /// <c>AvatarLoadoutTests</c> согласуются с задачами аватаров. Новый код его не использует.
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    public sealed class PocketHaptics : MonoBehaviour
    {
    }
}
