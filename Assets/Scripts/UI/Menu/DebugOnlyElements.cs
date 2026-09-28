using UnityEngine;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Показывает перечисленные элементы экрана только в режиме отладки (<see cref="DebugMode"/>) —
    /// например, кнопку «Отладка» на главном экране. Пара к <see cref="AdminOnlyElements"/>
    /// и лежит так же на самом экране: выключенный объект событие уже не обработает.
    /// </summary>
    public class DebugOnlyElements : MonoBehaviour
    {
        [SerializeField] private GameObject[] _elements = new GameObject[0];

        private void OnEnable()
        {
            DebugMode.Changed += Apply;
            Apply(DebugMode.Enabled);
        }

        private void OnDisable() => DebugMode.Changed -= Apply;

        private void Apply(bool enabled)
        {
            foreach (GameObject element in _elements)
            {
                if (element != null && element.activeSelf != enabled) element.SetActive(enabled);
            }
        }
    }
}

