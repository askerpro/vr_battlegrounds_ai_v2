using UnityEngine;

namespace VrBattlegrounds.LobbyAudio
{
    /// <summary>Проигрывает один эпизод; выбор событий и паузы принадлежат директору.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class LobbyAmbientEmitter : MonoBehaviour
    {
        private AudioSource _source;
        private LobbyAmbienceProfile.Episode _episode;
        private Vector3 _start, _end;
        private float _elapsed, _duration;
        public bool IsPlaying => _source != null && _source.isPlaying;

        public void Play(LobbyAmbienceProfile.Episode episode, AudioClip clip, Vector3 start, Vector3 end)
        {
            Stop();
            if (episode == null || clip == null) return;
            _source = GetComponent<AudioSource>();
            _episode = episode;
            _start = start;
            _end = end;
            _duration = Mathf.Min(Mathf.Max(.1f, episode.duration), clip.length);
            _elapsed = 0;
            transform.position = start;
            _source.playOnAwake = false;
            _source.loop = false;
            _source.clip = clip;
            _source.spatialBlend = episode.mode == LobbyAmbientMode.TwoDimensional ? 0 : 1;
            _source.minDistance = Mathf.Max(.1f, episode.minDistance);
            _source.maxDistance = Mathf.Max(_source.minDistance + 1, episode.maxDistance);
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.dopplerLevel = 0;
            _source.priority = 220;
            _source.volume = episode.fade > 0 ? 0 : Mathf.Clamp01(episode.volume);
            _source.Play();
        }

        private void Update()
        {
            if (!IsPlaying || _episode == null) return;
            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            if (_episode.mode == LobbyAmbientMode.Path) transform.position = Vector3.Lerp(_start, _end, t);
            float fade = Mathf.Min(Mathf.Max(0, _episode.fade), _duration * .5f);
            float gain = fade > 0 ? Mathf.Clamp01(Mathf.Min(_elapsed, _duration - _elapsed) / fade) : 1;
            _source.volume = Mathf.Clamp01(_episode.volume) * gain;
            if (_elapsed >= _duration) Stop();
        }

        public void Stop()
        {
            if (_source == null) _source = GetComponent<AudioSource>();
            if (_source != null) { _source.Stop(); _source.clip = null; }
            _episode = null;
        }
        private void OnDisable() => Stop();
    }
}
