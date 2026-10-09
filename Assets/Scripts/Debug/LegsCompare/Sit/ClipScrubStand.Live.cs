using System.Collections.Generic;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Как живые аватары стенда перемотки получают ноги из клипа строки.</summary>
    public enum LiveLegsMode
    {
        /// <summary>
        /// Как в игре при ногах на решателе: IK стоп в позы стоп клипа, подошва ботинка на полу (Ankle Height настроек
        /// ног аватара), колено — к колену клипа. Разницу высоты таза BodyIK и клипа гасит сгиб ноги.
        /// </summary>
        [InspectorName("Как в игре: IK стоп, подошва на полу")]
        GameIK,

        /// <summary>
        /// Анализ клипа: кости ног — локальные повороты клипа как есть (как AfterSolve игры на колене и сидя), без пола.
        /// Пол стенда опускается до самой низкой точки подошв — ботинки не прячутся.
        /// </summary>
        [InspectorName("Анализ клипа: поза как есть, пол ниже")]
        ClipPose,
    }

    /// <summary>
    /// Play стенда перемотки: живые аватары UltimateXR (<see cref="livePrefab"/>) по строкам. Верх — UltimateXR как в игре:
    /// камера — голова манипулятора, кисти — ручки <see cref="leftHand"/>/<see cref="rightHand"/> (как у стенда-кукловода),
    /// BodyIK ставит таз и корпус, решатели — руки. Ноги — после всех стадий UltimateXR, из позового манекена строки (клип на
    /// своём кадре), режимом <see cref="liveLegs"/>. <see cref="pelvisFromClip"/> — план «таз клипу»: поворот кости между
    /// тазом BodyIK и бёдрами (у MEF <c>CC_Base_Pelvis</c>) из клипа; в игре его пока нет. Позовые манекены в Play скрыты.
    /// </summary>
    public partial class ClipScrubStand
    {
        [Header("Play: живые аватары UltimateXR, ноги — клип строки")]
        [Tooltip("Аватар UltimateXR строк в Play (Optimized_MEF_Player). Пусто — в Play те же позовые манекены.")]
        public GameObject livePrefab;

        [Tooltip("Ручка левого контроллера (ребёнок тела манипулятора): поза кисти в универсальных осях UltimateXR.")]
        public Transform leftHand;

        [Tooltip("Ручка правого контроллера.")]
        public Transform rightHand;

        [Header("Ноги живых аватаров — переключатель режима")]
        [Tooltip("Как в игре: IK стоп по клипу, подошва на полу. Анализ клипа: поза клипа как есть, пол опускается под подошвы.")]
        public LiveLegsMode liveLegs = LiveLegsMode.GameIK;

        [Tooltip("Разделение таза на две кости (план, в игре пока нет): CC_Base_Hip — BodyIK (верх), CC_Base_Pelvis — родитель только бёдер — получает наклон и разворот таза из клипа. Корпус и руки не трогает.")]
        public bool pelvisFromClip;

        [Tooltip("Как в игре: пока Legs_Crouch ног аватара (формула игры по доле роста) ниже Clip Legs Start (обычно 0,5), ноги ведёт сам аватар — UxrAnimatedLegs с шагами, в том числе в приседе; дальше ноги плавно переходят на ноги стенда (к 1 — полностью). Выключено — ноги стенда всегда.")]
        public bool nativeLegsWhenStanding = true;

        [Tooltip("Пол стенда (опускается в режиме ClipPose).")]
        public Transform floor;

        [Tooltip("ClipPose: пол — на самой низкой точке подошв всех строк (иначе — на analysisFloorDrop ниже нуля).")]
        public bool analysisFloorAuto = true;

        [Tooltip("ClipPose без авто: на сколько опустить пол, м.")]
        public float analysisFloorDrop = 0.1f;

        /// <summary>Глаза над костью головы, м: голова манипулятора — камера (глаза), кадр — где голова клипа ниже на столько.</summary>
        public const float EyeAboveHead = 0.1f;

        private const string BodyPivotName = "Dummy Forward";

        private sealed class LiveLeg
        {
            public UxrLegIKSolver Solver;
            public Transform BendGoal;
            public Transform PoseThigh, PoseCalf, PoseFoot, PoseToes;
            public readonly List<(Transform live, Transform pose)> Bones = new List<(Transform live, Transform pose)>();
            public Vector3[] SoleInFoot, SoleInToes; // точки подошвы в осях стопы и пальцев (как CaptureSole ног UltimateXR)
            public Quaternion[] Native;               // поза ног самого аватара в кадре — для перехода на ноги стенда
        }

        private sealed class LiveRow
        {
            public UxrAvatar Avatar;
            public Transform Root;
            public Transform Pivot;              // Dummy Forward BodyIK — место и рысканье корпуса
            public Transform LeftHand, RightHand;
            public Quaternion LeftAxes, RightAxes;
            public Transform Pelvis, PosePelvis; // кость между тазом и бёдрами (MEF: CC_Base_Pelvis) или null
            public Puppet Pose;
            public int Index;                    // строка списка
            public UxrStandardAvatarController Controller;
            public readonly List<LiveLeg> Legs = new List<LiveLeg>();
        }

        private readonly List<LiveRow> _live = new List<LiveRow>();
        private bool _liveSubscribed;
        private System.IDisposable _focusPause;
        private float _floorBaseY;

        private bool LiveMode => Application.isPlaying && livePrefab != null;

        private void SpawnLive()
        {
            if (!LiveMode) return;
            if (floor != null) _floorBaseY = floor.position.y;
            for (int i = 0; i < _puppets.Count; i++)
            {
                if (!ShowAll && i != InspectedRow) continue; // один живой аватар — инспектируемой строки
                Puppet pose = _puppets[i];
                GameObject go = Instantiate(livePrefab, transform.TransformPoint(new Vector3(-RowOffset(i) * spacing, 0f, 0f)), transform.rotation);
                go.name = $"Live {i}: {RowLabel(i)}";
                var avatar = go.GetComponent<UxrAvatar>();
                if (avatar == null)
                {
                    Destroy(go);
                    continue;
                }

                avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
                foreach (Camera cam in go.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
                foreach (AudioListener listener in go.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;

                var row = new LiveRow
                {
                    Index = i,
                    Controller = go.GetComponent<UxrStandardAvatarController>(),
                    Avatar = avatar,
                    Root = go.transform,
                    Pose = pose,
                    LeftHand = avatar.GetHandBone(UxrHandSide.Left),
                    RightHand = avatar.GetHandBone(UxrHandSide.Right),
                    LeftAxes = HandAxes(avatar, UxrHandSide.Left),
                    RightAxes = HandAxes(avatar, UxrHandSide.Right),
                };
                MapLegs(row, avatar, pose);
                TextMesh label = PosePuppet.AddLabel(go.transform);
                label.characterSize = 0.05f;
                label.text = i.ToString();
                _live.Add(row);

            }

            if (!_liveSubscribed)
            {
                if (!DebugBootstrapGate.ManagedLaunchActive) _focusPause = UxrManager.BeginEditorFocusPauseOverride(false);
                UxrManager.StageUpdated += OnLiveStage;
                _liveSubscribed = true;
            }
        }

        /// <summary>Play с одним живым аватаром: инспектируемая строка сменилась — живой аватар пересоздаётся под неё.</summary>
        private void SyncLiveRow()
        {
            if (!LiveMode || ShowAll) return;
            if (_live.Count == 1 && _live[0].Index == InspectedRow) return;
            if (InspectedRow < 0 || InspectedRow >= _puppets.Count) return;
            ClearLive();
            SpawnLive();
        }

        private void ClearLive()
        {
            if (_liveSubscribed)
            {
                UxrManager.StageUpdated -= OnLiveStage;
                _focusPause?.Dispose(); _focusPause = null;
                _liveSubscribed = false;
            }

            foreach (LiveRow row in _live)
            {
                if (row.Root != null) Destroy(row.Root.gameObject);
                foreach (LiveLeg leg in row.Legs)
                {
                    if (leg.BendGoal != null) Destroy(leg.BendGoal.gameObject);
                }
            }

            _live.Clear();
            if (floor != null && Application.isPlaying) SetFloorY(_floorBaseY);
        }

        /// <summary>
        /// Кости ног живого аватара (разметка UltimateXR) ↔ кости позового манекена того же скелета (по имени); решатель ноги
        /// и точки подошвы на каждую ногу; кость таза ног — родитель бедра, если он не таз BodyIK (MEF: CC_Base_Pelvis).
        /// </summary>
        private void MapLegs(LiveRow row, UxrAvatar avatar, Puppet pose)
        {
            if (pose.Animator == null) return;
            var byName = new Dictionary<string, Transform>();
            foreach (Transform t in pose.Root.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
            float ankle = avatar.GetComponent<UxrStandardAvatarController>() is UxrStandardAvatarController c && c.Legs != null ? c.Legs.ankleHeight : 0.135f;

            UxrAvatarRig rig = avatar.AvatarRig;
            Transform thighParent = rig.LeftLeg.UpperLeg != null ? rig.LeftLeg.UpperLeg.parent : null;
            if (thighParent != null && thighParent != rig.Hips && byName.TryGetValue(thighParent.name, out Transform posePelvis))
            {
                row.Pelvis = thighParent;
                row.PosePelvis = posePelvis;
            }

            // Подошва — по позе «стоя» цепочки (стопа на полу), как CaptureSole ног UltimateXR по позе префаба копии рига.
            if (ChainOf(pose.Row).Count > 0) EvaluateChain(pose, 0, 0f);
            foreach (UxrAvatarLeg avatarLeg in new[] { rig.LeftLeg, rig.RightLeg })
            {
                var leg = new LiveLeg();
                foreach (Transform bone in new[] { avatarLeg.UpperLeg, avatarLeg.LowerLeg, avatarLeg.Foot, avatarLeg.Toes })
                {
                    if (bone != null && byName.TryGetValue(bone.name, out Transform source)) leg.Bones.Add((bone, source));
                }

                leg.PoseThigh = Find(byName, avatarLeg.UpperLeg);
                leg.PoseCalf = Find(byName, avatarLeg.LowerLeg);
                leg.PoseFoot = Find(byName, avatarLeg.Foot);
                leg.PoseToes = Find(byName, avatarLeg.Toes);
                if (leg.PoseFoot == null) continue;

                leg.Solver = new UxrLegIKSolver();
                leg.Solver.Initialize(avatarLeg.UpperLeg, avatarLeg.LowerLeg, avatarLeg.Foot, avatarLeg.Toes, avatar.transform.right);
                if (!leg.Solver.Initialized) leg.Solver = null;
                leg.BendGoal = new GameObject($"KneeGoal ({avatar.name})") { hideFlags = HideFlags.HideAndDontSave }.transform;
                CaptureSole(leg, pose.Root.transform, ankle);
                row.Legs.Add(leg);
            }
        }

        private static Transform Find(Dictionary<string, Transform> byName, Transform bone) =>
            bone != null && byName.TryGetValue(bone.name, out Transform t) ? t : null;

        /// <summary>Точки подошвы в осях стопы и пальцев манекена: пятка, под лодыжкой, под пальцами, носок (как ноги UltimateXR).</summary>
        private static void CaptureSole(LiveLeg leg, Transform root, float ankleHeight)
        {
            Vector3 up = root.up;
            Vector3 ankle = leg.PoseFoot.position;
            Vector3 toes = leg.PoseToes != null ? leg.PoseToes.position : ankle + root.forward * ankleHeight;
            Vector3 dir = Vector3.ProjectOnPlane(toes - ankle, up).normalized;
            Vector3 underAnkle = ankle - up * ankleHeight;
            Vector3 heel = underAnkle - dir * (0.55f * ankleHeight);
            Vector3 ball = toes - up * Vector3.Dot(toes - underAnkle, up);
            Vector3 tip = ball + dir * (0.3f * ankleHeight);
            Transform toeBone = leg.PoseToes != null ? leg.PoseToes : leg.PoseFoot;
            leg.SoleInFoot = new[] { Local(leg.PoseFoot, heel), Local(leg.PoseFoot, underAnkle) };
            leg.SoleInToes = new[] { Local(toeBone, ball), Local(toeBone, tip) };
        }

        private static Vector3 Local(Transform bone, Vector3 world) => Quaternion.Inverse(bone.rotation) * (world - bone.position);

        private static Quaternion HandAxes(UxrAvatar avatar, UxrHandSide side)
        {
            var info = avatar.AvatarRigInfo?.GetArmInfo(side);
            return info?.HandUniversalLocalAxes != null ? info.HandUniversalLocalAxes.UniversalToActualAxesRotation : Quaternion.identity;
        }

        private void OnLiveStage(UxrUpdateStage stage)
        {
            if (stage == UxrUpdateStage.Update) PlaceLiveInputs();
            else if (stage == UxrUpdateStage.PostProcess) ApplyLiveLegs();
        }

        /// <summary>До IK кадра: камера и кисти каждого живого аватара — по манипулятору (в осях стенда, от корня строки).</summary>
        private void PlaceLiveInputs()
        {
            if (head == null) return;
            Pose h = ToStand(head), l = ToStand(leftHand), r = ToStand(rightHand);
            foreach (LiveRow row in _live)
            {
                if (row.Avatar == null) continue;
                Transform cam = row.Avatar.CameraTransform;
                if (cam != null) cam.SetPositionAndRotation(row.Root.TransformPoint(h.position), row.Root.rotation * h.rotation);
                if (row.LeftHand != null && leftHand != null) row.LeftHand.SetPositionAndRotation(row.Root.TransformPoint(l.position), row.Root.rotation * l.rotation * row.LeftAxes);
                if (row.RightHand != null && rightHand != null) row.RightHand.SetPositionAndRotation(row.Root.TransformPoint(r.position), row.Root.rotation * r.rotation * row.RightAxes);
            }
        }

        /// <summary>
        /// После всех стадий UltimateXR (таз и корпус уже решены BodyIK): таз ног из клипа (если включено), затем ноги —
        /// IK по клипу с подошвой на полу или поза клипа как есть; в ClipPose пол опускается под подошвы.
        /// </summary>
        private void ApplyLiveLegs()
        {
            float lowestSole = float.MaxValue;
            float floorY = transform.position.y;
            foreach (LiveRow row in _live)
            {
                if (row.Avatar == null) continue;
                float w = StandLegsWeight(row);
                if (w <= 0f) continue; // стоя — ноги ведёт аватар (шаги UxrAnimatedLegs), стенд не вмешивается

                (Vector3 framePos, Quaternion frameRot) = ClipFrame(row);
                Quaternion nativePelvis = row.Pelvis != null ? row.Pelvis.localRotation : Quaternion.identity;
                if (pelvisFromClip && row.Pelvis != null && row.PosePelvis != null)
                {
                    row.Pelvis.rotation = frameRot * row.PosePelvis.rotation;
                    row.Pelvis.localRotation = Quaternion.Slerp(nativePelvis, row.Pelvis.localRotation, w);
                }

                foreach (LiveLeg leg in row.Legs)
                {
                    // Поза ног самого аватара (его решатель/клипы этого кадра) — для плавного перехода на ноги стенда.
                    if (leg.Native == null || leg.Native.Length != leg.Bones.Count) leg.Native = new Quaternion[leg.Bones.Count];
                    for (int b = 0; b < leg.Bones.Count; b++) leg.Native[b] = leg.Bones[b].live.localRotation;

                    // Начальная поза решателя — поза клипа (как в игре решатель начинает с прошлой позы ноги).
                    foreach ((Transform live, Transform pose) in leg.Bones) live.localRotation = pose.localRotation;
                    if (liveLegs == LiveLegsMode.GameIK && leg.Solver != null) SolveLeg(leg, row, framePos, frameRot, floorY);

                    if (w < 1f)
                    {
                        for (int b = 0; b < leg.Bones.Count; b++) leg.Bones[b].live.localRotation = Quaternion.Slerp(leg.Native[b], leg.Bones[b].live.localRotation, w);
                    }

                    lowestSole = Mathf.Min(lowestSole, SoleHeight(leg, leg.Solver != null ? leg.Solver.Foot : null, leg.Solver != null ? leg.Solver.Toes : null, floorY));
                }
            }

            if (floor == null) return;
            if (liveLegs == LiveLegsMode.ClipPose)
            {
                float drop = analysisFloorAuto ? Mathf.Min(0f, lowestSole < float.MaxValue ? lowestSole : 0f) : -Mathf.Abs(analysisFloorDrop);
                SetFloorY(_floorBaseY + drop);
            }
            else
            {
                SetFloorY(_floorBaseY);
            }
        }

        /// <summary>
        /// Доля ног стенда у живого аватара — по настоящему <c>Legs_Crouch</c> ног аватара (формула игры: доля роста, мёртвая
        /// зона), а не по уровню цепочки стенда: стенд линейно между позами и перехватывал ноги уже в лёгком полуприседе, где
        /// игра ещё шагает. До <c>Clip Legs Start</c> (0,5) ноги ведёт аватар (шаги, в том числе в приседе), от него до 1 —
        /// плавно (SmoothStep) на ноги стенда, глубже — полностью. Нет ног аватара — по уровню цепочки стенда.
        /// </summary>
        private float StandLegsWeight(LiveRow row)
        {
            if (!nativeLegsWhenStanding || row.Controller == null || row.Controller.AnimatedLegs == null) return 1f;
            float start = row.Controller.Legs != null ? row.Controller.Legs.clipLegsStart : 0.5f;
            UltimateXR.Animation.IK.UxrAnimatedLegs.CrouchDebugState crouch = row.Controller.AnimatedLegs.CrouchDebug;
            float level;
            if (crouch.Valid)
            {
                level = crouch.Crouch;
            }
            else
            {
                Puppet p = row.Pose;
                if (p == null || p.ChainHeads == null || p.ChainHeads.Length == 0 || !followHead || head == null) return 1f;
                level = p.HeadFrame < 0f ? p.ChainPose + p.ChainBlend : p.ChainHeads.Length - 1 + p.CandidateWeight;
            }

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, 1f, level));
        }

        /// <summary>
        /// Опора позы клипа в мире живого аватара — как корень копии рига в игре: под <c>Dummy Forward</c> BodyIK на полу,
        /// рысканье — его. Возвращает место и поворот, которые переводят позу из осей корня манекена в мир аватара.
        /// </summary>
        private (Vector3 position, Quaternion rotation) ClipFrame(LiveRow row)
        {
            if (row.Pivot == null) row.Pivot = row.Avatar.transform.Find(BodyPivotName);
            Transform pivot = row.Pivot != null ? row.Pivot : row.Root;
            Vector3 up = row.Avatar.transform.up;
            Vector3 onFloor = pivot.position + Vector3.Project(row.Avatar.transform.position - pivot.position, up);
            Vector3 forward = Vector3.ProjectOnPlane(pivot.forward, up);
            Quaternion yaw = Quaternion.LookRotation(forward.sqrMagnitude > 1e-6f ? forward : row.Avatar.transform.forward, up);
            Transform poseRoot = row.Pose.Root.transform;
            return (onFloor - yaw * Quaternion.Inverse(poseRoot.rotation) * poseRoot.position, yaw * Quaternion.Inverse(poseRoot.rotation));
        }

        /// <summary>IK ноги как в игре: цели — стопа и пальцы клипа, подошва не ниже пола; колено — к колену клипа.</summary>
        private static void SolveLeg(LiveLeg leg, LiveRow row, Vector3 framePos, Quaternion frameRot, float floorY)
        {
            Vector3 footPos = framePos + frameRot * leg.PoseFoot.position;
            Quaternion footRot = frameRot * leg.PoseFoot.rotation;
            Vector3 toePos = leg.PoseToes != null ? framePos + frameRot * leg.PoseToes.position : footPos;
            Quaternion toeRot = leg.PoseToes != null ? frameRot * leg.PoseToes.rotation : footRot;

            // Подошва ботинка (Ankle Height) не уходит в пол: стопа и пальцы поднимаются одним телом.
            float lowest = float.MaxValue;
            foreach (Vector3 p in leg.SoleInFoot) lowest = Mathf.Min(lowest, (footPos + footRot * p).y - floorY);
            foreach (Vector3 p in leg.SoleInToes) lowest = Mathf.Min(lowest, (toePos + toeRot * p).y - floorY);
            Vector3 lift = lowest < 0f ? Vector3.up * -lowest : Vector3.zero;
            footPos += lift;
            toePos += lift;

            UxrLegIKSolver s = leg.Solver;
            if (s.HasToes && leg.PoseToes != null)
            {
                s.TargetPosition = toePos;
                s.TargetRotation = toeRot;
                s.UseFootTarget = true;
                s.FootTargetPosition = footPos;
                s.FootTargetRotation = footRot;
            }
            else
            {
                s.TargetPosition = footPos;
                s.TargetRotation = footRot;
                s.UseFootTarget = false;
            }

            if (leg.PoseCalf != null)
            {
                leg.BendGoal.position = framePos + frameRot * leg.PoseCalf.position + lift;
                s.BendGoal = leg.BendGoal;
                s.BendGoalWeight = 1f;
            }

            s.PositionWeight = 1f;
            s.RotationWeight = 1f;
            s.Solve();
        }

        /// <summary>Самая низкая точка подошвы ноги живого аватара над полом, м.</summary>
        private static float SoleHeight(LiveLeg leg, Transform foot, Transform toes, float floorY)
        {
            if (foot == null || leg.SoleInFoot == null) return float.MaxValue;
            float lowest = float.MaxValue;
            foreach (Vector3 p in leg.SoleInFoot) lowest = Mathf.Min(lowest, (foot.position + foot.rotation * p).y - floorY);
            Transform toeBone = toes != null ? toes : foot;
            foreach (Vector3 p in leg.SoleInToes) lowest = Mathf.Min(lowest, (toeBone.position + toeBone.rotation * p).y - floorY);
            return lowest;
        }

        private void SetFloorY(float y)
        {
            Vector3 p = floor.position;
            if (Mathf.Abs(p.y - y) < 1e-4f) return;
            p.y = y;
            floor.position = p;
        }

        private Pose ToStand(Transform t) =>
            t == null ? default : new Pose(transform.InverseTransformPoint(t.position), Quaternion.Inverse(transform.rotation) * t.rotation);
    }
}
