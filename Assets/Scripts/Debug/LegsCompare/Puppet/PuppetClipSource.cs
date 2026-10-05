using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Шаг сценария из клипа: клип, сколько секунд играть и с какой скоростью (0,5 — шаг вдвое медленнее).</summary>
    [Serializable]
    public sealed class PuppetClipEntry
    {
        public AnimationClip clip;
        [Min(0.1f)] public float duration = 4f;
        [Range(0.1f, 2f)] public float speed = 1f;
    }

    /// <summary>
    /// Ввод «как живой человек»: клип с root motion играет на скрытом humanoid-риге (копия рига MEF), и каждый кадр поза
    /// середины глаз и кистей этого рига — манипулятору стенда. Голова идёт ровно так, как шла бы у человека, который
    /// исполняет клип: с покачиванием, наклонами и переносом веса.
    ///
    /// <para>
    /// Позы — в осях стенда относительно позы рига на старте. Поворот головы — отклонение кости головы от позы префаба рига
    /// (в ней голова смотрит вперёд). Кисти — в универсальных осях UltimateXR (<see cref="PuppetHands"/>): перевод из осей
    /// кости — <see cref="SetHandAxes"/> (риг — тот же скелет, что у аватаров MEF).
    /// </para>
    /// </summary>
    public sealed class PuppetClipSource
    {
        private const float CrossFade = 0.3f;

        private readonly GameObject _rig;
        private readonly Animator _animator;
        private readonly Transform _head, _leftEye, _rightEye, _leftHand, _rightHand;
        private readonly Quaternion _headRestInRoot;
        private Quaternion _leftAxes = Quaternion.identity, _rightAxes = Quaternion.identity;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private int _current;
        private float _fade = 1f;
        private Pose _start;

        public bool IsValid => _animator != null && _graph.IsValid();

        public PuppetClipSource(GameObject rigPrefab, Vector3 parkPosition)
        {
            if (rigPrefab == null) return;
            _rig = UnityEngine.Object.Instantiate(rigPrefab, parkPosition, Quaternion.identity);
            _rig.name = "PuppetStand_ClipRig";
            foreach (Renderer r in _rig.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (MonoBehaviour mb in _rig.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            _animator = Array.Find(_rig.GetComponentsInChildren<Animator>(true), a => a.isHuman);
            if (_animator == null) return;

            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _leftEye = _animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEye = _animator.GetBoneTransform(HumanBodyBones.RightEye);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _headRestInRoot = Quaternion.Inverse(_animator.transform.rotation) * _head.rotation;

            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = true;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("PuppetStand_Clip");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, 2);
            AnimationPlayableOutput.Create(_graph, "out", _animator).SetSourcePlayable(_mixer);
        }

        /// <summary>Поворот «универсальные оси → оси кости» кистей (<c>HandUniversalLocalAxes.UniversalToActualAxesRotation</c> аватара).</summary>
        public void SetHandAxes(Quaternion left, Quaternion right)
        {
            _leftAxes = left;
            _rightAxes = right;
        }

        /// <summary>Сменить клип: плавно (0,3 с), с текущего места рига. Первый вызов — с позы на старте.</summary>
        public void Play(AnimationClip clip, float speed, bool restart)
        {
            if (!IsValid || clip == null) return;
            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(true);
            playable.SetSpeed(speed);
            int next = restart ? 0 : 1 - _current;
            if (_mixer.GetInput(next).IsValid())
            {
                Playable old = _mixer.GetInput(next);
                _graph.Disconnect(_mixer, next);
                old.Destroy();
            }
            _graph.Connect(playable, 0, _mixer, next);
            if (restart)
            {
                _animator.transform.rotation = Quaternion.identity;
                _mixer.SetInputWeight(0, 1f);
                _mixer.SetInputWeight(1, 0f);
                _current = 0;
                _fade = 1f;
                _graph.Play();
                _graph.Evaluate(0f);
                _start = new Pose(_animator.transform.position, _animator.transform.rotation);
            }
            else
            {
                _current = next;
                _fade = 0f;
            }
        }

        /// <summary>Кадр: доводит смешивание. Позы читать после оценки Animator этого кадра — в <see cref="Read"/>.</summary>
        public void Tick(float dt)
        {
            if (!IsValid || _fade >= 1f) return;
            _fade = Mathf.Min(1f, _fade + dt / CrossFade);
            _mixer.SetInputWeight(_current, _fade);
            _mixer.SetInputWeight(1 - _current, 1f - _fade);
        }

        /// <summary>
        /// Тело (корень рига на полу — его ведёт root motion клипа), голова (середина глаз) и кисти (универсальные оси) в осях
        /// стенда относительно позы рига на старте.
        /// </summary>
        public void Read(out Pose body, out Pose head, out Pose left, out Pose right)
        {
            Quaternion inv = Quaternion.Inverse(_start.rotation);
            Transform root = _animator.transform;
            Vector3 rootPos = inv * (root.position - _start.position);
            Vector3 rootFwd = Vector3.ProjectOnPlane(inv * root.rotation * Vector3.forward, Vector3.up);
            body = new Pose(new Vector3(rootPos.x, 0f, rootPos.z), rootFwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(rootFwd, Vector3.up) : Quaternion.identity);
            Vector3 eye = _leftEye != null && _rightEye != null
                ? (_leftEye.position + _rightEye.position) * 0.5f
                : _head.position + _head.rotation * (_headRestInRoot * new Vector3(0f, 0.1f, 0.08f));
            head = new Pose(inv * (eye - _start.position), inv * _head.rotation * Quaternion.Inverse(_headRestInRoot));
            left = new Pose(inv * (_leftHand.position - _start.position), inv * _leftHand.rotation * Quaternion.Inverse(_leftAxes));
            right = new Pose(inv * (_rightHand.position - _start.position), inv * _rightHand.rotation * Quaternion.Inverse(_rightAxes));
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_rig != null) UnityEngine.Object.Destroy(_rig);
        }
    }
}
