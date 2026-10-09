using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Корень префаба вылета (патрон или гильза, этап ejection): только визуал — физика без сети, не хватается (нет
    /// граббабла), исчезает по таймеру пула <see cref="WeaponEjectaPool"/>. Сам он лишь проигрывает звук первого касания
    /// своим источником звука (без <c>PlayClipAtPoint</c>: на Quest каждый вызов создаёт объект).
    /// Позицию, скорость и время жизни задаёт пул — единственный владелец экземпляров.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class WeaponEjecta : MonoBehaviour
    {
        [Tooltip("Звуки первого касания (случайный). Пусто — без звука.")]
        [SerializeField] private AudioClip[] _impactClips = new AudioClip[0];

        [Tooltip("Громкость звука касания.")]
        [Range(0f, 1f)]
        [SerializeField] private float _impactVolume = 0.5f;

        [Tooltip("Скорость касания, м/с, ниже которой звука нет.")]
        [SerializeField] private float _minImpactSpeed = 0.4f;

        [Tooltip("Источник звука касания (3D) на этом же объекте.")]
        [SerializeField] private AudioSource _audio;

        private bool _sounded;

        public AudioClip[] ImpactClips => _impactClips;
        public AudioSource Audio => _audio;

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
            if (_audio == null || _impactClips == null || _impactClips.Length == 0) return;
            if (collision.relativeVelocity.sqrMagnitude < _minImpactSpeed * _minImpactSpeed) return;
            AudioClip clip = _impactClips[Random.Range(0, _impactClips.Length)];
            if (clip != null) _audio.PlayOneShot(clip, _impactVolume);
        }
    }
}
