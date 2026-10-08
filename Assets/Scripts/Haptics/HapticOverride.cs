using UltimateXR.Haptics;
using UnityEngine;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Свои клипы вибрации для конкретного якоря или предмета вместо роли из <see cref="HapticRoles" />. Ставится на объект
    /// якоря (<c>UxrGrabbableObjectAnchor</c>) — для готовности — или на корень предмета (<c>UxrGrabbableObject</c>) — для
    /// хвата, укладки и отпускания. Клип без формы — «как у роли». Используется <see cref="InteractionHaptics" />; своей логики
    /// нет — это данные, а не ещё один скрипт под сценарий.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HapticOverride : MonoBehaviour
    {
        [Tooltip("Готовность этого якоря: положить в него или взять из него.")]
        [SerializeField] private UxrHapticClip _anchorReady = new UxrHapticClip();
        [Tooltip("Этот предмет взят своей рукой.")]
        [SerializeField] private UxrHapticClip _grab = new UxrHapticClip();
        [Tooltip("Этот предмет уложен в якорь своей рукой.")]
        [SerializeField] private UxrHapticClip _place = new UxrHapticClip();
        [Tooltip("Этот предмет отпущен своей рукой (не в якорь).")]
        [SerializeField] private UxrHapticClip _release = new UxrHapticClip();

        public UxrHapticClip AnchorReady => _anchorReady;
        public UxrHapticClip Grab => _grab;
        public UxrHapticClip Place => _place;
        public UxrHapticClip Release => _release;

        /// <summary>Клип <paramref name="own" />, если у него есть форма, иначе <paramref name="role" />.</summary>
        public static UxrHapticClip Pick(UxrHapticClip own, UxrHapticClip role) => own != null && own.HasWaveform ? own : role;
    }
}
