using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Стенд перемотки кандидатов: манекены MEF в ряд, у каждого — цепочка поз аватара (<see cref="ClipScrubList.chain"/>:
    /// стоя → присед/колено → промежуточные, как уровни <c>Legs_Crouch</c>), ниже её последней позы — свой кандидат
    /// (<see cref="ClipScrubList.entries"/>). Позы цепочки — клипы аватара, отдельными строками их нет.
    /// <para>
    /// Манипулятор как у стенда-кукловода: <see cref="body"/> двигает и крутит ряд, <see cref="head"/> — камера (глаза).
    /// При <see cref="followHead"/> кость головы на <see cref="EyeAboveHead"/> ниже камеры: между позами цепочки — смешивание
    /// соседних по высоте головы (как 1D blend tree); ниже последней — кадр кандидата, где его голова на этой высоте (в
    /// отрезке строки; из нескольких мест — ближайшее к прошлому кадру, при входе в зону — к кадру входа строки). Без
    /// <see cref="followHead"/> — кандидат на кадре ползунка.
    /// </para>
    /// <para>
    /// Над манекеном — номер строки; в инспекторе — что сейчас ведёт позу, кадр и отличие от последней позы цепочки: ноги
    /// (RMS мышц ног Mecanim, 0 — совпадают), высота таза, наклон корпуса. «Ближайший» — кадр кандидата, ближе всего к
    /// последней позе цепочки (кадр входа). <see cref="spacing"/> = 0 — наложение. Edit Mode; Play — живые аватары
    /// (<c>ClipScrubStand.Live.cs</c>).
    /// </para>
    /// </summary>
    /// <summary>Вид стенда перемотки.</summary>
    public enum ClipScrubView
    {
        /// <summary>Подбор: общая цепочка, строки — кандидаты; на сцене инспектируемая строка.</summary>
        Tuning,

        /// <summary>Сохранённые: сохранённые комбинации в ряд, у каждой своя цепочка.</summary>
        Saved,
    }

    [ExecuteAlways]
    public partial class ClipScrubStand : MonoBehaviour
    {
        public ClipScrubList list;

        [Tooltip("Модель манекена: префаб аватара с humanoid Animator (MEF_Base_Avatar). Скрипты манекена удаляются.")]
        public GameObject puppetPrefab;

        [Tooltip("Шаг ряда, м. 0 — манекены друг в друге (наложение поз).")]
        public float spacing = 1.5f;

        [Tooltip("Подбор — общая цепочка и строки-кандидаты (видна инспектируемая); Сохранённые — сохранённые комбинации в ряд, у каждой своя цепочка.")]
        public ClipScrubView view = ClipScrubView.Tuning;

        [Tooltip("На сцене только инспектируемая строка (у манипулятора); остальные считаются, но не рисуются. В Play — один живой аватар. Выключено — весь ряд.")]
        public bool onlyInspected = true;

        [Tooltip("Поза — по высоте головы манипулятора: цепочка аватара, ниже её — кадр кандидата (иначе — ползунки инспектора).")]
        public bool followHead = true;

        [Tooltip("Переход по кадрам: на сколько метров ниже последней позы цепочки кандидат плавно набирает вес (поза цепочки уступает). 0 — жёсткое переключение.")]
        public float transitionBlendZone = 0.1f;

        [Tooltip("Тело манипулятора: место и поворот ряда.")]
        public Transform body;

        [Tooltip("Голова манипулятора (ребёнок тела) — камера, глаза: её высота ведёт позу строк при followHead; в Play — камера живых аватаров.")]
        public Transform head;

        /// <summary>Отличие позы манекена от последней позы цепочки.</summary>
        public struct PoseDelta
        {
            public float Legs;        // RMS разницы мышц ног (мышцы Mecanim нормированы −1…1)
            public float HipsHeight;  // разница высоты таза над полом, м (+ — выше)
            public float BodyAngle;   // разница поворота корпуса (bodyRotation), градусы

            /// <summary>Сводная оценка для поиска кадра: ноги + 2·|таз, м| + наклон/180.</summary>
            public float Score => Legs + 2f * Mathf.Abs(HipsHeight) + BodyAngle / 180f;
        }

        private sealed class Puppet
        {
            public GameObject Root;
            public Animator Animator;
            public Transform Hips;
            public PlayableGraph Graph;
            public AnimationMixerPlayable Mixer;
            public AnimationClipPlayable Candidate;
            public AnimationClip CandidateClip;
            public int CandidateInput;          // вход микшера кандидата — после поз цепочки
            public HumanPoseHandler PoseHandler;
            public HumanPose Pose;
            public float HipsHeight;
            public TextMesh Label;
            public Renderer[] Renderers;
            public bool Shown = true;
            public Transform HeadBone;
            public float[] HeadHeights;         // высота кости головы над полом на каждом кадре кандидата
            public float[] ChainHeads;          // высота кости головы в позах цепочки (сверху вниз, убывает)
            public float[] LastMuscles;         // последняя поза цепочки — эталон отличий
            public Quaternion LastBody;
            public float LastHips;
            public float HeadFrame = -1f;       // кадр кандидата по голове (−1 — голова в цепочке)
            public int ChainPose;               // голова в цепочке: между позой ChainPose и следующей
            public float ChainBlend;            // доля следующей позы
            public float CandidateWeight;       // вес кандидата ниже цепочки (остальное — последняя поза цепочки)
            public AnimationClipPlayable[] ChainPlayables; // входы поз цепочки — клипы меняются на лету, без пересборки
            public AnimationClip[] ChainClips;
            public float[] ChainFrames;
            public int Row;                     // строка: подбор — кандидат, сохранённые — комбинация
        }

        private static int[] s_legMuscles;

        private readonly List<Puppet> _puppets = new List<Puppet>();
        private GameObject _holder;
        private bool _dirty = true;
        private int _structure;
        private ClipScrubList _builtList;
        private GameObject _builtPuppetPrefab, _builtLivePrefab;

        /// <summary>
        /// Инспектируемая строка — одна на стенд (окно «Отладка аватара» и инспектор стенда); подсвечивается в сцене.
        /// Ставит редактор, в сцену не пишется.
        /// </summary>
        public int InspectedRow { get; set; }

        /// <summary>Вид «Сохранённые»: строки — сохранённые комбинации, у каждой своя цепочка.</summary>
        public bool SavedView => view == ClipScrubView.Saved;

        /// <summary>Число строк текущего вида.</summary>
        public int RowCount => list == null ? 0 : SavedView ? list.saved.Count : list.entries.Count;

        /// <summary>Цепочка поз строки: подбор — общая, сохранённые — своя у комбинации.</summary>
        public List<ClipScrubList.KeyPose> ChainOf(int row) => SavedView ? list.saved[row].chain : list.chain;

        /// <summary>Кандидат строки.</summary>
        public ClipScrubList.Entry EntryOf(int row) => SavedView ? list.saved[row].entry : list.entries[row];

        /// <summary>Зона смешивания перехода строки: подбор — общая стенда, сохранённые — своя у комбинации.</summary>
        public float ZoneOf(int row) => SavedView ? list.saved[row].blendZone : transitionBlendZone;

        /// <summary>Подпись строки: кандидат или имя комбинации.</summary>
        public string RowLabel(int row)
        {
            if (SavedView) return !string.IsNullOrEmpty(list.saved[row].label) ? list.saved[row].label : $"комбинация {row}";
            ClipScrubList.Entry e = list.entries[row];
            return !string.IsNullOrEmpty(e.label) ? e.label : e.clip != null ? e.clip.name : "—";
        }

        /// <summary>Весь ряд на сцене: вид «Сохранённые» (сравнение) или выключенный <see cref="onlyInspected"/>.</summary>
        private bool ShowAll => SavedView || !onlyInspected;

        /// <summary>Отличие манекена строки от последней позы цепочки сейчас (для инспектора).</summary>
        public bool TryGetDelta(int index, out PoseDelta delta)
        {
            delta = default;
            Puppet p = At(index);
            if (p == null || p.LastMuscles == null) return false;
            delta = Delta(p);
            return true;
        }

        /// <summary>Кадр кандидата строки в её отрезке, поза которого ближе всего к последней позе цепочки (шаг — 1 кадр). Кадр строки не меняет.</summary>
        public bool TryFindNearestFrame(int index, out float frame, out PoseDelta delta)
        {
            frame = 0f;
            delta = default;
            Puppet p = At(index);
            if (p == null || p.LastMuscles == null || p.CandidateClip == null) return false;
            ClipScrubList.Entry e = EntryOf(index);
            int from = 0, to = Mathf.RoundToInt(LastFrame(e.clip)); // по всему клипу: найденный кадр станет верхом отрезка

            float best = float.MaxValue;
            for (int f = from; f <= to; f++)
            {
                EvaluateCandidate(p, f);
                PoseDelta d = Delta(p);
                if (d.Score >= best) continue;
                best = d.Score;
                frame = f;
                delta = d;
            }

            EvaluateCurrent(p, e);
            return true;
        }

        /// <summary>
        /// Кадр входа кандидата: кадр отрезка с самой высокой головой — стык с последней позой цепочки. С него кандидат
        /// начинает, когда голова опускается ниже цепочки.
        /// </summary>
        public float EntryFrame(int index)
        {
            Puppet p = At(index);
            return p != null ? EntryFrame(p, EntryOf(index)) : EntryOf(index).rangeFrom;
        }

        /// <summary>Верх отрезка (конец с более высокой головой) — начало отрезка, а не его конец.</summary>
        public bool TopIsRangeStart(int index)
        {
            Puppet p = At(index);
            if (p == null || p.HeadHeights == null || p.HeadHeights.Length == 0) return true;
            (int from, int to) = Range(EntryOf(index), p.HeadHeights.Length - 1);
            return p.HeadHeights[from] >= p.HeadHeights[to];
        }

        private static float EntryFrame(Puppet p, ClipScrubList.Entry e)
        {
            float[] h = p.HeadHeights;
            if (h == null || h.Length == 0) return e.rangeFrom;
            (int from, int to) = Range(e, h.Length - 1);
            int top = from;
            for (int f = from; f <= to; f++)
            {
                if (h[f] > h[top]) top = f;
            }

            return top;
        }

        /// <summary>Кадр кандидата строки: по голове (ниже цепочки) или кадр просмотра (без следования за камерой).</summary>
        public float CurrentFrame(int index)
        {
            Puppet p = index >= 0 && index < _puppets.Count ? _puppets[index] : null;
            return followHead && p != null && p.HeadFrame >= 0f ? p.HeadFrame : EntryOf(index).frame;
        }

        /// <summary>Что ведёт позу строки сейчас: позы цепочки с долями или «кандидат, кадр …».</summary>
        public string PoseSource(int index)
        {
            Puppet p = At(index);
            if (p == null) return "";
            if (!followHead || head == null) return $"кандидат, кадр {EntryOf(index).frame:0.#}";
            if (p.HeadFrame >= 0f)
            {
                string cand = $"кандидат {p.CandidateWeight:P0}, кадр {p.HeadFrame:0.#}";
                return p.CandidateWeight >= 0.999f ? cand : $"{ChainLabel(index, ChainOf(index).Count - 1)} {1f - p.CandidateWeight:P0} + {cand}";
            }
            string a = ChainLabel(index, p.ChainPose);
            if (p.ChainBlend <= 0.001f || p.ChainPose + 1 >= ChainOf(index).Count) return a;
            return $"{a} {1f - p.ChainBlend:P0} + {ChainLabel(index, p.ChainPose + 1)} {p.ChainBlend:P0}";
        }

        private string ChainLabel(int row, int k)
        {
            ClipScrubList.KeyPose pose = ChainOf(row)[k];
            return !string.IsNullOrEmpty(pose.label) ? pose.label : pose.clip != null ? pose.clip.name : $"поза {k}";
        }

        /// <summary>Высота кости головы строки сейчас, м (NaN — нет манекена).</summary>
        public float HeadHeightNow(int index)
        {
            Puppet p = At(index);
            return p != null && p.HeadBone != null ? HeadNow(p) : float.NaN;
        }

        /// <summary>Последний кадр клипа при его частоте кадров.</summary>
        public static float LastFrame(AnimationClip clip) =>
            clip == null ? 0f : Mathf.Max(0f, Mathf.Round(clip.length * clip.frameRate));

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

        /// <summary>Пересборка — только при смене списка или префабов; остальные поля (зона, режимы, шаг) читаются каждый кадр.</summary>
        private void OnValidate()
        {
            if (list != _builtList || puppetPrefab != _builtPuppetPrefab || livePrefab != _builtLivePrefab) _dirty = true;
        }

        private void Update()
        {
            if (Application.isPlaying) Tick();
        }

#if UNITY_EDITOR
        private void EditorTick()
        {
            if (Application.isPlaying || this == null) return;
            Tick();
            SceneView.RepaintAll();
        }
#endif

        private void Tick()
        {
            int structure = StructureSignature();
            if (_dirty || structure != _structure)
            {
                _dirty = false;
                _structure = structure;
                Rebuild();
            }
            else
            {
                SyncClips();
            }

            if (list == null) return;
            SyncLiveRow();
            Vector3 bodyLocal = body != null ? transform.InverseTransformPoint(body.position) : Vector3.zero;
            Quaternion yaw = body != null ? Quaternion.Euler(0f, (Quaternion.Inverse(transform.rotation) * body.rotation).eulerAngles.y, 0f)
                                          : Quaternion.identity;
            bool byHead = followHead && head != null;
            float headHeight = byHead ? transform.InverseTransformPoint(head.position).y - EyeAboveHead : 0f;
            for (int i = 0; i < _puppets.Count; i++)
            {
                Puppet p = _puppets[i];
                // Камера стенда смотрит манекенам в лицо (против +Z): −X у неё справа — строки идут слева направо.
                p.Root.transform.localPosition = new Vector3(bodyLocal.x - RowOffset(i) * spacing, 0f, bodyLocal.z);
                SetShown(p, ShowAll || i == InspectedRow);
                p.Root.transform.localRotation = yaw;
                if (p.Label != null) p.Label.text = i.ToString();
                if (!p.Graph.IsValid()) continue;

                ClipScrubList.Entry e = EntryOf(i);
                if (byHead && !PlaceInChain(p, headHeight)) PlaceCandidate(p, e, headHeight, ZoneOf(i));
                else if (!byHead) p.HeadFrame = -1f;
                EvaluateCurrent(p, e);
            }
        }

        /// <summary>
        /// Голова в цепочке (не ниже её последней позы): поза и доля следующей по высоте головы. false — голова ниже
        /// цепочки, позу ведёт кандидат.
        /// </summary>
        private static bool PlaceInChain(Puppet p, float headHeight)
        {
            float[] h = p.ChainHeads;
            if (h == null || h.Length == 0 || headHeight < h[h.Length - 1]) return false;
            p.HeadFrame = -1f;
            p.ChainPose = 0;
            p.ChainBlend = 0f;
            for (int k = 0; k + 1 < h.Length; k++)
            {
                if (headHeight < h[k + 1]) continue;
                p.ChainPose = k;
                p.ChainBlend = Mathf.Clamp01(Mathf.InverseLerp(h[k], h[k + 1], headHeight));
                return true;
            }

            p.ChainPose = h.Length - 1; // ровно на последней позе
            return true;
        }

        /// <summary>
        /// Голова ниже цепочки: кадр кандидата и его вес. Поза (<see cref="ClipScrubList.Entry.poseBlend"/>) — кадр позы, вес
        /// от 0 на высоте последней позы цепочки до 1 на высоте головы позы (как blend tree). Переход — кадр по высоте головы,
        /// вес набирается в <see cref="transitionBlendZone"/> ниже последней позы цепочки.
        /// </summary>
        private static void PlaceCandidate(Puppet p, ClipScrubList.Entry e, float headHeight, float zone)
        {
            float last = p.ChainHeads != null && p.ChainHeads.Length > 0 ? p.ChainHeads[p.ChainHeads.Length - 1] : headHeight;
            if (e.poseBlend)
            {
                p.HeadFrame = e.frame;
                float poseHead = p.HeadHeights != null && p.HeadHeights.Length > 0
                    ? p.HeadHeights[Mathf.Clamp(Mathf.RoundToInt(e.frame), 0, p.HeadHeights.Length - 1)]
                    : last;
                p.CandidateWeight = last - poseHead > 1e-3f ? Mathf.Clamp01(Mathf.InverseLerp(last, poseHead, headHeight)) : 1f;
                return;
            }

            p.HeadFrame = FrameForHead(p, e, headHeight);
            p.CandidateWeight = zone > 1e-3f ? Mathf.Clamp01((last - headHeight) / zone) : 1f;
        }

        /// <summary>Поза строки по текущему состоянию: смешивание поз цепочки или кандидат (с последней позой цепочки).</summary>
        private void EvaluateCurrent(Puppet p, ClipScrubList.Entry e)
        {
            bool byHead = followHead && head != null && p.ChainHeads != null && p.ChainHeads.Length > 0;
            if (byHead && p.HeadFrame < 0f) EvaluateChain(p, p.ChainPose, p.ChainBlend);
            else if (byHead) EvaluateBlend(p, p.ChainHeads.Length - 1, p.CandidateWeight, p.HeadFrame);
            else EvaluateCandidate(p, e.frame);
        }

        /// <summary>Последняя поза цепочки <paramref name="k"/> с долей 1 − w и кандидат на кадре с долей w.</summary>
        private static void EvaluateBlend(Puppet p, int k, float w, float frame)
        {
            if (p.CandidateClip == null || !p.Candidate.IsValid() || w >= 0.999f)
            {
                EvaluateCandidate(p, frame);
                return;
            }

            int count = p.Mixer.GetInputCount();
            for (int i = 0; i < count; i++) p.Mixer.SetInputWeight(i, 0f);
            p.Mixer.SetInputWeight(k, 1f - w);
            p.Mixer.SetInputWeight(p.CandidateInput, w);
            p.Candidate.SetTime(p.CandidateClip.frameRate > 0f ? frame / p.CandidateClip.frameRate : 0f);
            Sample(p);
        }

        private static (int from, int to) Range(ClipScrubList.Entry e, float lastFrame)
        {
            int last = Mathf.RoundToInt(lastFrame);
            int from = Mathf.Clamp(Mathf.RoundToInt(e.rangeFrom), 0, last);
            int to = e.rangeTo > 0f ? Mathf.Clamp(Mathf.RoundToInt(e.rangeTo), 0, last) : last;
            return to < from ? (to, from) : (from, to);
        }

        /// <summary>
        /// Кадр отрезка кандидата, где голова манекена на высоте <paramref name="height"/>: пересечение высоты, ближайшее к
        /// прошлому кадру (при входе из цепочки — к кадру входа строки); нет пересечений — кадр с ближайшей высотой.
        /// </summary>
        private static float FrameForHead(Puppet p, ClipScrubList.Entry e, float height)
        {
            float[] h = p.HeadHeights;
            if (h == null || h.Length == 0) return e.frame;
            (int from, int to) = Range(e, h.Length - 1);
            float previous = p.HeadFrame >= 0f ? p.HeadFrame : EntryFrame(p, e);

            float best = float.NaN, bestGap = float.MaxValue;
            for (int f = from; f < to; f++)
            {
                float a = h[f] - height, b = h[f + 1] - height;
                if (a * b > 0f) continue;
                float cross = f + (Mathf.Abs(a - b) > 1e-6f ? a / (a - b) : 0f);
                float gap = Mathf.Abs(cross - previous);
                if (gap < bestGap) { bestGap = gap; best = cross; }
            }

            if (!float.IsNaN(best)) return best;
            int closest = from;
            for (int f = from; f <= to; f++)
            {
                if (Mathf.Abs(h[f] - height) < Mathf.Abs(h[closest] - height)) closest = f;
            }

            return closest;
        }

        /// <summary>Шкалы строки: голова в позах цепочки и на каждом кадре кандидата; последняя поза цепочки — эталон отличий.</summary>
        private void Measure(Puppet p)
        {
            MeasureChain(p);
            MeasureCandidate(p);
        }

        /// <summary>Высоты головы в позах цепочки (сверху вниз) и последняя поза цепочки — эталон отличий.</summary>
        private void MeasureChain(Puppet p)
        {
            if (!p.Graph.IsValid() || p.HeadBone == null) return;
            int n = ChainOf(p.Row).Count;
            p.ChainHeads = new float[n];
            for (int k = 0; k < n; k++)
            {
                EvaluateChain(p, k, 0f);
                // Высоты поз — сверху вниз: поза не ниже предыдущей сдвигается на 1 см ниже, чтобы смешивание не скакало.
                p.ChainHeads[k] = k == 0 ? HeadNow(p) : Mathf.Min(HeadNow(p), p.ChainHeads[k - 1] - 0.01f);
            }

            if (n > 0)
            {
                EvaluateChain(p, n - 1, 0f);
                p.LastMuscles = (float[])p.Pose.muscles.Clone();
                p.LastBody = p.Pose.bodyRotation;
                p.LastHips = p.HipsHeight;
            }
        }

        /// <summary>Высота головы на каждом кадре клипа кандидата — шкала «высота головы → кадр».</summary>
        private static void MeasureCandidate(Puppet p)
        {
            p.HeadHeights = null;
            if (!p.Graph.IsValid() || p.HeadBone == null || p.CandidateClip == null) return;
            int frames = Mathf.RoundToInt(LastFrame(p.CandidateClip)) + 1;
            p.HeadHeights = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                EvaluateCandidate(p, f);
                p.HeadHeights[f] = HeadNow(p);
            }
        }

        private static float HeadNow(Puppet p) => p.Root.transform.InverseTransformPoint(p.HeadBone.position).y;

        /// <summary>Смешивание позы цепочки <paramref name="k"/> и следующей с долей <paramref name="blend"/>.</summary>
        private static void EvaluateChain(Puppet p, int k, float blend)
        {
            int count = p.Mixer.GetInputCount();
            for (int i = 0; i < count; i++) p.Mixer.SetInputWeight(i, 0f);
            p.Mixer.SetInputWeight(k, 1f - blend);
            if (blend > 0f && k + 1 < p.CandidateInput) p.Mixer.SetInputWeight(k + 1, blend);
            Sample(p);
        }

        private static void EvaluateCandidate(Puppet p, float frame)
        {
            if (p.CandidateClip == null || !p.Candidate.IsValid()) return;
            int count = p.Mixer.GetInputCount();
            for (int i = 0; i < count; i++) p.Mixer.SetInputWeight(i, 0f);
            p.Mixer.SetInputWeight(p.CandidateInput, 1f);
            p.Candidate.SetTime(p.CandidateClip.frameRate > 0f ? frame / p.CandidateClip.frameRate : 0f);
            Sample(p);
        }

        private static void Sample(Puppet p)
        {
            if (!p.Graph.IsValid()) return;
            p.Graph.Evaluate(0f);
            p.PoseHandler.GetHumanPose(ref p.Pose);
            p.HipsHeight = p.Hips != null ? p.Root.transform.InverseTransformPoint(p.Hips.position).y : 0f;
        }

        private static PoseDelta Delta(Puppet p)
        {
            float sum = 0f;
            foreach (int m in LegMuscles())
            {
                float d = p.Pose.muscles[m] - p.LastMuscles[m];
                sum += d * d;
            }

            return new PoseDelta
            {
                Legs = Mathf.Sqrt(sum / LegMuscles().Length),
                HipsHeight = p.HipsHeight - p.LastHips,
                BodyAngle = Quaternion.Angle(p.Pose.bodyRotation, p.LastBody),
            };
        }

        /// <summary>Мышцы ног Mecanim: бёдра, голени, стопы, пальцы обеих ног.</summary>
        private static int[] LegMuscles()
        {
            if (s_legMuscles != null) return s_legMuscles;
            var legs = new List<int>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes")) legs.Add(i);
            }

            return s_legMuscles = legs.ToArray();
        }

        private Puppet At(int index) => index >= 0 && index < _puppets.Count && _puppets[index].Graph.IsValid() ? _puppets[index] : null;

        /// <summary>Состав стенда: число поз цепочки и строк. Меняется — полная пересборка; клипы и кадры — на лету (<see cref="SyncClips"/>).</summary>
        private int StructureSignature()
        {
            if (list == null) return 0;
            unchecked
            {
                int h = (list.GetHashCode() * 31) ^ ((int)view * 7919) ^ (RowCount * 397);
                for (int i = 0; i < RowCount; i++) h = h * 31 + ChainOf(i).Count;
                return h;
            }
        }

        /// <summary>
        /// Клипы и кадры на лету: клип позы цепочки — замена входа микшера у всех манекенов, кадр позы — время входа;
        /// клип кандидата — замена входа одного манекена. Затем пересчёт высот головы. Манекены и живые аватары не
        /// пересоздаются.
        /// </summary>
        private void SyncClips()
        {
            if (list == null) return;
            var changedRows = new HashSet<int>();
            foreach (Puppet p in _puppets)
            {
                if (!p.Graph.IsValid() || p.ChainClips == null || p.Row >= RowCount) continue;
                List<ClipScrubList.KeyPose> chain = ChainOf(p.Row);
                for (int k = 0; k < chain.Count && k < p.ChainClips.Length; k++)
                {
                    ClipScrubList.KeyPose pose = chain[k];
                    if (p.ChainClips[k] != pose.clip)
                    {
                        p.ChainPlayables[k] = Replace(p, k, p.ChainPlayables[k], pose.clip, pose.frame);
                        p.ChainClips[k] = pose.clip;
                        p.ChainFrames[k] = pose.frame;
                        changedRows.Add(p.Row);
                    }
                    else if (!Mathf.Approximately(p.ChainFrames[k], pose.frame))
                    {
                        if (p.ChainPlayables[k].IsValid()) p.ChainPlayables[k].SetTime(ClipTime(pose.clip, pose.frame));
                        p.ChainFrames[k] = pose.frame;
                        changedRows.Add(p.Row);
                    }
                }
            }

            for (int i = 0; i < _puppets.Count && i < RowCount; i++)
            {
                Puppet p = _puppets[i];
                AnimationClip clip = EntryOf(i).clip;
                if (!p.Graph.IsValid() || p.CandidateClip == clip) continue;
                p.Candidate = Replace(p, p.CandidateInput, p.Candidate, clip, EntryOf(i).frame);
                p.CandidateClip = clip;
                p.HeadFrame = -1f;
                MeasureCandidate(p);
            }

            foreach (Puppet p in _puppets)
            {
                if (changedRows.Contains(p.Row)) MeasureChain(p);
            }
        }

        private static AnimationClipPlayable Replace(Puppet p, int input, AnimationClipPlayable old, AnimationClip clip, float frame)
        {
            if (old.IsValid())
            {
                p.Graph.Disconnect(p.Mixer, input);
                old.Destroy();
            }

            return Connect(p, input, clip, frame);
        }

        private static float ClipTime(AnimationClip clip, float frame) => clip != null && clip.frameRate > 0f ? frame / clip.frameRate : 0f;

        private void Rebuild()
        {
            Clear();
            _builtList = list;
            _builtPuppetPrefab = puppetPrefab;
            _builtLivePrefab = livePrefab;
            if (list == null || puppetPrefab == null) return;

            _holder = new GameObject("Puppets (временные)") { hideFlags = HideFlags.DontSave };
            _holder.transform.SetParent(transform, false);
            _holder.SetActive(false); // Awake скриптов модели не вызывается: они удаляются до включения

            for (int i = 0; i < RowCount; i++)
            {
                ClipScrubList.Entry e = EntryOf(i);
                List<ClipScrubList.KeyPose> chain = ChainOf(i);
                Animator animator = PosePuppet.Create(puppetPrefab, _holder.transform, $"Puppet {i}: {RowLabel(i)}", out GameObject root);
                var puppet = new Puppet { Row = i, Root = root, Animator = animator, Label = PosePuppet.AddLabel(root.transform), CandidateClip = e.clip };
                puppet.Label.characterSize = 0.05f; // только номер строки инспектора — всё остальное там
                if (animator != null)
                {
                    puppet.Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    puppet.HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
                    puppet.PoseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                    puppet.Graph = PlayableGraph.Create($"ClipScrub {root.name}");
                    puppet.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(puppet.Graph, "Pose", animator);
                    puppet.CandidateInput = chain.Count;
                    puppet.Mixer = AnimationMixerPlayable.Create(puppet.Graph, chain.Count + 1);
                    output.SetSourcePlayable(puppet.Mixer);
                    puppet.ChainPlayables = new AnimationClipPlayable[chain.Count];
                    puppet.ChainClips = new AnimationClip[chain.Count];
                    puppet.ChainFrames = new float[chain.Count];
                    for (int k = 0; k < chain.Count; k++)
                    {
                        puppet.ChainPlayables[k] = Connect(puppet, k, chain[k].clip, chain[k].frame);
                        puppet.ChainClips[k] = chain[k].clip;
                        puppet.ChainFrames[k] = chain[k].frame;
                    }
                    puppet.Candidate = Connect(puppet, puppet.CandidateInput, e.clip, e.frame);
                    puppet.Graph.Play();
                }

                puppet.Renderers = root.GetComponentsInChildren<Renderer>(true);
                _puppets.Add(puppet);
            }

            _holder.SetActive(true);
            foreach (Puppet p in _puppets) Measure(p);
            SpawnLive();
        }

        private static AnimationClipPlayable Connect(Puppet p, int input, AnimationClip clip, float frame)
        {
            if (clip == null) return default;
            var playable = AnimationClipPlayable.Create(p.Graph, clip);
            playable.SetApplyFootIK(false); // как контроллер ног: поза клипа без Foot IK
            playable.SetSpeed(0f);
            playable.SetTime(clip.frameRate > 0f ? frame / clip.frameRate : 0f);
            p.Graph.Connect(playable, 0, p.Mixer, input);
            return playable;
        }

        /// <summary>Место строки в ряду: при <see cref="onlyInspected"/> — все у манипулятора (видна одна).</summary>
        private int RowOffset(int index) => ShowAll ? index : 0;

        /// <summary>Показ манекена: в Play при живых аватарах позовые манекены не рисуются никогда.</summary>
        private void SetShown(Puppet p, bool shown)
        {
            shown &= !LiveMode;
            if (p.Shown == shown || p.Renderers == null) return;
            p.Shown = shown;
            foreach (Renderer r in p.Renderers)
            {
                if (r != null) r.enabled = shown;
            }
        }

        private void Clear()
        {
            ClearLive();
            foreach (Puppet p in _puppets)
            {
                if (p.Graph.IsValid()) p.Graph.Destroy();
                p.PoseHandler?.Dispose();
            }

            _puppets.Clear();
            if (_holder != null) DestroyImmediate(_holder);
            _holder = null;
        }

        private void OnDrawGizmos()
        {
            foreach (Puppet p in _puppets)
            {
                if (p.Shown) PosePuppet.DrawLegs(p.Animator);
            }

            DrawInspected();
            if (head == null || body == null) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(body.position, head.position);
            // Уровень камеры манипулятора — линия через весь ряд.
            Vector3 left = head.position + transform.right * 1f, right = head.position - transform.right * (spacing * _puppets.Count + 1f);
            Gizmos.DrawLine(left, right);
        }

        /// <summary>Инспектируемая строка — рамка у ног её аватара (живого в Play, позового в Edit Mode).</summary>
        private void DrawInspected()
        {
            if (InspectedRow < 0 || InspectedRow >= _puppets.Count) return;
            GameObject live = LiveAvatarOf(InspectedRow);
            Transform root = live != null ? live.transform : _puppets[InspectedRow].Root != null ? _puppets[InspectedRow].Root.transform : null;
            if (root == null) return;
            Gizmos.color = new Color(1f, 0.85f, 0.1f);
            Gizmos.matrix = Matrix4x4.TRS(root.position + Vector3.up * 0.01f, root.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.9f, 0.02f, 0.9f));
            Gizmos.DrawWireCube(Vector3.up * 1f, new Vector3(0.9f, 2f, 0.9f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
