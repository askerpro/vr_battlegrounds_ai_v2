using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.LobbyAudio
{
    /// <summary>Единственный локальный владелец расписания косметического звука Lobby.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class LobbyAmbienceDirector : MonoBehaviour
    {
        public LobbyAmbienceProfile profile;
        public LobbyAmbientEmitter emitter;
        public Route[] routes = Array.Empty<Route>();
        [Serializable] public sealed class Route
        {
            public LobbyAmbientKind kind;
            public Transform start;
            public Transform end;
        }
        private AudioSource _forest;
        private Coroutine _schedule;
        private readonly Dictionary<LobbyAmbientKind, float> _next = new Dictionary<LobbyAmbientKind, float>();
        private readonly List<LobbyAmbienceProfile.Episode> _available = new List<LobbyAmbienceProfile.Episode>();
        private LobbyAmbientKind? _previous;

        private void OnEnable()
        {
            StopAll();
            if (gameObject.scene.name != "Lobby" || profile == null || emitter == null) return;
            _forest = GetComponent<AudioSource>();
            _forest.Stop();
            _forest.clip = profile.forest;
            _forest.loop = true;
            _forest.playOnAwake = false;
            _forest.spatialBlend = 0;
            _forest.volume = Mathf.Clamp01(profile.forestVolume);
            _forest.priority = 230;
            if (_forest.clip != null) _forest.Play();
            SceneManager.activeSceneChanged += SceneChanged;
            _schedule = StartCoroutine(Schedule());
        }

        private IEnumerator Schedule()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(Range(profile.globalPause, 1));
                _available.Clear();
                foreach (var e in profile.episodes ?? Array.Empty<LobbyAmbienceProfile.Episode>())
                    if (HasClips(e) && (!_next.TryGetValue(e.kind, out float next) || Time.unscaledTime >= next))
                        _available.Add(e);
                // При наличии альтернативы не повторяем один и тот же тип подряд.
                if (_available.Count > 1 && _previous.HasValue) _available.RemoveAll(e => e.kind == _previous.Value);
                if (_available.Count == 0) continue;
                yield return Episode(_available[UnityEngine.Random.Range(0, _available.Count)]);
            }
        }

        private IEnumerator Episode(LobbyAmbienceProfile.Episode e)
        {
            int count = UnityEngine.Random.Range(Mathf.Clamp(e.repeats.x, 1, 8), Mathf.Clamp(Mathf.Max(e.repeats.x, e.repeats.y), 1, 8) + 1);
            for (int i = 0; i < count; i++)
            {
                var valid = new List<AudioClip>();
                foreach (var clip in e.clips) if (clip != null) valid.Add(clip);
                if (valid.Count == 0) break;
                Route route = Array.Find(routes ?? Array.Empty<Route>(), r => r != null && r.kind == e.kind);
                Vector3 start = route?.start != null ? route.start.position : transform.position;
                Vector3 end = route?.end != null ? route.end.position : start;
                emitter.Play(e, valid[UnityEngine.Random.Range(0, valid.Count)], start, end);
                while (emitter != null && emitter.IsPlaying) yield return null;
                if (i + 1 < count) yield return new WaitForSecondsRealtime(1.5f);
            }
            _previous = e.kind;
            _next[e.kind] = Time.unscaledTime + Range(e.cooldown, 1);
        }

        /// <summary>Ручной запуск для прослушивания в Play; использует то же единственное расписание.</summary>
        public void PreviewEpisode(LobbyAmbientKind kind)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || profile == null || emitter == null) return;
            var episode = Array.Find(profile.episodes ?? Array.Empty<LobbyAmbienceProfile.Episode>(), e => e != null && e.kind == kind && HasClips(e));
            if (episode == null) return;
            if (_schedule != null) StopCoroutine(_schedule);
            emitter.Stop();
            _schedule = StartCoroutine(PreviewThenSchedule(episode));
        }
        private IEnumerator PreviewThenSchedule(LobbyAmbienceProfile.Episode episode)
        {
            yield return Episode(episode);
            yield return Schedule();
        }
        private static bool HasClips(LobbyAmbienceProfile.Episode e) => e != null && e.clips != null && Array.Exists(e.clips, c => c != null);
        private static float Range(Vector2 range, float minimum) => UnityEngine.Random.Range(Mathf.Max(minimum, range.x), Mathf.Max(minimum, Mathf.Max(range.x, range.y)));
        private void SceneChanged(Scene previous, Scene current) { if (current != gameObject.scene) StopAll(); }
        private void OnDisable() => StopAll();
        private void StopAll()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            if (_schedule != null) { StopCoroutine(_schedule); _schedule = null; }
            if (_forest == null) _forest = GetComponent<AudioSource>();
            if (_forest != null) _forest.Stop();
            if (emitter != null) emitter.Stop();
            _next.Clear();
            _previous = null;
        }
    }
}
