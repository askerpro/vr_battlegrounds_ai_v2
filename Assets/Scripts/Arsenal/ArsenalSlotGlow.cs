using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Светящаяся подложка слота за оружием: её цвет показывает состояние слота (задача arsenal-slot-lighting,
    ///     решение пользователя 2026-10-22). Заменяет realtime-лампу слота: свет на оружие даёт запечённый прожектор
    ///     сцены через пробу света в центре слота (инструмент «Подсветка слотов арсенала»), подложка только светится сама.
    ///     Цвет — блок свойств рендера (<c>_BaseColor</c> материала Unlit), материал общий для всех слотов.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class ArsenalSlotGlow : MonoBehaviour
    {
        [Tooltip("Множитель яркости цвета состояния.")]
        [SerializeField, Min(0f)] private float _strength = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        /// <summary>Подложка горит цветом состояния.</summary>
        public void Show(Color color)
        {
            Renderer target = Target();
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, new Color(color.r * _strength, color.g * _strength, color.b * _strength, 1f));
            target.SetPropertyBlock(_block);
            target.enabled = true;
        }

        /// <summary>Подложка погашена.</summary>
        public void Hide() => Target().enabled = false;

        public bool IsLit => Target().enabled;

        private Renderer Target() => _renderer != null ? _renderer : _renderer = GetComponent<Renderer>();
    }
}
