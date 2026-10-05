using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Стенд глазами: клип локомоции на двух скелетах рядом, прямо в Edit Mode (Play не нужен), без решателей ног и Legs
    /// Animator. Слева — манекен демо Legs Animator (чистый клип на его Animator), справа — MEF: клип играет копия рига
    /// (с неё аватар UxrLegs берёт цели стоп), меш MEF повторяет её позу. Клип, Foot IK, фаза и скорость — в инспекторе.
    ///
    /// <para>
    /// Живёт в стенде-плейграунде <see cref="AvatarPuppetStand"/> (режим <c>ClipPreview</c>, дочерний объект стенда); старая
    /// отдельная сцена <c>ClipFeetPreview</c> заменена им. Замер стоп по фазам — <see cref="MeasurePhases"/> (кнопка в
    /// инспекторе стенда), тем же <see cref="FootProbe"/>, что и замеры стенда.
    /// </para>
    ///
    /// <para>
    /// Зачем: заворот стопы и подошва в полу видны на обоих скелетах одинаково — значит, дело в клипе и его импорте, а не в
    /// пересадке на MEF и не в решателе. Сцену собирает меню <c>Tools/VR Battlegrounds/Debug/Clip Feet Preview</c>.
    /// </para>
    /// </summary>
    [ExecuteAlways]
    public sealed class ClipFeetPreview : MonoBehaviour
    {
        [Tooltip("Клип (FBX из Art/Animations/Locomotion/Mixamo*/ или клип Final IK для сравнения).")]
        public AnimationClip clip;

        [Tooltip("Foot IK гуманоида — как у состояний контроллера UxrLegs (включён).")]
        public bool footIK = true;

        [Tooltip("Проигрывать. Выкл — поза в фазе normalizedTime (двигай ползунок).")]
        public bool play = true;

        [Tooltip("Фаза клипа, 0…1 (при play — текущая).")]
        [Range(0f, 1f)]
        public float normalizedTime;

        [Tooltip("Скорость проигрывания.")]
        [Range(0.05f, 2f)]
        public float speed = 0.5f;

        [Tooltip("Второй клип для сравнения (например, поза на колене). Задан — справа ещё два MEF: этот клип и смесь двух.")]
        public AnimationClip compareClip;

        [Tooltip("Фаза второго клипа, 0…1 (не проигрывается).")]
        [Range(0f, 1f)]
        public float compareTime;

        [Tooltip("Вес второго клипа в смеси (как смешивание Animator: в мышцах гуманоида).")]
        [Range(0f, 1f)]
        public float compareBlend = 0.5f;

        [Tooltip("Манекен демо Legs Animator (FAnnequin_IdleGlue).")]
        public GameObject mannequinPrefab;

        [Tooltip("Копия рига MEF (MEF_Base_Avatar_LocomotionRig) — на ней играет клип.")]
        public GameObject mefRigPrefab;

        [Tooltip("Префаб MEF с мешем — повторяет позу копии.")]
        public GameObject mefMeshPrefab;

        [Tooltip("Расстояние между скелетами, м.")]
        public float spacing = 1.2f;

        private readonly List<Puppet> _puppets = new List<Puppet>();
        private double _lastTime;
        private AnimationClip _builtClip, _builtCompare;
        private bool _builtFootIK;

        /// <summary>Скелеты превью: имя, корень видимого скелета и Animator клипа (для замеров извне).</summary>
        public IEnumerable<(string name, GameObject root, Animator animator)> Puppets => _puppets.Select(p => (p.Name, p.Root, p.Animator));

        /// <summary>Пересобрать при необходимости и поставить позу в фазе <paramref name="phase"/> (замеры, снимки).</summary>
        public void PoseAt(float phase)
        {
            if (clip == null) return;
            if (NeedsBuild) Build();
            normalizedTime = phase;
            foreach (Puppet p in _puppets) Pose(p, phase);
        }

        private bool NeedsBuild => _puppets.Count == 0 || _builtClip != clip || _builtFootIK != footIK || _builtCompare != compareClip;

        private sealed class Puppet
        {
            public GameObject Root;           // что видно (манекен или меш MEF)
            public GameObject Driver;         // на чём играет клип (сам манекен или копия рига)
            public Animator Animator;
            public PlayableGraph Graph;
            public AnimationClipPlayable Playable;
            public AnimationClipPlayable Compare;    // второй клип (только у «смеси»)
            public AnimationMixerPlayable Mixer;
            public bool Mixed;
            public bool CompareOnly;                 // скелет играет только второй клип
            public readonly List<(Transform source, Transform target)> Bones = new List<(Transform, Transform)>();
            public Vector3 Origin;
            public string Name;
            public FootProbe Left, Right;
        }

        private void OnEnable()
        {
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            Teardown();
        }

        private void Tick()
        {
            if (this == null) return;
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - _lastTime);
            _lastTime = now;

            if (clip == null)
            {
                Teardown();
                return;
            }

            if (NeedsBuild) Build();
            if (play) normalizedTime = Mathf.Repeat(normalizedTime + dt * speed / Mathf.Max(clip.length, 1e-3f), 1f);

            // На месте: root motion не уводит скелет со своей отметки.
            foreach (Puppet p in _puppets) Pose(p, normalizedTime);

            SceneView.RepaintAll();
        }

        private void Build()
        {
            Teardown();
            _builtClip = clip;
            _builtFootIK = footIK;
            _builtCompare = compareClip;
            AddPuppet("mannequin", mannequinPrefab, null, Vector3.zero);
            AddPuppet("MEF", mefRigPrefab, mefMeshPrefab, Vector3.right * spacing);
            if (compareClip == null) return;
            // Справа: смесь двух клипов, затем второй клип.
            AddPuppet("MEF mix", mefRigPrefab, mefMeshPrefab, Vector3.right * spacing * 2f, mixed: true);
            AddPuppet("MEF compare", mefRigPrefab, mefMeshPrefab, Vector3.right * spacing * 3f, compareOnly: true);
        }

        private void AddPuppet(string name, GameObject driverPrefab, GameObject meshPrefab, Vector3 origin, bool mixed = false, bool compareOnly = false)
        {
            if (driverPrefab == null) return;

            GameObject driver = Spawn(driverPrefab, origin);
            Animator animator = driver.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isHuman);
            if (animator == null)
            {
                DestroyImmediate(driver);
                return;
            }

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.enabled = true;
            var puppet = new Puppet { Root = driver, Driver = driver, Animator = animator, Origin = origin, Name = name };
            if (meshPrefab != null)
            {
                puppet.Root = Spawn(meshPrefab, origin);
                var byName = puppet.Root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                foreach (Transform t in driver.GetComponentsInChildren<Transform>(true))
                    if (t != driver.transform && byName.TryGetValue(t.name, out Transform target)) puppet.Bones.Add((t, target));
            }

            // Пробы стоп — в позе префаба (подошва на полу), на видимом скелете.
            Transform Visible(HumanBodyBones bone)
            {
                Transform t = animator.GetBoneTransform(bone);
                foreach ((Transform source, Transform target) in puppet.Bones)
                    if (source == t) return target;
                return t;
            }
            puppet.Left = new FootProbe(puppet.Root.transform, Visible(HumanBodyBones.LeftFoot), Visible(HumanBodyBones.LeftToes), true);
            puppet.Right = new FootProbe(puppet.Root.transform, Visible(HumanBodyBones.RightFoot), Visible(HumanBodyBones.RightToes), false);

            puppet.Graph = PlayableGraph.Create("ClipFeetPreview");
            puppet.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            puppet.Playable = AnimationClipPlayable.Create(puppet.Graph, compareOnly ? compareClip : clip);
            puppet.Playable.SetApplyFootIK(footIK);
            puppet.Mixed = mixed;
            puppet.CompareOnly = compareOnly;
            Playable source = puppet.Playable;
            if (mixed)
            {
                puppet.Compare = AnimationClipPlayable.Create(puppet.Graph, compareClip);
                puppet.Compare.SetApplyFootIK(footIK);
                puppet.Mixer = AnimationMixerPlayable.Create(puppet.Graph, 2);
                puppet.Graph.Connect(puppet.Playable, 0, puppet.Mixer, 0);
                puppet.Graph.Connect(puppet.Compare, 0, puppet.Mixer, 1);
                source = puppet.Mixer;
            }

            AnimationPlayableOutput.Create(puppet.Graph, "out", animator).SetSourcePlayable(source);
            _puppets.Add(puppet);
        }

        /// <summary>
        /// Замер стоп клипа по фазам (<paramref name="samples"/> точек цикла) на обоих скелетах: разворот, завал, подошва, опора —
        /// в <paramref name="folder"/>/feet_phases.txt. Возвращает путь.
        /// </summary>
        public string MeasurePhases(string folder, int samples = 40)
        {
            if (clip == null) return null;
            if (NeedsBuild) Build();
            bool wasPlaying = play;
            play = false;
            var sb = new StringBuilder();
            sb.AppendLine($"клип {clip.name}, Foot IK {footIK}");
            sb.AppendLine("фаза | скелет | разворот Л/П, ° (> 0 наружу) | завал Л/П, ° (> 0 внутрь) | подошва Л/П, см | опора Л/П");
            foreach (Puppet p in _puppets) { p.Left.ResetSegment(); p.Right.ResetSegment(); }
            float dt = clip.length / samples;
            for (int i = 0; i <= samples; i++)
            {
                float phase = (float)i / samples;
                foreach (Puppet p in _puppets)
                {
                    Pose(p, phase);
                    p.Left.Measure(p.Driver.transform.position, p.Driver.transform.forward, dt);
                    p.Right.Measure(p.Driver.transform.position, p.Driver.transform.forward, dt);
                    if (i == 0) continue;   // первая точка — без скорости опоры
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000} | {1} | {2:0}/{3:0} | {4:0}/{5:0} | {6:0.0}/{7:0.0} | {8}/{9}",
                        phase, p.Name, p.Left.Yaw, p.Right.Yaw, p.Left.Roll, p.Right.Roll, p.Left.Sole * 100f, p.Right.Sole * 100f, p.Left.Planted ? 1 : 0, p.Right.Planted ? 1 : 0));
                }
            }
            sb.AppendLine();
            foreach (Puppet p in _puppets) sb.AppendLine($"итог {p.Name}: разворот в опоре Л/П | завал Л/П | подошва мин Л/П | подошва в опоре Л/П = " + FootProbe.Summary(p.Left, p.Right));
            play = wasPlaying;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "feet_phases.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private void Pose(Puppet p, float phase)
        {
            if (p.CompareOnly) p.Playable.SetTime(compareClip.length * compareTime);
            else p.Playable.SetTime(clip.length * phase);
            if (p.Mixed)
            {
                p.Compare.SetTime(compareClip.length * compareTime);
                p.Mixer.SetInputWeight(0, 1f - compareBlend);
                p.Mixer.SetInputWeight(1, compareBlend);
            }

            p.Graph.Evaluate();
            p.Driver.transform.SetPositionAndRotation(transform.position + transform.rotation * p.Origin, transform.rotation);
            foreach ((Transform source, Transform target) in p.Bones)
                target.SetPositionAndRotation(source.position, source.rotation);
        }

        /// <summary>Экземпляр без сохранения в сцену, все скрипты выключены (Legs Animator, UltimateXR, демо-скрипты).</summary>
        private GameObject Spawn(GameObject prefab, Vector3 origin)
        {
            GameObject go = Instantiate(prefab, transform.position + origin, transform.rotation, transform);
            go.hideFlags = HideFlags.DontSave;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            return go;
        }

        private void Teardown()
        {
            foreach (Puppet p in _puppets)
            {
                if (p.Graph.IsValid()) p.Graph.Destroy();
                if (p.Root != null) DestroyImmediate(p.Root);
                if (p.Driver != null && p.Driver != p.Root) DestroyImmediate(p.Driver);
            }

            _puppets.Clear();
        }
    }
}
