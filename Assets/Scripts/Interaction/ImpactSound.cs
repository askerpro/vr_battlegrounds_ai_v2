using System;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Звук удара предмета о мир: брошенное оружие и магазины падают со звуком (решение пользователя). Удар слабее
    /// <see cref="MinSpeed"/> молчит (предмет улёгся, катится), громкость растёт со скоростью до <see cref="FullSpeed"/>,
    /// частые удары одного падения гасятся паузой <see cref="Cooldown"/>. Предмет в руке не звучит — рука водит им по
    /// столу и стенам.
    ///
    /// <para>
    /// Звук местный: играет там, где физика предмета посчитала удар. Сетевой синхронизации нет — это не действие
    /// игрока, а следствие физики (правило «один автор» касается синхронизируемых методов).
    /// </para>
    /// Проверка — <c>ImpactSoundTests</c>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ImpactSound : MonoBehaviour
    {
        [Tooltip("Варианты удара — чередуются случайно, чтобы падения не звучали одинаково.")]
        [SerializeField] private AudioClip[] _clips = new AudioClip[0];
        [Tooltip("Источник (объёмный, на предмете). Play On Awake выключен.")]
        [SerializeField] private AudioSource _source;
        [SerializeField, Min(0f)] private float _minSpeed = 1.2f;
        [SerializeField, Min(0.01f)] private float _fullSpeed = 5f;
        [SerializeField, Min(0f)] private float _cooldown = 0.2f;
        [SerializeField, Range(0f, 1f)] private float _maxVolume = 0.9f;

        /// <summary>Сыгран удар: предмет, клип, громкость. Для тестов и отладки.</summary>
        public static event Action<ImpactSound, AudioClip, float> Played;

        public AudioClip[] Clips => _clips;
        public AudioSource Source => _source;
        public float MinSpeed => _minSpeed;
        public float FullSpeed => _fullSpeed;
        public float Cooldown => _cooldown;

        private UxrGrabbableObject _grabbable;
        private float _lastPlayed = float.NegativeInfinity;

        private void Awake() => _grabbable = GetComponent<UxrGrabbableObject>();

        private void OnCollisionEnter(Collision collision) => OnImpact(collision.relativeVelocity.magnitude, Time.time);

        /// <summary>Удар со скоростью <paramref name="speed"/> в момент <paramref name="time"/>.</summary>
        public void OnImpact(float speed, float time)
        {
            bool held = _grabbable != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_grabbable);
            if (held || !ShouldPlay(speed, time - _lastPlayed, _minSpeed, _cooldown)) return;
            if (_source == null || _clips == null || _clips.Length == 0) return;

            AudioClip clip = _clips[UnityEngine.Random.Range(0, _clips.Length)];
            if (clip == null) return;

            float volume = Volume(speed, _minSpeed, _fullSpeed, _maxVolume);
            _lastPlayed = time;
            _source.PlayOneShot(clip, volume);
            Played?.Invoke(this, clip, volume);
        }

        public static bool ShouldPlay(float speed, float sinceLast, float minSpeed, float cooldown) =>
            speed >= minSpeed && sinceLast >= cooldown;

        /// <summary>Громкость от скорости: у порога — треть, к <paramref name="fullSpeed"/> — полная.</summary>
        public static float Volume(float speed, float minSpeed, float fullSpeed, float maxVolume)
        {
            float t = Mathf.InverseLerp(minSpeed, Mathf.Max(minSpeed + 0.01f, fullSpeed), speed);
            return maxVolume * Mathf.Lerp(0.35f, 1f, t);
        }
    }
}
