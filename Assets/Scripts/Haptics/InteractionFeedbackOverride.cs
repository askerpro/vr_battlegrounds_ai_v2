using UnityEngine;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Свой отклик конкретного якоря или предмета вместо значения из <see cref="InteractionFeedbackConfig" /> — самое частное
    /// побеждает: предмет → якорь → конфиг. Ставится на объект якоря (<c>UxrGrabbableObjectAnchor</c>) или на корень предмета
    /// (<c>UxrGrabbableObject</c>). Пустое поле — «как в конфиге». Данные, своей логики нет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionFeedbackOverride : MonoBehaviour
    {
        [Tooltip("Готовность: якорь готов принять/отдать, предмет в досягаемости пустой руки.")]
        [SerializeField] private GameObject _ready;
        [Tooltip("Разово: предмет взят.")]
        [SerializeField] private GameObject _grab;
        [Tooltip("Разово: предмет уложен в якорь.")]
        [SerializeField] private GameObject _place;
        [Tooltip("Разово: предмет отпущен не в якорь.")]
        [SerializeField] private GameObject _release;

        public GameObject Ready => _ready;
        public GameObject Grab => _grab;
        public GameObject Place => _place;
        public GameObject Release => _release;
    }
}
