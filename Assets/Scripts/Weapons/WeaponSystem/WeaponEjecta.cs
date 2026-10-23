using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Корень префаба вылета (патрон или гильза, этап ejection): только визуал — физика без сети, не хватается (нет
    /// граббабла), исчезает по таймеру пула <see cref="WeaponEjectaPool"/>. Сам он лишь проигрывает звук первого касания
    /// своим источником звука (без <c>PlayClipAtPoint</c>: на Quest каждый вызов создаёт объект).
    /// Позицию, скорость и время жизни задаёт пул — единственный владелец экземпляров.
    ///
    /// <para>
    /// Клип касания: свои клипы префаба (<see cref="ImpactClips"/>, оверрайд) → звуки категории ствола, который выпустил
    /// вылет (<see cref="WeaponFeedbackDefaults.EjectaImpacts"/>; ссылку на массив ассета пул ставит при выпуске) → тишина.
    /// Префаб вылета общий для стволов разных категорий (гильза 9 мм — у пистолетов и MKR9), поэтому дефолт — от ствола, а не
    /// от префаба. Звук локальный: вылет и так не сетевой.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class WeaponEjecta : MonoBehaviour
    {
        [Tooltip("Свои звуки первого касания (случайный). Пусто — звуки категории ствола (WeaponFeedbackDefaults), нет и их — без звука.")]
        [SerializeField] private AudioClip[] _impactClips = new AudioClip[0];

        [Tooltip("Громкость звука касания.")]
        [Range(0f, 1f)]
        [SerializeField] private float _impactVolume = 0.5f;

        [Tooltip("Скорость касания, м/с, ниже которой звука нет.")]
        [SerializeField] private float _minImpactSpeed = 0.4f;

        [Tooltip("Источник звука касания (3D) на этом же объекте.")]
        [SerializeField] private AudioSource _audio;

        private bool _sounded;

        /// <summary>Свои клипы касания префаба (без дефолта категории).</summary>
        public AudioClip[] ImpactClips => _impactClips;
        public AudioSource Audio => _audio;

        /// <summary>Звуки касания категории ствола этого вылета: ссылка на массив ассета, ставит пул при выпуске; null — нет.</summary>
        internal AudioClip[] DefaultImpactClips { get; set; }

        internal Rigidbody Body { get; private set; }
        internal Collider Shape { get; private set; }
        internal GameObject Source { get; set; }
        internal float ExpiresAt { get; set; }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Shape = GetComponentInChildren<Collider>(true);
        }

        /// <summary>Новый вылет того же экземпляра: звук касания снова разрешён.</summary>
        internal void Rearm() => _sounded = false;

        private void OnCollisionEnter(Collision collision)
        {
            if (_sounded) return;
            _sounded = true;
            AudioClip[] clips = _impactClips != null && _impactClips.Length > 0 ? _impactClips : DefaultImpactClips;
            if (_audio == null || clips == null || clips.Length == 0) return;
            if (collision.relativeVelocity.sqrMagnitude < _minImpactSpeed * _minImpactSpeed) return;
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null) _audio.PlayOneShot(clip, _impactVolume);
        }
    }
}
