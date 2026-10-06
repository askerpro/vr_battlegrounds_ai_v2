using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Стенд подбора позы сидения: в ряд манекены MEF (только модель и humanoid Animator, без UltimateXR), каждый смешивает
    /// стоя → колено → свой кандидат сидения из <see cref="SitCandidateList"/> так же, как контроллер ног по
    /// <c>Legs_Crouch</c> (1D, линейно по мышцам, без Foot IK). Работает в Edit Mode.
    /// <para>
    /// Управление — манипулятор <see cref="body"/> (двигай и крути: ряд идёт за ним) с ребёнком <see cref="head"/>: высота
    /// головы над полом задаёт уровень каждого манекена, как высота камеры в игре — по высотам кости головы в трёх позах
    /// этого манекена (кандидаты разной глубины садятся на разной высоте головы). Над манекеном — уровень и веса клипов.
    /// Гизмо у коленей — куда смотрит колено: резкий разворот линии на переходе — перекрут.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public class SitCandidateStand : MonoBehaviour
    {
        [Tooltip("Качать голову манипулятора вверх-вниз: стоя → сидя самого глубокого кандидата → стоя (и в Edit Mode).")]
        public bool pingPong;

        [Tooltip("Скорость качания, метров в секунду.")]
        public float pingPongSpeed = 0.3f;

        public SitCandidateList list;

        [Tooltip("Модель манекена: префаб аватара с humanoid Animator (MEF_Base_Avatar). Скрипты манекена удаляются.")]
        public GameObject puppetPrefab;

        [Tooltip("Тело манипулятора: место и поворот ряда.")]
        public Transform body;

        [Tooltip("Голова манипулятора (ребёнок тела): высота над полом — уровень приседа манекенов.")]
        public Transform head;

        public float spacing = 1.2f;

        private readonly List<Puppet> _puppets = new List<Puppet>();
        private readonly StringBuilder _text = new StringBuilder();
        private GameObject _holder;
        private bool _dirty = true;
        private int _listSignature;
        private float _pingPongTime;

        private sealed class Puppet
        {
            public GameObject Root;
            public Animator Animator;
            public Transform HeadBone;
            public PlayableGraph Graph;
            public AnimationMixerPlayable Mixer;
            public string Title;
            public string[] ClipNames;
            public float[] HeadHeights;   // высота кости головы над полом в позах уровней 0, 1, 2
            public float Level;
            public TextMesh Label;
        }

        private void OnEnable()
        {
            _dirty = true;
#if UNITY_EDITOR
            EditorApplication.update += EditorTick;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorTick;
#endif
            Clear();
        }

        private void OnValidate()
        {
            _dirty = true;
        }

        private void Update()
        {
            if (Application.isPlaying) Tick(Time.deltaTime);
        }

#if UNITY_EDITOR
        private double _lastEditorTime;

        private void EditorTick()
        {
            if (Application.isPlaying || this == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = _lastEditorTime > 0 ? (float)(now - _lastEditorTime) : 0f;
            _lastEditorTime = now;
            Tick(dt);
            SceneView.RepaintAll();
        }
#endif

        private void Tick(float dt)
        {
            int signature = ListSignature();
            if (_dirty || signature != _listSignature)
            {
                _dirty = false;
                _listSignature = signature;
                Rebuild();
            }

            if (pingPong) PingPongHead(dt);

            Vector3 bodyLocal = body != null ? transform.InverseTransformPoint(body.position) : Vector3.zero;
            Quaternion yaw = body != null ? Quaternion.Euler(0f, (Quaternion.Inverse(transform.rotation) * body.rotation).eulerAngles.y, 0f)
                                          : Quaternion.identity;
            float headHeight = head != null ? transform.InverseTransformPoint(head.position).y : float.MaxValue;

            for (int i = 0; i < _puppets.Count; i++)
            {
                Puppet p = _puppets[i];
                p.Root.transform.localPosition = new Vector3(bodyLocal.x + i * spacing, 0f, bodyLocal.z);
                p.Root.transform.localRotation = yaw;
                if (!p.Graph.IsValid()) continue;

                p.Level = LevelFromHead(p.HeadHeights, headHeight);
                SetLevel(p, p.Level);
                p.Graph.Evaluate(0f);
                if (p.Label != null) p.Label.text = Describe(p);
            }
        }

        /// <summary>Вес клипов уровня: 0 — стоя, 1 — колено, 2 — сидя; между соседними — линейно, как 1D blend tree.</summary>
        private static void SetLevel(Puppet p, float level)
        {
            p.Mixer.SetInputWeight(0, Mathf.Clamp01(1f - level));
            p.Mixer.SetInputWeight(1, level <= 1f ? level : 2f - level);
            p.Mixer.SetInputWeight(2, Mathf.Clamp01(level - 1f));
        }

        /// <summary>Уровень по высоте головы: обратная кусочно-линейная по высотам головы манекена в трёх позах.</summary>
        private static float LevelFromHead(float[] h, float headHeight)
        {
            if (h == null) return 0f;
            if (headHeight >= h[0]) return 0f;
            if (headHeight >= h[1]) return h[0] - h[1] > 1e-4f ? (h[0] - headHeight) / (h[0] - h[1]) : 1f;
            if (headHeight >= h[2]) return 1f + (h[1] - h[2] > 1e-4f ? (h[1] - headHeight) / (h[1] - h[2]) : 1f);
            return 2f;
        }

        private void PingPongHead(float dt)
        {
            if (head == null || _puppets.Count == 0) return;
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (Puppet p in _puppets)
            {
                if (p.HeadHeights == null) continue;
                top = Mathf.Max(top, p.HeadHeights[0]);
                bottom = Mathf.Min(bottom, p.HeadHeights[2]);
            }

            if (top <= bottom) return;
            _pingPongTime += dt * pingPongSpeed;
            Vector3 local = transform.InverseTransformPoint(head.position);
            local.y = top - Mathf.PingPong(_pingPongTime, top - bottom);
            head.position = transform.TransformPoint(local);
        }

        private string Describe(Puppet p)
        {
            _text.Clear();
            _text.Append(p.Title).Append('\n');
            _text.Append("уровень ").Append(p.Level.ToString("0.00")).Append('\n');
            for (int i = 0; i < 3; i++)
            {
                float w = p.Mixer.GetInputWeight(i);
                if (w < 0.005f) continue;
                _text.Append(p.ClipNames[i]).Append("  ").Append(w.ToString("0.00")).Append('\n');
            }

            return _text.ToString();
        }

        private int ListSignature()
        {
            if (list == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + (list.stand != null ? list.stand.GetHashCode() : 0);
                h = h * 31 + (list.kneel != null ? list.kneel.GetHashCode() : 0);
                foreach (SitCandidateList.Candidate c in list.candidates)
                {
                    h = h * 31 + (c.clip != null ? c.clip.GetHashCode() : 0);
                    h = h * 31 + c.frame.GetHashCode();
                    h = h * 31 + (c.label != null ? c.label.GetHashCode() : 0);
                }

                return h;
            }
        }

        private void Rebuild()
        {
            Clear();
            if (list == null || puppetPrefab == null) return;

            _holder = new GameObject("Puppets (временные)") { hideFlags = HideFlags.DontSave };
            _holder.transform.SetParent(transform, false);
            _holder.SetActive(false); // Awake скриптов модели не вызывается: они удаляются до включения

            for (int i = 0; i < list.candidates.Count; i++)
            {
                SitCandidateList.Candidate c = list.candidates[i];
                Animator animator = PosePuppet.Create(puppetPrefab, _holder.transform, $"Puppet {i}: {c.label}", out GameObject root);
                root.transform.localPosition = new Vector3(i * spacing, 0f, 0f);

                var puppet = new Puppet
                {
                    Root = root,
                    Animator = animator,
                    Title = !string.IsNullOrEmpty(c.label) ? c.label : (c.clip != null ? c.clip.name : "—") + $" @{c.frame:0}",
                    ClipNames = new[] { ClipName(list.stand), ClipName(list.kneel), ClipName(c.clip) + $" @{c.frame:0}" },
                    Label = PosePuppet.AddLabel(root.transform),
                };
                if (animator != null)
                {
                    puppet.HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    BuildGraph(puppet, c);
                }

                _puppets.Add(puppet);
            }

            _holder.SetActive(true);
            foreach (Puppet p in _puppets) MeasureHeadHeights(p);
        }

        private static string ClipName(AnimationClip clip) => clip != null ? clip.name : "—";

        /// <summary>Высота кости головы над полом в чистых позах уровней 0, 1, 2 — шкала «высота головы → уровень».</summary>
        private static void MeasureHeadHeights(Puppet p)
        {
            if (!p.Graph.IsValid() || p.HeadBone == null) return;
            p.HeadHeights = new float[3];
            for (int level = 0; level < 3; level++)
            {
                SetLevel(p, level);
                p.Graph.Evaluate(0f);
                p.HeadHeights[level] = p.Root.transform.InverseTransformPoint(p.HeadBone.position).y;
            }

            // Кандидат «сидя» выше колена (или колено выше стоя) — шкала не монотонна; держим порядок, чтобы уровень не скакал.
            p.HeadHeights[1] = Mathf.Min(p.HeadHeights[1], p.HeadHeights[0] - 0.01f);
            p.HeadHeights[2] = Mathf.Min(p.HeadHeights[2], p.HeadHeights[1] - 0.01f);
        }

        private void BuildGraph(Puppet p, SitCandidateList.Candidate c)
        {
            p.Graph = PlayableGraph.Create($"SitCandidate {p.Root.name}");
            p.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(p.Graph, "Pose", p.Animator);
            p.Mixer = AnimationMixerPlayable.Create(p.Graph, 3);
            output.SetSourcePlayable(p.Mixer);
            Connect(p, 0, list.stand, 0f);
            Connect(p, 1, list.kneel, 0f);
            Connect(p, 2, c.clip, c.clip != null && c.clip.frameRate > 0f ? c.frame / c.clip.frameRate : 0f);
            p.Graph.Play();
        }

        private static void Connect(Puppet p, int input, AnimationClip clip, float time)
        {
            if (clip == null) return;
            var playable = AnimationClipPlayable.Create(p.Graph, clip);
            playable.SetApplyFootIK(false); // как состояние покоя контроллера ног
            playable.SetTime(time);
            playable.SetSpeed(0f);
            p.Graph.Connect(playable, 0, p.Mixer, input);
        }

        private void Clear()
        {
            foreach (Puppet p in _puppets)
            {
                if (p.Graph.IsValid()) p.Graph.Destroy();
            }

            _puppets.Clear();
            if (_holder != null) DestroyImmediate(_holder);
            _holder = null;
        }

        private void OnDrawGizmos()
        {
            foreach (Puppet p in _puppets) PosePuppet.DrawLegs(p.Animator);

            if (head != null && body != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(body.position, head.position);
            }
        }
    }
}
