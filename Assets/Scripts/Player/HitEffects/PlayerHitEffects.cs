using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Отклик на попадание по игроку (T-37): пятно крови на одежде в точке попадания и звук. Эффект, а не
    /// состояние: попадание симулирует каждая машина (<see cref="PlayerController.HitReceivedLocal"/>), и
    /// каждая показывает отклик у себя — без сети.
    ///
    /// <list type="bullet">
    /// <item><b>Пятно, не брызги</b> (решение пользователя): точно в точке попадания — тёмное отверстие
    ///       (<c>BulletHoleRockAlbedo</c> Particle Pack, ~2,5 см), вокруг — подтёк крови (<c>GoopStreakAlbedo</c>,
    ///       со случайным поворотом). Дочерний объект хитбокса — ходит с костью. На теле не больше
    ///       <see cref="_maxStainsPerBody"/>, старое уступает новому; уходят вместе с телом (смерть, возрождение).</item>
    /// <item><b>Урон отменён</b> правилом режима (разминка) — отклика нет: крови без урона не бывает.</item>
    /// <item><b>Попали в меня</b> — звук без пространства, «изнутри»; пятна в голову нет (камера в голове).</item>
    /// <item><b>Звуки</b> — свой набор для головы (<see cref="_headClips"/>) и для тела; пул <see cref="_maxVoices"/>
    ///       источников, новое попадание забирает самый старый.</item>
    /// </list>
    ///
    /// <para>
    /// Пятно лежит на поверхности хитбокса, а не меша: капсула повторяет тело приблизительно, и пятно может
    /// чуть висеть над одеждой. Живёт одним объектом на приложение (<c>Resources/PlayerHitEffects.prefab</c>,
    /// собирает <c>Tools/VR Battlegrounds/Gameplay/Build Hit Effects</c>); на сервере без графики не создаётся.
    /// </para>
    /// </summary>
    public sealed class PlayerHitEffects : MonoBehaviour
    {
        public const string Resource = "PlayerHitEffects";
        public const string StainName = "BloodStain";
        public const string HoleName = "BloodHole";

        [Tooltip("Подтёк крови вокруг точки попадания.")]
        [SerializeField] private Material _streakMaterial;

        [Tooltip("Отверстие точно в точке попадания.")]
        [SerializeField] private Material _holeMaterial;

        [Tooltip("Звуки попадания по телу — выбираются случайно.")]
        [SerializeField] private AudioClip[] _clips = new AudioClip[0];

        [Tooltip("Звуки попадания в голову; пусто — как по телу.")]
        [SerializeField] private AudioClip[] _headClips = new AudioClip[0];

        [SerializeField] private Vector2 _stainSize = new Vector2(0.07f, 0.12f);
        [SerializeField] private float _holeSize = 0.025f;
        [SerializeField] private int _maxStainsPerBody = 6;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _selfVolume = 0.6f;
        [SerializeField] private int _maxVoices = 6;

        private static Mesh _quad;
        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private int _nextVoice;

        public int VoiceCount => _voices.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            var prefab = Resources.Load<PlayerHitEffects>(Resource);
            if (prefab == null) return;

            PlayerHitEffects instance = Instantiate(prefab);
            instance.name = Resource;
            DontDestroyOnLoad(instance.gameObject);
        }

        /// <summary>Играть ли отклик: урон прошёл (не отменён правилом режима).</summary>
        public static bool Plays(bool canceled) => !canceled;

        /// <summary>Набор звуков: в голову — свой, если задан, иначе — как по телу.</summary>
        public static AudioClip[] ClipsFor(bool head, AudioClip[] body, AudioClip[] headClips) =>
            head && headClips != null && headClips.Length > 0 ? headClips : body;

        /// <summary>Ставить ли пятно: только на хитбоксе; у своего игрока — не в голову (камера внутри).</summary>
        public static bool ShowsStain(bool localTarget, bool hasZone, HitZone zone) =>
            hasZone && (!localTarget || zone != HitZone.Head);

        private void OnEnable() => PlayerController.HitReceivedLocal += OnHit;

        private void OnDisable() => PlayerController.HitReceivedLocal -= OnHit;

        private void OnHit(PlayerController target, UxrDamageEventArgs e)
        {
            if (target == null || e == null || !Plays(e.IsCanceled)) return;

            Collider hit = e.DamageType == UxrDamageType.ProjectileHit ? e.RaycastHit.collider : null;
            bool hasZone = Hitbox.TryGetPart(hit, out HitZone zone);

            UxrAvatar avatar = target.GetComponent<UxrAvatar>();
            bool local = avatar != null && avatar.AvatarMode == UxrAvatarMode.Local;

            Vector3 point = hit != null ? e.RaycastHit.point : target.transform.position + Vector3.up * 1.2f;
            if (ShowsStain(local, hasZone, zone)) PlaceStain(hit.transform, point, e.RaycastHit.normal);
            PlaySound(point, local, hasZone && zone == HitZone.Head);
        }

        /// <summary>
        /// Пятно крови на части тела <paramref name="part"/>: подтёк вокруг и отверстие точно в точке попадания.
        /// Открыт для тестов.
        /// </summary>
        public GameObject PlaceStain(Transform part, Vector3 point, Vector3 normal)
        {
            if (part == null || _streakMaterial == null || _holeMaterial == null) return null;
            if (normal.sqrMagnitude < 1e-6f) normal = (point - part.position).normalized;

            TrimStains(part.root, Mathf.Max(1, _maxStainsPerBody) - 1);

            // Лицом наружу (квад смотрит по -Z), чуть над поверхностью, со случайным поворотом.
            Quaternion facing = Quaternion.LookRotation(-normal);
            GameObject stain = Decal(StainName, part, point + normal * 0.003f,
                                     facing * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                                     Random.Range(_stainSize.x, _stainSize.y), _streakMaterial);
            GameObject hole = Decal(HoleName, part, point + normal * 0.004f,
                                    facing * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                                    _holeSize, _holeMaterial);
            hole.transform.SetParent(stain.transform, true);
            return stain;
        }

        private static GameObject Decal(string name, Transform part, Vector3 position, Quaternion rotation, float size, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(part, true);
            go.transform.SetPositionAndRotation(position, rotation);
            Vector3 parentScale = part.lossyScale;
            go.transform.localScale = new Vector3(size / Mathf.Max(1e-4f, parentScale.x), size / Mathf.Max(1e-4f, parentScale.y), 1f);

            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>Оставить на теле не больше <paramref name="keep"/> пятен — старые уходят.</summary>
        private static void TrimStains(Transform body, int keep)
        {
            var stains = new List<Transform>();
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                if (t.name == StainName) stains.Add(t);

            for (int i = 0; i < stains.Count - keep; i++)
            {
                if (Application.isPlaying) Destroy(stains[i].gameObject);
                else DestroyImmediate(stains[i].gameObject);
            }
        }

        public static int CountStains(Transform body)
        {
            int count = 0;
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
                if (t.name == StainName) count++;
            return count;
        }

        private static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "BloodStainQuad" };
            _quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            _quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            _quad.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            return _quad;
        }

        /// <summary>Звук попадания. Открыт для тестов.</summary>
        public void PlaySound(Vector3 point, bool self, bool head)
        {
            AudioClip[] clips = ClipsFor(head, _clips, _headClips);
            if (clips == null || clips.Length == 0) return;

            AudioSource voice;
            if (_voices.Count < Mathf.Max(1, _maxVoices))
            {
                var go = new GameObject("HitVoice");
                go.transform.SetParent(transform, false);
                voice = go.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
                voice.minDistance = 1f;
                voice.maxDistance = 25f;
                _voices.Add(voice);
            }
            else
            {
                voice = _voices[_nextVoice];
                _nextVoice = (_nextVoice + 1) % _voices.Count;
            }

            voice.transform.position = point;
            voice.spatialBlend = self ? 0f : 1f;
            voice.volume = self ? _selfVolume : _volume;
            voice.pitch = Random.Range(0.93f, 1.08f);
            voice.clip = clips[Random.Range(0, clips.Length)];
            voice.Play();
        }
    }
}
