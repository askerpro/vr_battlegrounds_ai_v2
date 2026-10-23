using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Скрытая LED-лента по бокам слота: тонкая тёплая полоска у бортиков показывает габарит слота, её цвет —
    ///     состояние слота (задача arsenal-slot-lighting, решения пользователя 2026-10-23). Заменяет realtime-лампу слота:
    ///     свет на оружие — запечённый (инструмент «Подсветка слотов арсенала»), лента только светится сама.
    ///     Квад во всю доску с текстурой боковых полос; цвет — блок свойств рендера (<c>_BaseColor</c> материала Unlit),
    ///     материал общий для всех слотов.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class ArsenalSlotGlow : MonoBehaviour
    {
        [Tooltip("Множитель яркости цвета состояния.")]
        [SerializeField, Min(0f)] private float _strength = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer _renderer;
        private MaterialPropertyBlock _block;

        public float Strength => _strength;

        /// <summary>Лента горит цветом состояния.</summary>
        public void Show(Color color)
        {
            Renderer target = Target();
            Paint(target, color, _strength, _block ??= new MaterialPropertyBlock());
            target.enabled = true;
        }

        /// <summary>Лента погашена.</summary>
        public void Hide() => Target().enabled = false;

        public bool IsLit => Target().enabled;

        /// <summary>Цвет ленты на рендере: цвет состояния × яркость. Общий для слота и редакторского превью станций.</summary>
        public static void Paint(Renderer target, Color color, float strength, MaterialPropertyBlock block)
        {
            block.SetColor(BaseColorId, new Color(color.r * strength, color.g * strength, color.b * strength, 1f));
            target.SetPropertyBlock(block);
        }

        private Renderer Target() => _renderer != null ? _renderer : _renderer = GetComponent<Renderer>();
    }
}
