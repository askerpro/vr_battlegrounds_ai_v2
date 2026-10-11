using Mirror;
using RootMotion.FinalIK;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using Unity.Profiling;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Опорная (левая) рука вооружённого клипового NPC: один проход готового LimbIK Final IK (<see cref="IKSolverLimb"/>)
    /// после видимого Animator. Пишет только повороты плеча, локтя и кисти левой руки; пальцы, правая кисть, корень,
    /// оружие и параметры Animator не меняются.
    ///
    /// <para>
    /// <b>Порядок кадра.</b> LateUpdate с порядком 10500: после проекции оружия <c>WeaponEquipmentBinding</c> (10000) и до
    /// gate <see cref="BotRuntimeController"/> (11000). Авто-обновление Final IK не используется: решатель принадлежит
    /// компоненту, без <c>LimbIK</c>/<c>SolverManager</c>, и вызывается вручную. Animator переписывает кости каждый кадр,
    /// поэтому проход не накапливается.
    /// </para>
    /// <para>
    /// <b>Цель.</b> Канон хвата UXR: <see cref="UxrGrabbableObject"/> оружия, grab point Support из
    /// <see cref="WeaponGripBinding"/> (точка 1), <see cref="UxrGrabPointInfo.GetGripPoseInfo(UxrAvatar, bool)"/> по цепочке
    /// префабов тела, <see cref="UxrGripPoseInfo.GripAlignTransformHandLeft"/>. Ссылка кэшируется, transform читается каждый
    /// кадр; позы и точки UXR не меняются. Левый <see cref="UxrGrabber"/> тела (ладонь) совмещается с align: цель кисти —
    /// align · (grabber в системе кисти)⁻¹.
    /// </para>
    /// <para>
    /// <b>Сеть.</b> Считается локально на сервере и на клиентах из уже синхронизированных корня, Animator и оружия;
    /// новых SyncVar/RPC нет. Включается только при вооружённой позе, действительной экипировке этого тела и найденной
    /// цели; вес меняется плавно (<see cref="BlendSeconds"/>). Компонент добавляет только генерация ClipBody
    /// (BotClipComposition) — в префабах аватаров игроков его нет.
    /// </para>
    /// <para>
    /// Включённый UXR-писатель костей на теле бота (контроллер аватара, IK-решатель UXR, трекинг, захватчик) — ошибка
    /// конфигурации: проход не выполняется, в лог — одна ошибка.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BotClipBody)), DefaultExecutionOrder(10500)]
    public sealed class BotSupportHandIK : MonoBehaviour
    {
        /// <summary>Время плавного включения/выключения веса, с.</summary>
        public const float BlendSeconds = .2f;
        /// <summary>Период повторного поиска экипировки этого тела, пока её нет, с.</summary>
        private const float RescanSeconds = .5f;
        private static readonly ProfilerMarker SolveMarker = new ProfilerMarker("BotSupportHandIK.Solve");
        private static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();

        private BotClipBody _clips;
        private UxrAvatar _avatar;
        private Animator _animator;
        private IKSolverLimb _solver;
        private Transform _upperArm, _forearm, _hand, _palm;
        private UxrStandardAvatarController[] _controllers;
        private UxrIKSolver[] _uxrSolvers;
        private UxrTrackingDevice[] _tracking;
        private UxrGrabber[] _grabbers;
        private WeaponEquipmentBinding _binding;
        private Transform _align;
        private float _nextScan;
        private bool _writerErrorLogged, _setupErrorLogged;

        /// <summary>Текущий вес прохода 0..1.</summary>
        public float Weight { get; private set; }
        /// <summary>Есть цель хвата этого кадра (оружие в руке, support-точка и align найдены).</summary>
        public bool HasTarget { get; private set; }
        /// <summary>Выбранная запись хвата: GUID префаба аватара или "default" (тихий fallback UXR); null — нет цели.</summary>
        public string GripEntry { get; private set; }
        /// <summary>Align-transform цели (только чтение, для стенда).</summary>
        public Transform SupportAlign => _align;
        /// <summary>Ладонь (левый UxrGrabber тела).</summary>
        public Transform Palm => _palm;
        /// <summary>Ошибка ладони до и после прохода этого кадра: расстояние, мм, и угол, градусы; NaN — не измерялась.</summary>
        public float PalmErrorBeforeMm { get; private set; } = float.NaN;
        public float PalmErrorAfterMm { get; private set; } = float.NaN;
        public float PalmAngleBeforeDeg { get; private set; } = float.NaN;
        public float PalmAngleAfterDeg { get; private set; } = float.NaN;
        /// <summary>Время прохода (цель + решатель) последнего кадра и накопленная статистика, микросекунды.</summary>
        public float LastSolveMicroseconds { get; private set; }
        public float MaxSolveMicroseconds { get; private set; }
        public double TotalSolveMicroseconds { get; private set; }
        public int SolveCount { get; private set; }
        /// <summary>Нарушение конфигурации (включённый UXR-писатель): проход не выполняется.</summary>
        public bool WriterConflict { get; private set; }

        private void LateUpdate()
        {
            if (!EnsureSolver())
            {
                Clear();
                return;
            }
            if (HasEnabledUxrWriter())
            {
                WriterConflict = true;
                if (!_writerErrorLogged)
                {
                    _writerErrorLogged = true;
                    GameLog.Player.Error($"[BotSupportHandIK] {name}: на теле бота включён UXR-писатель костей " +
                                         "(контроллер/IK/трекинг/захватчик) — ошибка конфигурации, опорная рука не решается.", this);
                }
                Clear();
                return;
            }
            WriterConflict = false;

            bool armed = _clips.Pose != BotClipPose.Unarmed && ResolveBinding();
            if (armed) ResolveAlign();
            else if (_binding == null) _align = null;
            // Без цели проход невозможен: вес сразу 0. Иначе вес идёт к 1/0 плавно (при выключении — к прежней цели).
            if (_align == null || !_align.gameObject.activeInHierarchy)
            {
                Clear();
                return;
            }
            Weight = Mathf.MoveTowards(Weight, armed ? 1f : 0f, Time.deltaTime / BlendSeconds);
            HasTarget = armed;
            if (Weight <= 0f)
            {
                PalmErrorBeforeMm = PalmErrorAfterMm = PalmAngleBeforeDeg = PalmAngleAfterDeg = float.NaN;
                return;
            }
            Solve();
        }

        /// <summary>Решатель на костях текущего Animator; пересоздаётся при смене Animator (повторный Bind).</summary>
        private bool EnsureSolver()
        {
            if (_clips == null) _clips = GetComponent<BotClipBody>();
            if (_clips == null || !_clips.IsConfigured || !_clips.Animator.isActiveAndEnabled) return false;
            if (_solver != null && _animator == _clips.Animator) return true;
            _solver = null;
            _animator = _clips.Animator;
            _avatar = GetComponent<UxrAvatar>();
            _upperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _forearm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _palm = null;
            foreach (var grabber in GetComponentsInChildren<UxrGrabber>(true))
                if (grabber.Side == UxrHandSide.Left) { _palm = grabber.transform; break; }
            _controllers = GetComponentsInChildren<UxrStandardAvatarController>(true);
            _uxrSolvers = GetComponentsInChildren<UxrIKSolver>(true);
            _tracking = GetComponentsInChildren<UxrTrackingDevice>(true);
            _grabbers = GetComponentsInChildren<UxrGrabber>(true);
            if (_avatar == null || _upperArm == null || _forearm == null || _hand == null || _palm == null)
            {
                if (!_setupErrorLogged)
                {
                    _setupErrorLogged = true;
                    GameLog.Player.Error($"[BotSupportHandIK] {name}: нет костей левой руки Animator или левого UxrGrabber — " +
                                         "опорная рука не решается.", this);
                }
                return false;
            }
            var solver = new IKSolverLimb(AvatarIKGoal.LeftHand)
            {
                bendModifier = IKSolverLimb.BendModifier.Animation, bendModifierWeight = 1f, maintainRotationWeight = 0f,
                IKPositionWeight = 0f, IKRotationWeight = 0f
            };
            if (!solver.SetChain(_upperArm, _forearm, _hand, transform)) return false;
            _solver = solver;
            return true;
        }

        private bool HasEnabledUxrWriter()
        {
            foreach (var component in _controllers) if (component != null && component.enabled) return true;
            foreach (var component in _uxrSolvers) if (component != null && component.enabled) return true;
            foreach (var component in _tracking) if (component != null && component.enabled) return true;
            foreach (var component in _grabbers) if (component != null && component.enabled) return true;
            return false;
        }

        /// <summary>Экипировка этого тела: кэш проверяется каждый кадр, поиск по spawned — не чаще <see cref="RescanSeconds"/>.</summary>
        private bool ResolveBinding()
        {
            if (_binding != null && _binding.HasValidAttachment && _binding.Owner == _avatar) return true;
            if (_binding != null) { _binding = null; _align = null; }
            if (Time.unscaledTime < _nextScan) return false;
            _nextScan = Time.unscaledTime + RescanSeconds;
            var spawned = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
            foreach (var identity in spawned.Values)
            {
                var binding = identity != null ? identity.GetComponent<WeaponEquipmentBinding>() : null;
                if (binding == null || !binding.HasValidAttachment || binding.Owner != _avatar) continue;
                _binding = binding;
                _align = null;
                GripEntry = null;
                return true;
            }
            return false;
        }

        /// <summary>Align support-точки по цепочке префабов тела; ссылка кэшируется на время экипировки.</summary>
        private void ResolveAlign()
        {
            if (_align != null || GripEntry != null) return;
            GripEntry = "none";
            var firearm = _binding.GetComponentInChildren<UxrFirearmWeapon>(true);
            if (!WeaponGripBinding.TryResolve(firearm, out var grip) || grip.Support < 0) return;
            var point = grip.Grip.GetGrabPoint(grip.Support);
            var info = point != null ? point.GetGripPoseInfo(_avatar) : null;
            if (info == null) return;
            GripEntry = info == point.DefaultGripPoseInfo ? "default" : info.AvatarPrefabGuid;
            _align = info.GripAlignTransformHandLeft;
        }

        private void Solve()
        {
            Clock.Restart();
            using (SolveMarker.Auto())
            {
                // Ладонь — ребёнок кисти: её смещение в системе кисти постоянно; цель кисти совмещает ладонь с align.
                Quaternion handRotation = _hand.rotation;
                Quaternion inverseHand = Quaternion.Inverse(handRotation);
                Quaternion palmInHand = inverseHand * _palm.rotation;
                Vector3 palmOffset = inverseHand * (_palm.position - _hand.position);
                Vector3 alignPosition = _align.position;
                Quaternion alignRotation = _align.rotation;
                PalmErrorBeforeMm = Vector3.Distance(_palm.position, alignPosition) * 1000f;
                PalmAngleBeforeDeg = Quaternion.Angle(_palm.rotation, alignRotation);
                Quaternion wristRotation = alignRotation * Quaternion.Inverse(palmInHand);
                _solver.IKPosition = alignPosition - wristRotation * palmOffset;
                _solver.IKRotation = wristRotation;
                _solver.IKPositionWeight = Weight;
                _solver.IKRotationWeight = Weight;
                _solver.Update();
                PalmErrorAfterMm = Vector3.Distance(_palm.position, alignPosition) * 1000f;
                PalmAngleAfterDeg = Quaternion.Angle(_palm.rotation, alignRotation);
            }
            Clock.Stop();
            float microseconds = (float)(Clock.ElapsedTicks * 1e6 / System.Diagnostics.Stopwatch.Frequency);
            LastSolveMicroseconds = microseconds;
            MaxSolveMicroseconds = Mathf.Max(MaxSolveMicroseconds, microseconds);
            TotalSolveMicroseconds += microseconds;
            SolveCount++;
        }

        private void Clear()
        {
            Weight = 0f;
            HasTarget = false;
            PalmErrorBeforeMm = PalmErrorAfterMm = PalmAngleBeforeDeg = PalmAngleAfterDeg = float.NaN;
        }

        private void OnDisable() => Clear();
    }
}
