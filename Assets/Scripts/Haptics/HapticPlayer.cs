using System;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEngine;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Проигрыватель вибрации на GO отклика — как <c>AudioSource</c> для звука. Форма (<see cref="UxrHapticWaveform" />) —
    /// общий ассет, правила воспроизведения (сила, приоритет, пауза повтора, кулдаун) — клип <see cref="UxrHapticClip" />
    /// этого компонента. Руку задаёт исполнитель отклика (<see cref="InteractionFeedback" />) до включения GO; без руки —
    /// тишина (вибрация всегда на конкретной руке своего игрока). Режим: пока GO активен — форма повторяется с паузой клипа;
    /// один раз — при включении GO.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HapticPlayer : MonoBehaviour
    {
        public enum PlayMode
        {
            /// <summary>Пока GO активен: форма повторяется с паузой клипа (готовность кармана, предмет в досягаемости).</summary>
            WhileActive,
            /// <summary>Один раз при включении GO (щелчок «нашёл», событие).</summary>
            OnceOnEnable,
        }

        [Tooltip("Форма и правила воспроизведения.")]
        [SerializeField] private UxrHapticClip _clip = new UxrHapticClip();
        [SerializeField] private PlayMode _mode = PlayMode.WhileActive;

        private bool _bound;
        private UxrHandSide _side;
        private HapticHandRole _role;
        private HapticHandle _handle;

        public UxrHapticClip Clip => _clip;
        public PlayMode Mode => _mode;

        /// <summary>
        /// Правка префаба отклика в инспекторе (редактор, в том числе Play Mode): исполнитель пересоздаёт активные экземпляры,
        /// чтобы новые правила зазвучали сразу.
        /// </summary>
        public static event Action<HapticPlayer> AssetEdited;

        /// <summary>
        /// Рука, на которой играть, и её роль: Secondary (предмет уже в другой руке) — сила × <see cref="UxrHapticClip.SecondaryHandGain" />.
        /// Вызывать до включения GO.
        /// </summary>
        public void Bind(UxrHandSide side, HapticHandRole role = HapticHandRole.Primary)
        {
            _bound = true;
            _side = side;
            _role = role;
        }

        public void Unbind() => _bound = false;

        private void OnEnable()
        {
            if (!_bound) return;
            if (_mode == PlayMode.WhileActive) _handle = HapticService.Begin(_clip, _side, this, 1f, _role);
            else HapticService.Play(_clip, _side, 1f, _role);
        }

        private void OnDisable()
        {
            _handle.End();
            _handle = default;
        }

        private void OnValidate()
        {
            // Только ассет префаба: у экземпляра на сцене сцена валидна.
            if (!gameObject.scene.IsValid()) AssetEdited?.Invoke(this);
        }
    }
}
