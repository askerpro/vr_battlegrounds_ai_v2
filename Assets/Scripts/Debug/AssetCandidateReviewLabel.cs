using UnityEngine;

namespace VrBattlegrounds.DebugTools
{
    /// <summary>Данные подписи стенда. Отрисовка находится в Editor, здесь нет игровой логики.</summary>
    public sealed class AssetCandidateReviewLabel : MonoBehaviour
    {
        [TextArea] public string Caption;
        public Color Color = Color.white;
    }
}
