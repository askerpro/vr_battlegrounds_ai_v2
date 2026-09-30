using UnityEngine;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Точка телепорта режима отладки, поставленная на карту: стенд, ракурс, место замера. Попадает в кнопки
    /// «Телепорт» планшета (<see cref="DebugTeleportTargets"/>) рядом с зонами и арсеналом. Игрок встаёт в
    /// позицию объекта и смотрит по его <c>forward</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugTeleportPoint : MonoBehaviour
    {
        [Tooltip("Подпись кнопки на планшете. Пусто — имя объекта.")]
        [SerializeField] private string _label;

        public string Label
        {
            get => string.IsNullOrEmpty(_label) ? name : _label;
            set => _label = value;
        }
    }
}
